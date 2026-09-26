/**
 * Unity <-> three.js coordinate conversion.
 *
 * Unity:    LEFT-handed,  Y up, +Z forward, euler applied Z -> X -> Y (intrinsic)
 * three.js: RIGHT-handed, Y up, -Z forward, euler default XYZ
 *
 * The conversion is a mirror on the Z axis. For a mirror on Z:
 *   - positions:   (x, y, z)      -> (x, y, -z)
 *   - quaternions: (x, y, z, w)   -> (-x, -y, z, w)
 *   - scales:      unchanged
 *
 * The quaternion rule follows from conjugating by the mirror matrix: mirroring
 * an axis negates the two rotation components that are perpendicular to it.
 *
 * Camera note: Unity cameras look down their local +Z; three.js cameras look
 * down their local -Z. After the mirror above, a Unity camera's world forward
 * maps onto the three.js camera's world forward with NO extra 180-degree yaw.
 * That is asserted in tests/coords.test.mjs because getting it wrong is the
 * single most common mistake when bridging the two engines.
 */

import * as THREE from 'three';

export interface UVec3 { x: number; y: number; z: number }
export interface UQuat { x: number; y: number; z: number; w: number }

const DEG2RAD = Math.PI / 180;

/** Unity position -> three.js position. */
export function toThreePosition(u: UVec3, out = new THREE.Vector3()): THREE.Vector3 {
  return out.set(u.x, u.y, -u.z);
}

/** three.js position -> Unity position. */
export function toUnityPosition(v: THREE.Vector3): UVec3 {
  return { x: v.x, y: v.y, z: -v.z };
}

/** Unity quaternion -> three.js quaternion. */
export function toThreeQuaternion(u: UQuat, out = new THREE.Quaternion()): THREE.Quaternion {
  return out.set(-u.x, -u.y, u.z, u.w);
}

/** three.js quaternion -> Unity quaternion. */
export function toUnityQuaternion(q: THREE.Quaternion): UQuat {
  return { x: -q.x, y: -q.y, z: q.z, w: q.w };
}

/**
 * Unity euler angles (degrees) -> Unity quaternion.
 * Unity applies rotations in the order Z, then X, then Y (intrinsic), which in
 * three.js Euler terms is the 'ZXY' order.
 */
export function unityEulerToQuaternion(e: UVec3): UQuat {
  const q = new THREE.Quaternion().setFromEuler(
    new THREE.Euler(e.x * DEG2RAD, e.y * DEG2RAD, e.z * DEG2RAD, 'ZXY'),
  );
  return { x: q.x, y: q.y, z: q.z, w: q.w };
}

/** Unity euler angles (degrees) -> three.js quaternion. */
export function toThreeQuaternionFromEuler(e: UVec3, out = new THREE.Quaternion()): THREE.Quaternion {
  return toThreeQuaternion(unityEulerToQuaternion(e), out);
}

/** Unity scale -> three.js scale (identical). */
export function toThreeScale(u: UVec3, out = new THREE.Vector3()): THREE.Vector3 {
  return out.set(u.x, u.y, u.z);
}

/** Unity direction vector -> three.js direction (same mirror as position). */
export function toThreeDirection(u: UVec3, out = new THREE.Vector3()): THREE.Vector3 {
  return out.set(u.x, u.y, -u.z).normalize();
}

/** Unity colour (linear floats, possibly >1 for HDR) -> three.js Color. */
export function toThreeColor(c: { r: number; g: number; b: number }, out = new THREE.Color()): THREE.Color {
  return out.setRGB(c.r, c.g, c.b, THREE.LinearSRGBColorSpace);
}

/** Read a `{x,y,z}` map with sane defaults. */
export function readVec3(v: unknown, dx = 0, dy = 0, dz = 0): UVec3 {
  const m = v as Record<string, unknown> | null | undefined;
  const n = (k: string, d: number) => {
    const raw = m?.[k];
    if (typeof raw === 'number') return raw;
    if (typeof raw === 'string') { const p = parseFloat(raw); return Number.isFinite(p) ? p : d; }
    return d;
  };
  return { x: n('x', dx), y: n('y', dy), z: n('z', dz) };
}

/** Read a `{x,y,z,w}` map with an identity default. */
export function readQuat(v: unknown): UQuat {
  const m = v as Record<string, unknown> | null | undefined;
  const n = (k: string, d: number) => {
    const raw = m?.[k];
    if (typeof raw === 'number') return raw;
    if (typeof raw === 'string') { const p = parseFloat(raw); return Number.isFinite(p) ? p : d; }
    return d;
  };
  return { x: n('x', 0), y: n('y', 0), z: n('z', 0), w: n('w', 1) };
}

/** Read an `{r,g,b,a}` map. */
export function readColor(v: unknown): { r: number; g: number; b: number; a: number } {
  const m = v as Record<string, unknown> | null | undefined;
  const n = (k: string, d: number) => {
    const raw = m?.[k];
    if (typeof raw === 'number') return raw;
    if (typeof raw === 'string') { const p = parseFloat(raw); return Number.isFinite(p) ? p : d; }
    return d;
  };
  return { r: n('r', 1), g: n('g', 1), b: n('b', 1), a: n('a', 1) };
}

/**
 * Unity's `field of view` is VERTICAL degrees, matching three.js `fov`, so no
 * conversion is needed. Exposed as a named function so the assumption is
 * explicit and greppable rather than an unexplained direct assignment.
 */
export function unityFovToThree(verticalDegrees: number): number {
  return verticalDegrees;
}
