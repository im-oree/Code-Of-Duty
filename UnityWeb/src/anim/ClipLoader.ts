/**
 * Animation clips: extracting them from model files and converting them into
 * the viewer's coordinate space.
 *
 * This project keeps its animation inside the FBX rather than as separate
 * `.anim` assets — `MonKent.fbx` alone carries 48 takes. three's FBXLoader
 * parses those into `AnimationClip`s already, so the work here is not parsing
 * but *conversion*: the curves are authored in the file's left-handed space,
 * while the bones they drive have been mirrored into three's right-handed one.
 *
 * Applying an unconverted clip to a mirrored skeleton produces a character that
 * moves confidently in the wrong direction — limbs crossing, rotations
 * inverted — which reads as "the animation is broken" rather than "the axes are
 * wrong". So every track is mirrored with the same rule used for transforms in
 * `Coords.ts`:
 *
 *   position    (x, y, z) -> (x, y, -z)
 *   quaternion  (x, y, z, w) -> (-x, -y, z, w)
 *   scale       unchanged
 */

import * as THREE from 'three';
import { loadModel } from '../render/ModelLoader.ts';

export interface ClipInfo {
  name: string;
  duration: number;
  trackCount: number;
  clip: THREE.AnimationClip;
}

const clipCache = new Map<string, ClipInfo[]>();

/** Reduce `Armature|mixamorig:Hips.quaternion` to `Hips` + `quaternion`. */
function splitTrackName(name: string): { node: string; property: string } {
  const dot = name.lastIndexOf('.');
  const property = dot >= 0 ? name.slice(dot + 1) : '';
  let node = dot >= 0 ? name.slice(0, dot) : name;
  // Strip hierarchy and namespace decoration.
  const slash = node.lastIndexOf('/');
  if (slash >= 0) node = node.slice(slash + 1);
  const bar = node.lastIndexOf('|');
  if (bar >= 0) node = node.slice(bar + 1);
  const colon = node.lastIndexOf(':');
  if (colon >= 0) node = node.slice(colon + 1);
  return { node, property };
}

/**
 * Mirror a clip's curves from the source file's handedness into three's.
 *
 * Returns a new clip; the original is left untouched so it can be re-converted
 * with a different scale if the same model is used at two import scales.
 */
export function mirrorClip(clip: THREE.AnimationClip, positionScale = 1): THREE.AnimationClip {
  const tracks: THREE.KeyframeTrack[] = [];

  for (const track of clip.tracks) {
    const { node, property } = splitTrackName(track.name);
    const values = Float32Array.from(track.values as ArrayLike<number>);

    if (property === 'position') {
      for (let i = 0; i < values.length; i += 3) {
        values[i] *= positionScale;
        values[i + 1] *= positionScale;
        values[i + 2] *= -positionScale;
      }
      tracks.push(new THREE.VectorKeyframeTrack(
        `${node}.position`, Array.from(track.times), Array.from(values),
      ));
    } else if (property === 'quaternion') {
      for (let i = 0; i < values.length; i += 4) {
        values[i] = -values[i];
        values[i + 1] = -values[i + 1];
        // z and w are unchanged by a Z mirror.
      }
      tracks.push(new THREE.QuaternionKeyframeTrack(
        `${node}.quaternion`, Array.from(track.times), Array.from(values),
      ));
    } else if (property === 'scale') {
      tracks.push(new THREE.VectorKeyframeTrack(
        `${node}.scale`, Array.from(track.times), Array.from(values),
      ));
    } else {
      // Morph targets, visibility and anything else pass through renamed only.
      const copy = track.clone();
      copy.name = property ? `${node}.${property}` : node;
      tracks.push(copy);
    }
  }

  const out = new THREE.AnimationClip(clip.name, clip.duration, tracks);
  out.resetDuration();
  return out;
}

/** Load and convert every clip a model file contains. Cached per path. */
export async function loadClips(modelPath: string): Promise<ClipInfo[]> {
  const cached = clipCache.get(modelPath);
  if (cached) return cached;

  const model = await loadModel(modelPath);
  const out: ClipInfo[] = [];

  if (model) {
    const raw = (model.root as THREE.Object3D & { animations?: THREE.AnimationClip[] }).animations
      ?? [];
    for (const clip of raw) {
      if (!clip.tracks.length) continue;
      // Positions in the clip share the geometry's units, so they need the same
      // import scale the meshes get. Rotations are unit-free.
      const converted = mirrorClip(clip, model.importScale);
      out.push({
        name: clip.name,
        duration: +converted.duration.toFixed(4),
        trackCount: converted.tracks.length,
        clip: converted,
      });
    }
  }

  out.sort((a, b) => a.name.localeCompare(b.name));
  clipCache.set(modelPath, out);
  return out;
}

export function clearClipCache(): void {
  clipCache.clear();
}
