/**
 * The seam between this project and the vendored Kokraf mesh kernel.
 *
 * Everything Kokraf-shaped is confined to this file. The rest of `src/model/`
 * talks to `MeshEngine` and knows nothing about vertex–edge–face internals,
 * so replacing the kernel later means rewriting this module and nothing else.
 * That matters here specifically because Kokraf is BSL-licensed — see
 * `vendor/kokraf/NOTICE.md`.
 *
 * Two things this layer is responsible for:
 *
 *  - **Unity coordinates.** Callers give and receive Unity-space values
 *    (left-handed, +Y up, +Z forward, one unit = one metre). The kernel and
 *    three.js are right-handed, so positions cross `x, y, -z` at this boundary
 *    and nowhere else.
 *  - **Headless operation.** Kokraf's operations are written for a mouse: they
 *    want an editor, a drag handle and a gizmo. We drive the underlying
 *    `VertexEditor` and the operations' static algorithm entry points instead,
 *    which take a mesh and a selection and nothing more.
 */

import * as THREE from 'three';
import { toThreePosition, toUnityPosition, type UVec3 } from '../unity/Coords.ts';

// @ts-expect-error -- vendored JavaScript, no type declarations upstream
import { MeshData } from '../../vendor/kokraf/js/core/MeshData.js';
// @ts-expect-error -- vendored JavaScript
import { MeshRendererAdapter } from '../../vendor/kokraf/js/geometry/MeshRendererAdapter.js';
// @ts-expect-error -- vendored JavaScript
import { VertexEditor } from '../../vendor/kokraf/js/vertex/VertexEditor.js';
// @ts-expect-error -- vendored JavaScript
import { ExtrudeOps } from '../../vendor/kokraf/js/operations/ExtrudeOps.js';

/**
 * A vertex position as the kernel stores it.
 *
 * Upstream is inconsistent: `MeshData.addVertex` keeps whatever it is handed,
 * and the duplicate path passes plain `{x, y, z}` literals while other paths
 * pass `THREE.Vector3`. Anything reading positions back therefore has to work
 * with components rather than Vector3 methods.
 */
interface KernelPosition { x: number; y: number; z: number }

/** How normals are generated when the mesh is turned into renderable geometry. */
export type ShadingMode = 'flat' | 'smooth' | 'auto';

export interface FaceRef { id: number; }

/**
 * Minimal stand-in for Kokraf's editor.
 *
 * `VertexEditor` only ever reaches for `editor.signals` (to tell a UI that
 * something changed) and `editor.execute` / `editor.add` on the interactive
 * paths we do not use. A recording no-op satisfies it and keeps upstream
 * unpatched.
 */
function headlessEditor(): Record<string, unknown> {
  const signal = () => ({ dispatch: () => {}, add: () => {}, remove: () => {} });
  return {
    signals: new Proxy({}, { get: () => signal() }),
    execute: (command: { execute?: () => void }) => command?.execute?.(),
    add: () => {},
  };
}

/**
 * One editable mesh: Kokraf's adjacency data plus the three.js object the
 * operations expect to find it on.
 */
export class EditableMesh {
  readonly object: THREE.Mesh;
  private readonly editor: ReturnType<typeof headlessEditor>;
  private readonly vertexEditor: {
    setObject(o: THREE.Mesh): void;
    updateGeometry(): void;
  } & Record<string, never>;
  /** True once the kernel's incremental render buffer exists and is current. */
  private geometryReady = false;

  constructor(name = 'Mesh') {
    this.object = new THREE.Mesh(new THREE.BufferGeometry(), new THREE.MeshStandardMaterial());
    this.object.name = name;
    this.object.userData.meshData = new MeshData();
    // The kernel reads the shading mode off userData when it rebuilds.
    this.object.userData.shading = 'flat';
    this.editor = headlessEditor();
    this.vertexEditor = new VertexEditor(this.editor);
    this.vertexEditor.setObject(this.object);
  }

  /** The raw kernel mesh. Use the typed helpers below in preference to this. */
  get data(): Record<string, Map<number, unknown>> {
    return this.object.userData.meshData;
  }

  get counts(): { vertices: number; edges: number; faces: number } {
    const d = this.data;
    return { vertices: d.vertices.size, edges: d.edges.size, faces: d.faces.size };
  }

  /** Add a vertex at a Unity-space position. Returns its id. */
  addVertex(p: UVec3): number {
    const v = (this.data as never as { addVertex(v: THREE.Vector3): { id: number } })
      .addVertex(toThreePosition(p));
    return v.id;
  }

  /**
   * Add a face from vertex ids, wound so its normal follows Unity's
   * left-handed convention (counter-clockwise seen from the outside).
   */
  addFace(vertexIds: number[]): number {
    const d = this.data as never as {
      vertices: Map<number, unknown>;
      addFace(v: unknown[]): { id: number };
    };
    // Crossing the mirror reverses handedness, so winding has to flip with it
    // or every face ends up inside-out.
    const verts = [...vertexIds].reverse().map((id) => d.vertices.get(id));
    if (verts.some((v) => v === undefined)) throw new Error('addFace: unknown vertex id');
    return d.addFace(verts).id;
  }

  private rawPosition(id: number): KernelPosition {
    const v = (this.data.vertices as Map<number, { position: KernelPosition }>).get(id);
    if (!v) throw new Error(`no vertex ${id}`);
    return v.position;
  }

  /** Unity-space position of a vertex. */
  vertexPosition(id: number): UVec3 {
    const p = this.rawPosition(id);
    return toUnityPosition(new THREE.Vector3(p.x, p.y, p.z));
  }

  /** Move a vertex to a Unity-space position. */
  setVertexPosition(id: number, p: UVec3): void {
    const target = toThreePosition(p);
    const raw = this.rawPosition(id);
    raw.x = target.x; raw.y = target.y; raw.z = target.z;
  }

  /** Shift a vertex by a Unity-space delta. */
  moveVertex(id: number, delta: UVec3): void {
    const d = toThreePosition(delta);
    const raw = this.rawPosition(id);
    // toThreePosition flips z, and a delta must flip with it.
    raw.x += d.x; raw.y += d.y; raw.z += d.z;
  }

  get faceIds(): number[] { return [...(this.data.faces as Map<number, unknown>).keys()]; }
  get vertexIds(): number[] { return [...(this.data.vertices as Map<number, unknown>).keys()]; }

  /** Vertex ids of a face, in winding order. */
  faceVertices(faceId: number): number[] {
    const f = (this.data.faces as Map<number, { vertexIds: number[] }>).get(faceId);
    if (!f) throw new Error(`no face ${faceId}`);
    return [...f.vertexIds];
  }

  /** Centre of a face, in Unity space. */
  faceCenter(faceId: number): UVec3 {
    const ids = this.faceVertices(faceId);
    const sum = ids.reduce(
      (acc, id) => {
        const p = this.vertexPosition(id);
        return { x: acc.x + p.x, y: acc.y + p.y, z: acc.z + p.z };
      },
      { x: 0, y: 0, z: 0 },
    );
    return { x: sum.x / ids.length, y: sum.y / ids.length, z: sum.z / ids.length };
  }

  /**
   * Extrude faces along a Unity-space offset.
   *
   * Kokraf's `ExtrudeOps` instance methods run a drag session; the static
   * `buildExtrusion` is the algorithm, and it wants only the editing engine,
   * the mesh and a selection. We call that, then translate the new vertices
   * ourselves — which is what the drag would eventually have done.
   */
  extrudeFaces(faceIds: number[], offset: UVec3): number[] {
    if (faceIds.length === 0) return [];
    // Topology edits maintain the kernel's render buffer incrementally, so it
    // has to exist first. Building straight after addVertex/addFace would
    // dereference an undefined buffer.
    this.syncGeometry();

    // The kernel derives the extrusion's side walls from the boundary edges of
    // the selection, and it only counts an edge as selected when *both* of its
    // vertices are in the vertex selection. The interactive editor keeps the
    // vertex, edge and face selections in sync as a side effect of clicking,
    // so its own call sites never pass this explicitly. Headless, the omission
    // is silent and costly: the faces are duplicated and pushed out, no walls
    // are built to join them to the shell, and the result is a detached lid
    // floating over a hole rather than a raised panel.
    const vertexIds = [...new Set(faceIds.flatMap((id) => this.faceVertices(id)))];

    const result = ExtrudeOps.buildExtrusion(
      this.vertexEditor, this.data, 'face',
      { vertexIds, edgeIds: [], faceIds },
    ) as { newVertexIds?: number[] } | null;
    if (!result?.newVertexIds) throw new Error('extrude produced nothing');

    for (const id of result.newVertexIds) this.moveVertex(id, offset);
    // Positions changed underneath the incremental buffer; force a rebuild.
    this.geometryReady = false;
    return result.newVertexIds;
  }

  /** Choose how normals are generated. Flat is the low-poly look. */
  setShading(mode: ShadingMode): void {
    this.object.userData.shading = mode;
    this.geometryReady = false;
  }

  /**
   * Make sure the kernel's geometry and render buffer exist and are current.
   *
   * `updateGeometry` rebuilds both together, which is what the incremental
   * topology operations rely on.
   */
  private syncGeometry(): void {
    this.vertexEditor.updateGeometry();
    this.geometryReady = true;
  }

  /**
   * Rebuild renderable geometry from the adjacency data and return it,
   * trimmed to exactly the data in use.
   *
   * The kernel deliberately over-allocates its buffers to twice what the mesh
   * needs, so interactive edits have somewhere to write without reallocating,
   * and zero-fills the slack. That is the right trade for an editor and the
   * wrong one for a shipped asset: the padding shows up as unused vertices at
   * the origin and degenerate triangles, which bloat the GLB and corrupt
   * anything that reasons over the vertex set (bounding boxes, the Unity
   * round-trip check). A full rebuild lays the live data out as a contiguous
   * prefix, so trimming to the allocators' used counts is exact.
   */
  buildGeometry(): THREE.BufferGeometry {
    this.syncGeometry();
    const padded = this.object.geometry as THREE.BufferGeometry;
    const buffer = this.object.userData.renderBuffer as {
      slotAllocator?: { usedCount: number };
      indexSlotAllocator?: { usedCount: number };
    } | undefined;
    const usedVerts = buffer?.slotAllocator?.usedCount;
    const usedIndices = buffer?.indexSlotAllocator?.usedCount;
    if (usedVerts === undefined || usedIndices === undefined) return padded;

    const compact = new THREE.BufferGeometry();
    for (const name of ['position', 'normal', 'uv'] as const) {
      const attr = padded.getAttribute(name) as THREE.BufferAttribute | undefined;
      if (!attr) continue;
      const size = attr.itemSize;
      compact.setAttribute(name, new THREE.BufferAttribute(
        (attr.array as Float32Array).slice(0, usedVerts * size), size,
      ));
    }
    const index = padded.getIndex();
    if (index) {
      compact.setIndex(new THREE.BufferAttribute(
        (index.array as Uint32Array).slice(0, usedIndices), 1,
      ));
    }
    compact.computeBoundingBox();
    compact.computeBoundingSphere();
    padded.dispose();
    this.object.geometry = compact;
    return compact;
  }

  /** Whether the geometry currently matches the mesh data. */
  get isGeometryCurrent(): boolean { return this.geometryReady; }
}
