/**
 * RectTransform layout — Unity's anchor/pivot maths, reproduced exactly.
 *
 * Unity's own relationship between the serialized fields is:
 *
 *   offsetMin = anchoredPosition - pivot * sizeDelta
 *   offsetMax = anchoredPosition + (1 - pivot) * sizeDelta
 *   rect.min  = parent.min + anchorMin * parent.size + offsetMin
 *   rect.max  = parent.min + anchorMax * parent.size + offsetMax
 *
 * That single pair of lines covers both the "point anchor" case (anchorMin ==
 * anchorMax, where sizeDelta *is* the size) and the "stretch" case (where
 * sizeDelta is a margin added to the stretched span). Special-casing them is a
 * common way to get this subtly wrong, so we don't.
 *
 * Everything here works in Unity's coordinate space: origin bottom-left, +Y up.
 * Conversion to Canvas2D's top-left origin happens once, at paint time.
 */

import type { UnityMap } from '../unity/YamlParser.ts';
import { num, vec2 } from '../unity/YamlParser.ts';

export interface Rect {
  /** Left edge, Unity space (+X right). */
  x: number;
  /** Bottom edge, Unity space (+Y up). */
  y: number;
  width: number;
  height: number;
}

export interface LayoutNode {
  fileID: string;
  name: string;
  /** Resolved rect in canvas space, Unity orientation. */
  rect: Rect;
  /** Accumulated scale from the canvas down to this node. */
  scale: number;
  /**
   * CanvasScaler factor in force for this node. Rects are already multiplied by
   * it; font sizes are not, because they arrive from TMP in canvas units and
   * must be scaled at paint time or text drifts out of its boxes on any
   * viewport that is not the reference resolution.
   */
  canvasScale: number;
  /** Accumulated Z rotation in radians (uGUI's only meaningful rotation in 2D). */
  rotation: number;
  /** Product of every CanvasGroup alpha above and including this node. */
  alpha: number;
  /** False when this node or any ancestor is inactive. */
  active: boolean;
  /** Rect that clips this node, from the nearest RectMask2D/Mask ancestor. */
  clip: Rect | null;
  depth: number;
  children: LayoutNode[];
  /** Components on the owning GameObject, keyed by resolved type name. */
  components: Map<string, UnityMap>;
}

export interface CanvasScalerSettings {
  /** 0 = ConstantPixelSize, 1 = ScaleWithScreenSize, 2 = ConstantPhysicalSize. */
  mode: number;
  referenceWidth: number;
  referenceHeight: number;
  /** 0 = match width, 1 = match height. */
  matchWidthOrHeight: number;
  /** 0 = shrink, 1 = expand, 2 = match. */
  screenMatchMode: number;
  scaleFactor: number;
}

export function readCanvasScaler(doc: UnityMap | undefined): CanvasScalerSettings {
  const ref = vec2(doc?.m_ReferenceResolution) ?? { x: 800, y: 600 };
  return {
    mode: num(doc?.m_UiScaleMode, 0),
    referenceWidth: ref.x || 800,
    referenceHeight: ref.y || 600,
    matchWidthOrHeight: num(doc?.m_MatchWidthOrHeight, 0),
    screenMatchMode: num(doc?.m_ScreenMatchMode, 0),
    scaleFactor: num(doc?.m_ScaleFactor, 1),
  };
}

/**
 * Compute the canvas scale factor exactly as CanvasScaler does, so a menu
 * authored for 1920x1080 lays out correctly at any capture size.
 */
export function canvasScaleFactor(
  s: CanvasScalerSettings,
  screenWidth: number,
  screenHeight: number,
): number {
  if (s.mode !== 1) return s.scaleFactor || 1;

  const logW = Math.log2(screenWidth / s.referenceWidth);
  const logH = Math.log2(screenHeight / s.referenceHeight);

  switch (s.screenMatchMode) {
    case 1: // Expand: never crop, so take the smaller axis ratio
      return Math.pow(2, Math.min(logW, logH));
    case 2: // Shrink: fill, allowing overflow
      return Math.pow(2, Math.max(logW, logH));
    default: {
      // MatchWidthOrHeight — the default and by far the most common.
      const t = Math.max(0, Math.min(1, s.matchWidthOrHeight));
      return Math.pow(2, logW * (1 - t) + logH * t);
    }
  }
}

/**
 * Solve one RectTransform against its parent's already-solved rect.
 * `parent` is null for the Canvas itself, which occupies the whole screen.
 */
export function solveRect(rt: UnityMap, parent: Rect | null, parentScale = 1): Rect {
  if (!parent) {
    // The root Canvas RectTransform: Unity forces it to screen size. Its own
    // sizeDelta holds the screen size in canvas units.
    const size = vec2(rt.m_SizeDelta) ?? { x: 0, y: 0 };
    return { x: 0, y: 0, width: size.x, height: size.y };
  }

  const anchorMin = vec2(rt.m_AnchorMin) ?? { x: 0, y: 0 };
  const anchorMax = vec2(rt.m_AnchorMax) ?? { x: 0, y: 0 };
  const anchoredPos = vec2(rt.m_AnchoredPosition) ?? { x: 0, y: 0 };
  const sizeDelta = vec2(rt.m_SizeDelta) ?? { x: 0, y: 0 };
  const pivot = vec2(rt.m_Pivot) ?? { x: 0.5, y: 0.5 };

  const anchorRefMinX = parent.x + anchorMin.x * parent.width;
  const anchorRefMinY = parent.y + anchorMin.y * parent.height;
  const anchorRefMaxX = parent.x + anchorMax.x * parent.width;
  const anchorRefMaxY = parent.y + anchorMax.y * parent.height;

  const offsetMinX = anchoredPos.x - pivot.x * sizeDelta.x;
  const offsetMinY = anchoredPos.y - pivot.y * sizeDelta.y;
  const offsetMaxX = anchoredPos.x + (1 - pivot.x) * sizeDelta.x;
  const offsetMaxY = anchoredPos.y + (1 - pivot.y) * sizeDelta.y;

  const minX = anchorRefMinX + offsetMinX * parentScale;
  const minY = anchorRefMinY + offsetMinY * parentScale;
  const maxX = anchorRefMaxX + offsetMaxX * parentScale;
  const maxY = anchorRefMaxY + offsetMaxY * parentScale;

  return { x: minX, y: minY, width: maxX - minX, height: maxY - minY };
}

/** Intersect two rects; returns null when they do not overlap. */
export function intersectRect(a: Rect, b: Rect): Rect | null {
  const x = Math.max(a.x, b.x);
  const y = Math.max(a.y, b.y);
  const right = Math.min(a.x + a.width, b.x + b.width);
  const top = Math.min(a.y + a.height, b.y + b.height);
  if (right <= x || top <= y) return null;
  return { x, y, width: right - x, height: top - y };
}
