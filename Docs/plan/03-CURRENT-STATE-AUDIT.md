# 03 — Current State Audit

Measured 2026-09-26 from the repository at commit `0e3d98f`.

## 1. By the numbers

| Metric | Value |
|---|---|
| Unity version | `6000.6.3f1`, URP 17.6.0 |
| Game C# | **129 files, 12,612 LOC** under `Assets/Scripts` |
| Networking | FishNet (vendored at `Assets/FishNet` + `Assets/External/FishNet`) |
| Scenes | `StartMenu`, `DMArena1`, `OfflineTest` |
| `StartMenu.unity` | **1,644 serialized objects** — 484 GameObjects, 322 MonoBehaviours, 304 `CanvasRenderer`, 252 `RectTransform`, 180 Transforms |
| Repo size | 74 MB |

### Script distribution

| Area | Files | LOC | Verdict |
|---|---|---|---|
| `UI/MainMenu` | 4 | **1,779** | One 1,033-LOC god class. Needs breaking up. |
| `Weapons` (all) | 27 | 2,496 | Reasonable spine, shallow feature set |
| `Network` | 10 | 1,179 | Solid; the best part of the codebase |
| `EventsSystem` (+Editor) | 20 | 1,317 | Inherited from the kit; mostly editor tooling |
| `UI` (HUD etc.) | 15 | 1,167 | Ad-hoc, no design system |
| `Rig` (+LocalRigs) | 21 | 1,062 | Procedural rig; the animation foundation |
| `Player` (+move, body) | 16 | 1,282 | State machine is thin |
| `Editor` | 3 | 563 | Game Config window |
| `Character` | 2 | 461 | `CharacterState` authority — good, underused |
| `Input` | 3 | 366 | `IInputSource` exists — **not yet adopted** |
| `Match` | 2 | 309 | Data definitions only, no match system |
| `Settings` | 3 | 281 | `InputBindings` + `GameSettings` |
| `Camera` (+Shake) | 3 | 350 | `CameraShake` is genuinely good |

## 2. What is genuinely good and must be preserved

1. **`CODNetworkManager` + `CODNetworkDiscovery` + `CODLobbyPlayer`.** The LAN story is real:
   internal host for solo, LAN host/join, UDP broadcast discovery, dedicated-server path, FishNet
   scene handoff. This is the correct spine and we build on it rather than replace it.
2. **`CameraShake`** — a perlin-noise, preset-driven, ownership-gated, Cinemachine-safe shake
   system. Keep as-is; extend with new presets.
3. **`CharacterState`** (`Assets/Scripts/Character/CharacterState.cs`, 424 LOC) — the 5-channel
   authority ported from the reference. Correct idea, must become mandatory.
4. **`IInputSource` / `PlayerInputSource` / `BotInputSource`** — the right abstraction already
   exists. Nothing uses it yet. Adopting it is Phase 1 work.
5. **`WeaponDatabase` + `PlayerLoadout`** — data-driven weapons with networked loadout application.
6. **`InputBindings` + `GameSettings`** — central registries, persisted, live-applied.
7. **Weapon Setup Wizard** (`COD / Weapons / Create Weapon From Selected Model`) — GLB → prefab
   automation. Extend rather than rewrite.

## 3. Confirmed defects (Phase 1 targets)

### 🐞 D1 — Two operators spawn in the main menu on Play

**Root cause: a self-contradictory bake/rebuild architecture in `CODMainMenu.cs`.**

Four independent mechanisms all create menu content, and they race:

| # | Mechanism | Location | Runs when |
|---|---|---|---|
| 1 | `[ExecuteAlways]` + `OnEnable` → `EditorApplication.delayCall` → `DeferredBakeMenu()` | `CODMainMenu.cs:43-79` | Every script recompile / scene open, in **edit mode** — and it calls `EditorSceneManager.MarkSceneDirty`, so the bake is *saved into the scene* |
| 2 | `Awake()` → `DestroyPreview()` | `:282` | Play start |
| 3 | `Start()` → `DestroyPreview()` → `MenuStage.Create()` → full rebuild | `:290-320` | Play start |
| 4 | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] Bootstrap()` → `TrySpawn()` → `new GameObject("CODMainMenu")` | `:233-250` | After **every** scene load |

The failure: mechanism 1 bakes `MenuStage` + `OperatorDisplay` into the saved scene as **root**
objects (`MenuStage.Create()` creates a scene-root GameObject, not a child of `CODMainMenu`).
On Play, `DestroyPreview()` sweeps by name and by component — but it uses **`DestroyImmediate`**
inside `Awake`/`Start` at runtime, while `DedupePreview()` uses deferred **`Destroy`**. Anything
destroyed with deferred `Destroy` is *still alive and still rendering for the rest of the frame*,
and `MenuStage.Create()` (called in `Start`, line 306) runs its own `DestroyImmediate` sweep
**before** the deferred destroys have been flushed. Net result: the old operator is still in the
scene while the new one is instantiated → **two operators**.

Secondary contributor: `TrySpawn` can add a *second* `CODMainMenu` if the scene's own instance
hasn't registered yet during an additive/handoff load, which doubles every subsequent build.

**Fix strategy:** delete the bake-and-purge dance entirely. See `29-PHASE-1-WORKPLAN.md` §2.

### 🐞 D2 — No UI in the main menu on Play

`Start()` wraps every step in `Phase(label, action)` which **swallows exceptions and continues**
(`:322-330`). If `Phase("3D stage", …)` or `Phase("purge baked copy", …)` throws — which it will
when `DestroyImmediate` is called on an object that is part of a prefab instance, or when
`MenuStage.Create()` finds `Camera.main == null` — the UI phase may still run but against a
half-destroyed hierarchy, and the canvas ends up parented to a destroyed object.

`HideLegacyMenu()` (`:341-348`) is also dangerous: it **deactivates every Canvas in the scene**
that isn't under a `CODMainMenu`. Combined with D1 (where the *surviving* `CODMainMenu` may not be
the one that built the canvas) this reliably blanks the UI.

### 🐞 D3 — No operator animation in the menu

`OperatorDisplay` builds the operator but nothing drives an `Animator` state; the menu operator is
posed statically. The intended behaviour ("one with animation holding the gun") requires an idle
clip + weapon attach, neither of which is wired.

### 🐞 D4 — Menu content is not in-scene / not editable

The user's requirement: *"every main menu thing is meant to be in scene not as prefab and must be
editable, I can see it in pause or play."* Today the menu is **rebuilt from code at runtime**, so
anything you change in the Inspector is destroyed on Play. Contradicts the stated goal.

### 🐞 D5 — Tactical sprint reads wrong

See `10-TACTICAL-SPRINT-REWORK.md` for the full diagnosis. Summary of code-level faults in
`Assets/Scripts/Weapons/WeaponMovementPose.cs`:

| Fault | Line | Effect |
|---|---|---|
| `ApplyThirdPersonPose` does `tp.rotation = pose * tp.rotation` and `tp.position += …` with **no cached base pose** | 226-234 | Accumulates every frame; fights the Animator; drifts |
| Pose applied in **`Update()`** | 106 | The Animator writes *after* Update → in third person the pose is overwritten. This is why TP looks broken. |
| Wobble added **after** the `maxMuzzleUpDegrees` clamp | 190-196 | Muzzle can exceed the safety ceiling |
| `Input.GetAxisRaw("Vertical")` — legacy input | 109 | **Tac sprint is unreachable on a gamepad** |
| Tac-sprint *state* lives in a weapon-pose component | whole file | Movement state owned by a cosmetic script; `CharacterMove` can't reason about it |
| Off-hand tuck target uses a hardcoded character-space point `(0.19, 1.12, 0.20)` | 47 | Breaks for any model whose height ≠ the kit's; no wrist rotation retarget → hand reads twisted |
| `IsSprinting` threshold `0.35` vs state-report threshold `0.5` | 88 / 143 | Inconsistent — a band where you can't fire but aren't "sprinting" |
| No arm/hand animation at all in TP | — | The user's "hands must stay visible" requirement is unimplemented |

### 🐞 D6 — Input abstraction is written but unused

`IInputSource` exists; `Input_Handler` and `WeaponMovementPose` still read `Input.GetAxisRaw` /
`InputBindings` directly. Until everything reads `IInputSource`, **bots cannot drive the character
and gamepad support is partial**. This blocks two headline requirements.

### 🐞 D7 — No agent-visible verification path

There is no Unity Editor in the build environment (confirmed: no Unity install, no `dotnet`, no
`mono`). `Docs/VISUAL_LOOP.md` describes an MCP relay that requires a locally running Editor on
the user's machine. **The agent currently cannot see anything it builds.** This is the highest-
priority structural problem and is why `06-UNITY-WEB-CLONE.md` exists.

## 4. Environment constraints (important)

| Capability | Status |
|---|---|
| Unity Editor | ❌ not installed in the agent sandbox |
| `dotnet` / `mono` / Roslyn | ❌ not available (so `Tools/verify-csharp.sh` cannot run here) |
| Node.js | ✅ v22.22.3 |
| Python | ✅ 3.11.2 |
| Headless Chromium + **WebGL 2.0** | ✅ **working** — obtained via npm (`@sparticuz/chromium` 131 + SwiftShader), renders a three.js scene in ~1.1 s |
| Blender | ❌ |
| Network egress | npm registry ✅, GitHub ✅, Google/Azure CDNs ❌, Debian apt ❌ |
| Hardware | 2 vCPU, 3 GB RAM, 20 GB disk |

**Consequences:**
1. All C# is written to compile but is verified by the user's Editor, plus a syntax/structure
   linter we build (`27-CODING-STANDARDS-AND-WORKFLOW.md`).
2. **All visual verification happens in the Unity Web Clone**, which renders the *real* Unity
   scene and prefab files. This is not a toy — it is the primary feedback loop.

## 5. Dependency notes

`Packages/manifest.json` already includes everything we need: Input System 1.20, Cinemachine 6.6,
glTFast 6.20, AI Navigation 2.0, ProBuilder 6.1, Timeline, TMP, Test Framework, Newtonsoft JSON,
URP 17.6. **No new packages are required for Phases 1–5.**

`com.unity.ai.assistant` / `com.unity.ai.inference` are present (MCP path) but the agent cannot
use them from this sandbox — they require the user's local Editor.

## 6. Audit verdict

The project is **a good spine with a thin body and one badly-architected limb**. Networking,
camera shake, state authority and the data layer are worth keeping. The menu is worth rewriting.
Movement and animation need to roughly triple in depth. The single biggest risk is not any
feature — it is that nothing can be *seen*, so quality claims are unverifiable. Phase 0 fixes that
before anything else is built.
