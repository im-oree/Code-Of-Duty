# Three-FPS → Code Of Duty — Feature Map (living document)

Reference repo: `https://github.com/im-oree/Three-FPS` (working clone kept at `.reference/Three-FPS`,
outside `Assets/` and ignored by Git so Unity never imports or compiles it).

> The reference README is **stale**. Everything below was read from the actual `src/` tree,
> `package.json`, `MULTIPLAYER.md`, `CHARACTER_STATE.md` and `NEXT_STEPS.md`.

---

## 0. What the reference actually is

A **server-authoritative multiplayer** FPS (three.js + Vite, Node backend), not the single-player
prototype the README describes. Single player is a 1-participant match on the same server.

| Area | Reference implementation |
|---|---|
| Server sim | `src/server/GameServer.ts` (46 KB) + `ServerWorld`, `ServerSystem` registry, `MovementSystem`, `CombatSystem`, `DamageSystem`, `MatchSystem`, `KillstreakSystem`, `AISystem`, `CollisionWorld` (server raycasts), `Bodies`, `SpawnSelector`, `RoomManager`, `Identity`, `EventLog`, `CameraDirector` |
| Net contract | `src/net/Protocol.ts` is the ONLY shared surface. `LocalTransport` (in-tab server) and `WebSocketTransport` (dedicated backend) implement it. Rooms + reaping, `verify:purity` fails the build if the server imports the renderer |
| Character authority | `CharacterStateSystem` — 5 orthogonal channels: locomotion, traversal, weaponAction, aim, carry (+ `grounded`, `weaponId` facts). Validated `request()`, one `TRANSITIONS` table, cross-channel interaction rules, audit + rejection logs, build-enforced |
| Movement | sprint, tactical sprint (double-tap), slide, crouch, jump, landing dip, stamina, head bob, surface footsteps, **jump-triggered vault/mantle** with HUD prompt |
| Weapons | `WeaponBase`/`WeaponProfile`, fire modes, recoil patterns, ballistics + falloff, projectiles + splash, muzzle/tracer/impact, casing physics, dropped mags, cycling action (pump/bolt), tactical vs empty reload, variable-zoom scope + hold breath, sway, pose offsets, weapon-part animator, melee combos |
| Roster | Rifle, Pistol, Shotgun, SMG, Sniper (4–10x), Rocket launcher, Fists |
| Animation | Layered animation engine: base locomotion / additive pose / one-shot action / procedural IK / procedural spring; priority table, blender, baked clips, pose sampler, idle fidget, perspective sync |
| Hands | `HandsRig`, `WeaponIK`, `WeaponViewmodel`, joint IK + springs, hand skins |
| Equipment | Frag, flashbang, concussion, smoke; cook-and-throw |
| Killstreaks | UAV, Airstrike, Attack Helicopter, piloted Missile/Gunship; tablet with live map |
| Vehicles | Vehicle system, land + air handling models, vehicle camera/HUD |
| Replay | client + server recorders, binary format, killcam director, follow/free cameras, theatre |
| Modes | `GameModeDefinition` data: FFA (8p, 30 kills, 10 min, top-3) and TDM (6v6, 75 kills, 10 min); sanitised custom overrides |
| Bots | `AgentController`, `BotProfile`, `Capability` registry, `Difficulty`, `Perception` (FOV cone), `Navigation`, `NeedsModel`, `Planner`, `SquadBlackboard`, `MoveTo`, combat/movement capabilities |
| Maps | prototype, firing range, Killhouse, shipment, facility, training range — per-map collision JSON, spawns, callouts, props, terrain; HDRI sky, weather, prop pools |
| Audio | per-surface footsteps/lands/impacts, per-weapon fire + reloads + switches + empty click + bolt/pump, UI set, ambience, streak stings |
| UI/HUD | MainMenu, Settings, Loadout, Operators, Pause, GameOver, Loading, ServerBrowser, Theatre; HUD, killfeed, match bar, minimap + radar, streak/equipment/missile HUD, scope overlay, traversal prompt, death + disorient overlays |
| Quality | quality presets, adaptive resolution, occlusion culler, shadow director, static batcher |

## 1. Where Code Of Duty already stands

**Present:** FishNet server-authoritative base (`NetCMDs`, `PlayerHealth`, `PlayerLifeController`,
`PlayerSpawner`), LAN discovery + browser, lobby, movement state machine
(`Stand`/`Crouch`/`Roll`/`Jump`/`InAir`), procedural rig/IK (`LocalRigs`), weapon controller +
database + scopes + recoil + bolt, hitscan/network bullets, 2-weapon loadout + melee stance,
operator skins, frag grenade (server-simulated), kill feed, HUD panels, tabbed main menu,
settings panel, `InputBindings`, `GameConfig`/`GameSettings`, editor windows.

**Missing (this build):** character state authority, vault/mantle, stamina, surface footsteps,
fire modes, ballistic falloff/projectiles, casings/mags, sway/pose layers, variable scope +
breath, equipment variety, **gunsmith/attachments**, perks, field upgrades,
**scorestreaks**, vehicles, replay/killcam/theatre, **game modes + match system + teams +
scoreboard + spawn selection**, minimap/radar, death camera/spectator, **bots**, input
abstraction + gamepad + controller UI nav, quality presets, HDR sky/weather, custom-match rules,
dedicated backend, progression.

## 2. Target parity tiers

- **Tier A — Three-FPS parity:** server-authoritative core, character state authority, quality/perf, replay.
- **Tier B — Modern Warfare multiplayer:** gunsmith, loadout/perks/equipment/field upgrades,
  full MW2019 scorestreak set, full mode list, match flow, progression, bots.
- **Tier C — Cinematics:** intro/deploy, killcam, final killcam, victory/defeat, nuke, streak cameras, theatre.
- **Tier D — UI/UX:** one screen framework, full front-end + HUD rebuild, controller nav.
- **Tier E — Warzone-class:** armor plates, loot, cash/buy stations, loadout drops, contracts, Gulag, gas circle, vehicles.

Full phased roadmap: `.freebuff` plan / this doc's companion `Docs/research/`.

## 3. Unity mapping

| COD concept | Unity/FishNet implementation |
|---|---|
| Character state authority | `CharacterState` (5 channels + facts, `request()`, `TRANSITIONS`, rejection log) |
| Input abstraction | `IInputSource` → `PlayerInputSource` / `BotInputSource` |
| Weapons | `WeaponProfile` ScriptableObjects |
| Gunsmith | `AttachmentDefinition` + 6-axis `WeaponStats` aggregation |
| Loadout | `LoadoutDefinition` persisted + synced on spawn |
| Scorestreaks | server `KillstreakSystem` + per-streak controller, client cameras/VFX |
| Modes | `GameModeDefinition` + `MatchSystem` |
| Matchmaking | `RoomManager`/`SessionManager` over FishNet; LAN now, service later |
| Cinematics | Timeline + Cinemachine; replay via binary recorder + camera rigs |
| UI | `ScreenFramework` + `UITheme` widgets (uGUI + TMP), focus navigation |
| Bots | server `AgentController : IInputSource` + NavMesh + perception/difficulty |
| Netcode | FishNet server-authoritative; server emits events, clients own presentation |
