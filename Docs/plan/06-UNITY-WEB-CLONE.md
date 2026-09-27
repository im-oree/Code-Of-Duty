# 06 — The Unity Web Clone (`UnityWeb/`)

## 1. Why this exists

The agent working on this project has **no Unity Editor** (confirmed: no Unity install, no
`dotnet`, no `mono` in the sandbox). Without a way to render the project, every claim about how
something *looks* is a guess. Animation, pose, lighting and UI work is unverifiable.

So we build a **Unity runtime clone in TypeScript + three.js** that reads the project's *real*
files — `.unity` scenes, `.prefab`s, `.mat`erials, `.asset`s, FBX/GLB models — and renders them in
a headless browser we can screenshot.

**It is not a game engine replacement.** It is a *faithful viewer + scripting sandbox* whose only
job is: *make what Unity would show, appear in a PNG the agent can read.*

Secondary payoff (and the reason it lives outside `Assets/`): the user can open it locally, load
scene files, and inspect the project in a browser without launching Unity. It is reusable across
sessions and across projects.

## 2. Hard requirements

| # | Requirement | Source |
|---|---|---|
| R1 | Lives **outside** `Assets/` so Unity never imports or compiles it | user |
| R2 | Opens real Unity scene files and renders them "properly with textures" | user |
| R3 | Replicates Unity's axis system and camera system | user |
| R4 | Supports character models, animations, and (progressively) scripts | user |
| R5 | Sky, fog, lighting — the scene should *look alike* | user |
| R6 | Runs headless for agent screenshots **and** hosted for the user's browser | doc 07 |
| R7 | Built progressively — usable early, deepened over time | user |

## 3. Coordinate system fidelity (R3) — the part that must be exactly right

Unity is **left-handed, Y-up, Z-forward**, rotations are **Z→X→Y** intrinsic euler, and it uses
**clockwise** front faces with UV origin **bottom-left**. three.js is **right-handed, Y-up,
Z-backward**, euler default **XYZ**, **counter-clockwise** front faces, UV origin bottom-left.

The conversion is a mirror on Z:

```ts
// position: Unity (x, y, z)  ->  three (x, y, -z)
export const v3 = (u: UVec3) => new THREE.Vector3(u.x, u.y, -u.z);

// quaternion: negate x and y (equivalently: conjugate then negate z)
// Unity q=(x,y,z,w) -> three (-x, -y, z, w)
export const quat = (u: UQuat) => new THREE.Quaternion(-u.x, -u.y, u.z, u.w);

// euler degrees: Unity applies Z, then X, then Y (intrinsic)
export function euler(u: UVec3) {
  const e = new THREE.Euler(
    THREE.MathUtils.degToRad(u.x),
    THREE.MathUtils.degToRad(u.y),
    THREE.MathUtils.degToRad(u.z), 'ZXY');
  return quat(new THREE.Quaternion().setFromEuler(e));   // then mirror
}

// scale is unchanged: (x, y, z)
// directions (forward/up/right) follow the same z-negation
```

**Winding:** because we mirror one axis, triangle winding flips. We do *not* flip index buffers
(expensive, and breaks skinning); instead every imported material gets `side` handling and we set
`renderer.outputColorSpace`, then compensate by scaling the *scene root* rather than per-mesh.
The chosen approach: **convert at the transform level only** (positions/rotations), and leave mesh
data untouched — Unity's FBX/GLB importers already produce geometry in a consistent handedness
that three.js loaders read correctly.

**Camera:** Unity cameras look down **+Z** in their local space; three.js cameras look down **−Z**.
After the position/rotation mirror above, a Unity camera's forward maps correctly with **no extra
180° yaw** — this is the single most common mistake and we assert it in a unit test
(`tests/coords.test.ts`) using a known scene with a camera at a known transform.

Field of view: Unity's `field of view` is **vertical** by default, same as three.js `fov`.
`orthographic size` = half-height → `top/bottom = ±size`.

## 4. File formats we must parse

### 4.1 Unity YAML
Unity's serialized format is YAML 1.1 with a custom tag scheme:

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1234567890            # classID 1 = GameObject, anchor = fileID
GameObject:
  m_Component:
  - component: {fileID: 1234567891}
  m_Name: MenuStage
```

It is **not** standard YAML (duplicate keys, the `!u!<classID> &<fileID>` document header,
`stripped` prefab documents). We write a purpose-built streaming parser rather than using a generic
YAML library — it's faster, tolerant, and we only need a subset.

Observed in `StartMenu.unity` (1,644 documents) — class IDs to support, by frequency:

| classID | Type | Count | Priority |
|---|---|---|---|
| 1 | GameObject | 484 | P0 |
| 114 | MonoBehaviour | 322 | P0 (uGUI components live here) |
| 224 | RectTransform | 304 | P0 |
| 222 | CanvasRenderer | 252 | P0 |
| 4 | Transform | 180 | P0 |
| 33 | MeshFilter | 36 | P0 |
| 23 | MeshRenderer | 36 | P0 |
| 21 | Material | 9 | P0 |
| 108 | Light | 3 | P0 |
| 82 | AudioSource | 2 | P2 |
| 198/199 | ParticleSystem / Renderer | 2 | P2 |
| 137 | SkinnedMeshRenderer | 2 | P1 |
| 20 | Camera | 1 | P0 |
| 223 | Canvas | 1 | P0 |
| 95 | Animator | 1 | P1 |
| 81 | AudioListener | 1 | P3 |
| 104 | RenderSettings | 1 | P0 (fog, ambient, skybox) |
| 157 | LightmapSettings | 1 | P3 |
| 29 | OcclusionCullingSettings | 1 | skip |
| 196 | NavMeshSettings | 1 | skip |
| 1660057539 | SceneRoots | 1 | P0 (root ordering) |

### 4.2 `.meta` files → GUID map
Every asset has a `.meta` with a `guid`. References in scenes are `{fileID: N, guid: G, type: T}`.
We build a project-wide `guid → relative path` index once at startup (walk `Assets/**/*.meta`,
~2,000 files, <1 s) and cache it.

### 4.3 MonoBehaviour fields
A `MonoBehaviour` document stores `m_Script: {fileID: 11500000, guid: <script guid>}` plus its
serialized fields by name. We resolve the script GUID → `.cs` path → class name, and dispatch to a
**component handler registry**. Unknown scripts are kept as inert data (name + fields) so the
inspector can show them; known ones (uGUI `Image`, `Text`, `Button`, TMP, our own) get behaviour.

### 4.4 Models
- **GLB/glTF** — `GLTFLoader` (native three.js). Primary path; our procedural pipeline emits GLB.
- **FBX** — `FBXLoader` (three.js addon). Handles the kit's existing character/weapon FBXs and
  their embedded animation takes.
- Unity `.fbx.meta` carries import settings (scale factor, rig type, clip splits). We read
  `scaleFactor` and clip definitions so animation splitting matches Unity.

## 5. Module layout

```
UnityWeb/
├── package.json            vite + three + typescript, no framework
├── src/
│   ├── unity/
│   │   ├── YamlParser.ts        streaming Unity-YAML → documents
│   │   ├── GuidIndex.ts         .meta walk → guid/path map
│   │   ├── ClassIds.ts          classID → type name table
│   │   ├── AssetDatabase.ts     load/cache scenes, prefabs, materials, assets
│   │   ├── PrefabResolver.ts    prefab instances + m_Modifications overrides
│   │   └── Coords.ts            the conversion in §3 (unit-tested)
│   ├── runtime/
│   │   ├── GameObject.ts        name, tag, layer, active, components
│   │   ├── Transform.ts         Unity-semantics transform over THREE.Object3D
│   │   ├── Component.ts         base + lifecycle (Awake/Start/Update/LateUpdate)
│   │   ├── SceneRuntime.ts      the loop: fixed + variable update, time, deltaTime
│   │   └── components/          MeshRenderer, SkinnedMeshRenderer, Light, Camera,
│   │                            Animator, Canvas, RectTransform, Image, Text, Button…
│   ├── render/
│   │   ├── Renderer.ts          WebGLRenderer setup matching URP-ish output
│   │   ├── MaterialMapper.ts    Unity/URP Lit + Unlit → MeshStandardMaterial
│   │   ├── Sky.ts               procedural skybox matching Unity's default
│   │   └── Fog.ts               Unity fog modes (Linear/Exp/Exp2) → three fog
│   ├── ui/
│   │   └── CanvasRenderer.ts    uGUI layout → an orthographic overlay scene
│   ├── scripting/
│   │   └── ScriptHost.ts        TS stand-ins for our C# behaviours (see §7)
│   ├── viewer/                  the user-facing browser app (scene picker, gizmos,
│   │                            hierarchy tree, inspector, play/pause, camera fly)
│   └── headless/
│       ├── capture.ts           CLI: render a scene/camera to PNG
│       └── harness.ts           scripted sequences → screenshot sets (doc 07)
└── tests/                       coords, parser, prefab-override tests
```

## 6. Rendering fidelity plan (R2, R5)

We are not reimplementing URP. We are matching what a viewer needs to judge a frame.

| Unity feature | Our approach | Tier |
|---|---|---|
| URP/Lit | `MeshStandardMaterial` — `_BaseColor`→`color`, `_Metallic`, `_Smoothness`→`1-roughness`, `_BaseMap`, `_BumpMap`, `_EmissionColor` | P0 |
| URP/Unlit | `MeshBasicMaterial` | P0 |
| Directional/Point/Spot lights | Direct equivalents; intensity scaled (Unity candela vs three) | P0 |
| Ambient (Flat / Trilight / Skybox) | `HemisphereLight` or `Scene.environment` from a generated PMREM | P0 |
| Fog (Linear/Exp/Exp2) | `THREE.Fog` / `FogExp2` | P0 |
| Default skybox | Procedural gradient sky matching Unity's built-in | P0 |
| Shadows | `PCFSoftShadowMap`, cascade approximation | P1 |
| Post (tonemap, bloom, vignette) | `EffectComposer` — only if the scene has a URP volume | P2 |
| Skinned meshes + Animator | three `SkinnedMesh` + `AnimationMixer`, Animator controller state approximated | P1 |
| Particles | Cheap billboard approximation | P2 |
| Lightmaps / GI | Ignored (we use realtime) — noted as a known divergence | — |

**Known-divergence policy:** anything we do not match exactly is listed in
`UnityWeb/DIVERGENCES.md`, so a screenshot is never mistaken for ground truth where it isn't.

## 7. Scripting (R4) — progressive, three tiers

We cannot execute C# in the browser. We don't need to. Three tiers, adopted progressively:

**Tier 1 — Static (P0).** Components are data. The scene renders in its authored state. Enough to
verify layout, lighting, materials, UI composition, and model scale/orientation.

**Tier 2 — Mirrored behaviours (P1, the sweet spot).** For the handful of scripts whose *visual*
output we need to iterate on (menu layout, `WeaponMovementPose`, `OperatorDisplay`, camera rigs),
we hand-write a **TypeScript mirror** in `src/scripting/mirrors/` with the *same field names* as
the C# class. Fields are populated from the scene YAML, so tuning a value in Unity's Inspector is
immediately reflected in the harness. The mirror implements `Update`/`LateUpdate` with the same
math.

This is honest and it is cheap: we mirror maybe 10 classes, not 129. Each mirror carries a header
comment pointing at its C# source and a `// MIRROR-OF: Assets/Scripts/...` tag, and a lint warns
when the C# file changes without the mirror being touched.

**Tier 3 — Compiled C# via WASM (P9+, optional).** If mirroring ever becomes a burden, the path is
a C#→WASM toolchain compiling `COD.Sim` (which by design has no rendering dependencies) and
binding it to the TS runtime. Explicitly **not** attempted before P9; the architecture in doc 05
(`Sim` has no `Presentation` reference) is what makes it possible later.

## 8. Usage

```bash
cd UnityWeb
npm install

# Agent: headless screenshot of a real Unity scene
npm run shot -- --scene Assets/Scenes/StartMenu.unity --camera Main --out ../Artifacts/menu.png
npm run shot -- --prefab Assets/Prefabs/Player.prefab --orbit 35 --out ../Artifacts/player.png

# Agent: a scripted sequence (doc 07)
npm run harness -- tacsprint

# User: interactive viewer in a browser (binds 0.0.0.0 for the Arena preview)
npm run dev
```

## 9. Build order

| Step | Deliverable | Gate |
|---|---|---|
| 1 | `YamlParser` + `GuidIndex` + `ClassIds` | Parses `StartMenu.unity` into 1,644 typed docs |
| 2 | `Coords` + unit tests | Round-trip transform tests pass |
| 3 | `SceneRuntime` + Transform/GameObject graph | Hierarchy built, parented correctly |
| 4 | Mesh/Material/Light/Camera/RenderSettings | First lit PNG of a 3D scene |
| 5 | Headless capture CLI | Agent reads a PNG of `StartMenu` |
| 6 | Prefab resolution + overrides | Prefab instances render |
| 7 | FBX/GLB model loading | Operator + weapons appear |
| 8 | uGUI canvas rendering | Menu UI appears |
| 9 | Skinning + `AnimationMixer` | Operator idles |
| 10 | Script mirrors + harness sequences | Tac-sprint iteration loop live |
| 11 | Viewer app (hierarchy/inspector/gizmos) | User can browse the project in a browser |

Steps 1–5 are P0. Steps 6–10 land through P1–P2. Step 11 is continuous.

## 10. Divergence discipline

Any time the harness and real Unity disagree, the **harness is wrong until proven otherwise**. We
record a case in `UnityWeb/DIVERGENCES.md` with: scene, camera, what we render, what Unity renders
(user-supplied screenshot), and the fix or the accepted gap. The harness earns trust by being
audited, not by being asserted.
