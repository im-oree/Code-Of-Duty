/**
 * Unity/URP material -> three.js material.
 *
 * We are not reimplementing URP. We map the properties that decide how a frame
 * READS — base colour, metallic, smoothness, emission, transparency, maps — so
 * a screenshot can be judged. Anything we do not match is listed in
 * DIVERGENCES.md rather than silently approximated.
 */

import * as THREE from 'three';
import { assets } from '../unity/AssetDatabase';
import { num, str, mapOf, arrayOf, asRef, type UnityMap, type UnityValue } from '../unity/YamlParser';

export interface MaterialProps {
  colors: Map<string, { r: number; g: number; b: number; a: number }>;
  floats: Map<string, number>;
  textures: Map<string, { guid?: string; scale: { x: number; y: number }; offset: { x: number; y: number } }>;
  shaderName: string;
}

/** Pull the `m_SavedProperties` block out of a parsed Material document. */
export function readMaterialProps(body: UnityMap, shaderName: string): MaterialProps {
  const props: MaterialProps = {
    colors: new Map(), floats: new Map(), textures: new Map(), shaderName,
  };
  const saved = mapOf(body.m_SavedProperties);
  if (!saved) return props;

  // Unity serializes these as sequences of single-entry maps: `- _Color: {...}`
  const readEntries = (raw: UnityValue | undefined, fn: (name: string, value: UnityValue) => void) => {
    for (const entry of arrayOf(raw)) {
      const m = mapOf(entry);
      if (!m) continue;
      // Newer Unity uses `{first: {name: _Color}, second: {...}}`
      const first = mapOf(m.first);
      if (first && 'name' in first) {
        fn(str(first.name), m.second as UnityValue);
        continue;
      }
      for (const [k, v] of Object.entries(m)) fn(k, v);
    }
  };

  readEntries(saved.m_Colors, (name, v) => {
    const c = mapOf(v);
    if (!c) return;
    props.colors.set(name, { r: num(c.r, 1), g: num(c.g, 1), b: num(c.b, 1), a: num(c.a, 1) });
  });

  readEntries(saved.m_Floats, (name, v) => { props.floats.set(name, num(v, 0)); });

  readEntries(saved.m_TexEnvs, (name, v) => {
    const t = mapOf(v);
    if (!t) return;
    const ref = asRef(t.m_Texture);
    const sc = mapOf(t.m_Scale);
    const of = mapOf(t.m_Offset);
    props.textures.set(name, {
      guid: ref?.guid,
      scale: { x: num(sc?.x, 1), y: num(sc?.y, 1) },
      offset: { x: num(of?.x, 0), y: num(of?.y, 0) },
    });
  });

  return props;
}

function firstColor(p: MaterialProps, names: string[], fallback: THREE.Color): THREE.Color {
  for (const n of names) {
    const c = p.colors.get(n);
    // Unity stores colours linear; three.js Color in linear working space.
    if (c) return new THREE.Color().setRGB(c.r, c.g, c.b, THREE.LinearSRGBColorSpace);
  }
  return fallback;
}

function firstFloat(p: MaterialProps, names: string[], fallback: number): number {
  for (const n of names) {
    const f = p.floats.get(n);
    if (f !== undefined) return f;
  }
  return fallback;
}

const textureCache = new Map<string, THREE.Texture | null>();

async function loadTexture(guid: string | undefined): Promise<THREE.Texture | null> {
  if (!guid) return null;
  const hit = textureCache.get(guid);
  if (hit !== undefined) return hit;

  const path = assets.pathForGuid(guid);
  if (!path || !/\.(png|jpg|jpeg)$/i.test(path)) {
    textureCache.set(guid, null);
    return null;
  }
  try {
    const url = `/api/file?path=${encodeURIComponent(path)}`;
    const tex = await new THREE.TextureLoader().loadAsync(url);
    tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
    // Unity's UV origin matches three.js; no flip needed for imported meshes.
    tex.flipY = false;
    textureCache.set(guid, tex);
    return tex;
  } catch {
    textureCache.set(guid, null);
    return null;
  }
}

/** A neutral stand-in used when a material cannot be resolved. */
export function fallbackMaterial(): THREE.Material {
  return new THREE.MeshStandardMaterial({ color: 0x8a8f96, roughness: 0.85, metalness: 0.0 });
}

/** Build a three.js material from a parsed Unity Material document. */
export async function buildMaterial(body: UnityMap): Promise<THREE.Material> {
  const shaderRef = asRef(body.m_Shader);
  let shaderName = str(body.m_Name, '');
  if (shaderRef?.guid) {
    const shaderPath = assets.pathForGuid(shaderRef.guid);
    if (shaderPath) shaderName = shaderPath;
  }
  const props = readMaterialProps(body, shaderName);
  const lower = shaderName.toLowerCase();

  const baseColor = firstColor(props, ['_BaseColor', '_Color', '_MainColor'], new THREE.Color(0xcccccc));
  const alpha = props.colors.get('_BaseColor')?.a ?? props.colors.get('_Color')?.a ?? 1;

  // Unlit shaders bypass lighting entirely.
  if (lower.includes('unlit') || lower.includes('sprite')) {
    return new THREE.MeshBasicMaterial({
      color: baseColor,
      transparent: alpha < 1,
      opacity: alpha,
      side: THREE.FrontSide,
    });
  }

  const smoothness = firstFloat(props, ['_Smoothness', '_Glossiness'], 0.5);
  const metallic = firstFloat(props, ['_Metallic'], 0.0);

  const mat = new THREE.MeshStandardMaterial({
    color: baseColor,
    roughness: THREE.MathUtils.clamp(1 - smoothness, 0.04, 1),
    metalness: THREE.MathUtils.clamp(metallic, 0, 1),
    transparent: alpha < 1,
    opacity: alpha,
  });

  // Emission is only active when Unity's keyword/flag is set.
  const emissionColor = props.colors.get('_EmissionColor');
  if (emissionColor) {
    const strength = Math.max(emissionColor.r, emissionColor.g, emissionColor.b);
    if (strength > 0.001) {
      mat.emissive = new THREE.Color().setRGB(
        emissionColor.r, emissionColor.g, emissionColor.b, THREE.LinearSRGBColorSpace);
      mat.emissiveIntensity = 1;
    }
  }

  const baseMap = props.textures.get('_BaseMap') ?? props.textures.get('_MainTex');
  if (baseMap?.guid) {
    const tex = await loadTexture(baseMap.guid);
    if (tex) {
      tex.colorSpace = THREE.SRGBColorSpace;
      tex.repeat.set(baseMap.scale.x, baseMap.scale.y);
      tex.offset.set(baseMap.offset.x, baseMap.offset.y);
      mat.map = tex;
    }
  }

  const bumpMap = props.textures.get('_BumpMap');
  if (bumpMap?.guid) {
    const tex = await loadTexture(bumpMap.guid);
    if (tex) { mat.normalMap = tex; mat.normalScale = new THREE.Vector2(1, 1); }
  }

  mat.needsUpdate = true;
  return mat;
}
