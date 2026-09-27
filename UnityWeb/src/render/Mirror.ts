/**
 * Handedness conversion for imported geometry.
 *
 * Unity is left-handed (+Z forward); three.js is right-handed (-Z forward).
 * `Coords.ts` reconciles them at the *transform* level by negating Z on
 * positions and mirroring quaternions. That is only half the job: vertex data
 * loaded straight out of an FBX or glTF is still in the source handedness, and
 * mixing the two is what turns a character into scattered limbs — the bones move
 * one way, the vertices they drive expect the other.
 *
 * So everything that carries geometry must go through the same mirror:
 *
 *   positions  z -> -z
 *   normals    z -> -z
 *   tangents   z -> -z
 *   winding    reversed, because mirroring flips triangle orientation and
 *              backface culling would otherwise hide every surface
 *   matrices   M * A * M, the similarity transform, where M = diag(1, 1, -1)
 *
 * M is its own inverse, which keeps the matrix case cheap and exact.
 */

import * as THREE from 'three';

/** diag(1, 1, -1) — the Unity/three mirror, and its own inverse. */
export const MIRROR = new THREE.Matrix4().makeScale(1, 1, -1);

/**
 * Express a matrix authored in Unity's handedness in three's.
 *
 * Used for skinning bind matrices: the bone inverses come out of the model file
 * in source space, but the bones driving them live in mirrored scene space.
 */
export function mirrorMatrix(m: THREE.Matrix4): THREE.Matrix4 {
  return new THREE.Matrix4().multiplyMatrices(MIRROR, m).multiply(MIRROR);
}

const mirrored = new WeakSet<THREE.BufferGeometry>();

/**
 * Mirror a geometry in place, once.
 *
 * Geometries are cached and shared between instances, so the WeakSet guard
 * matters: mirroring twice is identity and would silently undo the fix.
 */
export function mirrorGeometry(geometry: THREE.BufferGeometry): THREE.BufferGeometry {
  if (mirrored.has(geometry)) return geometry;
  mirrored.add(geometry);

  const negateZ = (name: string) => {
    const attr = geometry.getAttribute(name);
    if (!attr) return;
    const arr = attr.array as Float32Array;
    for (let i = 2; i < arr.length; i += attr.itemSize) arr[i] = -arr[i];
    attr.needsUpdate = true;
  };

  negateZ('position');
  negateZ('normal');
  negateZ('tangent');

  // Reverse winding so faces keep pointing outwards after the mirror.
  const index = geometry.getIndex();
  if (index) {
    const arr = index.array as Uint16Array | Uint32Array;
    for (let i = 0; i + 2 < arr.length; i += 3) {
      const t = arr[i];
      arr[i] = arr[i + 2];
      arr[i + 2] = t;
    }
    index.needsUpdate = true;
  } else {
    // Non-indexed: swap the first and third vertex of every triangle across
    // all attributes at once.
    for (const name of Object.keys(geometry.attributes)) {
      const attr = geometry.getAttribute(name);
      const arr = attr.array as Float32Array;
      const stride = attr.itemSize;
      for (let tri = 0; tri + 3 <= attr.count; tri += 3) {
        const a = tri * stride;
        const c = (tri + 2) * stride;
        for (let k = 0; k < stride; k++) {
          const t = arr[a + k];
          arr[a + k] = arr[c + k];
          arr[c + k] = t;
        }
      }
      attr.needsUpdate = true;
    }
  }

  geometry.computeBoundingBox();
  geometry.computeBoundingSphere();
  return geometry;
}
