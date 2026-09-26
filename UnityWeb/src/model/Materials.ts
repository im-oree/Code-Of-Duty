/**
 * Materials for the house style: flat-shaded, low-poly, readable at a glance.
 *
 * The game's look is "lowkey realistic COD-ish", not photoreal, so the default
 * here is an unlit-leaning PBR material with `flatShading` on and roughness
 * high. That reads as faceted low-poly in any light and, importantly, survives
 * the trip into Unity: glTF carries base colour, metallic and roughness, which
 * URP's Lit shader consumes directly.
 *
 * Procedural textures are generated rather than authored because the point is
 * that an AI can produce a complete asset unattended. They bake to PNG inside
 * the glTF, so nothing depends on this module at runtime.
 */

import * as THREE from 'three';

export interface MaterialSpec {
  name?: string;
  /** Base colour as `#rrggbb` or a 0xRRGGBB number. */
  color?: string | number;
  metallic?: number;
  roughness?: number;
  /** Faceted shading. On by default — it is the style. */
  flat?: boolean;
  emissive?: string | number;
  emissiveStrength?: number;
}

/** A flat-shaded PBR material with sensible low-poly defaults. */
export function material(spec: MaterialSpec = {}): THREE.MeshStandardMaterial {
  const m = new THREE.MeshStandardMaterial({
    color: new THREE.Color(spec.color ?? 0x9aa0a6),
    metalness: spec.metallic ?? 0.0,
    roughness: spec.roughness ?? 0.75,
    flatShading: spec.flat ?? true,
  });
  if (spec.emissive !== undefined) {
    m.emissive = new THREE.Color(spec.emissive);
    m.emissiveIntensity = spec.emissiveStrength ?? 1;
  }
  m.name = spec.name ?? 'Material';
  return m;
}

/**
 * The project palette.
 *
 * Named so a modelling script reads as intent — `palette.gunmetal` rather than
 * `0x2f3337` — and so the whole game can be recoloured in one place.
 */
export const palette = {
  gunmetal: 0x2f3337,
  polymer: 0x1c1f22,
  steel: 0x7d848b,
  brass: 0xb08d57,
  wood: 0x6b4c35,
  desertTan: 0xc2a678,
  olive: 0x5b6043,
  concrete: 0x9a9a92,
  glass: 0x88b6d6,
  skin: 0xc98f6b,
  orange: 0xff8a1f,
} as const;

export interface NoiseTextureOptions {
  size?: number;
  /** Two colours the noise mixes between. */
  from?: string | number;
  to?: string | number;
  /** Feature size in pixels. Larger is blotchier. */
  scale?: number;
  seed?: number;
}

/**
 * A deterministic value-noise texture, for camo, rust, concrete and the like.
 *
 * Deterministic on `seed` so a rebuilt asset is byte-identical and does not
 * churn in review. Uses no canvas, so it runs in Node as well as the browser.
 */
export function noiseTexture(options: NoiseTextureOptions = {}): THREE.DataTexture {
  const size = options.size ?? 128;
  const scale = Math.max(1, options.scale ?? 16);
  const from = new THREE.Color(options.from ?? palette.olive);
  const to = new THREE.Color(options.to ?? palette.desertTan);
  let seed = (options.seed ?? 1) >>> 0;

  // xorshift32 — small, fast, and reproducible across engines.
  const rand = () => {
    seed ^= seed << 13; seed >>>= 0;
    seed ^= seed >> 17;
    seed ^= seed << 5; seed >>>= 0;
    return seed / 0xffffffff;
  };

  const lattice = Math.ceil(size / scale) + 1;
  const grid = Array.from({ length: lattice * lattice }, () => rand());
  const smooth = (t: number) => t * t * (3 - 2 * t);
  const sample = (x: number, y: number): number => {
    const gx = x / scale, gy = y / scale;
    const x0 = Math.floor(gx), y0 = Math.floor(gy);
    const tx = smooth(gx - x0), ty = smooth(gy - y0);
    const at = (i: number, j: number) =>
      grid[(Math.min(j, lattice - 1) * lattice) + Math.min(i, lattice - 1)];
    const a = at(x0, y0) + (at(x0 + 1, y0) - at(x0, y0)) * tx;
    const b = at(x0, y0 + 1) + (at(x0 + 1, y0 + 1) - at(x0, y0 + 1)) * tx;
    return a + (b - a) * ty;
  };

  const data = new Uint8Array(size * size * 4);
  const mixed = new THREE.Color();
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      mixed.copy(from).lerp(to, sample(x, y));
      const i = (y * size + x) * 4;
      data[i] = Math.round(mixed.r * 255);
      data[i + 1] = Math.round(mixed.g * 255);
      data[i + 2] = Math.round(mixed.b * 255);
      data[i + 3] = 255;
    }
  }

  const texture = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.RepeatWrapping;
  texture.needsUpdate = true;
  return texture;
}
