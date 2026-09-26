/**
 * The Inspector pane.
 *
 * Shows every component on the selected GameObject with its full serialized
 * property set, formatted the way Unity formats it: vectors as labelled X/Y/Z
 * fields, colours as a swatch plus RGBA, object references as `Type (fileID)`
 * with a distinct colour for null.
 *
 * Crucially this reports what is actually *in the scene file* rather than a
 * prettified summary. The point of the tool is to answer "what did Unity
 * serialize here", so a field that looks odd must look odd here too.
 */

import * as THREE from 'three';
import type { ComponentData, NodeInfo } from '../runtime/SceneBuilder.ts';
import type { UnityMap, UnityValue } from '../unity/YamlParser.ts';
import { isRef, num } from '../unity/YamlParser.ts';
import { iconForComponent, namedIcon } from './Icons.ts';

/** Fields Unity hides on every object; noise in an inspector. */
const HIDDEN = new Set([
  'm_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance',
  'm_PrefabAsset', 'm_PrefabInternal', 'm_EditorHideFlags', 'm_EditorClassIdentifier',
  'm_GameObject', 'serializedVersion', 'm_Name', 'm_Script',
]);

/** Human label from a Unity field name: `m_LocalPosition` -> `Local Position`. */
function label(key: string): string {
  return key
    .replace(/^m_/, '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/^./, (c) => c.toUpperCase());
}

function isVecLike(v: UnityMap): 'vec' | 'color' | null {
  const keys = Object.keys(v);
  if (keys.length <= 4 && keys.every((k) => 'xyzw'.includes(k))) return 'vec';
  if (keys.length <= 4 && keys.every((k) => 'rgba'.includes(k))) return 'color';
  return null;
}

function fmtNumber(n: number): string {
  if (Number.isInteger(n)) return String(n);
  return String(+n.toFixed(5));
}

export class InspectorPane {
  private root: HTMLElement;
  private collapsed = new Set<string>();
  private current: NodeInfo | null = null;

  constructor() {
    this.root = document.getElementById('inspector-body')!;
  }

  clear(): void {
    this.current = null;
    this.root.innerHTML = '<div class="empty">Nothing selected</div>';
  }

  show(node: NodeInfo): void {
    this.current = node;
    this.render();
  }

  private render(): void {
    const node = this.current;
    if (!node) return this.clear();

    this.root.innerHTML = '';

    // ---- GameObject header --------------------------------------------
    const head = document.createElement('div');
    head.className = 'insp-head';
    const active = document.createElement('input');
    active.type = 'checkbox';
    active.checked = node.active;
    active.disabled = true;
    active.title = 'm_IsActive (read-only: this viewer does not edit scenes)';
    const nm = document.createElement('input');
    nm.className = 'nm';
    nm.value = node.name;
    nm.readOnly = true;
    head.append(active, nm);
    this.root.appendChild(head);

    const sub = document.createElement('div');
    sub.className = 'insp-sub';
    sub.innerHTML =
      `<span>Tag <b style="color:var(--text)">${node.tag}</b></span>` +
      `<span>Layer <b style="color:var(--text)">${node.layer}</b></span>` +
      `<span style="margin-left:auto;font-family:var(--mono)">fileID ${node.fileID}</span>`;
    this.root.appendChild(sub);

    // ---- Components -----------------------------------------------------
    for (const comp of node.components) {
      this.root.appendChild(this.componentCard(comp, node));
    }

    // ---- World transform, in both coordinate systems --------------------
    this.root.appendChild(this.worldCard(node));
  }

  private componentCard(comp: ComponentData, node: NodeInfo): HTMLElement {
    const wrap = document.createElement('div');
    wrap.className = 'comp';
    const title = comp.scriptName ?? comp.className;
    const key = `${node.fileID}:${comp.fileID}`;
    const isOpen = !this.collapsed.has(key);

    const head = document.createElement('div');
    head.className = 'comp-head';
    head.innerHTML =
      `<span class="tw">${isOpen ? '▼' : '▶'}</span>` +
      `<span class="ic">${iconForComponent(title)}</span>` +
      `<span class="ti"></span>` +
      `<span class="cid">${comp.classId}</span>`;
    head.querySelector('.ti')!.textContent = title;
    head.addEventListener('click', () => {
      if (this.collapsed.has(key)) this.collapsed.delete(key);
      else this.collapsed.add(key);
      this.render();
    });
    wrap.appendChild(head);

    if (!isOpen) return wrap;

    const body = document.createElement('div');
    body.className = 'comp-body';

    // Unity shows Transform's rotation as euler degrees, not the raw quaternion.
    if (comp.classId === 4 || comp.classId === 224) {
      this.appendTransformRows(body, comp.body);
    }

    let rows = 0;
    for (const [k, v] of Object.entries(comp.body)) {
      if (HIDDEN.has(k)) continue;
      if ((comp.classId === 4 || comp.classId === 224) && k === 'm_LocalRotation') continue;
      body.appendChild(this.propertyRow(label(k), v, 0));
      rows++;
    }
    if (rows === 0 && comp.classId !== 4 && comp.classId !== 224) {
      const e = document.createElement('div');
      e.className = 'prow';
      e.innerHTML = '<span class="k" style="color:var(--text-dim)">No serialized fields</span><span class="v"></span>';
      body.appendChild(e);
    }

    wrap.appendChild(body);
    return wrap;
  }

  /** Unity displays euler angles; the file stores a quaternion. Show both. */
  private appendTransformRows(body: HTMLElement, m: UnityMap): void {
    const q = m.m_LocalRotation as UnityMap | undefined;
    if (!q) return;
    const x = num(q.x, 0), y = num(q.y, 0), z = num(q.z, 0), w = num(q.w, 1);

    // ZXY order, matching Unity's Transform.localEulerAngles.
    const sinX = 2 * (w * x - y * z);
    const ex = Math.abs(sinX) >= 1 ? Math.sign(sinX) * Math.PI / 2 : Math.asin(sinX);
    const ey = Math.atan2(2 * (w * y + x * z), 1 - 2 * (x * x + y * y));
    const ez = Math.atan2(2 * (w * z + x * y), 1 - 2 * (x * x + z * z));
    const deg = (r: number) => +(r * 180 / Math.PI).toFixed(3);

    const row = document.createElement('div');
    row.className = 'prow';
    row.innerHTML =
      '<span class="k">Rotation</span>' +
      `<span class="v vec"><span><b>X</b>${deg(ex)}</span><span><b>Y</b>${deg(ey)}</span>` +
      `<span><b>Z</b>${deg(ez)}</span></span>`;
    body.appendChild(row);
  }

  private propertyRow(name: string, value: UnityValue, depth: number): HTMLElement {
    const row = document.createElement('div');
    row.className = depth > 0 ? 'prow nested' : 'prow';
    const k = document.createElement('span');
    k.className = 'k';
    k.textContent = name;
    const v = document.createElement('span');
    v.className = 'v';
    this.fillValue(v, value, depth);
    row.append(k, v);
    return row;
  }

  private fillValue(el: HTMLElement, value: UnityValue, depth: number): void {
    if (value === null) { el.textContent = 'null'; el.style.color = '#6f6f6f'; return; }

    if (typeof value === 'number') { el.textContent = fmtNumber(value); return; }
    if (typeof value === 'boolean') { el.textContent = value ? 'true' : 'false'; return; }
    if (typeof value === 'string') {
      el.textContent = value === '' ? '—' : value;
      if (value === '') el.style.color = '#6f6f6f';
      return;
    }

    if (Array.isArray(value)) {
      if (value.length === 0) { el.textContent = '[] (0)'; el.style.color = '#6f6f6f'; return; }
      el.textContent = `[${value.length}]`;
      if (depth < 2) {
        const box = document.createElement('div');
        for (let i = 0; i < Math.min(value.length, 12); i++) {
          box.appendChild(this.propertyRow(`[${i}]`, value[i], depth + 1));
        }
        if (value.length > 12) {
          const more = document.createElement('div');
          more.className = 'prow nested';
          more.innerHTML = `<span class="k">…</span><span class="v">${value.length - 12} more</span>`;
          box.appendChild(more);
        }
        el.appendChild(box);
      }
      return;
    }

    const map = value as UnityMap;

    if (isRef(map)) {
      const guid = map.guid as string | undefined;
      const fid = map.fileID as number;
      const span = document.createElement('span');
      span.className = fid === 0 && !guid ? 'ref null' : 'ref';
      span.textContent = fid === 0 && !guid
        ? 'None'
        : guid ? `${String(guid).slice(0, 8)}…:${fid}` : `fileID ${fid}`;
      el.appendChild(span);
      return;
    }

    const kind = isVecLike(map);
    if (kind === 'vec') {
      el.className = 'v vec';
      for (const axis of ['x', 'y', 'z', 'w']) {
        if (!(axis in map)) continue;
        const s = document.createElement('span');
        s.innerHTML = `<b>${axis.toUpperCase()}</b>`;
        s.append(fmtNumber(num(map[axis], 0)));
        el.appendChild(s);
      }
      return;
    }
    if (kind === 'color') {
      const r = num(map.r, 0), g = num(map.g, 0), b = num(map.b, 0), a = num(map.a, 1);
      const to255 = (x: number) => Math.round(Math.max(0, Math.min(1, x)) * 255);
      const sw = document.createElement('span');
      sw.className = 'swatch';
      sw.style.background = `rgba(${to255(r)},${to255(g)},${to255(b)},${a})`;
      el.appendChild(sw);
      el.append(`${fmtNumber(r)}, ${fmtNumber(g)}, ${fmtNumber(b)}, ${fmtNumber(a)}`);
      return;
    }

    const keys = Object.keys(map);
    el.textContent = `{${keys.length}}`;
    if (depth < 2) {
      const box = document.createElement('div');
      for (const key of keys.slice(0, 16)) {
        box.appendChild(this.propertyRow(label(key), map[key], depth + 1));
      }
      el.appendChild(box);
    }
  }

  /** Unity's transform values are left-handed; the viewer's are not. Show both. */
  private worldCard(node: NodeInfo): HTMLElement {
    const o = node.object3d;
    o.updateWorldMatrix(true, false);
    const p = o.getWorldPosition(new THREE.Vector3());
    const wrap = document.createElement('div');
    wrap.className = 'comp';
    wrap.innerHTML =
      `<div class="comp-head"><span class="tw"> </span><span class="ic">${namedIcon('transform')}</span>` +
      '<span class="ti">World Position</span></div>';
    const body = document.createElement('div');
    body.className = 'comp-body';
    const f = (n: number) => n.toFixed(3);
    body.innerHTML =
      `<div class="prow"><span class="k">Unity (left-handed)</span>` +
      `<span class="v vec"><span><b>X</b>${f(p.x)}</span><span><b>Y</b>${f(p.y)}</span>` +
      `<span><b>Z</b>${f(-p.z)}</span></span></div>` +
      `<div class="prow"><span class="k">three.js (right-handed)</span>` +
      `<span class="v vec"><span><b>X</b>${f(p.x)}</span><span><b>Y</b>${f(p.y)}</span>` +
      `<span><b>Z</b>${f(p.z)}</span></span></div>`;
    wrap.appendChild(body);
    return wrap;
  }
}
