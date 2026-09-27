# 29 — Phase 1 Work Plan (task level)

The user's instruction: *"fix issues first before anything"*, then build. This is the execution
plan for P0 + P1 (doc 04), broken into commits.

---

## Track 0 — Foundation (runs first, unblocks everything)

| # | Task | Output | Status |
|---|---|---|---|
| 0.1 | Verify a headless browser with WebGL exists | Chromium 131 + SwiftShader, 1.1 s/frame | ✅ **done** |
| 0.2 | Clone Three-FPS, gitignore it, keep it out of `Assets/` | `Reference/Three-FPS`, 261 files inventoried | ✅ **done** |
| 0.3 | Full feature inventory of the reference | `02-REFERENCE-FEATURE-MAP.md` | ✅ **done** |
| 0.4 | Audit the Unity project, enumerate defects | `03-CURRENT-STATE-AUDIT.md` (D1–D7) | ✅ **done** |
| 0.5 | Write the plan | 30 docs in `Docs/plan/` | ✅ **done** |
| 0.6 | `Tools/setup-headless-browser.sh` | Reproducible browser install | ☐ |
| 0.7 | `UnityWeb` scaffold + Unity YAML parser + GUID index | Parses `StartMenu.unity` (1,644 docs) | ☐ |
| 0.8 | Coordinate conversion + unit tests | `Coords.ts`, tests green | ☐ |
| 0.9 | Scene graph + mesh/material/light/camera + RenderSettings | First lit render of a real scene | ☐ |
| 0.10 | Headless capture CLI | `npm run shot` → PNG the agent can read | ☐ |
| 0.11 | Viewer app served for the user | Live preview in the browser | ☐ |
| 0.12 | `Tools/lint-csharp.mjs` | Gate A operational | ☐ |

---

## Track 1 — Bug fixes (D1–D4, D6)

### 1.1 Kill the bake/purge architecture  → fixes D1, D2, D4

**Delete:**
- `[ExecuteAlways]` + `OnEnable` + `DeferredBakeMenu` from `CODMainMenu`
- `DestroyPreview`, `DedupePreview`, `MatchesMenuName`, `PreviewRootNames`
- `Bootstrap` / `TrySpawn` / `OnSceneLoaded` (the `RuntimeInitializeOnLoadMethod` spawner)
- `HideLegacyMenu`
- The `Phase(label, action)` swallow-and-continue wrapper

**Replace with:**
```csharp
// Frontend scene contains the real, saved hierarchy.
// This component BINDS to it. It never creates or destroys menu content.
public class FrontendController : MonoBehaviour
{
    [SerializeField] MenuStage   stage;        // assigned in the Inspector
    [SerializeField] Canvas      canvas;
    [SerializeField] OperatorDisplay operatorDisplay;
    [SerializeField] TabBar      tabBar;

    void Awake()
    {
        if (!ValidateWiring()) return;   // logs precisely what's missing, then stops
        BindTabs(); BindPlayTab(); BindOperators(); BindLoadout();
    }
}
```
Plus a **one-time, on-demand** editor command `COD / UI / Generate Frontend Scene` that builds the
hierarchy and saves it. Never automatic.

**Acceptance:** enter Play → exactly one `MenuStage`, one `OperatorDisplay`, one menu `Canvas`;
an EditMode + PlayMode test asserts the counts; an Inspector edit to a menu object survives Play.

### 1.2 Operator animation + weapon in hand → fixes D3
- `OperatorDisplay` gets an `Animator` with `Loco_Idle` (generated clip, doc 23 §6) and a subtle
  idle-fidget.
- The selected primary is instantiated at `Attach_HandR` / `Grip_R` with correct hand IK.
- Changing operator or weapon updates in place — no destroy/recreate.

### 1.3 Boot scene + service ownership
- New `Boot` scene: creates the `DontDestroyOnLoad` service root
  (`NetworkManager`, `SettingsService`, `InputService`, `AudioManager`), then loads `Frontend`.
- Remove every scattered `RuntimeInitializeOnLoadMethod` manager spawner.
- **This is the structural fix for the whole duplicate-spawn class of bugs.**

### 1.4 Adopt `IInputSource` everywhere → fixes D6 — **DONE**

`IInputSource` existed but nothing referenced it, so bots had no way to drive a character.
It is now the only path intent travels.

- **`CharacterInput`** (new, execution order −100) owns one source per character and ticks it
  once per frame, before any consumer reads. `CharacterInput.For(component)` is the single
  resolver, so two components on one body can never bind to two different sources.
- **`PlayerInputSource`** is the only class in the game that touches a device. Keyboard and
  mouse route through `InputBindings`, so remapping in Settings still works; the gamepad routes
  through the Input System (radial deadzone 0.08/0.95, response exponent 1.8). Its duplicate
  `InputMap` keybinding table was deleted — two registries for "what key is Jump" is a bug
  waiting to happen.
- **`BotBrain`** (new, execution order −90) pins when an AI may write, between the edge-clear
  and the consumers.
- Converted: `Input_Handler`, `StandState`, `CrouchState`, `RollState`, `BodySlope_Handler`,
  `GrenadeThrower`, `ViewingResistance`, `WeaponMovementPose`. Movement states reach it via
  `characterMove.InputSource`.
- `Player.prefab` now carries `CharacterInput` + `PlayerInputSource` on the root; a spawner
  turns a body over to AI with `CharacterInput.MakeBotDriven()`.
- Fixed along the way: `BotInputSource.Press()` left an action held forever (a bot that tapped
  Jump once would read as holding it for the rest of the match); `BodySlope_Handler` scaled lean
  by the *previous* frame's wall clearance.
- **Enforced** by `Tools/verify-input-seam.mjs` — fails the build if a gameplay script reads a
  device, with a justified allowlist of 7 UI/debug exemptions. Verified to actually fail on an
  injected violation.
- Also closes **F4** (tac sprint was gated on `Input.GetAxisRaw("Vertical")`, so a gamepad player
  could sprint but never tac-sprint).

### 1.6 Weapon handling bugs found in play testing — **DONE**

Reported: in a networked match the gun flashes on screen and vanishes, the hands hold nothing,
and the console throws `NullReferenceException`; separately, in *both* offline and match, firing
the full-auto rifle makes the weapon appear to switch itself, while the pistol is fine.

Two independent causes:

- **Recoil compounded instead of recovering.** `RecoilController.ApplyPositionRecoil` runs a
  one-second curve and feeds its own output back through `lastPosition`/`lastRotation`. The N4
  rifle fires every 0.07 s, so every shot killed the recovery and restarted the curve from the
  already-displaced pose — roughly fourteen times a second. The weapon walked out of the hands
  within a moment, which reads as the gun switching or disappearing the instant you hold the
  trigger. `Glok_Pistol` is `singleShoot`, so its curve had time to finish: hence "the pistol
  works". Each kick now re-bases on a captured rest pose and settles exactly back onto it.
- **The loadout swap orphaned every reference to the gun it destroyed.** `PlayerLoadout` only
  runs in a networked match (`OnStartClient`), which is exactly why offline was fine. It
  destroys a slot's default weapon and instantiates the chosen one, but the hand IK kept
  aiming at `WeaponPoint` transforms inside the destroyed object, and the new gun never received
  the parenting, slot-weight and offset events that the `GunPickUp` animation fires — so it was
  never brought into the hands. `WeaponController.RebindAfterWeaponSwap()` now re-resolves the
  IK, re-raises `OnWeaponChange` for the listeners that cache per-weapon values, and replays the
  draw against the gun that actually exists.

Hardening done alongside: `GETCurrentSlot`/`GETCurrentWeapon` are bounds-checked and include
inactive objects (`activeID` is 1-based and starts at 0, which indexed `slots[-1]`); the local
fire path respects `canShoot` so you cannot fire a gun that is mid-holster, while the network
replay path deliberately does not, so remote shots are never swallowed; and `RecoilController`
no longer writes global `Time.timeScale` every frame from a per-player component.

### 1.5 Assembly definitions
- Introduce `Contracts` → `Net` → `Sim` → `Presentation` → `UI` → `Editor` in **one** commit
  (doc 05 §3). Partial adoption creates circular-reference errors.

---

## Track 2 — Tactical sprint rework (D5)

Full spec in `10-TACTICAL-SPRINT-REWORK.md`. Commit sequence:

| # | Commit | Fixes |
|---|---|---|
| 2.1 | Move tac-sprint state into `CharacterMove` + `CharacterState`; add `MovementTuning` asset | F5, F7 |
| 2.2 | Route all input through `IInputSource`; double-tap + hold + off modes | F4 |
| 2.3 | New `TacSprintPose` asset; `PerspectiveSync` producing one `CompositePose` | — |
| 2.4 | `ViewmodelPoseDriver` — cached base pose, `LateUpdate`, absolute application, clamp-after-wobble | F1, F2, F3 |
| 2.5 | `BodyPoseDriver` + `TacSprintUpper` additive layer + `OnAnimatorIK` hands with arm-derived wrists and rig-normalised targets | F6, F8 |
| 2.6 | Delete `WeaponMovementPose.cs` | — |
| 2.7 | Harness sequence `tacsprint.json`; iterate against contact sheets until approved | — |

**Gate:** the five acceptance shots in doc 10 §7 rendered, read, and judged good. Hands visible in
every frame of both transitions.

---

## Track 3 — UI design system + redesigned front-end

| # | Task |
|---|---|
| 3.1 | `UIThemeAsset` with every token from doc 20 §3 |
| 3.2 | Component library (doc 20 §5) — all states, all pad-navigable |
| 3.3 | `UIScreen` framework + screen stack + global back + shared header/footer |
| 3.4 | Focus graph system + focus ring + reachability test |
| 3.5 | PLAY tab (doc 21 §5) |
| 3.6 | SETTINGS — one component, used by the menu tab **and** the pause menu |
| 3.7 | OPERATORS + LOADOUT tabs with the live 3D stage |
| 3.8 | LOBBY + LOADING overlays |
| 3.9 | BARRACKS + STORE (honest placeholders) |
| 3.10 | `COD / UI / Audit` enforcement command |

---

## Track 4 — Controller support

| # | Task |
|---|---|
| 4.1 | Gamepad bindings for every action (doc 08 §2) |
| 4.2 | Stick processing: deadzone, exponent, accel, ADS scaling (doc 08 §4) |
| 4.3 | Aim assist: rotational, slowdown, precision — all input-space (doc 08 §5) |
| 4.4 | Device detection with hysteresis + glyph swapping |
| 4.5 | Menu navigation: bumpers, triggers, A/B, hold-to-confirm, auto-scroll |
| 4.6 | Rebinding UI for both devices with conflict detection |
| 4.7 | Haptics service |

---

## Track 5 — LAN verification & hardening

The LAN system is reported as mostly working. Tasks:

| # | Task |
|---|---|
| 5.1 | Broadcast on all interfaces; directed-probe response; mDNS fallback |
| 5.2 | Version hash in the beacon + mismatch state in the browser UI |
| 5.3 | Beacon payload: map, mode, player count, passworded, platform |
| 5.4 | Reconnect-with-score-intact within 60 s |
| 5.5 | Clear, actionable failure messages (no silent failures) |
| 5.6 | `Tools/net-harness` — N simulated clients, convergence + bandwidth assertions |

---

## Commit order

```
 1. tooling: setup-headless-browser + lint-csharp
 2. unityweb: scaffold + YAML parser + guid index + coords (+tests)
 3. unityweb: scene graph + render + capture CLI          ← FIRST SCREENSHOT
 4. unityweb: viewer app for the user
 5. arch: assembly definitions
 6. boot: Boot scene + service root; remove scattered spawners
 7. menu: delete bake/purge; FrontendController binds saved scene content   ← D1/D2/D4
 8. menu: generate-frontend-scene editor command
 9. menu: operator idle animation + weapon attach                          ← D3
10. input: PlayerInputSource complete; adopt IInputSource everywhere       ← D6  DONE
11. move: tac-sprint state into CharacterMove + MovementTuning             ← F5/F7  DONE
12. anim: PerspectiveSync + TacSprintPose + ViewmodelPoseDriver            ← F1/F2/F3  DONE
13. anim: BodyPoseDriver + additive layer + hand IK; delete WeaponMovementPose ← F6/F8
14. harness: tacsprint sequence; iterate to approval                       ← D5 closed
15. ui: theme asset + component library
16. ui: screen framework + header/footer + focus graph
17. ui: PLAY / SETTINGS / OPERATORS / LOADOUT / LOBBY / PAUSE
18. input: gamepad bindings, curves, aim assist, glyphs, menu nav
19. net: LAN discovery hardening + net harness
20. docs: update every spec doc with what actually shipped
```

### 1.7 Sprint ownership moved out of the viewmodel — DONE (item 11)

`WeaponMovementPose` is a cosmetic component: its job is tilting the gun. It also happened to
own the sprint rules. That had four consequences, all fixed here:

* **F5 — movement decided by a cosmetic component.** The double-tap timers, the tac-sprint
  countdown and the write to `characterMove.sprintSpeedMultiplier` all lived there. A character
  without the component could not tac sprint at all, which included every bot. The component
  also gated itself on `IsLocal`, so on every non-owned character the sprint state simply did
  not exist.
* **F7 — two thresholds for one decision.** `StandState` sprinted at `inputVector.y > 0`, the
  tac-sprint check needed `input.Move.y > 0.1`, and `IsSprinting` thresholded a *cosmetic blend*
  at `0.35` while the locomotion report thresholded the same blend at `0.5`. There was a band of
  stick deflection where the character sprinted but could never tac sprint, and another where
  fire was blocked but the state channel disagreed.
* **Locomotion was reported from animation weights**, i.e. the state channel lagged the cosmetic
  it was supposed to be driving — and it only ever reported `Idle`, `Sprint` or `TacSprint`, so
  `Walk` was never reported by anything.
* **Tuning scattered across prefabs.** Speed multiplier and durations were serialized per
  character.

Now: `CharacterMove.UpdateSprintState()` runs at the top of `Update()`, before the state ticks,
and is the single writer of the locomotion channel. `StandState` and `WeaponMovementPose` both
read `IsSprinting` / `IsTacSprinting`. Numbers live on `Assets/Settings/MovementTuning.asset`
(`MovementTuning`), with a built-in fallback so a missing reference degrades instead of throwing.

Two details worth keeping in mind for item 12:

* There is no `Walk → TacSprint` edge in the transition table, so engaging tac sprint steps
  through `Sprint`. That step is guarded by a `Locomotion == TacSprint` early-out — without it
  the pair re-fires every frame and oscillates the Carry channel between `Lowered` and `Ready`.
* Sprint is blocked by `StandState.walk`, which is driven by ADS from `CameraSwitcher`. That is
  why aiming cancels a sprint, and it is the one piece of sprint input still living outside
  `CharacterMove`.

### 1.8 One pose, applied in the right phase — DONE (item 12)

The first-person path was already correct: `WeaponSlotRig.Execute()` sets the weapon's position
and rotation *absolutely* from the hand pointers and only then adds the pose, so it cannot
accumulate. The third-person path did not do that, and the off-hand had a lifetime bug.

* **F1/F2 — the third-person weapon drifted away.** `ApplyThirdPersonPose` ran in `Update()`
  and did `tp.rotation = pose * tp.rotation`. Writing `.rotation` writes the LOCAL rotation, and
  the animator rewrites the parent BONE, never this child — so last frame's pose was still in
  the local rotation when this frame's was multiplied onto it. Once per frame, forever. Fixed by
  capturing the rest pose, restoring it before posing, and moving all transform writes to
  `LateUpdate` under `[DefaultExecutionOrder(100)]` so they land after the animator *and* after
  `RigExecutor` (order 0). Currently latent — `thirdPersonWeapon` is unassigned on the prefab —
  but it is the exact bug item 13's body driver would have inherited.
* **F3 — the muzzle clamp was not a clamp.** `maxMuzzleUpDegrees` was applied to `tacEuler.x`
  *before* blending and *before* the wobble, so the sprint offset and the wobble were both free
  to push past it. Now applied to the final value. With the shipped numbers the ceiling is not
  actually reached (peak ≈ 25° of 46°), so this is a correctness fix, not a visible one — but it
  is what makes the field mean what it says once someone tunes it.
* **Off-hand latched to the tuck point.** The original IK target was captured once and only
  refreshed when null. Swapping weapons mid tac-sprint captured either the previous gun's grip
  or — if the hand was tucked at that moment — the tuck transform itself, so "restore the
  original" restored the tuck and the left hand never returned to the handguard. Now the
  original is tracked every frame, skipping our own tuck transform.
* **Per-frame `Debug.Log` in the rig pipeline.** `LocalRig.AfterLocalRigUpdate()` logged a
  string per extension per character per frame. Removed.

The pose is now computed once per frame in `Update` and published as `PosePosition`/`PoseEuler`;
first person, third person and the off-hand all read those, so the perspectives cannot disagree.

**Still blocked:** the visible quality of tac sprint cannot be judged until `MonKent.fbx` is
re-exported with baking enabled — 10 of its 46 clips are fully flat and the best is 7.2%
non-flat, so the body does not move regardless of what the pose layer does. Items 13 and 14
are gated on that.

## Phase 1 exit criteria

- [ ] Enter Play on `Frontend`: **exactly one** operator, animated, holding the selected primary.
- [ ] Full UI present; every tab functional.
- [ ] Menu objects are saved scene content; Inspector edits survive Play.
- [ ] Navigate the entire front-end and start a solo match with **only** a gamepad.
- [ ] Tac sprint: the five acceptance shots approved; hands visible throughout both transitions;
      correct in first **and** third person.
- [ ] Zero banned-token lint hits; `COD / UI / Audit` clean.
- [ ] LAN: host on one machine, discovered and joined from another; version mismatch handled.
- [ ] The agent can render any scene in the project headlessly and look at it.
