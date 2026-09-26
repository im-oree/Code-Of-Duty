/**
 * glTF export, aimed squarely at Unity's importer.
 *
 * ## Why the axes work out
 *
 * There are three handedness conventions in play and it is worth writing down
 * why nothing needs flipping at export time:
 *
 *  - **Unity** is left-handed, +Y up, +Z forward.
 *  - **three.js and glTF** are both right-handed, +Y up, +Z toward the viewer.
 *  - This project's rule (see `unity/Coords.ts`) is Unity `(x, y, z)` becomes
 *    three `(x, y, -z)`.
 *
 * The modeller takes Unity coordinates and stores three coordinates, applying
 * that flip once, at the `MeshEngine` boundary. Unity's glTF importer applies
 * the *same* flip on the way in. The two cancel, so a vertex authored at Unity
 * `(0, 1, 2)` arrives in Unity at `(0, 1, 2)`.
 *
 * That claim is load-bearing, so it is asserted rather than trusted:
 * `verifyUnityRoundTrip` re-imports the exported buffer through the very code
 * that renders real Unity scenes, and checks the vertex comes back where it
 * started. `tests/model.test.ts` runs it on an asymmetric shape, which is the
 * only kind that can catch a sign error.
 *
 * Scale needs no correction either: glTF is metres and so is Unity, so the
 * importer's Scale Factor stays at 1 and `UnitScaleFactor` at 100.
 */

import * as THREE from 'three';
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js';
import { toUnityPosition, type UVec3 } from '../unity/Coords.ts';

/**
 * Give Node the one browser API `GLTFExporter` insists on.
 *
 * The exporter builds a `Blob` and then reads it back through a `FileReader`
 * to produce the GLB binary chunk. Node 22 has `Blob` but not `FileReader`, so
 * without this the whole pipeline would only run in a browser — and the point
 * of the modeller is that it runs headless, in CI, from a script.
 *
 * Scoped deliberately: only `readAsArrayBuffer` and `onloadend`, only when the
 * global is missing, and never in a browser.
 */
function ensureFileReader(): void {
  const g = globalThis as { FileReader?: unknown };
  if (g.FileReader) return;
  g.FileReader = class {
    result: ArrayBuffer | null = null;
    onloadend: (() => void) | null = null;
    onerror: ((e: unknown) => void) | null = null;
    readAsArrayBuffer(blob: Blob): void {
      blob.arrayBuffer().then(
        (buffer) => { this.result = buffer; this.onloadend?.(); },
        (error) => { this.onerror?.(error); },
      );
    }
  };
}

export interface ExportOptions {
  /** Embed textures and buffers in one binary file. Almost always what you want. */
  binary?: boolean;
  /** Include the skeleton and skin weights of any `SkinnedMesh`. */
  includeSkin?: boolean;
  /** Author tag written into the glTF asset block. */
  generator?: string;
}

/**
 * Serialise a scene or object to a `.glb` buffer.
 *
 * Works in Node as well as the browser: `GLTFExporter.parse` only touches the
 * DOM for image encoding, and the materials here carry `DataTexture`s that are
 * encoded from raw bytes.
 */
export async function exportGlb(
  root: THREE.Object3D,
  options: ExportOptions = {},
): Promise<Uint8Array> {
  ensureFileReader();
  const exporter = new GLTFExporter();
  const result = await exporter.parseAsync(root, {
    binary: options.binary ?? true,
    animations: [],
    includeCustomExtensions: false,
    // Skinned meshes need their bones in the export; three walks the scene
    // graph, and the armature is parented under the SkinnedMesh by `bindSkin`.
    onlyVisible: false,
  });

  if (result instanceof ArrayBuffer) return new Uint8Array(result);
  // Non-binary export returns JSON; callers asking for .glb should not hit this.
  return new TextEncoder().encode(JSON.stringify(result));
}

export interface RoundTripReport {
  ok: boolean;
  /** Largest discrepancy, in metres, between authored and re-imported vertices. */
  maxError: number;
  checked: number;
  detail: string;
}

/**
 * Re-import an exported buffer and confirm the geometry lands where Unity
 * would put it.
 *
 * This deliberately routes through `GLTFLoader` plus the project's own
 * Unity-space conversion rather than comparing raw glTF numbers: what matters
 * is not that the file is well-formed but that the asset appears in Unity at
 * the coordinates the modelling script asked for.
 */
export interface PartReference {
  /** Mesh node name, as authored. */
  part: string;
  /** Authored vertices in the part's own local space, Unity coordinates. */
  points: UVec3[];
}

export async function verifyUnityRoundTrip(
  glb: Uint8Array,
  expected: PartReference[],
  tolerance = 1e-4,
): Promise<RoundTripReport> {
  const { GLTFLoader } = await import('three/examples/jsm/loaders/GLTFLoader.js');
  const loader = new GLTFLoader();

  const buffer = glb.buffer.slice(glb.byteOffset, glb.byteOffset + glb.byteLength) as ArrayBuffer;
  const gltf = await loader.parseAsync(buffer, '');

  // Collect re-imported geometry per mesh node, in that node's own local
  // space. Comparing locally rather than in world space is what lets a
  // rotated or scaled part be checked at all: the node transform is a
  // separate concern from whether the vertices survived the handedness flip.
  const byPart = new Map<string, THREE.Vector3[]>();
  gltf.scene.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (!mesh.isMesh) return;
    const position = mesh.geometry.getAttribute('position');
    const points: THREE.Vector3[] = [];
    for (let i = 0; i < position.count; i++) {
      points.push(new THREE.Vector3().fromBufferAttribute(position, i));
    }
    byPart.set(mesh.name, points);
  });

  let maxError = 0;
  let checked = 0;
  const missing: string[] = [];
  let worstPart = '';

  for (const ref of expected) {
    const found = byPart.get(ref.part);
    if (!found || found.length === 0) { missing.push(ref.part); continue; }
    for (const want of ref.points) {
      // Nearest re-imported vertex to the authored one: export reorders and
      // duplicates vertices, so matching by index would be meaningless.
      let best = Infinity;
      for (const got of found) {
        const u = toUnityPosition(got);
        const d = Math.hypot(u.x - want.x, u.y - want.y, u.z - want.z);
        if (d < best) best = d;
      }
      checked++;
      if (best > maxError) { maxError = best; worstPart = ref.part; }
    }
  }

  if (missing.length > 0) {
    return {
      ok: false,
      maxError: Infinity,
      checked,
      detail: `exported file is missing mesh node(s): ${missing.join(', ')}`,
    };
  }

  const ok = maxError <= tolerance;
  return {
    ok,
    maxError,
    checked,
    detail: ok
      ? `${checked} reference vertices across ${expected.length} part(s) round-tripped within ${tolerance} m`
      : `worst reference vertex (part "${worstPart}") was ${maxError.toFixed(6)} m from where Unity would place it`
        + ` — check the handedness flip in MeshEngine, not the exporter`,
  };
}

/**
 * The `.meta` sidecar Unity would otherwise generate on first import.
 *
 * Writing it ourselves means the asset arrives with a stable GUID and the
 * right import settings instead of whatever the editor guesses, which is what
 * keeps scene references from breaking when a model is regenerated.
 */
export function modelMeta(guid: string, options: { rig?: boolean } = {}): string {
  return `fileFormatVersion: 2
guid: ${guid}
ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable: []
  externalObjects: {}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    animationCompression: 1
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    importVisibility: 1
    importBlendShapes: 1
    importCameras: 0
    importLights: 0
    swapUVChannels: 0
    generateSecondaryUV: 0
    weldVertices: 1
    keepQuads: 0
    indexFormat: 0
  importAnimation: 1
  importAnimatedCustomProperties: 0
  animationType: ${options.rig ? 3 : 0}
  humanoidOversampling: 1
  avatarSetup: ${options.rig ? 1 : 0}
  additionalBone: 0
  userData:
  assetBundleName:
  assetBundleVariant:
`;
}

/**
 * A GUID derived from the asset path.
 *
 * Deterministic on purpose: regenerating a model must not change its GUID, or
 * every scene and prefab referencing it breaks. 32 hex characters, as Unity
 * expects — see the parser note about 32-hex scalars staying strings.
 */
export function stableGuid(assetPath: string): string {
  // FNV-1a, run over four offset streams to fill 128 bits.
  const hash = (seed: number): string => {
    let h = seed >>> 0;
    for (let i = 0; i < assetPath.length; i++) {
      h ^= assetPath.charCodeAt(i);
      h = Math.imul(h, 0x01000193) >>> 0;
    }
    return h.toString(16).padStart(8, '0');
  };
  return hash(0x811c9dc5) + hash(0x1b873593) + hash(0xcc9e2d51) + hash(0x85ebca6b);
}
