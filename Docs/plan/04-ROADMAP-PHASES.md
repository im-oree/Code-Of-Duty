# 04 — Staged Roadmap

Every phase has: a **goal**, a **task list**, **exit criteria** that are objectively checkable, and
a **verification artifact** (usually a screenshot set produced by the harness in doc 07).

A phase is not "done" until its exit criteria are demonstrated. Phases begin with a **fix pass** on
anything the previous phase left broken — per the user's standing instruction: *fix issues first*.

```
P0 ── Foundation: see the game            (tooling)      ← unblocks everything
P1 ── Fix + front-end + input             (the ask)
P2 ── Movement & animation                (feel)
P3 ── Gunplay & HUD                       (combat)
P4 ── Loadout, gunsmith, perks, equipment (depth)
P5 ── Match, modes, netcode hardening     (the game)
P6 ── Maps & level pipeline               (content)
P7 ── Streaks, progression, audio, VFX    (reward)
P8 ── Bots                                (always populated)
P9 ── Performance & cross-platform        (ship quality)
P10─ Killcam, replay, cinematics          (polish)
P11─ Balance & playtest                   (tuning)
P12─ Release engineering                  (build + deploy)
```

---

## P0 — Foundation: make the game visible  ← *tooling, runs alongside P1*

**Goal:** the agent can render any Unity scene/prefab and look at it, without a Unity Editor.

- [ ] Headless Chromium + WebGL 2.0 in the sandbox — **DONE** (verified 2026-09-26)
- [ ] `UnityWeb/` — TypeScript Unity runtime clone, **outside `Assets/`**, gitignored deps
- [ ] Unity YAML parser: `.unity`, `.prefab`, `.mat`, `.asset`, `.meta` (GUID→path resolution)
- [ ] Component mapping: Transform/RectTransform, MeshFilter/MeshRenderer, SkinnedMeshRenderer,
      Camera, Light, Canvas + uGUI (Image/Text/Button/Layout), RenderSettings/fog/ambient
- [ ] Left-handed Unity → right-handed three.js coordinate conversion (positions, quaternions,
      euler order, UV flip, winding)
- [ ] FBX + GLB loading with material binding
- [ ] Headless capture CLI: `npm run shot -- --scene StartMenu --cam Main --out x.png`
- [ ] Live server mode with a scene picker for the user's browser
- **Exit:** `StartMenu.unity` renders in the harness; the agent reads the PNG and can describe it.
- **Artifact:** `Artifacts/p0/startmenu-*.png`
- **Doc:** `06`, `07`

## P1 — Fix the bugs, rebuild the front-end, land input  ← *the user's explicit "phase 1"*

**Goal:** the menu is correct, in-scene, editable, beautiful and controller-navigable. Tac sprint
reads right. Everything runs through one input contract.

**P1.a — Bug fixes (first)**
- [ ] D1: one operator. Delete the bake/purge race; single deterministic ownership model
- [ ] D2: UI always present; remove `HideLegacyMenu`'s scene-wide canvas kill
- [ ] D3: operator idle animation + weapon in hand
- [ ] D4: menu is **real saved scene content**, editable in the Inspector, survives Play
- [ ] D6: `IInputSource` adopted by `Input_Handler`, `CharacterMove`, `WeaponController`

**P1.b — Tac-sprint rework** (`10`)
- [ ] Move tac-sprint *state* out of `WeaponMovementPose` into `CharacterMove` + `CharacterState`
- [ ] Rewrite the pose: cached base, `LateUpdate`, clamped, both perspectives
- [ ] Hands visible and correct in FP and TP, including the transitions in and out
- [ ] Iterate against harness screenshots until it reads right

**P1.c — UI design system + redesigned menu** (`20`, `21`)
- [ ] `UITheme` → a real token set (colour, type scale, spacing, radii, motion)
- [ ] Widget library: button, tab, card, list row, slider, toggle, dropdown, modal, toast
- [ ] Top header with tabs: `PLAY · OPERATORS · LOADOUT · BARRACKS · STORE · SETTINGS`
- [ ] Every screen rebuilt on the system; pause + settings share the same components
- [ ] Focus/navigation model working on gamepad with visible focus ring

**P1.d — Controller support** (`08`)
- [ ] Full gamepad gameplay bindings + deadzone/response curves
- [ ] Menu navigation, tab bumpers, hold-to-confirm, on-screen glyphs that switch by device
- **Exit:** Play `StartMenu` → exactly one operator, animated, holding a gun; full UI; navigate
  every tab and start a solo match using **only** a gamepad. Tac sprint screenshots approved.
- **Artifact:** `Artifacts/p1/menu-*.png`, `Artifacts/p1/tacsprint-fp|tp-*.png`
- **Docs:** `08`, `10`, `20`, `21`, `29`

## P2 — Movement & animation

**Goal:** movement is complete and the body never lies about it.

- [ ] Slide (+ slide-cancel), stamina, mantle/vault with prompt, ledge-hang optional
- [ ] Surface-aware footsteps, landing dip, head bob, lean
- [ ] Animation layer compositor: base locomotion / additive / one-shot / IK / spring
- [ ] `PerspectiveSync` equivalent — FP and TP driven from one pose source
- [ ] Locomotion speed-sync (no foot sliding), 8-way blend, turn-in-place
- [ ] Procedural GLB pipeline produces a rigged operator (`23`)
- **Exit:** a scripted "movement reel" — walk, sprint, tac sprint, slide, jump, mantle, crouch —
  captured in FP and TP; every frame reads correctly.
- **Docs:** `09`, `10`, `11`, `23`

## P3 — Gunplay & HUD

- [ ] `WeaponProfile` ScriptableObjects replace the ad-hoc weapon data
- [ ] Fire modes (auto/burst/semi), authored recoil patterns, spread/bloom model
- [ ] Ballistics: travel time, damage falloff by range, penetration, hit zones
- [ ] Projectiles + splash; tracers, muzzle flash, impacts, casings, dropped mags
- [ ] ADS pipeline: FOV, sensitivity scaling, sway, hold-breath, variable zoom optics
- [ ] Tactical vs empty reload; cycling actions (pump/bolt)
- [ ] Full HUD (`22`): ammo, health, compass, minimap, killfeed, hitmarkers, damage direction
- **Exit:** a firing-range scene; every weapon class demonstrably distinct; HUD complete.
- **Docs:** `12`, `22`

## P4 — Loadout, gunsmith, perks, equipment

- [ ] `AttachmentDefinition` + socket system + 6-axis stat aggregation + 5-of-8 slot rule
- [ ] Gunsmith screen with live 3D preview and stat deltas
- [ ] 10 custom classes, 3 perk slots, lethal + tactical, field upgrade
- [ ] Networked loadout application on spawn
- **Exit:** build a class in the menu, spawn with exactly it, attachments visibly on the model.
- **Docs:** `13`, `14`

## P5 — Match, modes, netcode hardening

- [ ] `MatchSystem` (server): warmup → live → intermission → end, score, timer, teams
- [ ] Modes: Free-For-All, Team Deathmatch, Domination, Hardpoint, Search & Destroy
- [ ] Spawn selection (enemy proximity, line-of-sight, teammate weighting, spawn flipping)
- [ ] Scoreboard, end-of-match screen, MVP
- [ ] Netcode: client prediction + reconciliation for movement, lag compensation for hitscan,
      interpolation for remotes, snapshot budget, one enumerated protocol surface
- [ ] LAN: discovery hardening, reconnect, host migration decision documented
- **Exit:** 4-client LAN TDM to completion at simulated 80 ms/2% loss; no desync; hit reg fair.
- **Docs:** `16`, `18`

## P6 — Maps & level pipeline

- [ ] `LevelDefinition` asset schema (geometry refs, spawns, callouts, collision, lighting, sky)
- [ ] Prop catalog + pooling; procedural prop mesh generation (`23`)
- [ ] 3 shipping maps: a small chaos map, a medium 3-lane, a large map
- [ ] Minimap generation from the level asset
- [ ] HTML→uGUI transpiler (`24`) so UI can be iterated externally
- **Exit:** all 3 maps playable in all modes with valid spawns and callouts.
- **Docs:** `17`, `23`, `24`

## P7 — Streaks, progression, audio, VFX

- [ ] Streak framework + 6 original streaks (recon drone, mortar, sentry, air support, …)
- [ ] XP, levels, weapon levels, unlock gates, challenges
- [ ] Audio: weapon layers, surface footsteps, ambience, UI, streak stings, mix buses, occlusion
- [ ] VFX pass: explosions, smoke, blood hits, environment damage
- **Exit:** earn and deploy a streak; progression persists across sessions.
- **Docs:** `15`, `25`

## P8 — Bots

- [ ] `BotInputSource` completed; `AgentController` produces human-shaped input
- [ ] Perception (FOV cone, LOS, memory), NavMesh navigation, utility planner
- [ ] `BotProfile` + difficulty curves; squad blackboard
- [ ] Bots are network players — same prefab, same replication, same loadouts
- **Exit:** a match of 12 bots runs for 10 minutes with no intervention; a human observer given
  the killfeed cannot reliably identify which players are bots.
- **Doc:** `19`

## P9 — Performance & cross-platform

- [ ] Quality presets, adaptive resolution, shadow director, occlusion, static batching, LODs
- [ ] Budgets enforced by an automated perf test scene
- [ ] Input/UI validated for desktop + gamepad + (ready for) console layouts
- **Exit:** budgets in `26` met on the reference machine; no platform-specific code paths in
  gameplay.
- **Doc:** `26`

## P10 — Killcam, replay, cinematics
- [ ] Binary replay recorder, killcam director, spectator/free cameras, intro/deploy sequence.

## P11 — Balance & playtest
- [ ] Telemetry, TTK tables, map heatmaps, tuning passes.

## P12 — Release engineering
- [ ] Dedicated server build, CI, versioning, packaging, update path.

---

## Dependency graph

```
P0 ─────────────────────────────────────────────► (enables verification for all)
      │
P1 ───┼── input contract ──► P2 ──► P3 ──► P4
      │                        │      │
      └── UI system ──────────►│      └──► P7
                               │
P5 ◄── needs P3 (combat) ──────┘
 │
 ├──► P6 (maps need spawns from P5)
 ├──► P8 (bots need P5 match + P6 navmesh)
 └──► P10

P9 runs continuously from P3 onward; P11–P12 close out.
```

## Cadence

Each phase ships in slices that always leave the project runnable. No long-lived broken states.
Every slice ends with: compile check → harness screenshots → doc update → commit.
