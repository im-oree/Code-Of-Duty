/**
 * uGUI rendering.
 *
 * Screen-space canvases are painted with Canvas2D and composited over the WebGL
 * frame as a single full-screen textured quad. Two reasons for that choice over
 * building three.js meshes per widget:
 *
 *  1. Text. TextMeshPro renders signed-distance-field glyphs from a baked atlas.
 *     Reimplementing that faithfully is a large job, whereas the browser's own
 *     text engine — fed the same TTF that TMP baked from — lands very close for
 *     the sizes a menu uses, and handles wrapping, kerning and fallback for free.
 *  2. Draw order. uGUI is strictly hierarchy-ordered with no depth buffer, which
 *     is exactly Canvas2D's model. Painting in traversal order is correct by
 *     construction instead of needing renderOrder bookkeeping.
 *
 * The cost is that screen-space UI cannot interleave with 3D geometry — it is
 * always on top. That matches ScreenSpaceOverlay, which is what this project
 * uses (m_RenderMode: 0). World-space canvases are listed in DIVERGENCES.md.
 */

import * as THREE from 'three';
import type { UnityMap } from '../unity/YamlParser.ts';
import { num, str, color as readColor } from '../unity/YamlParser.ts';
import { assets } from '../unity/AssetDatabase.ts';
import {
  type LayoutNode,
  type Rect,
  canvasScaleFactor,
  intersectRect,
  readCanvasScaler,
  solveRect,
} from './UguiLayout.ts';

/* ------------------------------------------------------------------ fonts */

const fontCache = new Map<string, Promise<string | null>>();
const loadedFamilies = new Set<string>();

/**
 * Resolve a TMP font asset to a usable CSS font family.
 *
 * A TMP_FontAsset is a baked SDF atlas plus metrics, but it records the TTF it
 * came from in `m_SourceFontFileGUID`. We load that TTF directly.
 */
async function fontFamilyForAsset(guid: string): Promise<string | null> {
  const cached = fontCache.get(guid);
  if (cached) return cached;

  const task = (async (): Promise<string | null> => {
    try {
      const assetPath = assets.pathForGuid(guid);
      if (!assetPath) return null;

      const text = await assets.readText(assetPath);
      let ttfGuid = /m_SourceFontFileGUID:\s*([0-9a-f]{32})/.exec(text)?.[1];
      if (!ttfGuid) {
        ttfGuid = /m_SourceFontFile:\s*\{fileID:\s*\d+,\s*guid:\s*([0-9a-f]{32})/.exec(text)?.[1];
      }
      if (!ttfGuid) return null;

      const ttfPath = assets.pathForGuid(ttfGuid);
      if (!ttfPath) return null;

      const family = `uw-${ttfGuid.slice(0, 8)}`;
      if (!loadedFamilies.has(family)) {
        const buffer = await assets.readBuffer(ttfPath);
        const face = new FontFace(family, buffer);
        await face.load();
        (document as unknown as { fonts: FontFaceSet }).fonts.add(face);
        loadedFamilies.add(family);
      }
      return family;
    } catch (err) {
      console.warn('[ugui] font load failed', guid, err);
      return null;
    }
  })();

  fontCache.set(guid, task);
  return task;
}

/* --------------------------------------------------------------- textures */

const imageCache = new Map<string, Promise<HTMLImageElement | null>>();

async function loadSpriteImage(guid: string): Promise<HTMLImageElement | null> {
  const cached = imageCache.get(guid);
  if (cached) return cached;

  const task = (async (): Promise<HTMLImageElement | null> => {
    const path = assets.pathForGuid(guid);
    if (!path) return null;
    if (!/\.(png|jpg|jpeg|webp|gif)$/i.test(path)) return null; // TGA/PSD: see DIVERGENCES
    try {
      const img = new Image();
      img.src = `/api/file?path=${encodeURIComponent(path)}`;
      await img.decode();
      return img;
    } catch {
      return null;
    }
  })();

  imageCache.set(guid, task);
  return task;
}

/* ----------------------------------------------------------------- colour */

function cssColor(c: { r: number; g: number; b: number; a: number } | null, alpha = 1): string {
  if (!c) return 'rgba(0,0,0,0)';
  const to255 = (v: number) => Math.round(Math.max(0, Math.min(1, v)) * 255);
  return `rgba(${to255(c.r)},${to255(c.g)},${to255(c.b)},${(c.a * alpha).toFixed(4)})`;
}

/* ------------------------------------------------------------- rich text */

/** TMP rich text, reduced to what a menu actually uses. */
function stripRichText(s: string): string {
  return s
    .replace(/<\/?(b|i|u|s|sup|sub|color|size|font|align|mark|nobr|width|line-height|cspace|mspace|voffset|indent|smallcaps|uppercase|lowercase|style|link|sprite|pos|space|alpha|gradient|rotate|br)\b[^>]*>/gi, '')
    .replace(/\u200B/g, ''); // TMP writes a zero-width space for "empty"
}

interface TextStyle {
  family: string;
  size: number;
  weight: string;
  italic: boolean;
  color: string;
  hAlign: 'left' | 'center' | 'right';
  vAlign: 'top' | 'middle' | 'bottom';
  wrap: boolean;
  charSpacing: number;
  autoSizeMin: number;
  autoSizeMax: number;
  autoSize: boolean;
}

/** TMP alignment bitfields -> CSS-ish enums. */
function tmpHorizontal(v: number): 'left' | 'center' | 'right' {
  if (v & 2) return 'center';
  if (v & 4) return 'right';
  if (v & 8) return 'center'; // Justified: approximate
  return 'left';
}
function tmpVertical(v: number): 'top' | 'middle' | 'bottom' {
  if (v & 512) return 'middle';
  if (v & 1024) return 'bottom';
  if (v & 2048) return 'middle'; // Midline
  return 'top';
}

/* ------------------------------------------------------------- the painter */

export interface UguiStats {
  canvases: number;
  widgets: number;
  images: number;
  texts: number;
  warnings: string[];
}

export class UguiRenderer {
  readonly canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private texture: THREE.CanvasTexture;
  private quad: THREE.Mesh;
  readonly scene: THREE.Scene;
  readonly camera: THREE.OrthographicCamera;
  stats: UguiStats = { canvases: 0, widgets: 0, images: 0, texts: 0, warnings: [] };

  constructor() {
    this.canvas = document.createElement('canvas');
    this.canvas.width = 16;
    this.canvas.height = 16;
    const ctx = this.canvas.getContext('2d', { alpha: true });
    if (!ctx) throw new Error('Canvas2D unavailable — cannot render uGUI');
    this.ctx = ctx;

    this.texture = new THREE.CanvasTexture(this.canvas);
    this.texture.colorSpace = THREE.SRGBColorSpace;
    this.texture.minFilter = THREE.LinearFilter;
    this.texture.magFilter = THREE.LinearFilter;
    this.texture.generateMipmaps = false;

    this.scene = new THREE.Scene();
    this.camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    this.quad = new THREE.Mesh(
      new THREE.PlaneGeometry(2, 2),
      new THREE.MeshBasicMaterial({ map: this.texture, transparent: true, depthTest: false, depthWrite: false }),
    );
    this.scene.add(this.quad);
  }

  resize(width: number, height: number, pixelRatio = 1): void {
    const w = Math.max(1, Math.round(width * pixelRatio));
    const h = Math.max(1, Math.round(height * pixelRatio));
    if (this.canvas.width !== w || this.canvas.height !== h) {
      this.canvas.width = w;
      this.canvas.height = h;
    }
  }

  /** Composite the painted UI onto the current WebGL target. */
  present(renderer: THREE.WebGLRenderer): void {
    this.texture.needsUpdate = true;
    const prevAutoClear = renderer.autoClear;
    renderer.autoClear = false;
    renderer.render(this.scene, this.camera);
    renderer.autoClear = prevAutoClear;
  }

  clear(): void {
    this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  /**
   * Paint every screen-space canvas in `roots`.
   *
   * `screenW/H` are CSS pixels; the backing canvas may be larger for DPR.
   */
  async paint(roots: LayoutNode[], screenW: number, screenH: number): Promise<void> {
    this.clear();
    this.stats = { canvases: roots.length, widgets: 0, images: 0, texts: 0, warnings: [] };

    const dpr = this.canvas.width / Math.max(1, screenW);
    const ctx = this.ctx;
    ctx.save();
    ctx.scale(dpr, dpr);

    for (const root of roots) {
      await this.paintNode(root, screenH);
    }

    ctx.restore();
  }

  private async paintNode(node: LayoutNode, screenH: number): Promise<void> {
    if (!node.active || node.alpha <= 0.001) return;
    this.stats.widgets++;

    const ctx = this.ctx;
    ctx.save();

    if (node.clip) {
      const c = node.clip;
      ctx.beginPath();
      ctx.rect(c.x, screenH - (c.y + c.height), c.width, c.height);
      ctx.clip();
    }

    // Unity rect (bottom-left origin) -> Canvas2D (top-left origin).
    const r = node.rect;
    const top = screenH - (r.y + r.height);

    if (node.rotation !== 0) {
      const cx = r.x + r.width / 2;
      const cy = top + r.height / 2;
      ctx.translate(cx, cy);
      ctx.rotate(-node.rotation);
      ctx.translate(-cx, -cy);
    }

    const drawRect: Rect = { x: r.x, y: top, width: r.width, height: r.height };

    const image = node.components.get('UnityEngine.UI.Image');
    if (image) await this.paintImage(image, drawRect, node.alpha);

    const rawImage = node.components.get('UnityEngine.UI.RawImage');
    if (rawImage) await this.paintRawImage(rawImage, drawRect, node.alpha);

    const tmp = node.components.get('TMPro.TextMeshProUGUI');
    if (tmp) await this.paintTmpText(tmp, drawRect, node.alpha, node.canvasScale);

    const legacy = node.components.get('UnityEngine.UI.Text');
    if (legacy) this.paintLegacyText(legacy, drawRect, node.alpha, node.canvasScale);

    for (const child of node.children) {
      await this.paintNode(child, screenH);
    }

    ctx.restore();
  }

  private async paintImage(doc: UnityMap, rect: Rect, alpha: number): Promise<void> {
    this.stats.images++;
    const ctx = this.ctx;
    const tint = readColor(doc.m_Color) ?? { r: 1, g: 1, b: 1, a: 1 };
    const spriteGuid = /guid:\s*([0-9a-f]{32})/.exec(str(doc.m_Sprite) ?? '')?.[1]
      ?? (typeof doc.m_Sprite === 'object' && doc.m_Sprite
        ? (doc.m_Sprite as Record<string, unknown>).guid as string | undefined
        : undefined);

    const img = spriteGuid ? await loadSpriteImage(spriteGuid) : null;

    if (!img) {
      // No sprite (or an unsupported format): Unity renders a plain tinted
      // quad, which is exactly what most of this menu's panels are.
      ctx.fillStyle = cssColor(tint, alpha);
      ctx.fillRect(rect.x, rect.y, rect.width, rect.height);
      return;
    }

    const fillAmount = num(doc.m_FillAmount, 1);
    ctx.save();
    if (fillAmount < 1 && num(doc.m_Type, 0) === 3) {
      // Filled: only the horizontal/vertical cases matter for bars.
      const method = num(doc.m_FillMethod, 0);
      ctx.beginPath();
      if (method === 0) {
        const w = rect.width * fillAmount;
        ctx.rect(num(doc.m_FillOrigin, 0) === 1 ? rect.x + rect.width - w : rect.x, rect.y, w, rect.height);
      } else {
        const h = rect.height * fillAmount;
        ctx.rect(rect.x, num(doc.m_FillOrigin, 0) === 1 ? rect.y : rect.y + rect.height - h, rect.width, h);
      }
      ctx.clip();
    }

    ctx.globalAlpha = tint.a * alpha;
    ctx.drawImage(img, rect.x, rect.y, rect.width, rect.height);

    // Approximate the tint by multiplying the drawn sprite.
    if (tint.r < 0.999 || tint.g < 0.999 || tint.b < 0.999) {
      ctx.globalCompositeOperation = 'multiply';
      ctx.fillStyle = cssColor({ ...tint, a: 1 }, 1);
      ctx.fillRect(rect.x, rect.y, rect.width, rect.height);
    }
    ctx.restore();
  }

  private async paintRawImage(doc: UnityMap, rect: Rect, alpha: number): Promise<void> {
    this.stats.images++;
    const guid = typeof doc.m_Texture === 'object' && doc.m_Texture
      ? ((doc.m_Texture as Record<string, unknown>).guid as string | undefined)
      : undefined;
    const img = guid ? await loadSpriteImage(guid) : null;
    const tint = readColor(doc.m_Color) ?? { r: 1, g: 1, b: 1, a: 1 };
    const ctx = this.ctx;
    if (!img) {
      ctx.fillStyle = cssColor(tint, alpha);
      ctx.fillRect(rect.x, rect.y, rect.width, rect.height);
      return;
    }
    ctx.save();
    ctx.globalAlpha = tint.a * alpha;
    ctx.drawImage(img, rect.x, rect.y, rect.width, rect.height);
    ctx.restore();
  }

  private async paintTmpText(doc: UnityMap, rect: Rect, alpha: number, uiScale: number): Promise<void> {
    const raw = str(doc.m_text) ?? '';
    const text = stripRichText(raw).trim();
    if (!text) return;
    this.stats.texts++;

    const fontGuid = typeof doc.m_fontAsset === 'object' && doc.m_fontAsset
      ? ((doc.m_fontAsset as Record<string, unknown>).guid as string | undefined)
      : undefined;
    const family = (fontGuid ? await fontFamilyForAsset(fontGuid) : null) ?? 'sans-serif';

    // TMP FontStyles is a bitfield:
    //   Bold 1, Italic 2, Underline 4, LowerCase 8, UpperCase 16, SmallCaps 32
    const styleBits = num(doc.m_fontStyle, 0);
    const fontSize = num(doc.m_fontSize, 14) * uiScale;

    const style: TextStyle = {
      family,
      size: fontSize,
      weight: styleBits & 1 ? '700' : '400',
      italic: (styleBits & 2) !== 0,
      color: cssColor(readColor(doc.m_fontColor) ?? { r: 1, g: 1, b: 1, a: 1 }, alpha),
      hAlign: tmpHorizontal(num(doc.m_HorizontalAlignment, 1)),
      vAlign: tmpVertical(num(doc.m_VerticalAlignment, 256)),
      wrap: num(doc.m_TextWrappingMode, num(doc.m_enableWordWrapping, 1)) !== 0,
      // TMP measures character spacing in em/100, not pixels: the advance it
      // adds is characterSpacing * fontSize / 100. Treating it as pixels makes
      // every tracked label roughly five times too wide.
      charSpacing: (num(doc.m_characterSpacing, 0) * fontSize) / 100,
      autoSize: num(doc.m_enableAutoSizing, 0) !== 0,
      autoSizeMin: num(doc.m_fontSizeMin, 8) * uiScale,
      autoSizeMax: num(doc.m_fontSizeMax, 72) * uiScale,
    };

    const shown = styleBits & 16 ? text.toUpperCase()
      : styleBits & 8 ? text.toLowerCase()
        : text;
    this.drawText(shown, rect, style);
  }

  private paintLegacyText(doc: UnityMap, rect: Rect, alpha: number, uiScale: number): void {
    const text = str(doc.m_Text) ?? '';
    if (!text.trim()) return;
    this.stats.texts++;

    const fontData = (typeof doc.m_FontData === 'object' && doc.m_FontData
      ? doc.m_FontData as UnityMap
      : {}) as UnityMap;
    const anchor = num(fontData.m_Alignment, 0);
    const style: TextStyle = {
      family: 'sans-serif',
      size: num(fontData.m_FontSize, 14) * uiScale,
      weight: '400',
      italic: false,
      color: cssColor(readColor(doc.m_Color) ?? { r: 1, g: 1, b: 1, a: 1 }, alpha),
      hAlign: anchor % 3 === 0 ? 'left' : anchor % 3 === 1 ? 'center' : 'right',
      vAlign: anchor < 3 ? 'top' : anchor < 6 ? 'middle' : 'bottom',
      wrap: true,
      charSpacing: 0,
      autoSize: false,
      autoSizeMin: 8,
      autoSizeMax: 72,
    };
    this.drawText(text, rect, style);
  }

  private drawText(text: string, rect: Rect, style: TextStyle): void {
    const ctx = this.ctx;
    ctx.save();

    let size = style.size;
    const setFont = (px: number) => {
      ctx.font = `${style.italic ? 'italic ' : ''}${style.weight} ${px}px "${style.family}", sans-serif`;
    };
    setFont(size);

    let lines = style.wrap ? wrapLines(ctx, text, rect.width) : text.split('\n');

    // TMP auto-size shrinks until the text fits its rect.
    if (style.autoSize) {
      size = Math.min(style.autoSizeMax, size);
      for (; size > style.autoSizeMin; size -= 1) {
        setFont(size);
        lines = style.wrap ? wrapLines(ctx, text, rect.width) : text.split('\n');
        const widest = Math.max(...lines.map((l) => ctx.measureText(l).width));
        if (widest <= rect.width && lines.length * size * 1.2 <= rect.height) break;
      }
    }

    const lineHeight = size * 1.2;
    const blockHeight = lines.length * lineHeight;

    let y: number;
    switch (style.vAlign) {
      case 'middle': y = rect.y + (rect.height - blockHeight) / 2; break;
      case 'bottom': y = rect.y + rect.height - blockHeight; break;
      default: y = rect.y;
    }

    ctx.fillStyle = style.color;
    ctx.textBaseline = 'top';
    ctx.textAlign = style.hAlign === 'center' ? 'center' : style.hAlign === 'right' ? 'right' : 'left';
    const x = style.hAlign === 'center' ? rect.x + rect.width / 2
      : style.hAlign === 'right' ? rect.x + rect.width
        : rect.x;

    for (const line of lines) {
      if (style.charSpacing !== 0) {
        drawSpaced(ctx, line, x, y, style.charSpacing, style.hAlign, rect);
      } else {
        ctx.fillText(line, x, y);
      }
      y += lineHeight;
    }

    ctx.restore();
  }
}

function wrapLines(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const out: string[] = [];
  for (const paragraph of text.split('\n')) {
    const words = paragraph.split(/\s+/).filter(Boolean);
    if (words.length === 0) { out.push(''); continue; }
    let line = words[0];
    for (let i = 1; i < words.length; i++) {
      const candidate = `${line} ${words[i]}`;
      if (ctx.measureText(candidate).width <= maxWidth) line = candidate;
      else { out.push(line); line = words[i]; }
    }
    out.push(line);
  }
  return out;
}

function drawSpaced(
  ctx: CanvasRenderingContext2D,
  line: string,
  x: number,
  y: number,
  spacing: number,
  align: 'left' | 'center' | 'right',
  rect: Rect,
): void {
  const total = [...line].reduce((w, ch) => w + ctx.measureText(ch).width + spacing, -spacing);
  let cursor = align === 'center' ? rect.x + (rect.width - total) / 2
    : align === 'right' ? rect.x + rect.width - total
      : x;
  const prevAlign = ctx.textAlign;
  ctx.textAlign = 'left';
  for (const ch of line) {
    ctx.fillText(ch, cursor, y);
    cursor += ctx.measureText(ch).width + spacing;
  }
  ctx.textAlign = prevAlign;
}

export { canvasScaleFactor, readCanvasScaler, solveRect, intersectRect };
