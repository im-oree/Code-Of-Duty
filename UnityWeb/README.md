# Unity Web Clone

A TypeScript + three.js runtime that opens **real Unity scene and prefab files** and renders them
in a browser — headless for automated screenshots, or interactive for inspection.

It lives outside `Assets/` on purpose, so Unity never imports or compiles it.

Full design: [`Docs/plan/06-UNITY-WEB-CLONE.md`](../Docs/plan/06-UNITY-WEB-CLONE.md)
Verification loop: [`Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md`](../Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md)

## Why it exists

The agent working on this project has no Unity Editor. Without a renderer, every claim about how
something *looks* is a guess. This makes animation, pose, lighting and UI work verifiable.

## Quick start

```bash
# one-time, if the sandbox has no chromium yet
../Tools/setup-headless-browser.sh

npm install

# interactive viewer (binds 0.0.0.0 for the Arena live preview)
npm run dev            # -> http://localhost:5180

# headless screenshot of a real Unity scene
npm run shot -- --scene Assets/Scenes/DMArena1.unity --out ../Artifacts/arena.png
npm run shot -- --scene Assets/Scenes/StartMenu.unity --camera orbit \
                --orbit 0.7,0.45,22,0,2,0 --size 1600x900
```

### `shot` options

| Flag | Meaning |
|---|---|
| `--scene <path>` | Project-relative `.unity` path (required) |
| `--out <path>` | Output PNG (default `../Artifacts/<scene>.png`) |
| `--camera <name>` | A camera in the scene, or `orbit` |
| `--orbit y,p,d[,tx,ty,tz]` | Orbit yaw/pitch/distance and target |
| `--size WxH` | Viewport size (default 1280x720) |
| `--chrome` | Keep the editor panels visible in the capture |
| `--overlay` | Show the scene/warning overlay |
| `--keep-server` | Leave the vite server running for the next call |

## What works today

- Unity YAML parser (`.unity`, `.prefab`, `.mat`, `.asset`) — the real format, with its quirks
- GUID → asset path index built from `Assets/**/*.meta`
- Full GameObject/Transform/RectTransform hierarchy
- Unity → three.js coordinate conversion (left→right handed, `ZXY` euler order)
- Built-in primitive meshes, URP Lit/Unlit materials, textures
- Directional / point / spot lights with shadows
- `RenderSettings`: fog (Linear/Exp/Exp2), ambient (Flat/Trilight/Skybox), default sky
- Scene cameras with Unity's FOV and clip planes
- Hierarchy tree + inspector + orbit navigation
- Headless capture that drives the *same page* the user sees

Measured on `Assets/Scenes/StartMenu.unity`: 1,644 documents → 484 GameObjects parsed and built.

## What does not work yet

Tracked honestly in [`DIVERGENCES.md`](DIVERGENCES.md). Headlines: FBX/GLB meshes, skinned
meshes, prefab instances, uGUI widget layout, particles, post-processing.

**Standing rule: the harness is wrong until proven otherwise.** When it disagrees with the Unity
Editor, log the case in `DIVERGENCES.md` and fix the harness.

## The editor shell

Opening the page gives you a Unity-shaped editor rather than a bare canvas:

- **Hierarchy** — the real scene graph, with per-type icons, search, and the
  inactive/active state Unity shows. Selecting a row drives the Inspector.
- **Inspector** — every component on the selected GameObject with its actual
  serialised fields, GUID references resolved to asset paths, and world position
  printed in both Unity's left-handed and three.js's right-handed coordinates so
  a mismatch is visible instead of inferred.
- **Project** — the whole `Assets/` **and `Packages/`** tree, served straight
  from disk. Text assets (`.mat`, `.controller`, `.cs`, `.unity`) show their
  source, which is usually faster than opening a file to answer "what is
  actually in this thing".
- **Console** — scene-build warnings, so problems surface where you are looking.
- **Scene / Game tabs** — free orbit camera versus the scene's own camera at its
  own FOV. The two disagreeing is itself a finding.

Toolbar toggles: `UI`, `Orphan UI` (UI with no Canvas ancestor — see F1 in
DIVERGENCES.md), `Gizmos`, `Wire`, plus FOV and Frame.

`?clean=1` strips the chrome for pure-viewport captures, which is what
`npm run shot` uses by default.

## Testing

```bash
npm test     # parser + layout unit tests (Node's runner, type-stripped)
npm run check  # tsc --noEmit && npm test
```

35 tests cover the Unity YAML dialect (escapes, refs, negative fileIDs, stripped
prefab docs), uGUI's anchor maths against hand-worked cases, prefab expansion, and
the modelling pipeline's geometric invariants. Every suite exists because it caught
a real bug that rendered plausibly but wrongly.

## Debugging a scene

`shot` can run arbitrary probes against the live page, which is usually faster
than adding one-off code to the viewer:

```bash
npm run shot -- --scene Assets/Scenes/StartMenu.unity --out /tmp/x.png \
  --eval "window.uw.findObjects('Operator')" \
  --eval "window.uw.skinInfo()"
```

Useful entries on `window.uw`: `report()`, `cameras()`, `hierarchy()`,
`findObjects(name)`, `frameObject(name)`, `skinInfo()`, `uiStats()`,
`setUiVisible(bool)`, `setOrphanUi(bool)`, `paintUi()`.

## Modelling

A script-driven modeller that emits Unity-ready GLB assets, so props can be
authored, reviewed and revised in the same headless loop as everything else
here — no Editor, no DCC tool, no manual export step.

```bash
npm run model -- --script models/ammo-crate.model.ts            # build + verify
npm run model -- --script models/ammo-crate.model.ts --preview  # and render it in a scene
```

Defaults: writes `Assets/Models/Generated/<name>.glb` plus a `.meta` with a
stable guid, previews into `Assets/Scenes/StartMenu.unity`, shoots to
`Artifacts/models/<name>.png`. Override with `--out`, `--into`, `--at x,y,z`,
`--shot`, `--size WxH`, `--no-verify`.

A model is a plain TypeScript function. Shapes are parametric and edits are
predicate-driven rather than index-driven, so changing a segment count does not
invalidate the edits that follow it:

```ts
export default function build(m: Modeler): void {
  m.box('Body', { size: { x: 0.86, y: 0.34, z: 0.44 },
                  segments: { x: 6, y: 2, z: 3 }, pivot: 'base' },
                { material: { color: palette.olive, roughness: 0.85 } });

  // Reinforcing ribs: pick faces by where they are, not by id.
  m.extrude('Body', { x: 0, y: 0, z: 0.022 },
            (c) => Math.abs(c.z) > 0.22 - 1e-4 && Math.abs(c.x) > 0.43 * 0.52);
}
```

Everything is authored in **Unity coordinates and metres** — +Y up, +Z forward,
pivots where a Unity prop expects them. The handedness flip to three.js and back
out through glTF happens at the boundary, and every build asserts it:
`toGlbVerified` re-imports the exported GLB and checks sampled vertices of every
part land within 0.1 mm of where Unity would place them.

### Correctness, and why it is checked this way

Bad geometry does not throw. A reversed face is not an error, it is an invisible
face, and the first crate this pipeline produced exported cleanly, passed its
round-trip check, and rendered as an open shell full of holes. So the invariants
that a render would reveal are asserted directly in `tests/model.test.ts`:

- **Winding.** No primitive states a winding; each face declares the direction it
  should face and `addFacing` derives the order, so the convention lives in one
  place instead of being re-guessed per face against a mirrored axis.
- **Orientation is verified by edge consistency and signed volume**, not by
  "does this normal point away from the centre". The latter is only true for
  convex shapes and reports correct geometry as broken as soon as a model grows
  a rib or a recess.
- **No allocator padding in shipped assets.** The mesh kernel over-allocates its
  buffers to twice the size the mesh needs as editing headroom and zero-fills
  the slack; `buildGeometry` trims to the live prefix so that padding never
  reaches a `.glb` as origin vertices and degenerate triangles.
- **Non-finite dimensions throw** instead of producing NaN geometry that is
  unrenderable and undiagnosable.

The mesh kernel itself is vendored in `vendor/kokraf/` and reached only through
`src/model/MeshEngine.ts`; see `vendor/kokraf/NOTICE.md` for provenance, the
licence position, and the kernel behaviours worth knowing about.
