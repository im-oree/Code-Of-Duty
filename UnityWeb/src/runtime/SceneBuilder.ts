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
import { assets } from '../unity/AssetDatabase.ts';
import {
  num, str, bool, mapOf, arrayOf, asRef,
  type UnityDocument, type UnityFile, type UnityMap,
} from '../unity/YamlParser.ts';
import { classNameOf, TRANSFORM_CLASS_IDS, UGUI_SCRIPT_GUIDS } from '../unity/ClassIds.ts';
import {
  readVec3, readQuat, toThreePosition, toThreeQuaternion, toThreeScale,
  unityFovToThree, toThreeColor, readColor,
} from '../unity/Coords.ts';
import { buildMaterial, fallbackMaterial } from '../render/MaterialMapper.ts';
import { applyRenderSettings } from '../render/Environment.ts';
import { expandPrefabInstances } from '../unity/PrefabExpander.ts';
import { resolveMesh, bindToSceneBones, clearModelCache, type ResolveStrategy } from '../render/ModelLoader.ts';
import {
  type LayoutNode, type Rect, type CanvasScalerSettings,
  canvasScaleFactor, readCanvasScaler, solveRect, intersectRect,
} from '../ui/UguiLayout.ts';

export interface ComponentData {
  classId: number;
  className: string;
  /** Verbatim Unity anchor. See NodeInfo.fileID. */
  fileID: string;
  scriptName?: string;
  body: UnityMap;
}

export interface NodeInfo {
  name: string;
  /** Verbatim Unity anchor. A string because 64-bit ids do not survive a double. */
  fileID: string;
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
  /** Meshes loaded out of FBX/glTF/OBJ files, as opposed to built-in primitives. */
  modelMeshes: number;
  skinnedMeshes: number;
  uiWidgets: number;
  /** PrefabInstance documents assembled into real objects before the build. */
  prefabInstances: number;
  /** Documents those instances contributed. */
  prefabObjects: number;
  warnings: string[];
  unmappedScripts: Map<string, number>;
  /** How each model mesh reference was resolved — see ModelLoader. */
  meshResolution: Map<ResolveStrategy, number>;
}

/**
 * A Unity Animator found in the scene, with everything needed to drive it.
 *
 * The clips usually live inside the model file rather than as separate assets,
 * so we record which model the Animator's skinned meshes came from — that is
 * where `loadClips` will find the takes.
 */
export interface AnimatorRef {
  path: string;
  object: THREE.Object3D;
  controllerGuid: string | null;
  avatarGuid: string | null;
  /** Model file supplying the animation takes, if one could be determined. */
  modelPath: string | null;
  applyRootMotion: boolean;
}

/** A screen-space canvas, kept unsolved until the viewport size is known. */
export interface UiCanvasInfo {
  name: string;
  rootFileID: number;
  renderMode: number;
  scaler: CanvasScalerSettings;
  sortOrder: number;
  /**
   * True when this is a root RectTransform with no Canvas ancestor. Unity
   * renders nothing for those, so neither do we by default — but the viewer can
   * opt in to previewing them, which is how you see what the screen is supposed
   * to look like while the parenting is still broken.
   */
  orphan: boolean;
}

export interface BuiltScene {
  scene: THREE.Scene;
  root: THREE.Group;
  nodes: NodeInfo[];
  cameras: CameraInfo[];
  report: BuildReport;
  uiCanvases: UiCanvasInfo[];
  animators: AnimatorRef[];
  /** Solve every screen-space canvas for a given viewport, in draw order. */
  layoutUi(screenW: number, screenH: number, includeOrphans?: boolean): LayoutNode[];
}

/** RectTransform is classID 224; only those participate in uGUI layout. */
function doc224(d: UnityDocument): boolean { return d.classId === 224; }

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
  private docs = new Map<string, UnityDocument>();
  private gameObjectOfComponent = new Map<number, number>();
  private transformOfGameObject = new Map<number, UnityDocument>();
  private objectByFileID = new Map<number, THREE.Object3D>();
  private materialCache = new Map<string, THREE.Material>();
  /** RectTransform bookkeeping, used to solve uGUI layout after the build. */
  private rectInfo = new Map<number, {
    tDoc: UnityDocument; name: string; active: boolean;
    components: Map<string, UnityMap>;
  }>();
  private uiCanvases: UiCanvasInfo[] = [];
  private animators: AnimatorRef[] = [];
  /** Model path each object subtree's skinned meshes came from. */
  private modelForObject = new Map<THREE.Object3D, string>();
  /** SkinnedMeshRenderers are resolved after the hierarchy exists, so bones are findable. */
  private pendingSkins: Array<{ doc: UnityDocument; obj: THREE.Object3D; goDoc: UnityDocument; path: string }> = [];

  private report: BuildReport = {
    gameObjects: 0, meshes: 0, lights: 0, cameras: 0, canvases: 0,
    modelMeshes: 0, skinnedMeshes: 0, uiWidgets: 0,
    prefabInstances: 0, prefabObjects: 0,
    warnings: [], unmappedScripts: new Map(), meshResolution: new Map(),
  };

  async build(scenePath: string): Promise<BuiltScene> {
    clearModelCache();
    this.file = await assets.readYaml(scenePath);

    // A scene stores prefabs as a single PrefabInstance plus overrides, so the
    // objects have to be assembled before anything can be read literally.
    // Idempotent: re-running finds no PrefabInstance left and returns early,
    // which matters because AssetDatabase caches parsed files.
    const expansion = await expandPrefabInstances(this.file, async (guid) => {
      const path = assets.pathForGuid(guid);
      if (!path) return null;
      // An imported model is a prefab asset as far as Unity is concerned, but
      // it is not YAML -- do not hand it to the parser.
      if (/\.(fbx|obj|gltf|glb|dae|blend)$/i.test(path)) return { kind: 'model', path };
      const loaded = await assets.readYamlByGuid(guid);
      return loaded ? { kind: 'prefab', file: loaded.file } : null;
    });
    if (expansion.instances > 0) {
      this.report.prefabInstances = expansion.instances;
      this.report.prefabObjects = expansion.added;
    }
    for (const w of expansion.warnings) this.report.warnings.push(w);
    if (expansion.modelInstances.length > 0) {
      const unique = [...new Set(expansion.modelInstances)];
      this.report.warnings.push(
        `${expansion.modelInstances.length} model-prefab instance(s) not assembled `
        + `(objects are synthesised by Unity's importer, not stored in a file): `
        + unique.join(', '),
      );
    }

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

    // ---- Pass 4: skinning, now that every bone Transform exists ----
    for (const pending of this.pendingSkins) {
      await this.applySkinnedMeshRenderer(pending.doc, pending.obj, pending.goDoc, pending.path);
    }

    // A scene with no light at all renders black; say so rather than show a void.
    if (this.report.lights === 0 && !scene.environment) {
      this.report.warnings.push('scene contains no lights and no environment map');
    }

    this.detectOrphanUi();

    // Animators without an Avatar still need a clip source; use the model any
    // skinned mesh below them was loaded from.
    for (const animator of this.animators) {
      if (animator.modelPath) continue;
      animator.object.traverse((child) => {
        if (animator.modelPath) return;
        const found = this.modelForObject.get(child);
        if (found) animator.modelPath = found;
      });
    }

    const layoutUi = (screenW: number, screenH: number, includeOrphans = false) =>
      this.layoutUi(screenW, screenH, includeOrphans);
    return {
      scene, root, nodes, cameras, report: this.report,
      uiCanvases: this.uiCanvases, animators: this.animators, layoutUi,
    };
  }

  /**
   * Solve every screen-space canvas for a viewport size.
   *
   * Deferred until render time because CanvasScaler's result depends on the
   * actual screen dimensions, and the viewer can be resized freely.
   */
  private layoutUi(screenW: number, screenH: number, includeOrphans = false): LayoutNode[] {
    const out: LayoutNode[] = [];
    const ordered = [...this.uiCanvases].sort((a, b) => a.sortOrder - b.sortOrder);

    for (const canvasInfo of ordered) {
      if (canvasInfo.orphan && !includeOrphans) continue;
      if (canvasInfo.renderMode !== 0) continue; // world/camera space: see DIVERGENCES
      const info = this.rectInfo.get(canvasInfo.rootFileID);
      if (!info) continue;

      const scale = canvasScaleFactor(canvasInfo.scaler, screenW, screenH);
      // Unity sizes the root canvas rect in canvas units, then scales it up.
      const screenRect: Rect = { x: 0, y: 0, width: screenW / scale, height: screenH / scale };

      // A real Canvas is driven to screen size by Unity, so its own anchors are
      // ignored. An orphaned panel is not — it still has meaningful anchors and
      // must be solved against the screen, or everything inside it is offset.
      const rootRect = canvasInfo.orphan
        ? solveRect(info.tDoc.body, screenRect, 1)
        : screenRect;

      const node = this.buildLayoutNode(canvasInfo.rootFileID, rootRect, 1, 0, 1, true, null, 0, scale);
      if (node) out.push(node);
    }
    return out;
  }

  /**
   * Find root RectTransforms that have no Canvas above them.
   *
   * This is a real and easily-missed authoring fault: in Unity such a subtree is
   * completely invisible with no error, because uGUI only draws what a Canvas
   * owns. Reporting it is more useful than rendering it, so we do both — a loud
   * warning, plus an opt-in preview.
   */
  private detectOrphanUi(): void {
    const canvasRoots = new Set(this.uiCanvases.map((c) => c.rootFileID));
    const template = this.uiCanvases.find((c) => !c.orphan);

    for (const [fileID, info] of this.rectInfo) {
      if (canvasRoots.has(fileID)) continue;
      const father = asRef(info.tDoc.body.m_Father);
      if (father && father.fileID !== 0) continue; // has a parent; inherits its canvas

      this.uiCanvases.push({
        name: info.name,
        rootFileID: fileID,
        renderMode: 0,
        // Preview them with the real canvas's scaler so sizes are meaningful.
        scaler: template?.scaler ?? readCanvasScaler(undefined),
        sortOrder: 1000,
        orphan: true,
      });
      this.report.warnings.push(
        `UI not rendered: "${info.name}" is a root RectTransform with no Canvas parent — ` +
        'Unity draws nothing for this subtree',
      );
    }

    if (this.uiCanvases.some((c) => c.orphan)) {
      const emptyCanvases = this.uiCanvases
        .filter((c) => !c.orphan && (this.rectInfo.get(c.rootFileID)?.tDoc.body.m_Children as unknown[] | undefined)?.length !== undefined
          && arrayOf(this.rectInfo.get(c.rootFileID)!.tDoc.body.m_Children).length === 0)
        .map((c) => c.name);
      if (emptyCanvases.length) {
        this.report.warnings.push(
          `Canvas ${emptyCanvases.map((n) => `"${n}"`).join(', ')} has no children while ` +
          `${this.uiCanvases.filter((c) => c.orphan).length} UI subtree(s) sit at the scene root — ` +
          'they need reparenting under the Canvas',
        );
      }
    }
  }

  private buildLayoutNode(
    fileID: number,
    rect: Rect,
    scale: number,
    rotation: number,
    alpha: number,
    active: boolean,
    clip: Rect | null,
    depth: number,
    canvasScale: number,
  ): LayoutNode | null {
    const info = this.rectInfo.get(fileID);
    if (!info) return null;

    const node: LayoutNode = {
      fileID: String(fileID),
      name: info.name,
      // Canvas units -> screen pixels happens once, here.
      rect: {
        x: rect.x * canvasScale, y: rect.y * canvasScale,
        width: rect.width * canvasScale, height: rect.height * canvasScale,
      },
      scale, rotation, alpha,
      canvasScale,
      active: active && info.active,
      clip,
      depth,
      children: [],
      components: info.components,
    };
    this.report.uiWidgets++;

    // CanvasGroup multiplies alpha down the subtree and can disable it entirely.
    const group = info.components.get('CanvasGroup');
    let childAlpha = alpha;
    if (group) {
      childAlpha *= num(group.m_Alpha, 1);
      node.alpha = childAlpha;
    }

    // RectMask2D / Mask clip their descendants to their own rect.
    let childClip = clip;
    if (info.components.has('UnityEngine.UI.RectMask2D') || info.components.has('UnityEngine.UI.Mask')) {
      childClip = clip ? intersectRect(clip, node.rect) : node.rect;
    }

    const childScale = scale;
    for (const entry of arrayOf(info.tDoc.body.m_Children)) {
      const ref = asRef(entry);
      if (!ref) continue;
      const childInfo = this.rectInfo.get(ref.fileID);
      if (!childInfo) continue;

      const localScale = mapOf(childInfo.tDoc.body.m_LocalScale);
      const sx = num(localScale?.x, 1);
      const childRect = solveRect(childInfo.tDoc.body, rect, 1);

      // Apply the child's own scale about its rect centre, the way uGUI does.
      let scaled = childRect;
      if (sx !== 1) {
        const cx = childRect.x + childRect.width / 2;
        const cy = childRect.y + childRect.height / 2;
        scaled = {
          x: cx - (childRect.width * sx) / 2,
          y: cy - (childRect.height * sx) / 2,
          width: childRect.width * sx,
          height: childRect.height * sx,
        };
      }

      const rotZ = this.zRotationOf(childInfo.tDoc);
      const child = this.buildLayoutNode(
        ref.fileID, scaled, childScale * sx, rotation + rotZ,
        childAlpha, node.active, childClip, depth + 1, canvasScale,
      );
      if (child) node.children.push(child);
    }

    return node;
  }

  /** uGUI only meaningfully rotates about Z; pull that out of the quaternion. */
  private zRotationOf(tDoc: UnityDocument): number {
    const q = mapOf(tDoc.body.m_LocalRotation);
    if (!q) return 0;
    const x = num(q.x, 0), y = num(q.y, 0), z = num(q.z, 0), w = num(q.w, 1);
    return Math.atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
  }

  /** Recursively build one transform and its children. */
  private async buildTransform(
    tDoc: UnityDocument,
    parent: THREE.Object3D,
    parentPath: string,
    cameras: CameraInfo[],
  ): Promise<NodeInfo | null> {
    const goRef = asRef(tDoc.body.m_GameObject);
    const goDoc = goRef ? this.docs.get(goRef.fileIDText) : undefined;

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
        const cDoc = this.docs.get(ref.fileIDText);
        if (!cDoc) continue;

        const data: ComponentData = {
          classId: cDoc.classId,
          className: classNameOf(cDoc.classId),
          fileID: cDoc.id,
          body: cDoc.body,
        };
        if (cDoc.classId === 114) {
          data.scriptName = this.resolveScriptName(cDoc);
        }
        components.push(data);
        await this.applyComponent(cDoc, data, obj, goDoc, path, cameras);
      }
    }

    // RectTransforms feed the uGUI layout pass; keep their components indexed
    // by resolved type name so the painter can look up Image/Text directly.
    if (doc224(tDoc)) {
      const byType = new Map<string, UnityMap>();
      for (const c of components) {
        byType.set(c.scriptName ?? c.className, c.body);
      }
      this.rectInfo.set(tDoc.fileID, { tDoc, name, active, components: byType });
    }

    // ---- Children, in Unity's authored order ----
    const children: NodeInfo[] = [];
    for (const entry of arrayOf(tDoc.body.m_Children)) {
      const ref = asRef(entry);
      if (!ref) continue;
      const child = this.docs.get(ref.fileIDText);
      if (!child) continue;
      const node = await this.buildTransform(child, obj, path, cameras);
      if (node) children.push(node);
    }

    return { name, fileID: tDoc.id, active, layer, tag, components, object3d: obj, children };
  }

  /**
   * Resolve a MonoBehaviour's type name.
   *
   * Unity serializes `m_EditorClassIdentifier` as "Assembly::Namespace.Type" for
   * any script that lives in a named assembly — which covers every package type
   * (UnityEngine.UI.Image, TMPro.TextMeshProUGUI, ...). That is authoritative and
   * needs no GUID table, so we trust it first and only fall back to resolving the
   * MonoScript GUID for this project's own Assembly-CSharp scripts.
   */
  private resolveScriptName(doc: UnityDocument): string {
    const editorId = str(doc.body.m_EditorClassIdentifier, '');
    if (editorId) {
      const typeName = editorId.includes('::') ? editorId.split('::')[1] : editorId;
      if (typeName) return typeName;
    }
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
      case 223: this.registerCanvas(doc, obj, goDoc); break;
      case 95: this.registerAnimator(doc, obj, path); break;
      case 137: this.pendingSkins.push({ doc, obj, goDoc, path }); break;
      case 198: this.report.warnings.push(`ParticleSystem not yet rendered: ${path}`); break;
      default: break;
    }
    void data;
  }

  /**
   * Record an Animator. The model supplying its clips is resolved after the
   * build, once we know which model each skinned mesh underneath it used.
   */
  private registerAnimator(doc: UnityDocument, obj: THREE.Object3D, path: string): void {
    const controller = asRef(doc.body.m_Controller);
    const avatar = asRef(doc.body.m_Avatar);
    this.animators.push({
      path,
      object: obj,
      controllerGuid: controller?.guid ?? null,
      avatarGuid: avatar?.guid ?? null,
      // An Avatar is created by the model importer, so its GUID names the model
      // file — which is also where the animation takes are.
      modelPath: avatar?.guid ? assets.pathForGuid(avatar.guid) : null,
      applyRootMotion: num(doc.body.m_ApplyRootMotion, 0) !== 0,
    });
  }

  /** Record a Canvas plus its CanvasScaler so layout can be solved later. */
  private registerCanvas(doc: UnityDocument, obj: THREE.Object3D, goDoc: UnityDocument): void {
    this.report.canvases++;

    let scalerBody: UnityMap | undefined;
    for (const entry of arrayOf(goDoc.body.m_Component)) {
      const ref = asRef(mapOf(entry)?.component);
      if (!ref) continue;
      const c = this.docs.get(ref.fileIDText);
      if (c?.classId !== 114) continue;
      if (this.resolveScriptName(c) === 'UnityEngine.UI.CanvasScaler') { scalerBody = c.body; break; }
    }

    const transform = this.transformOfGameObject.get(goDoc.fileID);
    if (!transform) return;

    this.uiCanvases.push({
      name: obj.name,
      rootFileID: transform.fileID,
      renderMode: num(doc.body.m_RenderMode, 0),
      scaler: readCanvasScaler(scalerBody),
      sortOrder: num(doc.body.m_SortingOrder, 0),
      orphan: false,
    });
  }

  /**
   * SkinnedMeshRenderer.
   *
   * Unity stores `m_Bones` as fileIDs pointing at Transforms in this same scene,
   * so the skeleton is already part of the graph we built — including whatever
   * pose the artist saved. We bind the FBX's skin weights to those live objects
   * rather than to the copy inside the model file, which is what makes the menu
   * operator appear in its authored pose instead of T-pose.
   */
  private async applySkinnedMeshRenderer(
    rendererDoc: UnityDocument,
    obj: THREE.Object3D,
    _goDoc: UnityDocument,
    path: string,
  ): Promise<void> {
    const meshRef = asRef(rendererDoc.body.m_Mesh);
    if (!meshRef?.guid) {
      this.report.warnings.push(`SkinnedMeshRenderer has no mesh: ${path}`);
      return;
    }

    const resolved = await resolveMesh(meshRef.guid, meshRef.fileID, obj.name);
    this.countResolution(resolved.strategy);
    if (!resolved.mesh) {
      this.report.warnings.push(`skinned mesh not found in ${resolved.modelPath || meshRef.guid}: ${path}`);
      return;
    }

    const materials = await this.readMaterials(rendererDoc);

    const bones: THREE.Object3D[] = [];
    for (const entry of arrayOf(rendererDoc.body.m_Bones)) {
      const ref = asRef(entry);
      const bone = ref ? this.objectByFileID.get(ref.fileID) : undefined;
      if (bone) bones.push(bone);
    }

    if (bones.length > 0 && resolved.mesh.skinned) {
      const bound = bindToSceneBones(bones, resolved.mesh);
      if (bound) {
        const skinned = new THREE.SkinnedMesh(
          resolved.mesh.geometry, materials.length === 1 ? materials[0] : materials,
        );
        skinned.name = `${obj.name}__skin`;
        skinned.castShadow = true;
        skinned.receiveShadow = true;
        // Bones live elsewhere in the graph, so this mesh must not also inherit
        // its own parent's transform on top of the skinning result.
        skinned.bindMode = THREE.DetachedBindMode;
        skinned.frustumCulled = false; // deformed bounds are not the rest bounds
        obj.add(skinned);
        skinned.bind(bound.skeleton, resolved.mesh.bindMatrix ?? new THREE.Matrix4());
        this.modelForObject.set(obj, resolved.modelPath);

        this.report.skinnedMeshes++;
        this.report.meshes++;
        if (!bound.exactBindPoses) {
          this.report.warnings.push(
            `skinned mesh bind poses did not match bone count ` +
            `(${bones.length} scene bones vs ${resolved.mesh.boneInverses?.length ?? 0} in model), ` +
            `using current pose as bind pose: ${path}`,
          );
        }
        return;
      }
    }

    // No usable skeleton: still show the geometry so the silhouette is visible,
    // and say plainly that it is unskinned.
    const fallback = new THREE.Mesh(
      resolved.mesh.geometry, materials.length === 1 ? materials[0] : materials,
    );
    fallback.name = `${obj.name}__unskinned`;
    obj.add(fallback);
    this.report.meshes++;
    this.report.warnings.push(
      `skinned mesh rendered unskinned (${bones.length} bones, skinned=${resolved.mesh.skinned}): ${path}`,
    );
  }

  private countResolution(strategy: ResolveStrategy): void {
    this.report.meshResolution.set(strategy, (this.report.meshResolution.get(strategy) ?? 0) + 1);
  }

  private async readMaterials(rendererDoc: UnityDocument): Promise<THREE.Material[]> {
    const materials: THREE.Material[] = [];
    for (const entry of arrayOf(rendererDoc.body.m_Materials)) {
      const ref = asRef(entry);
      materials.push(await this.resolveMaterial(ref?.guid, ref?.fileID));
    }
    if (materials.length === 0) materials.push(fallbackMaterial());
    return materials;
  }

  private async applyMeshRenderer(
    rendererDoc: UnityDocument,
    obj: THREE.Object3D,
    goDoc: UnityDocument,
  ): Promise<void> {
    // Find the sibling MeshFilter to get the geometry.
    let geometry: THREE.BufferGeometry | null = null;
    // Unity bakes the importer's scale factor into the mesh asset; we apply it
    // to the instance instead, so the shared geometry stays reusable.
    let modelScale = 1;
    for (const entry of arrayOf(goDoc.body.m_Component)) {
      const ref = asRef(mapOf(entry)?.component);
      if (!ref) continue;
      const c = this.docs.get(ref.fileIDText);
      if (c?.classId !== 33) continue;
      const meshRef = asRef(c.body.m_Mesh);
      if (!meshRef) continue;
      const builtin = BUILTIN_MESHES[meshRef.fileID];
      if (builtin) {
        geometry = builtin();
      } else if (meshRef.guid) {
        const resolved = await resolveMesh(meshRef.guid, meshRef.fileID, obj.name);
        this.countResolution(resolved.strategy);
        if (resolved.mesh) {
          geometry = resolved.mesh.geometry;
          modelScale = resolved.importScale;
          if (resolved.needsModelMatrix) {
            // The scene transform we are hanging this on does not stand for
            // the model node the geometry came from, so nothing else will
            // supply the node's place inside the model. Bake it in here.
            geometry = geometry.clone();
            geometry.applyMatrix4(resolved.mesh.modelMatrix);
          }
          this.report.modelMeshes++;
          if (resolved.strategy === 'stable-index' || resolved.strategy === 'largest-mesh') {
            // Honest about guesses: the fileID -> sub-mesh mapping is not
            // recoverable from these .meta files (empty internalIDToNameTable).
            this.report.warnings.push(
              `mesh matched by ${resolved.strategy} (name "${obj.name}" not in ${resolved.modelPath})`,
            );
          }
        } else {
          this.report.warnings.push(`mesh not found: ${resolved.modelPath || meshRef.guid}`);
        }
      } else {
        this.report.warnings.push(`unresolvable mesh reference fileID=${meshRef.fileID}`);
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
    if (modelScale !== 1) mesh.scale.setScalar(modelScale);
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
