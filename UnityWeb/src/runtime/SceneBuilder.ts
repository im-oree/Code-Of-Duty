/**
 * Turns a parsed Unity scene (or prefab) into a three.js scene graph.
 *
 * Responsibilities:
 *   - resolve GameObject <-> component links through fileIDs
 *   - build the Transform/RectTransform hierarchy with Unity's coordinate rules
 *   - instantiate meshes, materials, lights and cameras
 *   - apply RenderSettings (fog / ambient / sky)
 *   - keep every unmapped component as inert data so the inspector can show it
 *
 * Anything it cannot represent is recorded in `report.warnings` rather than
 * silently skipped: a viewer that quietly drops content is worse than useless,
 * because it produces confident wrong conclusions.
 */

import * as THREE from 'three';
import { assets } from '../unity/AssetDatabase';
import {
  num, str, bool, mapOf, arrayOf, asRef,
  type UnityDocument, type UnityFile, type UnityMap,
} from '../unity/YamlParser';
import { classNameOf, TRANSFORM_CLASS_IDS, UGUI_SCRIPT_GUIDS } from '../unity/ClassIds';
import {
  readVec3, readQuat, toThreePosition, toThreeQuaternion, toThreeScale,
  unityFovToThree, toThreeColor, readColor,
} from '../unity/Coords';
import { buildMaterial, fallbackMaterial } from '../render/MaterialMapper';
import { applyRenderSettings } from '../render/Environment';

export interface ComponentData {
  classId: number;
  className: string;
  fileID: number;
  scriptName?: string;
  body: UnityMap;
}

export interface NodeInfo {
  name: string;
  fileID: number;
  active: boolean;
  layer: number;
  tag: string;
  components: ComponentData[];
  object3d: THREE.Object3D;
  children: NodeInfo[];
}

export interface CameraInfo {
  name: string;
  path: string;
  camera: THREE.PerspectiveCamera | THREE.OrthographicCamera;
  depth: number;
}

export interface BuildReport {
  gameObjects: number;
  meshes: number;
  lights: number;
  cameras: number;
  canvases: number;
  warnings: string[];
  unmappedScripts: Map<string, number>;
}

export interface BuiltScene {
  scene: THREE.Scene;
  root: THREE.Group;
  nodes: NodeInfo[];
  cameras: CameraInfo[];
  report: BuildReport;
}

/** Unity's built-in primitive meshes, referenced by fileID in the default resources. */
const BUILTIN_MESHES: Record<number, () => THREE.BufferGeometry> = {
  10202: () => new THREE.BoxGeometry(1, 1, 1),                  // Cube
  10206: () => new THREE.CylinderGeometry(0.5, 0.5, 1, 24),     // Cylinder
  10207: () => new THREE.SphereGeometry(0.5, 24, 16),           // Sphere
  10208: () => new THREE.CapsuleGeometry(0.5, 1, 8, 16),        // Capsule
  10209: () => new THREE.PlaneGeometry(10, 10),                 // Plane
  10210: () => new THREE.PlaneGeometry(1, 1),                   // Quad
};

export class SceneBuilder {
  private file!: UnityFile;
  private docs = new Map<number, UnityDocument>();
  private gameObjectOfComponent = new Map<number, number>();
  private transformOfGameObject = new Map<number, UnityDocument>();
  private objectByFileID = new Map<number, THREE.Object3D>();
  private materialCache = new Map<string, THREE.Material>();

  private report: BuildReport = {
    gameObjects: 0, meshes: 0, lights: 0, cameras: 0, canvases: 0,
    warnings: [], unmappedScripts: new Map(),
  };

  async build(scenePath: string): Promise<BuiltScene> {
    this.file = await assets.readYaml(scenePath);
    this.docs = this.file.byFileID;

    const scene = new THREE.Scene();
    const root = new THREE.Group();
    root.name = scenePath.split('/').pop() ?? 'Scene';
    scene.add(root);

    // ---- Pass 1: index components back to their owning GameObject ----
    for (const doc of this.file.documents) {
      if (doc.classId === 1) {
        for (const entry of arrayOf(doc.body.m_Component)) {
          const m = mapOf(entry);
          const ref = asRef(m?.component ?? (m as UnityMap | null)?.['component']);
          if (ref) this.gameObjectOfComponent.set(ref.fileID, doc.fileID);
        }
      }
      if (TRANSFORM_CLASS_IDS.has(doc.classId)) {
        const owner = asRef(doc.body.m_GameObject);
        if (owner) this.transformOfGameObject.set(owner.fileID, doc);
      }
    }
    // Some components reference their GameObject directly; use that as a backstop.
    for (const doc of this.file.documents) {
      if (doc.classId === 1 || doc.stripped) continue;
      const owner = asRef(doc.body.m_GameObject);
      if (owner && !this.gameObjectOfComponent.has(doc.fileID)) {
        this.gameObjectOfComponent.set(doc.fileID, owner.fileID);
      }
    }

    // ---- Pass 2: environment ----
    const renderSettings = this.file.documents.find((d) => d.classId === 104);
    if (renderSettings) applyRenderSettings(scene, renderSettings.body);

    // ---- Pass 3: hierarchy, from the transforms that have no parent ----
    const roots: UnityDocument[] = [];
    for (const doc of this.file.documents) {
      if (!TRANSFORM_CLASS_IDS.has(doc.classId) || doc.stripped) continue;
      const parent = asRef(doc.body.m_Father);
      if (!parent || parent.fileID === 0) roots.push(doc);
    }

    const nodes: NodeInfo[] = [];
    const cameras: CameraInfo[] = [];
    for (const t of roots) {
      const node = await this.buildTransform(t, root, '', cameras);
      if (node) nodes.push(node);
    }

    // A scene with no light at all renders black; say so rather than show a void.
    if (this.report.lights === 0 && !scene.environment) {
      this.report.warnings.push('scene contains no lights and no environment map');
    }

    return { scene, root, nodes, cameras, report: this.report };
  }

  /** Recursively build one transform and its children. */
  private async buildTransform(
    tDoc: UnityDocument,
    parent: THREE.Object3D,
    parentPath: string,
    cameras: CameraInfo[],
  ): Promise<NodeInfo | null> {
    const goRef = asRef(tDoc.body.m_GameObject);
    const goDoc = goRef ? this.docs.get(goRef.fileID) : undefined;

    const name = goDoc ? str(goDoc.body.m_Name, 'GameObject') : 'Transform';
    const active = goDoc ? bool(goDoc.body.m_IsActive, true) : true;
    const layer = goDoc ? num(goDoc.body.m_Layer, 0) : 0;
    const tag = goDoc ? str(goDoc.body.m_TagString, 'Untagged') : 'Untagged';

    const obj = new THREE.Group();
    obj.name = name;
    obj.visible = active;

    // Unity -> three.js transform
    const lp = readVec3(mapOf(tDoc.body.m_LocalPosition));
    const lr = readQuat(mapOf(tDoc.body.m_LocalRotation));
    const ls = readVec3(mapOf(tDoc.body.m_LocalScale), 1, 1, 1);
    toThreePosition(lp, obj.position);
    toThreeQuaternion(lr, obj.quaternion);
    toThreeScale(ls, obj.scale);

    parent.add(obj);
    this.objectByFileID.set(tDoc.fileID, obj);
    this.report.gameObjects++;

    const path = parentPath ? `${parentPath}/${name}` : name;

    // ---- Components on this GameObject ----
    const components: ComponentData[] = [];
    if (goDoc) {
      for (const entry of arrayOf(goDoc.body.m_Component)) {
        const m = mapOf(entry);
        const ref = asRef(m?.component);
        if (!ref) continue;
        const cDoc = this.docs.get(ref.fileID);
        if (!cDoc) continue;

        const data: ComponentData = {
          classId: cDoc.classId,
          className: classNameOf(cDoc.classId),
          fileID: cDoc.fileID,
          body: cDoc.body,
        };
        if (cDoc.classId === 114) {
          data.scriptName = this.resolveScriptName(cDoc);
        }
        components.push(data);
        await this.applyComponent(cDoc, data, obj, goDoc, path, cameras);
      }
    }

    // ---- Children, in Unity's authored order ----
    const children: NodeInfo[] = [];
    for (const entry of arrayOf(tDoc.body.m_Children)) {
      const ref = asRef(entry);
      if (!ref) continue;
      const child = this.docs.get(ref.fileID);
      if (!child) continue;
      const node = await this.buildTransform(child, obj, path, cameras);
      if (node) children.push(node);
    }

    return { name, fileID: tDoc.fileID, active, layer, tag, components, object3d: obj, children };
  }

  /** Resolve a MonoBehaviour's script name from its MonoScript GUID. */
  private resolveScriptName(doc: UnityDocument): string {
    const ref = asRef(doc.body.m_Script);
    if (!ref?.guid) return 'MonoBehaviour';
    const known = UGUI_SCRIPT_GUIDS[ref.guid];
    if (known) return known;
    const path = assets.pathForGuid(ref.guid);
    if (path) {
      const file = path.split('/').pop() ?? '';
      return file.replace(/\.cs$/i, '');
    }
    const count = this.report.unmappedScripts.get(ref.guid) ?? 0;
    this.report.unmappedScripts.set(ref.guid, count + 1);
    return `Script(${ref.guid.slice(0, 8)})`;
  }

  private async applyComponent(
    doc: UnityDocument,
    data: ComponentData,
    obj: THREE.Object3D,
    goDoc: UnityDocument,
    path: string,
    cameras: CameraInfo[],
  ): Promise<void> {
    switch (doc.classId) {
      case 33: break;                                   // MeshFilter, handled with the renderer
      case 23: await this.applyMeshRenderer(doc, obj, goDoc); break;
      case 108: this.applyLight(doc, obj); break;
      case 20: this.applyCamera(doc, obj, path, cameras); break;
      case 223: this.report.canvases++; break;
      case 137: this.report.warnings.push(`SkinnedMeshRenderer not yet rendered: ${path}`); break;
      case 198: this.report.warnings.push(`ParticleSystem not yet rendered: ${path}`); break;
      default: break;
    }
    void data;
  }

  private async applyMeshRenderer(
    rendererDoc: UnityDocument,
    obj: THREE.Object3D,
    goDoc: UnityDocument,
  ): Promise<void> {
    // Find the sibling MeshFilter to get the geometry.
    let geometry: THREE.BufferGeometry | null = null;
    for (const entry of arrayOf(goDoc.body.m_Component)) {
      const ref = asRef(mapOf(entry)?.component);
      if (!ref) continue;
      const c = this.docs.get(ref.fileID);
      if (c?.classId !== 33) continue;
      const meshRef = asRef(c.body.m_Mesh);
      if (!meshRef) continue;
      const builtin = BUILTIN_MESHES[meshRef.fileID];
      if (builtin) geometry = builtin();
      else {
        const p = assets.pathForGuid(meshRef.guid);
        this.report.warnings.push(`mesh asset not yet loaded: ${p ?? meshRef.guid ?? meshRef.fileID}`);
      }
      break;
    }
    if (!geometry) return;

    // Materials
    const materials: THREE.Material[] = [];
    for (const entry of arrayOf(rendererDoc.body.m_Materials)) {
      const ref = asRef(entry);
      materials.push(await this.resolveMaterial(ref?.guid, ref?.fileID));
    }
    if (materials.length === 0) materials.push(fallbackMaterial());

    const mesh = new THREE.Mesh(geometry, materials.length === 1 ? materials[0] : materials);
    mesh.name = `${obj.name}__mesh`;
    mesh.castShadow = num(rendererDoc.body.m_CastShadows, 1) !== 0;
    mesh.receiveShadow = num(rendererDoc.body.m_ReceiveShadows, 1) !== 0;
    obj.add(mesh);
    this.report.meshes++;
  }

  private async resolveMaterial(guid: string | undefined, fileID?: number): Promise<THREE.Material> {
    const key = guid ?? `builtin:${fileID ?? 0}`;
    const hit = this.materialCache.get(key);
    if (hit) return hit;

    let mat: THREE.Material | null = null;
    if (guid) {
      const loaded = await assets.readYamlByGuid(guid);
      const doc = loaded?.file.documents.find((d) => d.classId === 21);
      if (doc) mat = await buildMaterial(doc.body);
    }
    if (!mat) mat = fallbackMaterial();
    this.materialCache.set(key, mat);
    return mat;
  }

  private applyLight(doc: UnityDocument, obj: THREE.Object3D): void {
    const type = num(doc.body.m_Type, 1);      // 0=Spot 1=Directional 2=Point 4=Area
    const intensity = num(doc.body.m_Intensity, 1);
    const range = num(doc.body.m_Range, 10);
    const color = toThreeColor(readColor(mapOf(doc.body.m_Color)));
    const shadows = num(mapOf(doc.body.m_Shadows)?.m_Type, 0) !== 0;

    let light: THREE.Light | null = null;
    if (type === 1) {
      const d = new THREE.DirectionalLight(color, intensity);
      // A Unity light points down its local +Z; after the mirror that is the
      // object's local -Z in three.js, which is exactly where a three.js light
      // target sits by default when placed one unit ahead.
      d.target.position.set(0, 0, -1);
      obj.add(d.target);
      light = d;
    } else if (type === 2) {
      // Unity point lights are range-limited with a different falloff model;
      // decay=2 plus distance=range is the closest physical match.
      light = new THREE.PointLight(color, intensity, range, 2);
    } else if (type === 0) {
      const outer = num(doc.body.m_SpotAngle, 30) * (Math.PI / 180);
      const s = new THREE.SpotLight(color, intensity, range, outer / 2, 0.3, 2);
      s.target.position.set(0, 0, -1);
      obj.add(s.target);
      light = s;
    }
    if (!light) return;

    light.castShadow = shadows;
    if (light.shadow) {
      light.shadow.mapSize.set(1024, 1024);
      light.shadow.bias = -0.0008;
    }
    obj.add(light);
    this.report.lights++;
  }

  private applyCamera(
    doc: UnityDocument,
    obj: THREE.Object3D,
    path: string,
    cameras: CameraInfo[],
  ): void {
    const orthographic = num(doc.body.orthographic, 0) !== 0;
    const near = num(doc.body['near clip plane'], 0.3);
    const far = num(doc.body['far clip plane'], 1000);

    let cam: THREE.PerspectiveCamera | THREE.OrthographicCamera;
    if (orthographic) {
      const size = num(doc.body['orthographic size'], 5);
      cam = new THREE.OrthographicCamera(-size, size, size, -size, near, far);
    } else {
      cam = new THREE.PerspectiveCamera(
        unityFovToThree(num(doc.body['field of view'], 60)), 16 / 9, near, far);
    }
    cam.name = `${obj.name}__camera`;
    obj.add(cam);

    // Background: Unity clearFlags 2 = solid colour.
    cameras.push({ name: obj.name, path, camera: cam, depth: num(doc.body.m_Depth, 0) });
    this.report.cameras++;
  }
}

/** Convenience wrapper. */
export async function buildScene(scenePath: string): Promise<BuiltScene> {
  return new SceneBuilder().build(scenePath);
}
