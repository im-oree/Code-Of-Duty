# 02 — Reference Feature Map (Three-FPS)

**Source:** `https://github.com/im-oree/Three-FPS`
**Local clone:** `Reference/Three-FPS` (gitignored; outside `Assets/` so Unity never imports it)
**Measured:** 261 TypeScript files, **52,434 LOC**, three.js 0.170 + Vite + `ws` + Rapier3D.

This document is the **feature specification** derived from reading the actual `src/` tree. It is
not a port list — we write our own C#. It answers one question: *what does a complete version of
this game contain?*

Column `Ours` = state in the Unity project today (`✅` done, `🟡` partial, `❌` missing).

---

## 1. Engine core — `src/core` (20 files, 3,095 LOC)

| Module | Responsibility | Ours |
|---|---|---|
| `Engine`, `Clock`, `Renderer`, `SceneManager` | Fixed-step sim loop, render loop separation | ✅ (Unity) |
| `AssetLoader`, `AssetParserPool`, `workers/assetParser.worker` | Off-thread asset parsing, load budgeting | ✅ (Unity) |
| `EventBus` | Decoupled pub/sub between systems | 🟡 `EventsSystem` exists |
| `InputManager`, `InputContextStack` | Input with a **context stack** (menu > pause > vehicle > player) | 🟡 no context stack |
| `LoadProgress` | Deterministic loading screen progress | ❌ |
| `RenderLayers` | Named layer registry (viewmodel/world/UI separation) | 🟡 ad-hoc |
| `SettingsStore`, `CheatsStore` | Persisted settings; dev cheats | ✅ / ❌ |
| `quality/QualityPresets`, `AdaptiveResolution`, `RenderQualityManager`, `ShadowDirector`, `OcclusionCuller`, `StaticBatcher`, `FrustumCullingManager` | A full performance subsystem: presets, dynamic res, shadow cascade direction, occlusion, batching | 🟡 presets only |

**Takeaway:** the reference treats *performance as a feature with its own subsystem*. We must too
(see `26-PERFORMANCE-AND-QUALITY.md`).

## 2. Character authority — `src/character` (6 files, 1,704 LOC)

`CharacterStateSystem.ts` (452 LOC) is the most important file in the reference.

- **5 orthogonal channels:** `locomotion`, `traversal`, `weaponAction`, `aim`, `carry`
- Plus **facts**: `grounded`, `weaponId`
- One `TRANSITIONS` table per channel; all changes go through a validated `request()`
- **Cross-channel interaction rules** (e.g. starting a reload forces `aim → hip`)
- Bounded **audit log** and **rejection log** for debugging
- A build step (`verify:state`) fails CI if a transition table is incomplete

Also: `ThirdPersonBody` (772 LOC) — the full TP body; `RemotePlayers` (364) — remote
representation; `JointIK`, `JointSpring`, `HandSkinRegistry`.

**Ours:** `Assets/Scripts/Character/CharacterState.cs` (424 LOC) ports this idea. 🟡 — exists but
is only wired into `CharacterMove` and `WeaponMovementPose`, not the whole game.

## 3. Movement & player — `src/player` (12 files, 2,637 LOC)

| Feature | File | Ours |
|---|---|---|
| Ground/air movement, accel curves | `PlayerMovement` (435) | 🟡 `CharacterMove` |
| Sprint / **tactical sprint** (double-tap) | `PlayerMovement` | 🟡 in `WeaponMovementPose` (wrong home) |
| Slide (+ cancel) | `PlayerMovement` | ❌ |
| Crouch (hold/toggle) | `PlayerMovement` | ✅ |
| Jump + landing dip | `PlayerMovement` | ✅ / 🟡 |
| **Stamina** | `StaminaSystem` (57) | ❌ |
| **Vault / mantle** (jump-triggered, HUD prompt) | `VaultSystem` (321) | ❌ |
| Head bob | `HeadBob` (57) | 🟡 camera shake only |
| Surface-aware footsteps | `FootstepSystem` (43) | ❌ |
| Camera (FP), FOV, recoil coupling | `PlayerCamera` (361) | 🟡 Cinemachine |
| **Perspective controller** (FP↔TP) | `PerspectiveController` (239) | 🟡 `CameraSwitcher` |
| Death camera | `DeathCamera` (181) | ❌ |
| Status effects (flash/concussion/stun) | `ActiveStatusEffects` (112) | ❌ |
| Health/armour | `PlayerHealth` (90) | ✅ |

## 4. Weapons — `src/weapons` (33 files, 5,582 LOC)

The largest gameplay cluster. Broken into:

- **Definition layer:** `WeaponProfile` (292) + `definitions/` — Rifle, SMG, Shotgun, Sniper,
  Pistol, RocketLauncher, Fists. Data-driven stat blocks.
- **Firing:** `WeaponBase` (227), `WeaponManager` (529), `FireModeSystem` (81 — auto/burst/semi),
  `RecoilSystem` (85) + `RecoilPatterns` (77 — authored per-weapon kick sequences),
  `BallisticsSystem` (292 — travel time, drop, damage falloff by range),
  `ProjectileSystem` (176 — rockets/grenades with splash), `ExplosionDamageResolver` (99).
- **Feedback:** `MuzzleFlashEffect`, `TracerEffect`, `ImpactEffect`, `CasingPhysics`,
  `DroppedMagSystem`, `WeaponPartAnimator` (bolt/slide/charging handle).
- **Handling:** `WeaponSway` (131), `WeaponPoseOffsets` (217 — per-state pose table),
  `ScopeSystem` (201 — variable zoom + hold-breath), `ReloadSystem` (161 — tactical vs empty),
  `CyclingActionSystem` (126 — pump/bolt per-shot cycling).
- **Viewmodel:** `WeaponViewmodel` (845) + `HandsRig` (487) + `WeaponIK` (234).
- **Melee:** `MeleeHitDetection` (90), `MeleeComboTracker` (36).

**Ours:** `WeaponController`, `WeaponDatabase`, sights, recoil, bolt, hitscan bullets, 2-slot
loadout, melee stance. 🟡 — roughly 40% of the surface, missing fire modes, ballistics,
projectiles, casings, sway layering, variable zoom, pose tables.

## 5. Animation — `src/animation` (18) + `src/animation-engine` (9) = 1,766 LOC

Two cooperating systems:

1. **`animation-engine/`** — the compositor: `OperatorAnimEngine`, `PoseComposer`, `AnimLayer`,
   `AnimEventScheduler`, and 5 layer types: `BaseLocomotionLayer`, `AdditivePoseLayer`,
   `OneShotActionLayer`, `ProceduralIKLayer`, `ProceduralSpringLayer`.
2. **`animation/`** — the state machine: `AnimationStateMachine` (227), `AnimationBlender` (156),
   `AnimationLayerCompositor` (202), `AnimationPriorityTable` (53), `BakedClipLoader`,
   `ClipPoseSampler`, `IdleFidgetController`, **`PerspectiveSync` (149)**, plus 10 state classes
   (Idle/Walk/Sprint/Crouch/Jump/Slide/ADS/Fire/Reload/SwitchWeapon).

**`PerspectiveSync` is the key insight for our tac-sprint bug:** the reference has an explicit
module whose entire job is keeping the first-person viewmodel and the third-person body telling
the same story. We have no equivalent. See `10-TACTICAL-SPRINT-REWORK.md` and
`11-ANIMATION-SYSTEM.md`.

## 6. Server — `src/server` (36 files, 8,721 LOC)

| Layer | Modules |
|---|---|
| Core | `GameServer` (1,106), `ServerWorld` (371), `ServerSystem` registry (53), `Bodies` (161) |
| Systems | `MovementSystem` (314), `CombatSystem` (430), `DamageSystem` (315), `MatchSystem` (483), `KillstreakSystem` (405), `AISystem` (223) |
| World | `CollisionWorld` (431 — server-side raycasts), `LevelStore` (181), `SpawnSelector` (272) |
| Session | `RoomManager` (150), `Identity` (183), `EventLog` (93), `GameModes` (127), `WeaponStats` (71), `KillstreakStats` (110) |
| Cinematic | `CameraDirector` (267), `ReplayRecorder` (132) |

**Critical discipline:** `tools/verify/server-purity.mjs` **fails the build if the server imports
the renderer**. That enforced boundary is why the same code runs headless. We replicate this with
Unity assembly definitions (see `05-ARCHITECTURE-OVERVIEW.md`).

## 7. Bots — `src/server/ai` (14 files, 2,352 LOC)

| Module | Role |
|---|---|
| `AgentController` (357) | The bot "brain" — produces the same input a player would |
| `BotProfile` (247) | Personality: aggression, accuracy, reaction time, preferred range |
| `Difficulty` (167) | Difficulty curves applied to the profile |
| `Perception` (229) | FOV cone, line-of-sight, memory, last-known-position |
| `Navigation` (487) | Pathing over the map graph |
| `NeedsModel` (70), `Planner` (117) | Utility scoring → goal selection |
| `SquadBlackboard` (88) | Shared team knowledge |
| `Capability` (163) + `capabilities/Combat` (302), `capabilities/Movement` (250) | Composable action set |
| `behaviours/MoveTo` (253) | Steering + local avoidance |

**This is exactly the architecture the user asked for.** See `19-BOTS-AS-PLAYERS.md`.

## 8. Networking — `src/net` (12 files, 2,271 LOC)

`Protocol.ts` (390) is the *only* shared surface between client and server. Three transports
implement it identically: `LocalTransport` (in-process), `WebSocketTransport` (dedicated),
`RtcTransport` (P2P). Plus `GameClient`, `GameSession`, `HostedSession`, `InputRelay`,
`LobbyClient`/`LobbyProtocol`, `ServerClock` (time sync).

**Ours:** FishNet + `CODNetworkManager` + UDP `CODNetworkDiscovery` + `CODLobbyPlayer` + `NetCMDs`.
✅ for the transport story, 🟡 for the protocol discipline — we don't yet have a single
enumerated protocol surface.

## 9. Everything else

| Area | Reference modules | LOC | Ours |
|---|---|---|---|
| **Killstreaks** | `KillstreakManager`, 4 controllers (UAV/Airstrike/Attack-Heli/Missile), `JetFlightController`, `MissileFlightController`, `GroundTargetingMode`, `CinematicPathValidator`, `JetVFX` | 2,352 | ❌ |
| **Equipment** | `EquipmentManager`, `KillstreakTablet` (438), `TabletLiveMap`, `TabletUIScreens` (502), `ThrowableEffects` | 1,747 | 🟡 frag only |
| **UI** | 30 files: `UIManager`, 9 menus (Main 825, Loadout 357, Settings 288, ServerBrowser 264, Operators, Pause, GameOver, Loading, Theatre), HUD (Manager 293, Minimap 406, MissileHUD 369, Killfeed, MatchBar, KillstreakHUD, EquipmentHUD, DeathOverlay, DisorientOverlays), `ScopeOverlay`, `OperatorShowcase` (603) | 6,312 | 🟡 ~1,200 LOC, no design system |
| **Environment** | `LevelDefinition` (466), `LevelLoader` (727), `PropCatalog` (563), `PropPool` (722), `MapBuilder`, `HDRISkyManager`, `Weather`, sway animators, `ColliderShapeBuilder` | 3,292 | ❌ (one hand-built arena) |
| **Replay** | `BinaryFormat` (526), `ClipPlayer`, `KillcamDirector`, `FollowCameraRig`, `FreeCameraRig`, `AntiClipSolver`, `ClientRecorder`, `ReplayCodec` | 1,908 | ❌ |
| **Vehicles** | `VehicleSystem` (499), `Vehicle` (390), `VehicleDefinitions`, land + air handling models, camera, HUD | 2,469 | ❌ (deferred) |
| **Audio** | `AudioManager` (325), `SoundLibrary`, `GameAudioBindings` | 652 | 🟡 |
| **VFX** | `ExplosionEffect` (411), `SmokeVolume`, vehicle animators | 798 | 🟡 |
| **Physics** | `PhysicsWorld`, `PlayerCharacterController` (231), `ColliderFactory`, `RigidBodyPool` | 533 | ✅ (Unity) |
| **World** | `CalloutZoneRegistry`, `RadarContactRegistry`, `VisionObstructionRegistry`, `TeleportPadSystem` | 733 | ❌ |
| **Camera** | `CameraShakeController` (168), `CinematicCameraController` (317), `ShakeTriggers` | 578 | ✅ shake, ❌ cinematic |
| **Customization** | `LoadoutManager`, `OperatorRoster`, `SkinManager` | 310 | 🟡 |
| **Constants** | `utils/Constants.ts` — **1,579 LOC of tuning data in one place** | 1,579 | ❌ scattered |

## 10. Maps in the reference

`prototype`, `firing range`, `killhouse`, `shipment`, `facility`, `training range` — each with
per-map collision JSON, spawn sets, callout zones, prop placement, terrain, HDRI sky and weather.
Built by `tools/generate*` scripts (procedural generation, then baked). We adopt this approach in
`17-MAPS-AND-LEVEL-PIPELINE.md` and `23-ASSET-PIPELINE-MODELS.md`.

## 11. Verification culture (the thing worth stealing most)

`package.json` exposes **32 `verify:*` scripts**. Acceptance tests per design document
(`acceptance-doc1/2/5/n/tps/...`), subsystem tests (`server-movement`, `server-combat`,
`server-match`, `server-killstreaks`), architecture lints (`server-purity`, `chokepoint-lint`,
`state-authority`), and **screenshot generators** (`generate:previews`, `verify:shots:docn`).

**This is the single most important thing to replicate.** Our equivalent is
`07-VERIFICATION-AND-VISUAL-LOOP.md`.

## 12. Gap summary → phase mapping

| Missing capability | Phase |
|---|---|
| Menu/scene determinism, UI design system, controller nav | **P1** |
| Movement completeness (slide, mantle, stamina, footsteps), tac-sprint rework, animation layering, FP/TP parity | **P2** |
| Gunplay depth (fire modes, ballistics, projectiles, sway, scopes), HUD | **P3** |
| Gunsmith, loadouts, perks, equipment, field upgrades | **P4** |
| Match system, modes, teams, scoreboard, spawn selection, netcode hardening | **P5** |
| Map pipeline + 3 real maps, prop system, level format | **P6** |
| Streaks, progression, audio, VFX | **P7** |
| Bots | **P8** |
| Performance subsystem, quality presets, cross-platform polish | **P9** |
| Killcam/replay, cinematics | **P10** |
