/**
 * Unity Web Clone — viewer entry point.
 *
 * Serves two consumers from one code path:
 *   1. the user, in a browser, inspecting real Unity scenes; and
 *   2. the headless capture script (scripts/shot.mjs), which drives the same
 *      page through `window.uw` and screenshots the canvas.
 *
 * Keeping them identical is deliberate: the agent must be looking at exactly
 * what the user can look at.
 */

import * as THREE from 'three';
import { assets } from './unity/AssetDatabase.ts';
import { UguiRenderer } from './ui/UguiRenderer.ts';
import { buildScene, type BuiltScene, type NodeInfo } from './runtime/SceneBuilder.ts';

/* ------------------------------------------------------------------ */
/* Renderer                                                            */
/* ------------------------------------------------------------------ */

const viewport = document.getElementById('viewport') as HTMLDivElement;
const statusEl = document.getElementById('status') as HTMLDivElement;
const statusText = document.getElementById('status-text') as HTMLDivElement;
const overlay = document.getElementById('overlay') as HTMLDivElement;
const statsEl = document.getElementById('stats') as HTMLDivElement;
const treeEl = document.getElementById('tree') as HTMLDivElement;
const inspEl = document.getElementById('insp') as HTMLDivElement;
const scenePicker = document.getElementById('scene-picker') as HTMLSelectElement;
const cameraPicker = document.getElementById('camera-picker') as HTMLSelectElement;

const renderer = new THREE.WebGLRenderer({ antialias: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(1);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.NeutralToneMapping;
renderer.toneMappingExposure = 1.0;
viewport.appendChild(renderer.domElement);

let built: BuiltScene | null = null;
let activeCamera: THREE.PerspectiveCamera | THREE.OrthographicCamera;
let orbit = { yaw: 0.6, pitch: 0.42, dist: 14, target: new THREE.Vector3(0, 1.2, 0) };
let usingOrbit = true;

const orbitCam = new THREE.PerspectiveCamera(50, 16 / 9, 0.05, 4000);
activeCamera = orbitCam;

function setStatus(text: string | null, error = false) {
  if (text === null) { statusEl.classList.add('hidden'); return; }
  statusEl.classList.remove('hidden');
  statusText.textContent = text;
  statusText.className = error ? 'bad' : '';
  statusEl.querySelector('.spin')?.classList.toggle('hidden', error);
}

/** Screen-space uGUI, painted to a 2D canvas and composited over the 3D frame. */
const ugui = new UguiRenderer();
let uiDirty = true;
let uiPainting = false;
let showUi = true;
/** Preview UI subtrees that are not under a Canvas (Unity would show nothing). */
let showOrphanUi = true;

function resize() {
  const w = viewport.clientWidth || 1280;
  const h = viewport.clientHeight || 720;
  renderer.setSize(w, h, false);
  ugui.resize(w, h, Math.min(window.devicePixelRatio || 1, 2));
  uiDirty = true;
  const aspect = w / h;
  if (activeCamera instanceof THREE.PerspectiveCamera) {
    activeCamera.aspect = aspect;
    activeCamera.updateProjectionMatrix();
  }
}
window.addEventListener('resize', resize);

function updateOrbit() {
  const cp = Math.cos(orbit.pitch), sp = Math.sin(orbit.pitch);
  orbitCam.position.set(
    orbit.target.x + orbit.dist * cp * Math.sin(orbit.yaw),
    orbit.target.y + orbit.dist * sp,
    orbit.target.z + orbit.dist * cp * Math.cos(orbit.yaw),
  );
  orbitCam.lookAt(orbit.target);
}

/* ------------------------------------------------------------------ */
/* Mouse navigation                                                    */
/* ------------------------------------------------------------------ */

let dragging = false, lastX = 0, lastY = 0, panning = false;
renderer.domElement.addEventListener('mousedown', (e) => {
  dragging = true; panning = e.button === 1 || e.shiftKey; lastX = e.clientX; lastY = e.clientY;
});
window.addEventListener('mouseup', () => { dragging = false; });
window.addEventListener('mousemove', (e: MouseEvent) => {
  if (!dragging || !usingOrbit) return;
  const dx = e.clientX - lastX, dy = e.clientY - lastY;
  lastX = e.clientX; lastY = e.clientY;
  if (panning) {
    const right = new THREE.Vector3().setFromMatrixColumn(orbitCam.matrix, 0);
    const up = new THREE.Vector3().setFromMatrixColumn(orbitCam.matrix, 1);
    const k = orbit.dist * 0.0016;
    orbit.target.addScaledVector(right, -dx * k).addScaledVector(up, dy * k);
  } else {
    orbit.yaw -= dx * 0.006;
    orbit.pitch = THREE.MathUtils.clamp(orbit.pitch + dy * 0.006, -1.45, 1.45);
  }
});
renderer.domElement.addEventListener('wheel', (e: WheelEvent) => {
  if (!usingOrbit) return;
  e.preventDefault();
  orbit.dist = THREE.MathUtils.clamp(orbit.dist * (1 + Math.sign(e.deltaY) * 0.12), 0.3, 3000);
}, { passive: false });

/* ------------------------------------------------------------------ */
/* Scene loading                                                       */
/* ------------------------------------------------------------------ */

async function loadScene(path: string) {
  setStatus(`Building ${path.split('/').pop()}…`);
  treeEl.innerHTML = '';
  inspEl.innerHTML = '';
  try {
    const t0 = performance.now();
    built = await buildScene(path);
    uiDirty = true;
    const ms = Math.round(performance.now() - t0);

    // Camera list
    cameraPicker.innerHTML = '<option value="__orbit">Orbit camera</option>';
    built.cameras
      .sort((a, b) => b.depth - a.depth)
      .forEach((c, i) => {
        const o = document.createElement('option');
        o.value = String(i);
        o.textContent = `${c.name}  (${c.path})`;
        cameraPicker.appendChild(o);
      });

    frameAll();
    // Prefer an authored camera when the scene has one — that is what Unity shows.
    if (built.cameras.length > 0) { cameraPicker.value = '0'; selectCamera('0'); }
    else selectCamera('__orbit');

    buildTree(built.nodes);

    const r = built.report;
    statsEl.innerHTML =
      `<b>${r.gameObjects}</b> objects · <b>${r.meshes}</b> meshes · ` +
      `<b>${r.lights}</b> lights · <b>${r.cameras}</b> cameras · ` +
      `<b>${r.canvases}</b> canvas · <b>${assets.indexSize}</b> guids · <b>${ms}</b>ms`;

    overlay.innerHTML =
      `${path}<br>` +
      (r.warnings.length
        ? `<span class="warn">${r.warnings.length} warning(s) — first: ${r.warnings[0]}</span>`
        : `<span style="color:var(--ok)">no warnings</span>`);

    setStatus(null);
    (window as unknown as Record<string, unknown>).__uwReady = true;
  } catch (err) {
    console.error(err);
    setStatus(`Failed: ${(err as Error).message}`, true);
    (window as unknown as Record<string, unknown>).__uwError = String(err);
  }
}

function frameAll() {
  if (!built) return;
  const box = new THREE.Box3().setFromObject(built.root);
  if (box.isEmpty()) return;
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  orbit.target.copy(center);
  orbit.dist = Math.max(size.length() * 0.8, 1.5);
  updateOrbit();
}

function selectCamera(value: string) {
  if (!built) return;
  if (value === '__orbit') {
    usingOrbit = true;
    activeCamera = orbitCam;
  } else {
    const info = built.cameras[parseInt(value, 10)];
    if (info) { usingOrbit = false; activeCamera = info.camera; }
  }
  resize();
}

/* ------------------------------------------------------------------ */
/* Hierarchy + inspector                                               */
/* ------------------------------------------------------------------ */



function buildTree(nodes: NodeInfo[]) {
  treeEl.innerHTML = '';
  const render = (list: NodeInfo[], depth: number) => {
    for (const n of list) {
      const row = document.createElement('div');
      row.className = 'node' + (n.active ? '' : ' off');
      row.style.paddingLeft = `${8 + depth * 12}px`;
      const names = n.components
        .map((c) => c.scriptName ?? c.className)
        .filter((c) => c !== 'Transform' && c !== 'RectTransform');
      row.innerHTML =
        `<span class="tw">${n.children.length ? '▸' : ''}</span>` +
        `<span>${escapeHtml(n.name)}</span>` +
        (names.length ? `<span class="cmp">${escapeHtml(names.slice(0, 3).join(' '))}</span>` : '');
      row.onclick = () => { select(n, row); };
      treeEl.appendChild(row);
      if (depth < 6) render(n.children, depth + 1);
    }
  };
  render(nodes, 0);
}

function select(n: NodeInfo, row: HTMLElement) {
  document.querySelectorAll('.node.sel').forEach((e) => e.classList.remove('sel'));
  row.classList.add('sel');

  const p = n.object3d.getWorldPosition(new THREE.Vector3());
  const rows: string[] = [
    row2('Name', n.name),
    row2('Active', String(n.active)),
    row2('Layer / Tag', `${n.layer} / ${n.tag}`),
    row2('fileID', String(n.fileID)),
    row2('World pos (three)', `${f(p.x)}, ${f(p.y)}, ${f(p.z)}`),
    row2('World pos (Unity)', `${f(p.x)}, ${f(p.y)}, ${f(-p.z)}`),
    row2('Children', String(n.children.length)),
  ];
  const chips = n.components
    .map((c) => `<span class="cmp-chip">${escapeHtml(c.scriptName ?? c.className)}</span>`)
    .join('');
  rows.push(`<div class="insp-row"><div class="k">Components (${n.components.length})</div><div>${chips}</div></div>`);
  inspEl.innerHTML = rows.join('');
}

const f = (v: number) => v.toFixed(3);
const row2 = (k: string, v: string) =>
  `<div class="insp-row"><div class="k">${escapeHtml(k)}</div><div class="v">${escapeHtml(v)}</div></div>`;
function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] as string));
}

/* ------------------------------------------------------------------ */
/* Loop                                                                */
/* ------------------------------------------------------------------ */

/**
 * Repaint the uGUI layer. Async because fonts and sprites load on demand, so
 * we mark it dirty and let the result appear a frame or two later rather than
 * blocking the 3D view.
 */
async function repaintUi(): Promise<void> {
  if (!built || uiPainting) return;
  uiPainting = true;
  try {
    const w = viewport.clientWidth || 1280;
    const h = viewport.clientHeight || 720;
    const roots = built.layoutUi(w, h, showOrphanUi);
    await ugui.paint(roots, w, h);
  } catch (err) {
    console.warn('[ui] paint failed', err);
  } finally {
    uiPainting = false;
  }
}

function drawFrame(): void {
  if (!built) return;
  renderer.render(built.scene, activeCamera);
  if (showUi) ugui.present(renderer);
}

function tick() {
  requestAnimationFrame(tick);
  if (usingOrbit) updateOrbit();
  if (uiDirty && !uiPainting) { uiDirty = false; void repaintUi(); }
  drawFrame();
}

/* ------------------------------------------------------------------ */
/* Boot                                                                */
/* ------------------------------------------------------------------ */

async function boot() {
  // `?clean=1` strips the editor chrome so headless captures are pure viewport.
  if (new URLSearchParams(location.search).get('clean') === '1') {
    document.body.classList.add('clean');
    const app = document.getElementById('app')!;
    app.style.gridTemplateRows = '1fr';
    (document.querySelector('header') as HTMLElement).style.display = 'none';
    (document.querySelector('main') as HTMLElement).style.gridTemplateColumns = '1fr';
    (document.getElementById('hierarchy') as HTMLElement).style.display = 'none';
    (document.getElementById('inspector') as HTMLElement).style.display = 'none';
    if (new URLSearchParams(location.search).get('overlay') !== '1') overlay.style.display = 'none';
  }
  setStatus('Loading project index…');
  await assets.loadIndex();

  const scenes = await assets.listFiles('.unity');
  scenePicker.innerHTML = '';
  for (const s of scenes) {
    const o = document.createElement('option');
    o.value = s;
    o.textContent = s.replace(/^Assets\//, '');
    scenePicker.appendChild(o);
  }

  const params = new URLSearchParams(location.search);
  const wanted = params.get('scene');
  const initial = (wanted && scenes.includes(wanted)) ? wanted
    : scenes.find((s) => s.includes('StartMenu')) ?? scenes[0];
  if (initial) { scenePicker.value = initial; await loadScene(initial); }
  else setStatus('No .unity scenes found under Assets/', true);

  const camParam = params.get('camera');
  if (camParam) applyCameraByName(camParam);

  resize();
  tick();
}

function applyCameraByName(name: string) {
  if (!built) return;
  if (name === 'orbit') { cameraPicker.value = '__orbit'; selectCamera('__orbit'); return; }
  const idx = built.cameras.findIndex((c) => c.name === name || c.path === name);
  if (idx >= 0) { cameraPicker.value = String(idx); selectCamera(String(idx)); }
}

scenePicker.onchange = () => loadScene(scenePicker.value);
cameraPicker.onchange = () => selectCamera(cameraPicker.value);
(document.getElementById('reload') as HTMLButtonElement).onclick = () => loadScene(scenePicker.value);
(document.getElementById('frame') as HTMLButtonElement).onclick = () => {
  cameraPicker.value = '__orbit'; selectCamera('__orbit'); frameAll();
};

/** Control surface for the headless capture script. */
(window as unknown as Record<string, unknown>).uw = {
  loadScene,
  setCamera: applyCameraByName,
  setOrbit: (yaw: number, pitch: number, dist: number, tx = 0, ty = 1.2, tz = 0) => {
    cameraPicker.value = '__orbit'; selectCamera('__orbit');
    orbit.yaw = yaw; orbit.pitch = pitch; orbit.dist = dist;
    orbit.target.set(tx, ty, tz); updateOrbit();
  },
  frameAll,
  /** Dump skinning diagnostics: bone counts, and model vs scene bone geometry. */
  skinInfo: () => {
    if (!built) return [];
    const out: Array<Record<string, unknown>> = [];
    built.root.traverse((o) => {
      const sm = o as THREE.SkinnedMesh;
      if (!sm.isSkinnedMesh || !sm.skeleton) return;
      const bones = sm.skeleton.bones;
      const inverses = sm.skeleton.boneInverses;
      const bonePos = bones.map((b) => {
        b.updateWorldMatrix(true, false);
        return new THREE.Vector3().setFromMatrixPosition(b.matrixWorld);
      });
      // Where the bind matrices say each bone sat when the mesh was skinned.
      const bindPos = inverses.map((m) =>
        new THREE.Vector3().setFromMatrixPosition(new THREE.Matrix4().copy(m).invert()));
      const spreadOf = (pts: THREE.Vector3[]) => {
        if (!pts.length) return 0;
        const c = new THREE.Vector3();
        pts.forEach((p) => c.add(p));
        c.divideScalar(pts.length);
        return Math.max(...pts.map((p) => p.distanceTo(c)));
      };
      const geo = sm.geometry;
      geo.computeBoundingBox();
      const gb = geo.boundingBox!;
      out.push({
        name: sm.name,
        bones: bones.length,
        inverses: inverses.length,
        boneNames: bones.slice(0, 5).map((b) => b.name),
        sceneBoneSpread: +spreadOf(bonePos).toFixed(4),
        bindBoneSpread: +spreadOf(bindPos).toFixed(4),
        geometrySize: gb.getSize(new THREE.Vector3()).toArray().map((v) => +v.toFixed(3)),
        firstSceneBone: bonePos[0]?.toArray().map((v) => +v.toFixed(3)),
        firstBindBone: bindPos[0]?.toArray().map((v) => +v.toFixed(3)),
      });
    });
    return out;
  },
  /**
   * Point the orbit camera at a named object (substring match, case-insensitive)
   * and pull back far enough to see all of it. Returns what it framed.
   */
  frameObject: (needle: string | null) => {
    if (!built) return null;
    if (!needle) { frameAll(); return { name: '<scene>' }; }
    // Collect every match, then take the first that actually has geometry —
    // UI Groups share names with world objects and would otherwise win with an
    // empty bounding box.
    const want = needle.toLowerCase();
    const matches: THREE.Object3D[] = [];
    built.root.traverse((o) => {
      if (o.name && o.name.toLowerCase().includes(want)) matches.push(o);
    });
    if (matches.length === 0) return null;

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
    usingOrbit = true;
    activeCamera = orbitCam;
    resize();
    updateOrbit();
    return {
      name: hit.name,
      centre: centre.toArray().map((v) => +v.toFixed(3)),
      size: size.toArray().map((v) => +v.toFixed(3)),
    };
  },
  /** List objects whose name matches, with world positions — for diagnosis. */
  findObjects: (needle: string) => {
    if (!built) return [];
    const want = needle.toLowerCase();
    const out: Array<Record<string, unknown>> = [];
    built.root.traverse((o) => {
      if (!o.name || !o.name.toLowerCase().includes(want)) return;
      const p = o.getWorldPosition(new THREE.Vector3());
      out.push({
        name: o.name, type: o.type, visible: o.visible,
        world: p.toArray().map((v) => +v.toFixed(3)),
      });
    });
    return out;
  },
  report: () => {
    if (!built) return null;
    const r = built.report;
    return {
      ...r,
      unmappedScripts: Object.fromEntries(r.unmappedScripts),
      meshResolution: Object.fromEntries(r.meshResolution),
    };
  },
  cameras: () => built?.cameras.map((c) => ({ name: c.name, path: c.path, depth: c.depth })) ?? [],
  hierarchy: () => built?.nodes.map(summarise) ?? [],
  renderOnce: () => drawFrame(),
  /** Await a full UI repaint — the capture script uses this before shooting. */
  paintUi: async () => { uiDirty = false; await repaintUi(); drawFrame(); },
  setUiVisible: (v: boolean) => { showUi = v; uiDirty = true; },
  setOrphanUi: (v: boolean) => { showOrphanUi = v; uiDirty = true; },
  uiStats: () => ugui.stats,
};

function summarise(n: NodeInfo): unknown {
  return {
    name: n.name, active: n.active,
    components: n.components.map((c) => c.scriptName ?? c.className),
    children: n.children.map(summarise),
  };
}

boot();
