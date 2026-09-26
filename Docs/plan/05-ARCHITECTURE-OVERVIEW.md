# 05 — Architecture Overview

## 1. The one rule

> **The server simulates. Clients present. Input is the only thing that flows upward.**

Everything else in this document is a consequence of that sentence.

## 2. Layer diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│ PRESENTATION  (client only, never affects outcomes)                  │
│  Viewmodel · ThirdPersonBody · AnimationCompositor · CameraStack      │
│  HUD · Menus · VFX · Audio · CameraShake · Killfeed                   │
└───────────────▲──────────────────────────────────┬───────────────────┘
                │ state snapshots / events         │ intent
┌───────────────┴──────────────────────────────────▼───────────────────┐
│ SIMULATION  (server-authoritative; runs headless)                    │
│  MatchSystem · MovementSystem · CombatSystem · DamageSystem           │
│  SpawnSelector · StreakSystem · EquipmentSystem · AISystem            │
│  CharacterState (5-channel authority)                                 │
└───────────────▲──────────────────────────────────┬───────────────────┘
                │ IInputSource frames               │ world queries
┌───────────────┴──────────────────────────────────▼───────────────────┐
│ CONTRACTS  (shared, no UnityEngine rendering types)                  │
│  IInputSource · InputFrame · NetProtocol · WeaponProfile              │
│  LevelDefinition · GameModeDefinition · LoadoutDefinition             │
└──────────────────────────────────────────────────────────────────────┘
```

## 3. Assembly definitions (the enforced boundary)

The reference project enforces "the server must not import the renderer" with a build lint. Unity
gives us a stronger tool: **assembly definitions**. Each `.asmdef` declares its allowed references;
violating it is a *compile error*, not a lint warning.

```
Assets/Scripts/
├── Contracts/        COD.Contracts.asmdef
│     └─ refs: (none but Unity core math/collections)
│     └─ Interfaces, enums, ScriptableObject data schemas, InputFrame, protocol IDs
│
├── Sim/              COD.Sim.asmdef
│     └─ refs: COD.Contracts, FishNet, Unity.Physics/AI
│     └─ MUST NOT reference: COD.Presentation, COD.UI, TMPro, Cinemachine, URP
│
├── Presentation/     COD.Presentation.asmdef
│     └─ refs: COD.Contracts, COD.Sim (read-only), Cinemachine, URP, Animation
│
├── UI/               COD.UI.asmdef
│     └─ refs: COD.Contracts, COD.Presentation, TMPro, uGUI
│
├── Net/              COD.Net.asmdef
│     └─ refs: COD.Contracts, COD.Sim, FishNet
│
└── Editor/           COD.Editor.asmdef   (Editor platform only)
      └─ refs: everything
```

**Migration note:** the project currently has no asmdefs — everything is in `Assembly-CSharp`.
Introducing them is a P1 task and must be done in one commit, because partial adoption creates
circular reference errors. Order: `Contracts` → `Net` → `Sim` → `Presentation` → `UI` → `Editor`.

**Payoff:** compile times drop sharply (only the touched assembly rebuilds), the dedicated-server
build can strip `Presentation`+`UI` entirely, and architectural drift becomes impossible.

## 4. The input contract (the most important interface)

```csharp
namespace CodeOfDuty.Contracts
{
    public enum InputActionId { Fire, Aim, Reload, Jump, Crouch, Prone, Sprint,
        TacSprint, Slide, Melee, Interact, SwitchWeapon, Lethal, Tactical,
        FieldUpgrade, Streak, Scoreboard, Pause, /* … */ }

    /// One frame of intent. Nothing in the game reads a keyboard directly.
    public readonly struct InputFrame
    {
        public readonly uint Tick;            // client tick for reconciliation
        public readonly Vector2 Move;         // -1..1, already deadzoned/curved
        public readonly Vector2 Look;         // delta, already sensitivity-scaled
        public readonly ButtonMask Pressed;   // edge-down this frame
        public readonly ButtonMask Held;
        public readonly ButtonMask Released;
    }

    public interface IInputSource
    {
        InputFrame Sample(float dt);
        bool IsHuman { get; }                 // for UI glyphs & telemetry only
        InputDeviceKind Device { get; }       // KeyboardMouse | Gamepad | Bot
    }
}
```

Three implementations, and **only** three:

| Implementation | Used by |
|---|---|
| `PlayerInputSource` | The local human. Input System → `InputFrame`. |
| `BotInputSource` | Bots. `AgentController` writes into it. Indistinguishable downstream. |
| `ReplayInputSource` | Replay/killcam playback and deterministic tests. |

**Rule:** if a gameplay file contains the token `Input.`, `Keyboard.`, or `Gamepad.`, it is a bug.
A lint enforces this (`27-CODING-STANDARDS-AND-WORKFLOW.md`).

## 5. Character state authority

`CharacterState` is the single truth about what a character is doing. Five **orthogonal** channels
(a character has exactly one value in each, simultaneously):

| Channel | Values |
|---|---|
| `Locomotion` | Idle · Walk · Run · Sprint · TacSprint · Crouch · CrouchWalk · Prone · Slide |
| `Traversal` | Grounded · Jumping · Falling · Mantling · Vaulting · Landing |
| `WeaponAction` | None · Firing · Reloading · Switching · Cycling · Melee · Throwing · Inspecting |
| `Aim` | Hip · Transitioning · ADS |
| `Carry` | Primary · Secondary · Lethal · Tactical · FieldUpgrade · Streak · Fists |

Rules:
- Every change goes through `Request(channel, value, reason)` which validates against a per-channel
  transition table and returns accept/reject.
- **Cross-channel rules** run after each accepted change (e.g. `WeaponAction→Reloading` forces
  `Aim→Hip`; `Locomotion→TacSprint` forces `Aim→Hip` and `WeaponAction→None`).
- Rejections are logged to a bounded ring buffer for debugging.
- An editor verifier fails if a transition table has unreachable or undefined entries.

**Presentation reads `CharacterState`. It never writes it.** This is exactly the rule
`WeaponMovementPose` breaks today (doc 03, D5) and Phase 1 fixes.

## 6. Tick model

| Loop | Rate | Owns |
|---|---|---|
| Server simulation | 30 Hz fixed (64 Hz option for LAN) | authoritative movement, combat, match |
| Client prediction | matches server tick | local player movement replay |
| Client render | uncapped | interpolation, animation, camera, VFX |
| Network send | 20 Hz (client input at sim rate) | snapshots, delta-compressed |

Animation and camera always run at render rate and interpolate — never at tick rate.

## 7. Data assets (nothing hardcoded)

| Asset | Type | Defines |
|---|---|---|
| `WeaponProfile` | SO | all weapon stats, timings, recoil, ballistics, audio, model refs |
| `AttachmentDefinition` | SO | slot, stat modifiers, socket, mesh |
| `LoadoutDefinition` | SO/persisted | class: weapons + attachments + perks + equipment |
| `PerkDefinition` | SO | slot, effect hooks |
| `EquipmentDefinition` | SO | lethal/tactical behaviour parameters |
| `StreakDefinition` | SO | cost, category, controller type |
| `GameModeDefinition` | SO | rules, scoring, teams, timers |
| `LevelDefinition` | SO | geometry, spawns, callouts, sky, lighting, minimap |
| `OperatorDefinition` | SO | model, skin, voice, faction |
| `UIThemeAsset` | SO | every colour, size, font, duration in the UI |
| `GameConfig` | SO | global rules (already exists) |

All of them are editable in the **COD > Game Config** editor window, which we extend rather than
replace.

## 8. Scene structure

| Scene | Contains |
|---|---|
| `Boot` *(new)* | Nothing but a bootstrapper: loads settings, spawns persistent managers, routes to `Frontend`. Makes startup deterministic. |
| `Frontend` (was `StartMenu`) | The menu **as real saved scene content**: canvas hierarchy, 3D stage, operator, lights, camera. Fully editable. |
| `MP_<MapName>` | One scene per map, built from a `LevelDefinition`. |
| `DevRange` | Firing range / test bed. |

Persistent managers (`NetworkManager`, `AudioManager`, `SettingsService`, `InputService`) live on a
`DontDestroyOnLoad` root created by `Boot`, not by `RuntimeInitializeOnLoadMethod` scattered across
files. **This removes an entire class of duplicate-spawn bugs** (doc 03, D1).

## 9. Networking placement

See `18-NETWORKING-LAN-FIRST.md`. In brief:
- FishNet `NetworkManager` + `Tugboat` (UDP) transport.
- Server-authoritative movement with client prediction + reconciliation.
- Lag-compensated hitscan (server rewinds colliders to the shooter's view time).
- LAN discovery over raw UDP broadcast (transport-agnostic — already implemented and good).
- The *same* build runs as: internal host (solo), LAN host, LAN client, dedicated server.

## 10. Directory layout (target)

```
Assets/
├── Scripts/
│   ├── Contracts/      interfaces, enums, data schemas
│   ├── Sim/            Character/ Movement/ Combat/ Match/ Spawning/ AI/
│   ├── Presentation/   Animation/ Camera/ Viewmodel/ VFX/ Audio/
│   ├── UI/             Framework/ Theme/ Widgets/ Screens/ HUD/
│   ├── Net/            Manager/ Discovery/ Lobby/ Protocol/ Sync/
│   └── Editor/         Windows/ Wizards/ Verifiers/
├── Data/               all ScriptableObject instances
├── Art/                Models/ Materials/ Textures/ Animations/
├── Scenes/
└── Settings/

UnityWeb/               ← the TS/three.js Unity clone (NOT under Assets)
Reference/              ← gitignored: Three-FPS clone + UI research
Artifacts/              ← gitignored: harness screenshots
Docs/plan/              ← this plan
Tools/                  ← build + verification scripts
```

## 11. Anti-patterns banned in this codebase

1. `FindObjectOfType` in hot paths or in `Start` for wiring — use explicit references or a service
   locator initialised by `Boot`.
2. `DestroyImmediate` at runtime. Ever.
3. Mixing `Destroy` (deferred) and `DestroyImmediate` in the same cleanup routine — this is
   literally bug D1.
4. `[ExecuteAlways]` components that mutate and dirty the scene automatically.
5. `try/catch` that swallows and continues in initialisation (bug D2) — fail loud in dev builds.
6. Gameplay logic in `Update` that must be authoritative — it belongs on the server tick.
7. Reading raw input outside `PlayerInputSource`.
8. Presentation code writing to `CharacterState`.
