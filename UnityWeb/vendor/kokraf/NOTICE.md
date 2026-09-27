# Vendored: Kokraf modelling core

Upstream: <https://github.com/sengchor/kokraf>
Commit:   `922be29860e26dc1b70287d05ff271e83cbd9244`
Vendored: 2026-09-26

## Licence — read this before shipping

Kokraf is **Business Source License 1.1**, not an open-source licence. The full
text is in `LICENSE` beside this file. The terms that matter:

> **Licence Usage Limitation:** This software may not be used in a commercial
> application or service without purchasing a commercial license from
> kokraf.com.
>
> **Additional Use Grant:** Free for non-commercial, educational, and personal
> use.
>
> **Change Date:** May 11, 2029 — on that date it becomes Apache 2.0.

So: developing and playing this game privately is covered by the Additional Use
Grant. **Selling it, putting it behind a paywall, or running it as a commercial
service is not**, until either a licence is bought from kokraf.com or the Change
Date passes.

This is why the vendored code is confined to this directory and reached only
through `src/model/MeshEngine.ts`. That file is the single seam: swapping Kokraf
out for another mesh kernel means reimplementing one module, not unpicking the
modeller. Nothing outside `src/model/` imports `vendor/kokraf` directly, and
nothing in the Unity game project (`Assets/`) contains any of this code — the
modeller's *output* is plain glTF geometry, which carries no licence obligation.

## What was taken, and what was not

Taken — the headless modelling engine, 56 files:

| Directory | Files | What it is |
|-----------|-------|------------|
| `js/core` | 2 | `MeshData`, the vertex–edge–face adjacency model, and region snapshots |
| `js/vertex` | 12 | The editing engine: duplicate, transform, topology, selection, fill, subdivide, dissolve, delete |
| `js/operations` | 12 | Extrude, inset, bevel, loop cut, knife, edge slide, booleans |
| `js/geometry` | 7 | VEF → `BufferGeometry`, normals, triangulation, manifold repair |
| `js/commands` | 19 | Undo/redo command objects the operations construct |
| `js/utils` | 3 | Normal alignment and transform helpers |
| `js/uv` | 1 | Seam snapshots |

Not taken: the browser UI, toolbar, panels, menus, Supabase backend, project
browser, the `explore` and `about` pages, the MCP server, and all art assets.
None of it is needed to model headlessly, and leaving it out keeps the
BSL-covered surface small.

Third-party runtime dependencies these files pull in: `three`, `earcut`,
`manifold-3d` — all installed from npm under their own licences.

## Local modifications

None. The files are byte-for-byte upstream so they can be re-synced with a
plain copy. Everything we add lives in `src/model/`.

Why that matters: Kokraf's operations are written for a mouse. `ExtrudeOps`
and friends expect an interactive session — an `editor`, a drag `handle`, a
gizmo, a snap manager. Rather than patch that out of upstream, `MeshEngine.ts`
drives the underlying `VertexEditor` and the operations' *static* algorithm
entry points, which take a mesh and a selection and nothing else. Same
algorithms, no GUI.

## Kernel behaviours worth knowing

Notes from integrating this headlessly. These are upstream's design choices,
sane for an interactive editor and surprising outside one. Nothing here is
patched in the vendored copy; all of it is handled in `src/model/MeshEngine.ts`.

1. **`MeshRendererAdapter.toBufferGeometry` defaults to `mode: "angle"`, which
   matches none of its own switch cases** and returns `undefined` rather than
   throwing. Always pass `'flat' | 'smooth' | 'auto'`.

2. **Buffers are over-allocated 2x and zero-filled.** `buildDuplicatedMeshData`
   sizes the position/index arrays to twice what the mesh needs so incremental
   edits have somewhere to write. The slack is real data as far as three.js is
   concerned: unused vertices at the origin and degenerate triangles. The true
   counts are on `renderBuffer.slotAllocator.usedCount` and
   `indexSlotAllocator.usedCount`, valid as a contiguous prefix after a full
   rebuild.

3. **Topology operations need a live `renderBuffer` before they run.**
   `VertexEditor.addVertex` and friends maintain the render buffer
   incrementally, so calling them on a freshly built mesh fails with
   `Cannot read properties of undefined (reading 'vertexIdToBufferIndex')`.
   Call `updateGeometry()` first; it populates `object.userData.renderBuffer`
   and reads its shading mode from `object.userData.shading`.

4. **Vertex positions are not consistently `THREE.Vector3`.** `MeshData.addVertex`
   stores whatever it is handed, and the duplicate path passes plain `{x, y, z}`
   literals. Read and write positions by component; `v.position.add(...)` will
   throw on vertices produced by an extrusion.

5. **Selections must be internally consistent.** `buildExtrusion` derives its
   side walls from `getBoundaryEdges`, which only counts an edge when *both*
   endpoints are in the **vertex** selection. The editor keeps vertex, edge and
   face selections in sync as a side effect of clicking, so passing only
   `faceIds` silently produces no walls: the faces are duplicated and displaced,
   leaving a detached cap floating over a hole.

6. **Only the static entry points are headless-usable.** Instance methods on the
   operations expect an `editor`, a drag handle, a gizmo, a snap manager and
   live signals. `ExtrudeOps.buildExtrusion(vertexEditor, meshData, mode,
   selection)` and its siblings take everything they need as arguments.
