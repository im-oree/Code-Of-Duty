/**
 * Model loading: FBX / glTF / OBJ -> three.js geometry, resolved the way Unity
 * resolves it.
 *
 * Unity references a mesh inside a model file as `{fileID, guid}` where the guid
 * identifies the *file* and the fileID identifies one mesh *inside* it. The
 * fileID is a hash Unity computes at import time from the node name and type.
 * We cannot reproduce that hash, and this project's model .meta files carry an
 * empty `internalIDToNameTable`, so there is no recorded mapping either.
 *
 * So we resolve by name instead, in decreasing order of confidence, and record
 * which strategy won so the viewer can show it and DIVERGENCES.md stays honest.
 */

import * as THREE from 'three';
import { FBXLoader } from 'three/examples/jsm/loaders/FBXLoader.js';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { OBJLoader } from 'three/examples/jsm/loaders/OBJLoader.js';
import { assets } from '../unity/AssetDatabase.ts';
import { mirrorGeometry, mirrorMatrix } from './Mirror.ts';

/** One mesh discovered inside a model file. */
export interface ModelMesh {
  name: string;
  geometry: THREE.BufferGeometry;
  /** Material(s) the model file itself declares, used when Unity has none. */
  material: THREE.Material | THREE.Material[];
  /** Bone names in skin order, when the source was a SkinnedMesh. */
  boneNames?: string[];
  /** Inverse bind matrices in the same order as `boneNames`. */
  boneInverses?: THREE.Matrix4[];
  /**
   * The mesh node's own world matrix at bind time.
   *
   * three applies this before the bone transforms, and FBX exporters routinely
   * park the up-axis correction and unit scale on it. Replacing it with identity
   * flattens the character — which is exactly what happened here before this was
   * carried through.
   */
  bindMatrix?: THREE.Matrix4;
  /** Vertex count, used for the "largest mesh" fallback and for reporting. */
  vertexCount: number;
  /** True when the source node was a SkinnedMesh. */
  skinned: boolean;
  /** Name of the mesh node inside the model, for locating it in a clone. */
  nodeName: string;
}

export interface LoadedModel {
  path: string;
  meshes: ModelMesh[];
  byName: Map<string, ModelMesh>;
  /** The raw scene graph, kept so callers can fall back to whole-model display. */
  root: THREE.Object3D;
  /**
   * Uniform scale Unity's model importer applies, so geometry lands in metres.
   * See `readImportScale` for how it is derived.
   */
  importScale: number;
}

/**
 * Work out the scale Unity's ModelImporter applies to a model file.
 *
 * Unity bakes this into the imported mesh asset, and every Transform in the
 * scene is authored assuming it has already happened — so a viewer that skips it
 * renders weapons a hundred times too big. Two inputs combine:
 *
 *   - `meshes.globalScale` in the .meta: the "Scale Factor" field in the
 *     importer inspector (0.165 for this project's rifles).
 *   - the file's own `UnitScaleFactor`, in centimetres per file unit, honoured
 *     when `useFileScale: 1`. 1.0 means the file is in centimetres, 100.0 means
 *     metres.
 *
 * effectiveScale = globalScale * UnitScaleFactor / 100
 *
 * Verified against Unity's own derived value: N4_Rifle.fbx.meta records
 * 0.165 * 1/100 = 0.00165, and MonKent.fbx.meta records 1 * 100/100 = 1.
 */
function readUnitScaleFactor(buffer: ArrayBuffer): number | null {
  const bytes = new Uint8Array(buffer, 0, Math.min(buffer.byteLength, 262144));
  const needle = 'UnitScaleFactor';

  let at = -1;
  outer:
  for (let i = 0; i + needle.length < bytes.length; i++) {
    for (let j = 0; j < needle.length; j++) {
      if (bytes[i + j] !== needle.charCodeAt(j)) continue outer;
    }
    at = i + needle.length;
    break;
  }
  if (at < 0) return null;

  // Binary FBX stores the value as a 'D' (double) property a few short strings
  // later: "double", "Number", "". None of those, nor their little-endian
  // length prefixes, contain the byte 0x44, so the first one is ours.
  const view = new DataView(buffer);
  for (let i = at; i < Math.min(at + 64, bytes.length - 8); i++) {
    if (bytes[i] !== 0x44) continue;
    const value = view.getFloat64(i + 1, true);
    if (Number.isFinite(value) && value > 1e-4 && value < 1e6) return value;
  }

  // ASCII FBX: P: "UnitScaleFactor", "double", "Number", "",100
  const text = new TextDecoder('latin1').decode(bytes.subarray(at, at + 128));
  const m = /,\s*([0-9]*\.?[0-9]+)/.exec(text);
  if (m) {
    const value = parseFloat(m[1]);
    if (Number.isFinite(value) && value > 1e-4 && value < 1e6) return value;
  }
  return null;
}

async function readImportScale(path: string, buffer: ArrayBuffer): Promise<number> {
  // glTF is defined to be in metres, and Unity imports it 1:1.
  if (/\.(glb|gltf)$/i.test(path)) return 1;

  let globalScale = 1;
  let useFileScale = true;
  try {
    const meta = await assets.readText(`${path}.meta`);
    // The first `globalScale` under `meshes:` is the importer's Scale Factor;
    // later ones belong to the human description and must not be used.
    const meshesBlock = meta.slice(meta.indexOf('meshes:'));
    const gs = /globalScale:\s*([0-9.eE+-]+)/.exec(meshesBlock);
    if (gs) globalScale = parseFloat(gs[1]);
    const ufs = /useFileScale:\s*(\d)/.exec(meta);
    if (ufs) useFileScale = ufs[1] === '1';
  } catch {
    /* no .meta: fall back to the file scale alone */
  }

  let fileScale = 1;
  if (useFileScale) {
    const unit = readUnitScaleFactor(buffer);
    if (unit !== null) fileScale = unit / 100;
  }

  const scale = globalScale * fileScale;
  return Number.isFinite(scale) && scale > 0 ? scale : 1;
}

export type ResolveStrategy =
  | 'exact-name'
  | 'normalized-name'
  | 'only-mesh'
  | 'largest-mesh'
  | 'stable-index'
  | 'failed';

export interface ResolvedMesh {
  mesh: ModelMesh | null;
  strategy: ResolveStrategy;
  modelPath: string;
  /** Unity's model import scale; apply to static meshes to land in metres. */
  importScale: number;
}

const modelCache = new Map<string, Promise<LoadedModel | null>>();

/** Strip Unity/DCC decoration so "Rifle_LOD0 (1)" and "rifle lod0" compare equal. */
function normalizeName(n: string): string {
  return n
    .toLowerCase()
    .replace(/\(\s*\d+\s*\)\s*$/, '')
    .replace(/[\s_\-.]+/g, '')
    .replace(/(lod\d+|mesh|geo|grp|low|high)$/g, '');
}

const identity = new THREE.Matrix4();

function collectMeshes(root: THREE.Object3D): ModelMesh[] {
  const out: ModelMesh[] = [];
  root.traverse((child) => {
    const asMesh = child as THREE.Mesh;
    if (!asMesh.isMesh || !asMesh.geometry) return;

    // Bake the node's own transform inside the model file into the geometry.
    // Unity does this at import time; skipping it loses any scaling or
    // orientation the artist put on the node rather than the vertices.
    const geometry = (asMesh.geometry as THREE.BufferGeometry).clone();
    if (!(child as THREE.SkinnedMesh).isSkinnedMesh) {
      child.updateWorldMatrix(true, false);
      const local = new THREE.Matrix4().copy(child.matrixWorld);
      if (!local.equals(identity)) geometry.applyMatrix4(local);
    }
    // Vertex data is still in the source file's handedness; the scene graph
    // around it is not. Mirror here, once, so the two agree.
    mirrorGeometry(geometry);

    const entry: ModelMesh = {
      name: child.name || asMesh.geometry.name || `mesh${out.length}`,
      geometry,
      material: asMesh.material,
      vertexCount: geometry.getAttribute('position')?.count ?? 0,
      skinned: false,
      nodeName: child.name || '',
    };

    const skinned = child as THREE.SkinnedMesh;
    if (skinned.isSkinnedMesh && skinned.skeleton) {
      entry.skinned = true;
      entry.boneNames = skinned.skeleton.bones.map((b) => b.name);
      // Bind matrices must cross the same mirror as the vertices they act on.
      entry.boneInverses = skinned.skeleton.boneInverses.map((m) => mirrorMatrix(m));
      entry.bindMatrix = mirrorMatrix(skinned.bindMatrix ?? new THREE.Matrix4());
    }
    out.push(entry);
  });
  return out;
}

async function parseModel(path: string, buffer: ArrayBuffer): Promise<THREE.Object3D> {
  const ext = path.slice(path.lastIndexOf('.')).toLowerCase();

  if (ext === '.fbx') {
    // three's FBXLoader handles both the binary ("Kaydara FBX Binary") and
    // ASCII dialects; this project's models are binary.
    return new FBXLoader().parse(buffer, '');
  }

  if (ext === '.glb' || ext === '.gltf') {
    const loader = new GLTFLoader();
    return new Promise<THREE.Object3D>((resolve, reject) => {
      loader.parse(buffer, '', (gltf) => resolve(gltf.scene), reject);
    });
  }

  if (ext === '.obj') {
    const text = new TextDecoder().decode(buffer);
    return new OBJLoader().parse(text);
  }

  throw new Error(`unsupported model format: ${ext}`);
}

/** Load (and cache) a model file by project-relative path. */
export function loadModel(path: string): Promise<LoadedModel | null> {
  const cached = modelCache.get(path);
  if (cached) return cached;

  const task = (async (): Promise<LoadedModel | null> => {
    try {
      const buffer = await assets.readBuffer(path);
      const root = await parseModel(path, buffer);
      const importScale = await readImportScale(path, buffer);
      const meshes = collectMeshes(root);
      const byName = new Map<string, ModelMesh>();
      for (const m of meshes) {
        byName.set(m.name, m);
        const norm = normalizeName(m.name);
        if (!byName.has(norm)) byName.set(norm, m);
      }
      return { path, meshes, byName, root, importScale };
    } catch (err) {
      console.warn(`[model] failed to load ${path}:`, err);
      return null;
    }
  })();

  modelCache.set(path, task);
  return task;
}

/**
 * Resolve a Unity `m_Mesh: {fileID, guid}` reference to a concrete mesh.
 *
 * `hintName` should be the name of the GameObject carrying the renderer —
 * Unity names imported GameObjects after their source FBX nodes, so this is
 * usually an exact hit.
 */
export async function resolveMesh(
  guid: string,
  fileID: string | number,
  hintName: string,
): Promise<ResolvedMesh> {
  const path = assets.pathForGuid(guid);
  if (!path) return { mesh: null, strategy: 'failed', modelPath: '', importScale: 1 };

  const model = await loadModel(path);
  if (!model || model.meshes.length === 0) {
    return { mesh: null, strategy: 'failed', modelPath: path, importScale: 1 };
  }
  const importScale = model.importScale;

  const exact = model.byName.get(hintName);
  if (exact) return { mesh: exact, strategy: 'exact-name', modelPath: path, importScale };

  const norm = model.byName.get(normalizeName(hintName));
  if (norm) return { mesh: norm, strategy: 'normalized-name', modelPath: path, importScale };

  if (model.meshes.length === 1) {
    return { mesh: model.meshes[0], strategy: 'only-mesh', modelPath: path, importScale };
  }

  // Deterministic last resort: sort fileIDs seen for this model and index into
  // the mesh list. Stable across runs, so screenshots stay comparable even
  // when the mapping is a guess.
  const key = String(fileID);
  let seen = fileIdOrder.get(path);
  if (!seen) { seen = []; fileIdOrder.set(path, seen); }
  if (!seen.includes(key)) { seen.push(key); seen.sort(); }
  const idx = seen.indexOf(key);
  if (idx >= 0 && idx < model.meshes.length) {
    return { mesh: model.meshes[idx], strategy: 'stable-index', modelPath: path, importScale };
  }

  const largest = model.meshes.reduce((a, b) => (b.vertexCount > a.vertexCount ? b : a));
  return { mesh: largest, strategy: 'largest-mesh', modelPath: path, importScale };
}

const fileIdOrder = new Map<string, string[]>();

/**
 * Bind a model's skin to the bone Transforms that already exist in the scene.
 *
 * Unity serialises a SkinnedMeshRenderer's `m_Bones` as fileIDs pointing at real
 * Transforms in the same scene, in the same order as the mesh's bind poses. That
 * makes the scene graph itself the skeleton — including whatever pose the artist
 * saved — so there is nothing to retarget and no name matching to get wrong.
 *
 * The only correction needed is handedness: the bind matrices come out of the
 * model file in Unity's left-handed space while the bones have already been
 * mirrored into three's, so the inverses are mirrored to match (done at load
 * time, in `collectMeshes`).
 *
 * Scale needs no special handling either. The bind inverses carry the model's
 * own units and the scene bones carry Unity's, and the two cancel:
 *
 *   bone.world * boneInverse * v  ==  importScale * (bind-pose vertex)
 */
export function bindToSceneBones(
  sceneBones: THREE.Object3D[],
  mesh: ModelMesh,
): { skeleton: THREE.Skeleton; exactBindPoses: boolean } | null {
  if (sceneBones.length === 0) return null;
  const bones = sceneBones as THREE.Bone[];

  let inverses = mesh.boneInverses;
  let exact = true;

  // Unity and the model file should agree on bone count. When they do not,
  // derive the inverses from the scene's current pose instead: correct when the
  // saved pose is the bind pose, and visibly wrong rather than subtly wrong
  // otherwise — which is what the warning is for.
  if (!inverses || inverses.length !== bones.length) {
    exact = false;
    inverses = bones.map((b) => {
      b.updateWorldMatrix(true, false);
      return new THREE.Matrix4().copy(b.matrixWorld).invert();
    });
  }

  return { skeleton: new THREE.Skeleton(bones, inverses), exactBindPoses: exact };
}

/**
 * Uniform scale that maps a model file's units onto the scene's metres, measured
 * from a reference object of known size rather than guessed from the file header.
 */
export function measureModelScale(model: LoadedModel, expectedRadius: number): number {
  const box = new THREE.Box3().setFromObject(model.root);
  if (box.isEmpty()) return 1;
  const radius = box.getSize(new THREE.Vector3()).length() * 0.5;
  return radius > 1e-6 ? expectedRadius / radius : 1;
}

/** Free cached models — used when the viewer reloads a scene. */
export function clearModelCache(): void {
  modelCache.clear();
  fileIdOrder.clear();
}
