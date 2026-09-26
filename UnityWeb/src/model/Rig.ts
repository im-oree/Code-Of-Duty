/**
 * Armatures and skinning.
 *
 * Kokraf is a mesh modeller and has no concept of a skeleton, so this is ours.
 * It exists because a weapon or an operator that cannot be rigged cannot enter
 * the game: Unity needs a `SkinnedMeshRenderer` with bones, bind poses and
 * per-vertex weights, and glTF needs a `skin` node to carry them.
 *
 * Bones are declared in **Unity space** and with Unity's naming habits, so a
 * humanoid rig built here maps onto Unity's `HumanBodyBones` without a
 * translation table. Positions are bone *heads*, in metres, in model space.
 */

import * as THREE from 'three';
import { toThreePosition, type UVec3 } from '../unity/Coords.ts';
import type { EditableMesh } from './MeshEngine.ts';

export interface BoneSpec {
  name: string;
  /** Head position in Unity model space. */
  head: UVec3;
  /** Parent bone name, or null for the root. */
  parent?: string | null;
}

/** A vertex's influences: bone index -> weight. At most four survive export. */
export type VertexWeights = Map<number, number>;

/**
 * A skeleton plus the weights binding a mesh to it.
 *
 * Kept separate from the mesh so the same armature can drive several meshes —
 * body, gear, and attachments all skinned to one operator skeleton.
 */
export class Armature {
  readonly bones: THREE.Bone[] = [];
  readonly names: string[] = [];
  private readonly index = new Map<string, number>();

  constructor(specs: BoneSpec[]) {
    // Two passes: create every bone before wiring parents, so declaration
    // order does not have to be topological.
    for (const spec of specs) {
      const bone = new THREE.Bone();
      bone.name = spec.name;
      this.index.set(spec.name, this.bones.length);
      this.names.push(spec.name);
      this.bones.push(bone);
    }
    for (const spec of specs) {
      const bone = this.bones[this.index.get(spec.name)!];
      const head = toThreePosition(spec.head);
      if (spec.parent) {
        const parent = this.bones[this.index.get(spec.parent) ?? -1];
        if (!parent) throw new Error(`bone "${spec.name}" has unknown parent "${spec.parent}"`);
        // Bone transforms are local to the parent.
        const parentHead = toThreePosition(specs.find((s) => s.name === spec.parent)!.head);
        bone.position.copy(head).sub(parentHead);
        parent.add(bone);
      } else {
        bone.position.copy(head);
      }
    }
  }

  get root(): THREE.Bone {
    const root = this.bones.find((b) => !(b.parent instanceof THREE.Bone));
    if (!root) throw new Error('armature has no root bone');
    return root;
  }

  boneIndex(name: string): number {
    const i = this.index.get(name);
    if (i === undefined) throw new Error(`no bone named "${name}"`);
    return i;
  }

  /** World-space (model-space) head of a bone, in three coordinates. */
  headOf(i: number): THREE.Vector3 {
    this.bones[i].updateWorldMatrix(true, false);
    return this.bones[i].getWorldPosition(new THREE.Vector3());
  }

  /**
   * Bind matrices, as three's `Skeleton` wants them.
   *
   * The rest pose is whatever the bones are in when this is called, so build
   * the armature in its bind pose and bind before animating.
   */
  toSkeleton(): THREE.Skeleton {
    this.root.updateWorldMatrix(true, true);
    return new THREE.Skeleton(this.bones);
  }
}

export interface AutoWeightOptions {
  /**
   * How sharply influence falls off with distance. Higher is tighter: 4 gives
   * a crisp, almost rigid split at joints, 1.5 a soft blend.
   */
  falloff?: number;
  /** Maximum bones influencing one vertex. glTF and Unity both cap at 4. */
  maxInfluences?: number;
  /** Bones to ignore, by name — useful for IK targets and attachment points. */
  exclude?: string[];
}

/**
 * Weight every vertex by proximity to bone *segments*.
 *
 * Distance to the bone's line segment, not to its head, is what makes this
 * usable: weighting by head position alone makes a forearm claim the whole
 * hand, because the hand's vertices are nearer the wrist joint than the
 * elbow. Measuring to the segment gives each limb its own span.
 *
 * This is a starting point, not a substitute for painting weights. It is
 * deterministic, so an AI can auto-weight, screenshot a deformed pose, and
 * then override the vertices that look wrong with `setWeights`.
 */
export function autoWeight(
  mesh: EditableMesh,
  armature: Armature,
  options: AutoWeightOptions = {},
): Map<number, VertexWeights> {
  const falloff = options.falloff ?? 2.5;
  const maxInfluences = Math.max(1, Math.min(4, options.maxInfluences ?? 4));
  const excluded = new Set(options.exclude ?? []);

  // Each bone is the segment from its head to its parent's head. A root bone
  // has no segment, so it degenerates to a point, which is what we want.
  const segments = armature.bones.map((bone, i) => {
    const head = armature.headOf(i);
    const parent = bone.parent instanceof THREE.Bone
      ? bone.parent.getWorldPosition(new THREE.Vector3())
      : head.clone();
    return { a: parent, b: head, usable: !excluded.has(armature.names[i]) };
  });

  const distanceToSegment = (p: THREE.Vector3, a: THREE.Vector3, b: THREE.Vector3): number => {
    const ab = b.clone().sub(a);
    const lengthSq = ab.lengthSq();
    if (lengthSq < 1e-12) return p.distanceTo(a);
    const t = Math.max(0, Math.min(1, p.clone().sub(a).dot(ab) / lengthSq));
    return p.distanceTo(a.clone().addScaledVector(ab, t));
  };

  const out = new Map<number, VertexWeights>();
  for (const vid of mesh.vertexIds) {
    const p = toThreePosition(mesh.vertexPosition(vid));

    const scored: Array<{ bone: number; weight: number }> = [];
    for (let i = 0; i < segments.length; i++) {
      if (!segments[i].usable) continue;
      const d = distanceToSegment(p, segments[i].a, segments[i].b);
      scored.push({ bone: i, weight: 1 / Math.pow(Math.max(d, 1e-4), falloff) });
    }
    scored.sort((x, y) => y.weight - x.weight);

    const kept = scored.slice(0, maxInfluences);
    const total = kept.reduce((s, k) => s + k.weight, 0) || 1;
    const weights: VertexWeights = new Map();
    for (const k of kept) weights.set(k.bone, k.weight / total);
    out.set(vid, weights);
  }
  return out;
}

/**
 * Turn a mesh plus weights into a `SkinnedMesh`.
 *
 * The geometry is rebuilt first, because skin attributes have to be indexed
 * the same way as the render vertices — and the kernel duplicates vertices per
 * face, so a mesh vertex can appear many times in the buffer. The mapping is
 * recovered by matching positions, which is exact here: the duplicates are
 * copies, not approximations.
 */
export function bindSkin(
  mesh: EditableMesh,
  armature: Armature,
  weights: Map<number, VertexWeights>,
  material?: THREE.Material,
): THREE.SkinnedMesh {
  const geometry = mesh.buildGeometry();
  const position = geometry.getAttribute('position');
  const count = position.count;

  // Position -> mesh vertex id, so duplicated render vertices inherit the
  // weights of the mesh vertex they came from.
  const key = (x: number, y: number, z: number) =>
    `${x.toFixed(5)},${y.toFixed(5)},${z.toFixed(5)}`;
  const byPosition = new Map<string, number>();
  for (const vid of mesh.vertexIds) {
    const p = toThreePosition(mesh.vertexPosition(vid));
    byPosition.set(key(p.x, p.y, p.z), vid);
  }

  const indices = new Uint16Array(count * 4);
  const amounts = new Float32Array(count * 4);
  let unmatched = 0;
  for (let i = 0; i < count; i++) {
    const vid = byPosition.get(key(position.getX(i), position.getY(i), position.getZ(i)));
    const w = vid !== undefined ? weights.get(vid) : undefined;
    if (!w) {
      // Rather than leave a vertex unweighted (it would collapse to the
      // origin), pin it to the root bone and report how often that happened.
      indices[i * 4] = 0;
      amounts[i * 4] = 1;
      unmatched++;
      continue;
    }
    let slot = 0;
    for (const [bone, amount] of w) {
      if (slot >= 4) break;
      indices[i * 4 + slot] = bone;
      amounts[i * 4 + slot] = amount;
      slot++;
    }
  }
  if (unmatched > 0) {
    console.warn(`[rig] ${unmatched}/${count} render vertices had no weight; pinned to root`);
  }

  geometry.setAttribute('skinIndex', new THREE.BufferAttribute(indices, 4));
  geometry.setAttribute('skinWeight', new THREE.BufferAttribute(amounts, 4));

  const skinned = new THREE.SkinnedMesh(
    geometry,
    material ?? new THREE.MeshStandardMaterial({ color: 0x9aa0a6, flatShading: true }),
  );
  skinned.name = mesh.object.name;
  skinned.add(armature.root);
  skinned.bind(armature.toSkeleton());
  skinned.frustumCulled = false;
  return skinned;
}

/**
 * A minimal humanoid skeleton using Unity's bone names.
 *
 * Proportions are a 1.8 m adult in A-pose. It is here so rigging can be tried
 * end to end without hand-authoring twenty bones, and because matching Unity's
 * naming means the avatar maps automatically on import.
 */
export function humanoidSkeleton(height = 1.8): BoneSpec[] {
  const s = height / 1.8;
  const p = (x: number, y: number, z: number): UVec3 => ({ x: x * s, y: y * s, z: z * s });
  return [
    { name: 'Hips', head: p(0, 0.95, 0), parent: null },
    { name: 'Spine', head: p(0, 1.08, 0), parent: 'Hips' },
    { name: 'Chest', head: p(0, 1.22, 0), parent: 'Spine' },
    { name: 'Neck', head: p(0, 1.48, 0), parent: 'Chest' },
    { name: 'Head', head: p(0, 1.58, 0), parent: 'Neck' },
    { name: 'LeftShoulder', head: p(0.05, 1.44, 0), parent: 'Chest' },
    { name: 'LeftUpperArm', head: p(0.18, 1.44, 0), parent: 'LeftShoulder' },
    { name: 'LeftLowerArm', head: p(0.45, 1.44, 0), parent: 'LeftUpperArm' },
    { name: 'LeftHand', head: p(0.70, 1.44, 0), parent: 'LeftLowerArm' },
    { name: 'RightShoulder', head: p(-0.05, 1.44, 0), parent: 'Chest' },
    { name: 'RightUpperArm', head: p(-0.18, 1.44, 0), parent: 'RightShoulder' },
    { name: 'RightLowerArm', head: p(-0.45, 1.44, 0), parent: 'RightUpperArm' },
    { name: 'RightHand', head: p(-0.70, 1.44, 0), parent: 'RightLowerArm' },
    { name: 'LeftUpperLeg', head: p(0.09, 0.92, 0), parent: 'Hips' },
    { name: 'LeftLowerLeg', head: p(0.09, 0.50, 0), parent: 'LeftUpperLeg' },
    { name: 'LeftFoot', head: p(0.09, 0.08, 0), parent: 'LeftLowerLeg' },
    { name: 'RightUpperLeg', head: p(-0.09, 0.92, 0), parent: 'Hips' },
    { name: 'RightLowerLeg', head: p(-0.09, 0.50, 0), parent: 'RightUpperLeg' },
    { name: 'RightFoot', head: p(-0.09, 0.08, 0), parent: 'RightLowerLeg' },
  ];
}
