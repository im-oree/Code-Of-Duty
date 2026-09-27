/**
 * Parametric starting shapes, built as quad meshes in Unity space.
 *
 * These are deliberately segment-parameterised rather than fixed: the house
 * style is low poly, but "low poly" is a resolution choice, not a modelling
 * technique. Build a barrel with `radialSegments: 24` while you get the
 * proportions right, then drop it to 8 for the shipping asset and the shape
 * survives. Everything is metres, +Y up, +Z forward.
 */

import type { UVec3 } from '../unity/Coords.ts';
import { EditableMesh } from './MeshEngine.ts';

const v = (x: number, y: number, z: number): UVec3 => ({ x, y, z });

/** Reject NaN/Infinity early: a bad option silently yields unrenderable geometry. */
function finite(label: string, ...values: number[]): void {
  for (const n of values) {
    if (!Number.isFinite(n)) throw new Error(`${label}: dimension is not a finite number (got ${n})`);
  }
}

/**
 * Add a face, given the direction it should look towards.
 *
 * Winding is the single easiest thing to get wrong here, and when it is wrong
 * the face does not error -- it just turns invisible from the outside, which
 * reads as a hole in the model. Two reversals are in play: Unity is
 * left-handed and three.js is right-handed, so the z flip reverses handedness,
 * and `EditableMesh.addFace` reverses the vertex order to compensate. Reasoning
 * about that per face is how you end up with five good sides and one hole.
 *
 * So no caller states a winding. They state the outward direction, which is
 * always obvious from the shape, and the ordering convention lives here alone,
 * pinned by the primitive orientation tests.
 */
function addFacing(mesh: EditableMesh, ids: number[], outward: UVec3): number {
  const pts = ids.map((id) => mesh.vertexPosition(id));
  // Newell normal of the polygon as listed, in Unity coordinates.
  let nx = 0, ny = 0, nz = 0;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[i], b = pts[(i + 1) % pts.length];
    nx += (a.y - b.y) * (a.z + b.z);
    ny += (a.z - b.z) * (a.x + b.x);
    nz += (a.x - b.x) * (a.y + b.y);
  }
  // The Unity->three mirror and addFace's compensating reversal cancel out, so
  // the listed order is the correct one exactly when this normal already
  // agrees with the outward direction.
  const listedIsCorrect = nx * outward.x + ny * outward.y + nz * outward.z > 0;
  return mesh.addFace(listedIsCorrect ? ids : [...ids].reverse());
}

export interface BoxOptions {
  size?: UVec3;
  /** Where the origin sits relative to the box. Unity props are usually pivoted at the base. */
  pivot?: 'center' | 'base';
  segments?: UVec3;
}

/**
 * An axis-aligned box.
 *
 * Segmented boxes are the usual starting point for a hard-surface prop: more
 * loops along an axis means more places to extrude or bevel later.
 */
export function box(mesh: EditableMesh, options: BoxOptions = {}): EditableMesh {
  const size = options.size ?? v(1, 1, 1);
  const seg = options.segments ?? v(1, 1, 1);
  const nx = Math.max(1, Math.round(seg.x));
  const ny = Math.max(1, Math.round(seg.y));
  const nz = Math.max(1, Math.round(seg.z));
  const yShift = options.pivot === 'base' ? size.y / 2 : 0;
  finite('box', size.x, size.y, size.z);

  // One shared vertex lattice, so the box is a closed, welded surface rather
  // than six loose planes. Interior points are never referenced by a face.
  const id = new Map<string, number>();
  const at = (i: number, j: number, k: number): number => {
    const key = `${i},${j},${k}`;
    const hit = id.get(key);
    if (hit !== undefined) return hit;
    const made = mesh.addVertex(v(
      (i / nx - 0.5) * size.x,
      (j / ny - 0.5) * size.y + yShift,
      (k / nz - 0.5) * size.z,
    ));
    id.set(key, made);
    return made;
  };

  for (let j = 0; j < ny; j++) {
    for (let k = 0; k < nz; k++) {
      addFacing(mesh, [at(0, j, k), at(0, j + 1, k), at(0, j + 1, k + 1), at(0, j, k + 1)], v(-1, 0, 0));
      addFacing(mesh, [at(nx, j, k), at(nx, j + 1, k), at(nx, j + 1, k + 1), at(nx, j, k + 1)], v(1, 0, 0));
    }
  }
  for (let i = 0; i < nx; i++) {
    for (let k = 0; k < nz; k++) {
      addFacing(mesh, [at(i, 0, k), at(i + 1, 0, k), at(i + 1, 0, k + 1), at(i, 0, k + 1)], v(0, -1, 0));
      addFacing(mesh, [at(i, ny, k), at(i + 1, ny, k), at(i + 1, ny, k + 1), at(i, ny, k + 1)], v(0, 1, 0));
    }
  }
  for (let i = 0; i < nx; i++) {
    for (let j = 0; j < ny; j++) {
      addFacing(mesh, [at(i, j, 0), at(i + 1, j, 0), at(i + 1, j + 1, 0), at(i, j + 1, 0)], v(0, 0, -1));
      addFacing(mesh, [at(i, j, nz), at(i + 1, j, nz), at(i + 1, j + 1, nz), at(i, j + 1, nz)], v(0, 0, 1));
    }
  }
  return mesh;
}

export interface CylinderOptions {
  radius?: number;
  radiusTop?: number;
  height?: number;
  radialSegments?: number;
  heightSegments?: number;
  capped?: boolean;
  pivot?: 'center' | 'base';
}

/**
 * A cylinder or truncated cone, aligned to +Y.
 *
 * `radiusTop` different from `radius` gives a cone or a taper, which covers
 * most of what a barrel, magazine or suppressor needs.
 */
export function cylinder(mesh: EditableMesh, options: CylinderOptions = {}): EditableMesh {
  const rBottom = options.radius ?? 0.5;
  const rTop = options.radiusTop ?? rBottom;
  const height = options.height ?? 1;
  const radial = Math.max(3, Math.round(options.radialSegments ?? 8));
  const rows = Math.max(1, Math.round(options.heightSegments ?? 1));
  const capped = options.capped ?? true;
  const base = options.pivot === 'base' ? 0 : -height / 2;
  finite('cylinder', rBottom, rTop, height);

  const ring: number[][] = [];
  for (let r = 0; r <= rows; r++) {
    const t = r / rows;
    const radius = rBottom + (rTop - rBottom) * t;
    const y = base + height * t;
    const row: number[] = [];
    for (let s = 0; s < radial; s++) {
      const a = (s / radial) * Math.PI * 2;
      row.push(mesh.addVertex(v(Math.cos(a) * radius, y, Math.sin(a) * radius)));
    }
    ring.push(row);
  }

  for (let r = 0; r < rows; r++) {
    for (let s = 0; s < radial; s++) {
      const n = (s + 1) % radial;
      // Outward is radial, sampled at the middle of the quad's arc.
      const a = ((s + 0.5) / radial) * Math.PI * 2;
      addFacing(mesh, [ring[r][s], ring[r][n], ring[r + 1][n], ring[r + 1][s]],
                v(Math.cos(a), 0, Math.sin(a)));
    }
  }

  if (capped) {
    // n-gon caps rather than a triangle fan: fewer vertices, and the quad
    // topology stays clean for later edits.
    addFacing(mesh, [...ring[0]], v(0, -1, 0));
    addFacing(mesh, [...ring[rows]], v(0, 1, 0));
  }
  return mesh;
}

export interface PlaneOptions {
  size?: { x: number; z: number };
  segments?: { x: number; z: number };
}

/** A flat grid on the XZ plane — ground, panels, decals, cloth to sculpt. */
export function plane(mesh: EditableMesh, options: PlaneOptions = {}): EditableMesh {
  const size = options.size ?? { x: 1, z: 1 };
  const nx = Math.max(1, Math.round(options.segments?.x ?? 1));
  const nz = Math.max(1, Math.round(options.segments?.z ?? 1));
  finite('plane', size.x, size.z);

  const grid: number[][] = [];
  for (let i = 0; i <= nx; i++) {
    const row: number[] = [];
    for (let k = 0; k <= nz; k++) {
      row.push(mesh.addVertex(v((i / nx - 0.5) * size.x, 0, (k / nz - 0.5) * size.z)));
    }
    grid.push(row);
  }
  for (let i = 0; i < nx; i++) {
    for (let k = 0; k < nz; k++) {
      addFacing(mesh, [grid[i][k], grid[i + 1][k], grid[i + 1][k + 1], grid[i][k + 1]], v(0, 1, 0));
    }
  }
  return mesh;
}

export interface SphereOptions {
  radius?: number;
  segments?: number;
  rings?: number;
}

/** A UV sphere. Quads everywhere except the two pole rings. */
export function sphere(mesh: EditableMesh, options: SphereOptions = {}): EditableMesh {
  const radius = options.radius ?? 0.5;
  const segments = Math.max(3, Math.round(options.segments ?? 12));
  const rings = Math.max(2, Math.round(options.rings ?? 8));
  finite('sphere', radius);

  const top = mesh.addVertex(v(0, radius, 0));
  const bottom = mesh.addVertex(v(0, -radius, 0));
  const band: number[][] = [];
  for (let r = 1; r < rings; r++) {
    const phi = (r / rings) * Math.PI;
    const y = Math.cos(phi) * radius;
    const rr = Math.sin(phi) * radius;
    const row: number[] = [];
    for (let s = 0; s < segments; s++) {
      const a = (s / segments) * Math.PI * 2;
      row.push(mesh.addVertex(v(Math.cos(a) * rr, y, Math.sin(a) * rr)));
    }
    band.push(row);
  }

  // The sphere is centred on the origin, so a face's outward direction is
  // simply the direction of its own centre.
  const outwardOf = (ids: number[]): UVec3 => {
    const c = ids.map((id) => mesh.vertexPosition(id))
      .reduce((acc, p) => v(acc.x + p.x, acc.y + p.y, acc.z + p.z), v(0, 0, 0));
    return v(c.x / ids.length, c.y / ids.length, c.z / ids.length);
  };
  const addRadial = (ids: number[]): void => { addFacing(mesh, ids, outwardOf(ids)); };

  for (let s = 0; s < segments; s++) {
    const n = (s + 1) % segments;
    addRadial([top, band[0][n], band[0][s]]);
    addRadial([bottom, band[rings - 2][s], band[rings - 2][n]]);
  }
  for (let r = 0; r < band.length - 1; r++) {
    for (let s = 0; s < segments; s++) {
      const n = (s + 1) % segments;
      addRadial([band[r][s], band[r][n], band[r + 1][n], band[r + 1][s]]);
    }
  }
  return mesh;
}
