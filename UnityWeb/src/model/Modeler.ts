/**
 * The modeller's public surface — the thing an AI actually drives.
 *
 * Design rules, because the primary operator here is a language model working
 * from a terminal, not a person with a mouse:
 *
 *  - **Everything is callable and nothing is modal.** There is no selection
 *    state to get wrong, no active tool, no gizmo. Operations take explicit
 *    arguments and return ids.
 *  - **Everything is Unity-shaped.** Metres, +Y up, +Z forward, left-handed.
 *    Bone names match Unity's humanoid rig. Exports land in `Assets/` with a
 *    `.meta` whose GUID is stable across regeneration.
 *  - **Everything reports.** Operations return counts and ids; `summary()`
 *    describes the whole scene as plain data. A model that cannot see the
 *    viewport can still tell what it built — and then confirm it visually with
 *    `npm run model -- --preview`.
 *
 * A modelling script is an ES module exporting a default function that takes a
 * `Modeler` and builds into it. See `models/` for worked examples.
 */

import * as THREE from 'three';
import type { UVec3 } from '../unity/Coords.ts';
import { EditableMesh, type ShadingMode } from './MeshEngine.ts';
import { box, cylinder, plane, sphere, type BoxOptions, type CylinderOptions, type PlaneOptions, type SphereOptions } from './Primitives.ts';
import { material, palette, noiseTexture, type MaterialSpec } from './Materials.ts';
import { Armature, autoWeight, bindSkin, humanoidSkeleton, type BoneSpec, type VertexWeights } from './Rig.ts';
import { exportGlb, verifyUnityRoundTrip, type PartReference, stableGuid, modelMeta } from './UnityExport.ts';

export interface PartOptions {
  name?: string;
  material?: MaterialSpec;
  /** Placement in Unity space, applied to the whole part. */
  position?: UVec3;
  /** Euler degrees, Unity order. */
  rotation?: UVec3;
  scale?: UVec3;
  shading?: ShadingMode;
}

/** One named mesh in the model. */
export interface Part {
  name: string;
  mesh: EditableMesh;
  options: PartOptions;
}

export class Modeler {
  readonly parts: Part[] = [];
  readonly root = new THREE.Group();
  private armature: Armature | null = null;
  private weights: Map<number, VertexWeights> | null = null;
  private skinnedPart: string | null = null;

  readonly name: string;

  constructor(name = 'Model') {
    this.name = name;
    this.root.name = name;
  }

  /* ---------------- building ---------------- */

  /** Create an empty part to build into by hand. */
  part(name: string, options: PartOptions = {}): Part {
    const mesh = new EditableMesh(name);
    if (options.shading) mesh.setShading(options.shading);
    const part: Part = { name, mesh, options: { ...options, name } };
    this.parts.push(part);
    return part;
  }

  box(name: string, geometry: BoxOptions = {}, options: PartOptions = {}): Part {
    const part = this.part(name, options);
    box(part.mesh, geometry);
    return part;
  }

  cylinder(name: string, geometry: CylinderOptions = {}, options: PartOptions = {}): Part {
    const part = this.part(name, options);
    cylinder(part.mesh, geometry);
    return part;
  }

  plane(name: string, geometry: PlaneOptions = {}, options: PartOptions = {}): Part {
    const part = this.part(name, options);
    plane(part.mesh, geometry);
    return part;
  }

  sphere(name: string, geometry: SphereOptions = {}, options: PartOptions = {}): Part {
    const part = this.part(name, options);
    sphere(part.mesh, geometry);
    return part;
  }

  find(name: string): Part {
    const hit = this.parts.find((p) => p.name === name);
    if (!hit) throw new Error(`no part named "${name}" (have: ${this.parts.map((p) => p.name).join(', ')})`);
    return hit;
  }

  /**
   * Extrude the faces of a part that satisfy a predicate.
   *
   * Selecting by predicate rather than by id is what makes this usable without
   * a viewport: "every face whose centre is above y = 0.4" is something a model
   * can reason about, where face id 37 is not.
   */
  extrude(partName: string, offset: UVec3, where: (center: UVec3, faceId: number) => boolean): number {
    const part = this.find(partName);
    const chosen = part.mesh.faceIds.filter((id) => where(part.mesh.faceCenter(id), id));
    if (chosen.length === 0) return 0;
    part.mesh.extrudeFaces(chosen, offset);
    return chosen.length;
  }

  /** Move the vertices of a part that satisfy a predicate. Useful for tapers and bends. */
  nudge(partName: string, where: (p: UVec3, id: number) => boolean, by: (p: UVec3) => UVec3): number {
    const part = this.find(partName);
    let moved = 0;
    for (const id of part.mesh.vertexIds) {
      const p = part.mesh.vertexPosition(id);
      if (!where(p, id)) continue;
      part.mesh.setVertexPosition(id, by(p));
      moved++;
    }
    return moved;
  }

  /* ---------------- rigging ---------------- */

  /** Define the skeleton. Pass `humanoid()` for a Unity-named humanoid rig. */
  rig(bones: BoneSpec[]): Armature {
    this.armature = new Armature(bones);
    return this.armature;
  }

  /** A 1.8 m humanoid skeleton with Unity `HumanBodyBones` names. */
  humanoid(height?: number): BoneSpec[] { return humanoidSkeleton(height); }

  /**
   * Weight a part to the armature by proximity, and mark it as the skinned part.
   *
   * Returns how many vertices got each bone as their strongest influence, so a
   * bad rig is visible in the numbers before it is visible in a render.
   */
  skin(partName: string, options: Parameters<typeof autoWeight>[2] = {}): Record<string, number> {
    if (!this.armature) throw new Error('call rig() before skin()');
    const part = this.find(partName);
    this.weights = autoWeight(part.mesh, this.armature, options);
    this.skinnedPart = partName;

    const tally: Record<string, number> = {};
    for (const w of this.weights.values()) {
      let bestBone = -1; let best = -1;
      for (const [bone, amount] of w) if (amount > best) { best = amount; bestBone = bone; }
      const name = this.armature.names[bestBone] ?? '?';
      tally[name] = (tally[name] ?? 0) + 1;
    }
    return tally;
  }

  /* ---------------- output ---------------- */

  /**
   * Assemble the three.js scene graph.
   *
   * Parts keep their own transforms so the exported asset has a hierarchy
   * Unity can address, rather than one welded blob.
   */
  build(): THREE.Group {
    this.root.clear();
    for (const part of this.parts) {
      const spec = part.options.material ?? {};
      const mat = material({ name: `${part.name}_Mat`, ...spec });

      let object: THREE.Object3D;
      if (this.armature && this.skinnedPart === part.name && this.weights) {
        object = bindSkin(part.mesh, this.armature, this.weights, mat);
      } else {
        part.mesh.buildGeometry();
        part.mesh.object.material = mat;
        object = part.mesh.object;
      }
      object.name = part.name;

      const p = part.options.position;
      const r = part.options.rotation;
      const s = part.options.scale;
      // Unity -> three on the way out, same flip the vertices took.
      if (p) object.position.set(p.x, p.y, -p.z);
      if (r) {
        object.rotation.set(
          THREE.MathUtils.degToRad(-r.x),
          THREE.MathUtils.degToRad(-r.y),
          THREE.MathUtils.degToRad(r.z),
          'ZXY',
        );
      }
      if (s) object.scale.set(s.x, s.y, s.z);

      this.root.add(object);
    }
    return this.root;
  }

  /** Plain-data description of everything built. The eyes of a headless caller. */
  summary(): Record<string, unknown> {
    const parts = this.parts.map((p) => {
      const c = p.mesh.counts;
      return {
        name: p.name,
        vertices: c.vertices,
        edges: c.edges,
        faces: c.faces,
        position: p.options.position ?? { x: 0, y: 0, z: 0 },
      };
    });
    const totals = parts.reduce(
      (a, p) => ({ vertices: a.vertices + p.vertices, faces: a.faces + p.faces }),
      { vertices: 0, faces: 0 },
    );
    return {
      name: this.name,
      parts,
      totals,
      rig: this.armature
        ? { bones: this.armature.names.length, names: this.armature.names, skinned: this.skinnedPart }
        : null,
    };
  }

  /** Export to a `.glb` buffer. */
  async toGlb(): Promise<Uint8Array> {
    return exportGlb(this.build(), { binary: true, includeSkin: !!this.armature });
  }

  /**
   * Export and verify that Unity would place the geometry where it was authored.
   *
   * Samples the extreme corners of every part, which is where a handedness or
   * scale mistake shows up largest.
   */
  async toGlbVerified(): Promise<{ glb: Uint8Array; report: Awaited<ReturnType<typeof verifyUnityRoundTrip>> }> {
    // Sample each part in its own local space: the extremes along every axis,
    // which are the most sensitive to a sign error, plus a spread of ordinary
    // vertices so a partial corruption cannot slip through between them.
    const reference: PartReference[] = [];
    for (const part of this.parts) {
      const ids = part.mesh.vertexIds;
      if (ids.length === 0) continue;
      const points = ids.map((id) => part.mesh.vertexPosition(id));

      const picked = new Map<number, UVec3>();
      for (const axis of ['x', 'y', 'z'] as const) {
        let lo = 0, hi = 0;
        points.forEach((p, i) => {
          if (p[axis] < points[lo][axis]) lo = i;
          if (p[axis] > points[hi][axis]) hi = i;
        });
        picked.set(lo, points[lo]);
        picked.set(hi, points[hi]);
      }
      const stride = Math.max(1, Math.floor(points.length / 16));
      for (let i = 0; i < points.length; i += stride) picked.set(i, points[i]);

      reference.push({ part: part.name, points: [...picked.values()] });
    }
    const glb = await this.toGlb();
    const report = await verifyUnityRoundTrip(glb, reference);
    return { glb, report };
  }
}

export { palette, material, noiseTexture, stableGuid, modelMeta };
export type { MaterialSpec, BoneSpec, UVec3 };
