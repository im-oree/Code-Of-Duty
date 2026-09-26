/**
 * Unity Web Clone — editor shell and agent control surface.
 *
 * Two audiences share one page, on purpose:
 *
 *   - a person, who gets a Unity-shaped editor (Hierarchy, Inspector, Project,
 *     Console, Scene view) to look around a scene;
 *   - an agent, which drives the same page through `window.uw` and screenshots
 *     it headlessly.
 *
 * They must be the same page. If the capture path rendered through a separate
 * code path, "it looks right in the screenshot" would stop being evidence about
 * what the user sees.
 */

import * as THREE from 'three';
import { assets } from './unity/AssetDatabase.ts';
import { buildScene, type BuiltScene, type NodeInfo } from './runtime/SceneBuilder.ts';
import { UguiRenderer } from './ui/UguiRenderer.ts';
import { HierarchyPane } from './editor/Hierarchy.ts';
import { InspectorPane } from './editor/Inspector.ts';
import { ProjectBrowser, type ProjectEntry } from './editor/ProjectBrowser.ts';
import { iconForFile, namedIcon } from './editor/Icons.ts';
import { ConsolePane } from './editor/Console.ts';
import { loadClips } from './anim/ClipLoader.ts';
import { loadController } from './anim/AnimatorController.ts';
import { AnimatorInstance, AnimatorSet } from './anim/AnimatorRuntime.ts';

/* ------------------------------------------------------------------ */
/* Elements                                                            */
/* ------------------------------------------------------------------ */

const viewport = document.getElementById('viewport')!;
const overlay = document.getElementById('overlay')!;
const gizmoEl = document.getElementById('gizmo')!;
const statusEl = document.getElementById('status')!;
const statusText = document.getElementById('status-text')!;
const statsEl = document.getElementById('stats')!;
const scenePicker = document.getElementById('scene-picker') as HTMLSelectElement;
const cameraPicker = document.getElementById('camera-picker') as HTMLSelectElement;
const fovInput = document.getElementById('fov-input') as HTMLInputElement;
const viewModeEl = document.getElementById('view-mode')!;

function setStatus(text: string, error = false) {
  statusEl.classList.remove('hidden');
  statusText.textContent = text;
  statusText.className = error ? 'bad' : '';
  statusEl.querySelector('.spin')?.classList.toggle('hidden', error);
}
function hideStatus() { statusEl.classList.add('hidden'); }

/* ------------------------------------------------------------------ */
/* Renderer and cameras                                                */
/* ------------------------------------------------------------------ */

const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.NeutralToneMapping;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
viewport.appendChild(renderer.domElement);

const orbitCam = new THREE.PerspectiveCamera(60, 16 / 9, 0.02, 3000);
let activeCamera: THREE.Camera = orbitCam;
let usingOrbit = true;

const orbit = { yaw: 0.7, pitch: 0.28, dist: 12, target: new THREE.Vector3(0, 1, 0) };

function updateOrbit() {
  const cp = Math.cos(orbit.pitch), sp = Math.sin(orbit.pitch);
  orbitCam.position.set(
    orbit.target.x + orbit.dist * cp * Math.sin(orbit.yaw),
    orbit.target.y + orbit.dist * sp,
    orbit.target.z + orbit.dist * cp * Math.cos(orbit.yaw),
  );
  orbitCam.lookAt(orbit.target);
}

const ugui = new UguiRenderer();
let uiDirty = true;
let uiPainting = false;
let showUi = true;
let showOrphanUi = true;
let showGizmos = true;
let wireframe = false;
let paused = false;

let built: BuiltScene | null = null;
let selectedNode: NodeInfo | null = null;
const animators = new AnimatorSet();
const clock = new THREE.Clock();

/** Editor-only helpers (grid, light markers) kept out of the built scene. */
const gizmoGroup = new THREE.Group();
gizmoGroup.name = '__gizmos';

function resize() {
  const w = viewport.clientWidth || 1280;
  const h = viewport.clientHeight || 720;
  renderer.setSize(w, h, false);
  ugui.resize(w, h, Math.min(window.devicePixelRatio || 1, 2));
  uiDirty = true;
  const aspect = w / h;
  for (const cam of [orbitCam, activeCamera]) {
    if (cam instanceof THREE.PerspectiveCamera) {
      cam.aspect = aspect;
      cam.updateProjectionMatrix();
    }
  }
}
window.addEventListener('resize', resize);

/* ------------------------------------------------------------------ */
/* Editor panes                                                        */
/* ------------------------------------------------------------------ */

const consolePane = new ConsolePane();
const inspector = new InspectorPane();
const hierarchy = new HierarchyPane({
  onSelect: (node) => {
    selectedNode = node;
    inspector.show(node);
  },
});
const project = new ProjectBrowser({
  onOpenScene: (path) => { void loadScene(path); },
  onSelectAsset: (entry) => showAssetInInspector(entry),
});

async function showAssetInInspector(entry: ProjectEntry) {
  const body = document.getElementById('inspector-body')!;
  body.innerHTML =
    `<div class="insp-head"><span class="ic">${iconForFile(entry.name, entry.dir, 16)}</span>` +
    `<input class="nm" readonly value="${entry.name.replace(/"/g, '&quot;')}" /></div>` +
    '<div class="insp-sub"><span>Asset</span></div>';

  const card = document.createElement('div');
  card.className = 'comp';
  card.innerHTML = `<div class="comp-head"><span class="tw"> </span>` +
                   `<span class="ic">${namedIcon('asset')}</span>` +
                   '<span class="ti">Import Info</span></div>';
  const cb = document.createElement('div');
  cb.className = 'comp-body';
  cb.innerHTML =
    `<div class="prow"><span class="k">Path</span><span class="v">${entry.path}</span></div>` +
    `<div class="prow"><span class="k">GUID</span><span class="v">${entry.guid ?? '—'}</span></div>` +
    `<div class="prow"><span class="k">Size</span><span class="v">${entry.size} bytes</span></div>`;
  card.appendChild(cb);
  body.appendChild(card);

  // Text assets get a source preview, which is often the fastest way to check
  // what a .mat or .controller actually contains.
  if (/\.(unity|prefab|mat|asset|controller|anim|cs|json|txt|md|shader|asmdef)$/i.test(entry.name)
      && entry.size < 400_000) {
    try {
      const text = await assets.readText(entry.path);
      const pre = document.createElement('div');
      pre.className = 'comp';
      pre.innerHTML = `<div class="comp-head"><span class="tw"> </span>` +
                      `<span class="ic">${namedIcon('script')}</span>` +
                      '<span class="ti">Source</span></div>';
      const pb = document.createElement('div');
      pb.className = 'comp-body';
      pb.innerHTML = `<pre style="font:10px/1.5 var(--mono);white-space:pre-wrap;color:#b9b9b9;` +
                     `max-height:420px;overflow:auto">${
                       text.slice(0, 20000).replace(/[&<>]/g, (c) =>
                         ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]!))
                     }</pre>`;
      pre.appendChild(pb);
      body.appendChild(pre);
    } catch { /* binary or unreadable */ }
  }
}

/* ------------------------------------------------------------------ */
/* Scene loading                                                       */
/* ------------------------------------------------------------------ */

async function loadScene(path: string) {
  setStatus(`Building ${path.split('/').pop()}…`);
  animators.clear();
  inspector.clear();
  selectedNode = null;

  try {
    const t0 = performance.now();
    built = await buildScene(path);
    const ms = Math.round(performance.now() - t0);

    built.scene.add(gizmoGroup);
    buildGizmos();

    // Cameras
    cameraPicker.innerHTML = '<option value="__orbit">Scene camera (orbit)</option>';
    built.cameras.sort((a, b) => b.depth - a.depth).forEach((c, i) => {
      const o = document.createElement('option');
      o.value = String(i);
      o.textContent = `${c.name}`;
      cameraPicker.appendChild(o);
    });

    hierarchy.setScene(built.nodes);
    consolePane.setWarnings(built.report.warnings);

    await setupAnimators();

    if (scenePicker.value !== path) scenePicker.value = path;
    selectCamera(built.cameras.length ? '0' : '__orbit');
    cameraPicker.value = built.cameras.length ? '0' : '__orbit';
    if (!built.cameras.length) frameAll();

    applyWireframe();
    uiDirty = true;

    const r = built.report;
    statsEl.textContent =
      `${r.gameObjects} obj · ${r.meshes} mesh · ${r.skinnedMeshes} skin · ${r.lights} light · ` +
      `${r.canvases} canvas · ${animators.all().length} animator · ${r.warnings.length} warn · ${ms} ms`;

    const firstWarn = r.warnings[0];
    overlay.innerHTML =
      `<div>${path}</div>` +
      (firstWarn
        ? `<div class="w">${r.warnings.length} warning(s) — ${firstWarn.slice(0, 110)}</div>`
        : '<div style="color:var(--ok)">no warnings</div>');

    hideStatus();
    (window as unknown as Record<string, unknown>).__uwReady = true;
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    setStatus(`Failed: ${message}`, true);
    consolePane.log('err', `Scene build failed: ${message}`);
    (window as unknown as Record<string, unknown>).__uwError = message;
  }
}

/** Create an AnimatorInstance per Unity Animator and start its default state. */
async function setupAnimators() {
  if (!built) return;
  for (const ref of built.animators) {
    const clips = ref.modelPath ? await loadClips(ref.modelPath) : [];
    const controllerPath = ref.controllerGuid ? assets.pathForGuid(ref.controllerGuid) : null;
    const controller = controllerPath ? await loadController(controllerPath) : null;

    const instance = new AnimatorInstance(
      ref.object, ref.path, clips, controller, controllerPath, ref.applyRootMotion,
    );
    animators.add(instance);

    if (clips.length === 0) {
      consolePane.log('warn',
        `Animator "${ref.path}" has no clips` +
        `${ref.modelPath ? ` in ${ref.modelPath}` : ' (no model resolved)'}`);
    } else if (!instance.playDefault()) {
      consolePane.log('warn', `Animator "${ref.path}" could not start a default state`);
    }
  }
}

function selectCamera(value: string) {
  if (!built) return;
  if (value === '__orbit') {
    usingOrbit = true;
    activeCamera = orbitCam;
    viewModeEl.textContent = 'Scene';
  } else {
    const info = built.cameras[Number(value)];
    if (info) {
      usingOrbit = false;
      activeCamera = info.camera;
      viewModeEl.textContent = `Game · ${info.name}`;
    }
  }
  if (activeCamera instanceof THREE.PerspectiveCamera) {
    fovInput.value = String(Math.round(activeCamera.fov));
  }
  resize();
}

function frameAll() {
  if (!built) return;
  const box = new THREE.Box3().setFromObject(built.root);
  if (box.isEmpty()) return;
  const size = box.getSize(new THREE.Vector3());
  const centre = box.getCenter(new THREE.Vector3());
  orbit.target.copy(centre);
  const radius = Math.max(size.x, size.y, size.z) * 0.5 || 1;
  orbit.dist = (radius / Math.tan((orbitCam.fov * Math.PI) / 360)) * 1.6;
  usingOrbit = true;
  activeCamera = orbitCam;
  cameraPicker.value = '__orbit';
  updateOrbit();
}

/** Grid and light markers, drawn only in the Scene view like Unity's gizmos. */
function buildGizmos() {
  gizmoGroup.clear();
  const grid = new THREE.GridHelper(40, 40, 0x555555, 0x333333);
  (grid.material as THREE.Material).transparent = true;
  (grid.material as THREE.Material).opacity = 0.32;
  gizmoGroup.add(grid);

  built?.root.traverse((o) => {
    if (o instanceof THREE.DirectionalLight || o instanceof THREE.PointLight
        || o instanceof THREE.SpotLight) {
      const s = new THREE.Mesh(
        new THREE.SphereGeometry(0.09, 8, 6),
        new THREE.MeshBasicMaterial({ color: (o as THREE.Light).color, wireframe: true }),
      );
      o.getWorldPosition(s.position);
      gizmoGroup.add(s);
    }
  });
  gizmoGroup.visible = showGizmos && usingOrbit;
}

function applyWireframe() {
  built?.root.traverse((o) => {
    const m = (o as THREE.Mesh).material;
    if (!m) return;
    for (const mat of Array.isArray(m) ? m : [m]) {
      if ('wireframe' in mat) (mat as THREE.MeshStandardMaterial).wireframe = wireframe;
    }
  });
}

/* ------------------------------------------------------------------ */
/* Rendering                                                           */
/* ------------------------------------------------------------------ */

async function repaintUi(): Promise<void> {
  if (!built || uiPainting) return;
  uiPainting = true;
  try {
    const w = viewport.clientWidth || 1280;
    const h = viewport.clientHeight || 720;
    await ugui.paint(built.layoutUi(w, h, showOrphanUi), w, h);
  } catch (err) {
    console.warn('[ui] paint failed', err);
  } finally {
    uiPainting = false;
  }
}

function drawFrame(): void {
  if (!built) return;
  gizmoGroup.visible = showGizmos && usingOrbit;
  renderer.render(built.scene, activeCamera);
  if (showUi) ugui.present(renderer);
  if (usingOrbit) {
    gizmoEl.textContent =
      `yaw ${orbit.yaw.toFixed(2)} pitch ${orbit.pitch.toFixed(2)} dist ${orbit.dist.toFixed(2)}`;
  } else {
    const cam = activeCamera as THREE.PerspectiveCamera;
    gizmoEl.textContent = cam.isPerspectiveCamera ? `fov ${cam.fov.toFixed(1)}°` : 'orthographic';
  }
}

function tick() {
  requestAnimationFrame(tick);
  const dt = clock.getDelta();
  if (!paused) animators.update(dt);
  if (usingOrbit) updateOrbit();
  if (uiDirty && !uiPainting) { uiDirty = false; void repaintUi(); }
  drawFrame();
}

/* ------------------------------------------------------------------ */
/* Input                                                               */
/* ------------------------------------------------------------------ */

let dragging: 'rotate' | 'pan' | null = null;
let lastX = 0, lastY = 0;

renderer.domElement.addEventListener('mousedown', (e: MouseEvent) => {
  dragging = (e.button === 1 || e.shiftKey) ? 'pan' : 'rotate';
  lastX = e.clientX; lastY = e.clientY;
  e.preventDefault();
});
window.addEventListener('mouseup', () => { dragging = null; });
window.addEventListener('mousemove', (e: MouseEvent) => {
  if (!dragging || !usingOrbit) return;
  const dx = e.clientX - lastX, dy = e.clientY - lastY;
  lastX = e.clientX; lastY = e.clientY;
  if (dragging === 'rotate') {
    orbit.yaw -= dx * 0.006;
    orbit.pitch = Math.max(-1.5, Math.min(1.5, orbit.pitch + dy * 0.006));
  } else {
    const right = new THREE.Vector3().setFromMatrixColumn(orbitCam.matrix, 0);
    const up = new THREE.Vector3().setFromMatrixColumn(orbitCam.matrix, 1);
    const k = orbit.dist * 0.0016;
    orbit.target.addScaledVector(right, -dx * k).addScaledVector(up, dy * k);
  }
});
renderer.domElement.addEventListener('wheel', (e: WheelEvent) => {
  if (!usingOrbit) return;
  orbit.dist = Math.max(0.15, orbit.dist * (1 + Math.sign(e.deltaY) * 0.1));
  e.preventDefault();
}, { passive: false });

window.addEventListener('keydown', (e) => {
  if ((e.target as HTMLElement)?.tagName === 'INPUT') return;
  if (e.key === 'f' || e.key === 'F') {
    if (selectedNode) api.frameObject(selectedNode.name); else frameAll();
  }
});

/* ------------------------------------------------------------------ */
/* Toolbar                                                             */
/* ------------------------------------------------------------------ */

function toggleButton(id: string, initial: boolean, onChange: (v: boolean) => void) {
  const el = document.getElementById(id)!;
  let value = initial;
  el.classList.toggle('on', value);
  el.addEventListener('click', () => {
    value = !value;
    el.classList.toggle('on', value);
    onChange(value);
  });
  return {
    set(v: boolean) { value = v; el.classList.toggle('on', v); onChange(v); },
    get() { return value; },
  };
}

const uiToggle = toggleButton('btn-ui', true, (v) => { showUi = v; uiDirty = true; });
const orphanToggle = toggleButton('btn-orphan', true, (v) => { showOrphanUi = v; uiDirty = true; });
const gizmoToggle = toggleButton('btn-gizmos', true, (v) => { showGizmos = v; });
const wireToggle = toggleButton('btn-wire', false, (v) => { wireframe = v; applyWireframe(); });

document.getElementById('btn-play')!.addEventListener('click', () => {
  if (scenePicker.value) void loadScene(scenePicker.value);
});
const pauseBtn = document.getElementById('btn-pause')!;
pauseBtn.addEventListener('click', () => {
  paused = !paused;
  pauseBtn.classList.toggle('on', paused);
});
document.getElementById('btn-step')!.addEventListener('click', () => {
  animators.update(1 / 60);
  drawFrame();
});
document.getElementById('btn-frame')!.addEventListener('click', () => {
  if (selectedNode) api.frameObject(selectedNode.name); else frameAll();
});
fovInput.addEventListener('change', () => {
  const v = Number(fovInput.value);
  if (Number.isFinite(v) && v > 0) api.setFov(v);
});
scenePicker.addEventListener('change', () => { void loadScene(scenePicker.value); });
cameraPicker.addEventListener('change', () => selectCamera(cameraPicker.value));

// Project / Console tab switching
const tabProject = document.getElementById('tab-project')!;
const tabConsole = document.getElementById('tab-console')!;
const projectBody = document.getElementById('project-body')!;
const consoleBody = document.getElementById('console-body')!;
tabProject.addEventListener('click', () => {
  tabProject.classList.add('on'); tabConsole.classList.remove('on');
  projectBody.style.display = 'grid'; consoleBody.style.display = 'none';
});
tabConsole.addEventListener('click', () => {
  tabConsole.classList.add('on'); tabProject.classList.remove('on');
  consoleBody.style.display = 'block'; projectBody.style.display = 'none';
});
document.getElementById('tab-scene')!.addEventListener('click', () => {
  cameraPicker.value = '__orbit'; selectCamera('__orbit');
  document.getElementById('tab-scene')!.classList.add('on');
  document.getElementById('tab-game')!.classList.remove('on');
});
document.getElementById('tab-game')!.addEventListener('click', () => {
  if (built?.cameras.length) { cameraPicker.value = '0'; selectCamera('0'); }
  document.getElementById('tab-game')!.classList.add('on');
  document.getElementById('tab-scene')!.classList.remove('on');
});

/* ------------------------------------------------------------------ */
/* Agent API                                                           */
/* ------------------------------------------------------------------ */

function findObjects(needle: string): THREE.Object3D[] {
  if (!built) return [];
  const want = needle.toLowerCase();
  const out: THREE.Object3D[] = [];
  built.root.traverse((o) => {
    if (o.name && o.name.toLowerCase().includes(want)) out.push(o);
  });
  return out;
}

/**
 * Resolve one scene node from a human-written needle.
 *
 * Accepted forms, in the order they are tried:
 *   "&123456"          a transform or GameObject fileID — always unambiguous
 *   "A/B/C"            a path; matched against the tail of each node's full
 *                      path, so "RightHand/N4_Rifle" is enough to pin down the
 *                      one rifle that hangs off the hand
 *   "Name"             exact name match, then substring match
 *
 * Substring matching is last because it is the one that silently returns the
 * wrong object (searching "N4_Rifle" finds the UI card "W_N4_Rifle" first).
 */
function findNodeInfo(needle: string): NodeInfo | null {
  if (!built) return null;
  const raw = needle.trim();

  if (raw.startsWith('&')) {
    const want = raw.slice(1);
    let hit: NodeInfo | null = null;
    const walk = (list: NodeInfo[]) => {
      for (const n of list) {
        if (!hit && String(n.fileID) === want) hit = n;
        walk(n.children);
      }
    };
    walk(built.nodes);
    return hit;
  }

  if (raw.includes('/')) {
    const want = raw.replace(/^\/+/, '').toLowerCase();
    const segments = want.split('/').length;
    let hit: NodeInfo | null = null;
    const walk = (list: NodeInfo[], prefix: string[]) => {
      for (const n of list) {
        const path = [...prefix, n.name];
        if (!hit && path.slice(-segments).join('/').toLowerCase() === want) hit = n;
        walk(n.children, path);
      }
    };
    walk(built.nodes, []);
    return hit;
  }

  const want = raw.toLowerCase();
  let exact: NodeInfo | null = null;
  let partial: NodeInfo | null = null;
  const walk = (list: NodeInfo[]) => {
    for (const n of list) {
      const lower = n.name.toLowerCase();
      if (lower === want && !exact) exact = n;
      else if (lower.includes(want) && !partial) partial = n;
      walk(n.children);
    }
  };
  walk(built.nodes);
  return exact ?? partial;
}

const v3 = (v: THREE.Vector3) => v.toArray().map((x) => +x.toFixed(4));

/**
 * The agent-facing surface.
 *
 * Grouped by what you are trying to do rather than by internal structure, and
 * everything returns plain JSON so it survives `page.evaluate`.
 */
const api = {
  /* ---- scene ---- */
  loadScene: (path: string) => loadScene(path),
  listScenes: async () => (await assets.listFiles('.unity')),
  report: () => {
    if (!built) return null;
    const r = built.report;
    return {
      ...r,
      unmappedScripts: Object.fromEntries(r.unmappedScripts),
      meshResolution: Object.fromEntries(r.meshResolution),
    };
  },
  warnings: () => built?.report.warnings ?? [],
  console: () => consolePane.counts(),

  /* ---- hierarchy and inspection ---- */
  hierarchy: (maxDepth = 99) => {
    const conv = (n: NodeInfo, d: number): Record<string, unknown> => ({
      name: n.name,
      fileID: n.fileID,
      active: n.active,
      tag: n.tag,
      layer: n.layer,
      components: n.components.map((c) => c.scriptName ?? c.className),
      children: d < maxDepth ? n.children.map((c) => conv(c, d + 1)) : [],
    });
    return built?.nodes.map((n) => conv(n, 0)) ?? [];
  },
  findObjects: (needle: string) => findObjects(needle).map((o) => {
    o.updateWorldMatrix(true, false);
    return {
      name: o.name,
      type: o.type,
      visible: o.visible,
      world: v3(o.getWorldPosition(new THREE.Vector3())),
      children: o.children.length,
    };
  }),
  /** Full component dump for one object, as the Inspector shows it. */
  inspect: (needle: string) => {
    const node = findNodeInfo(needle);
    if (!node) return null;
    node.object3d.updateWorldMatrix(true, false);
    const wp = node.object3d.getWorldPosition(new THREE.Vector3());
    return {
      name: node.name,
      fileID: node.fileID,
      active: node.active,
      tag: node.tag,
      layer: node.layer,
      worldThree: v3(wp),
      worldUnity: [+wp.x.toFixed(4), +wp.y.toFixed(4), +(-wp.z).toFixed(4)],
      components: node.components.map((c) => ({
        type: c.scriptName ?? c.className,
        classId: c.classId,
        fileID: c.fileID,
        properties: c.body,
      })),
      children: node.children.map((c) => c.name),
    };
  },
  /**
   * Pose of `child` expressed in `parent`'s space, in Unity conventions.
   *
   * This is the question rig work actually asks — "where does this grip sit
   * relative to that hand bone" — and answering it from world positions alone
   * loses the rotation, which is usually the part that is wrong. `parent` may
   * be any node, not just an ancestor. With no `parent`, gives the world pose.
   *
   * Mirroring note: local matrices are stored three-side as M·A·M, so
   * inverse(P_world)·C_world is itself mirrored, and one final M·_·M puts the
   * answer back into Unity's left-handed frame.
   */
  relative: (child: string, parent?: string) => {
    const c = findNodeInfo(child);
    if (!c) return null;
    c.object3d.updateWorldMatrix(true, false);
    let m = c.object3d.matrixWorld.clone();
    if (parent) {
      const p = findNodeInfo(parent);
      if (!p) return null;
      p.object3d.updateWorldMatrix(true, false);
      m = new THREE.Matrix4().copy(p.object3d.matrixWorld).invert().multiply(m);
    }
    const mirror = new THREE.Matrix4().makeScale(1, 1, -1);
    const unity = new THREE.Matrix4().multiplyMatrices(mirror, m).multiply(mirror);
    const pos = new THREE.Vector3();
    const rot = new THREE.Quaternion();
    const scl = new THREE.Vector3();
    unity.decompose(pos, rot, scl);
    const euler = new THREE.Euler().setFromQuaternion(rot, 'ZXY');
    const r4 = (n: number) => +n.toFixed(4);
    const deg = (n: number) => +((n * 180) / Math.PI).toFixed(2);
    return {
      child: c.name,
      parent: parent ? findNodeInfo(parent)!.name : '(world)',
      position: pos.toArray().map(r4),
      rotation: rot.toArray().map(r4),
      euler: [deg(euler.x), deg(euler.y), deg(euler.z)],
      scale: scl.toArray().map(r4),
      distance: r4(pos.length()),
    };
  },
  select: (needle: string) => {
    const node = findNodeInfo(needle);
    if (!node) return null;
    selectedNode = node;
    hierarchy.select(node.fileID);
    inspector.show(node);
    return { name: node.name, fileID: node.fileID };
  },

  /* ---- camera ---- */
  cameras: () => built?.cameras.map((c) => ({
    name: c.name, path: c.path, depth: c.depth,
    fov: (c.camera as THREE.PerspectiveCamera).fov ?? null,
  })) ?? [],
  setCamera: (nameOrIndex: string | number) => {
    if (!built) return false;
    if (nameOrIndex === 'orbit' || nameOrIndex === '__orbit') {
      cameraPicker.value = '__orbit'; selectCamera('__orbit'); return true;
    }
    const idx = typeof nameOrIndex === 'number'
      ? nameOrIndex
      : built.cameras.findIndex((c) => c.name.toLowerCase().includes(String(nameOrIndex).toLowerCase()));
    if (idx < 0 || idx >= built.cameras.length) return false;
    cameraPicker.value = String(idx);
    selectCamera(String(idx));
    return true;
  },
  setFov: (fov: number) => {
    const cam = activeCamera as THREE.PerspectiveCamera;
    if (!cam.isPerspectiveCamera) return false;
    cam.fov = Math.max(1, Math.min(179, fov));
    cam.updateProjectionMatrix();
    fovInput.value = String(Math.round(cam.fov));
    return true;
  },
  setClip: (near: number, far: number) => {
    const cam = activeCamera as THREE.PerspectiveCamera;
    cam.near = near; cam.far = far; cam.updateProjectionMatrix();
    return true;
  },
  /** Position the orbit camera explicitly, in three-space coordinates. */
  setOrbit: (yaw: number, pitch: number, dist: number, tx = 0, ty = 1, tz = 0) => {
    orbit.yaw = yaw; orbit.pitch = pitch; orbit.dist = dist;
    orbit.target.set(tx, ty, tz);
    usingOrbit = true; activeCamera = orbitCam;
    cameraPicker.value = '__orbit';
    resize(); updateOrbit();
    return true;
  },
  /** Place the camera at a point and aim it at another. Unity coords accepted. */
  lookAt: (from: [number, number, number], at: [number, number, number], unity = false) => {
    const f = new THREE.Vector3(from[0], from[1], unity ? -from[2] : from[2]);
    const t = new THREE.Vector3(at[0], at[1], unity ? -at[2] : at[2]);
    orbit.target.copy(t);
    orbit.dist = f.distanceTo(t);
    const d = new THREE.Vector3().subVectors(f, t);
    orbit.pitch = Math.asin(THREE.MathUtils.clamp(d.y / (orbit.dist || 1), -1, 1));
    orbit.yaw = Math.atan2(d.x, d.z);
    usingOrbit = true; activeCamera = orbitCam;
    cameraPicker.value = '__orbit';
    updateOrbit();
    return { from: v3(orbitCam.position), target: v3(orbit.target) };
  },
  cameraState: () => {
    const cam = activeCamera as THREE.PerspectiveCamera;
    cam.updateWorldMatrix(true, false);
    return {
      orbit: usingOrbit,
      position: v3(cam.getWorldPosition(new THREE.Vector3())),
      target: usingOrbit ? v3(orbit.target) : null,
      fov: cam.isPerspectiveCamera ? cam.fov : null,
      near: cam.near, far: cam.far,
      yaw: orbit.yaw, pitch: orbit.pitch, dist: orbit.dist,
    };
  },
  frameAll: () => { frameAll(); return true; },
  frameObject: (needle: string | null) => {
    if (!built) return null;
    if (!needle) { frameAll(); return { name: '<scene>' }; }
    const matches = findObjects(needle);
    if (!matches.length) return null;
    let hit: THREE.Object3D | null = null;
    let box = new THREE.Box3();
    for (const candidate of matches) {
      const b = new THREE.Box3().setFromObject(candidate);
      if (!b.isEmpty()) { hit = candidate; box = b; break; }
    }
    if (!hit) return null;
    const size = box.getSize(new THREE.Vector3());
    const centre = box.getCenter(new THREE.Vector3());
    const radius = Math.max(size.x, size.y, size.z) * 0.5 || 1;
    orbit.target.copy(centre);
    orbit.dist = (radius / Math.tan((orbitCam.fov * Math.PI) / 360)) * 1.7;
    usingOrbit = true; activeCamera = orbitCam;
    cameraPicker.value = '__orbit';
    resize(); updateOrbit();
    return { name: hit.name, centre: v3(centre), size: v3(size) };
  },

  /* ---- animation ---- */
  animators: () => animators.info(),
  clips: (who?: string) => animators.find(who)?.clipNames() ?? [],
  controller: (who?: string) => animators.find(who)?.controller ?? null,
  play: (clipOrState: string, who?: string, opts?: { loop?: boolean; speed?: number; fade?: number }) =>
    animators.find(who)?.play(clipOrState, opts ?? {}) ?? false,
  stopAnim: (who?: string) => { animators.find(who)?.stop(); return true; },
  /** Absolute time in seconds. */
  setAnimTime: (seconds: number, who?: string) => {
    const a = animators.find(who);
    if (!a) return false;
    a.setTime(seconds);
    drawFrame();
    return true;
  },
  /** Normalised 0..1 position in the current clip — the usual way to pose. */
  setAnimNormalized: (t: number, who?: string) => {
    const a = animators.find(who);
    if (!a) return false;
    a.setNormalizedTime(t);
    drawFrame();
    return true;
  },
  setAnimSpeed: (speed: number, who?: string) => {
    animators.find(who)?.setSpeed(speed);
    return true;
  },
  pauseAnim: (value = true) => { paused = value; pauseBtn.classList.toggle('on', paused); return paused; },
  stepAnim: (dt = 1 / 60) => { animators.update(dt); drawFrame(); return true; },
  /** Bone world positions, for checking a pose numerically. */
  bones: (who?: string, filter?: string) => {
    const a = animators.find(who);
    if (!a) return [];
    const want = filter?.toLowerCase();
    const out: Array<Record<string, unknown>> = [];
    a.root.traverse((o) => {
      if (!(o as THREE.Bone).isBone && o.type !== 'Bone' && !o.name) return;
      if (want && !o.name.toLowerCase().includes(want)) return;
      o.updateWorldMatrix(true, false);
      const e = new THREE.Euler().setFromQuaternion(o.quaternion, 'ZXY');
      out.push({
        name: o.name,
        parent: o.parent?.name ?? null,
        world: v3(o.getWorldPosition(new THREE.Vector3())),
        local: v3(o.position),
        localScale: v3(o.scale),
        localEulerDeg: [e.x, e.y, e.z].map((r) => +(r * 180 / Math.PI).toFixed(2)),
      });
    });
    return out;
  },

  /* ---- UI ---- */
  setUiVisible: (v: boolean) => { uiToggle.set(v); return v; },
  setOrphanUi: (v: boolean) => { orphanToggle.set(v); return v; },
  setGizmos: (v: boolean) => { gizmoToggle.set(v); return v; },
  setWireframe: (v: boolean) => { wireToggle.set(v); return v; },
  uiStats: () => ugui.stats,
  /** Flattened solved UI layout — rect, text and colour per widget. */
  uiLayout: () => {
    const active = built;
    if (!active) return [];
    const w = viewport.clientWidth || 1280;
    const h = viewport.clientHeight || 720;
    const out: Array<Record<string, unknown>> = [];
    const walk = (nodes: ReturnType<typeof active.layoutUi>, depth: number) => {
      for (const n of nodes) {
        out.push({
          name: n.name,
          depth,
          active: n.active,
          rect: [n.rect.x, n.rect.y, n.rect.width, n.rect.height].map((v) => +v.toFixed(1)),
          components: [...n.components.keys()],
        });
        walk(n.children, depth + 1);
      }
    };
    walk(active.layoutUi(w, h, showOrphanUi), 0);
    return out;
  },
  paintUi: async () => { uiDirty = false; await repaintUi(); drawFrame(); return ugui.stats; },

  /* ---- assets ---- */
  listAssets: (ext: string) => assets.listFiles(ext),
  readAsset: (path: string) => assets.readText(path),
  guidOf: (path: string) => assets.guidForPath(path),
  pathOfGuid: (guid: string) => assets.pathForGuid(guid),

  /* ---- escape hatch ---- */
  /**
   * Raw three.js handles for one-off investigation the typed API does not
   * cover. Returns live objects, so anything read out of here must be reduced
   * to plain JSON before it crosses the page boundary.
   */
  raw: () => ({ THREE, scene: built?.scene ?? null, built }),

  /* ---- render ---- */
  renderOnce: () => { drawFrame(); return true; },
  setPaused: (v: boolean) => { paused = v; pauseBtn.classList.toggle('on', v); return v; },
  viewportSize: () => [viewport.clientWidth, viewport.clientHeight],
};

(window as unknown as Record<string, unknown>).uw = api;

/* ------------------------------------------------------------------ */
/* Boot                                                                */
/* ------------------------------------------------------------------ */

async function boot() {
  const params = new URLSearchParams(location.search);

  // `?clean=1` strips the editor chrome so captures are pure viewport.
  if (params.get('clean') === '1') {
    document.getElementById('menubar')!.style.display = 'none';
    document.getElementById('toolbar')!.style.display = 'none';
    document.getElementById('leftcol')!.style.display = 'none';
    document.getElementById('rightcol')!.style.display = 'none';
    document.getElementById('project-pane')!.style.display = 'none';
    document.getElementById('app')!.style.gridTemplateRows = '1fr';
    document.getElementById('docks')!.style.gridTemplateColumns = '1fr';
    document.getElementById('midcol')!.style.gridTemplateRows = '1fr';
    (document.querySelector('#scene-pane .pane-tabs') as HTMLElement).style.display = 'none';
    (document.getElementById('scene-pane') as HTMLElement).style.gridTemplateRows = '1fr';
    if (params.get('overlay') !== '1') { overlay.style.display = 'none'; gizmoEl.style.display = 'none'; }
  }

  setStatus('Indexing project…');
  await assets.loadIndex();
  void project.init();

  const scenes = await assets.listFiles('.unity');
  scenePicker.innerHTML = '';
  for (const s of scenes) {
    const o = document.createElement('option');
    o.value = s;
    o.textContent = s.replace(/^Assets\//, '');
    scenePicker.appendChild(o);
  }

  const wanted = params.get('scene')
    ?? scenes.find((s) => s.includes('StartMenu'))
    ?? scenes[0];

  resize();
  tick();

  if (wanted) {
    scenePicker.value = wanted;
    await loadScene(wanted);
  } else {
    setStatus('No .unity scenes found', true);
  }

  const cam = params.get('camera');
  if (cam) api.setCamera(cam === 'orbit' ? 'orbit' : cam);
  const fov = params.get('fov');
  if (fov) api.setFov(Number(fov));
  if (params.get('ui') === '0') api.setUiVisible(false);
  if (params.get('orphan') === '0') api.setOrphanUi(false);
}

boot().catch((err) => {
  const message = err instanceof Error ? err.message : String(err);
  setStatus(`Boot failed: ${message}`, true);
  (window as unknown as Record<string, unknown>).__uwError = message;
});
