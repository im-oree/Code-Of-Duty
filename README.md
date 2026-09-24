# Code Of Duty

A military-shooter project built in Unity on top of the Online FPS/TPS Kit
(procedural, code-driven animations — no separate hands rig, character +
weapons all in one), fully converted from Photon PUN2 → Mirror → **FishNet**
with a server-authoritative architecture.

## Networking architecture (FishNet)

Networking is the base of everything — all game logic flows through the server:

- **`CODNetworkManager`** (`Resources/Network/NetworkManager` prefab, spawned on
  demand by any scene via `EnsureExists()`; plain orchestrator on top of the
  FishNet `NetworkManager` + `Tugboat` transport):
  - *Offline / solo*: starts an **internal host** (server + client in one process)
    — opening the arena scene directly auto-boots it, like a real game.
  - *LAN host*: same host, advertised on the local network.
  - *LAN client*: joins via the server browser, quick play, or direct IP.
  - *Dedicated server*: headless builds auto-start the server (same code path).
  - Scene flow: host calls `BeginGame()` → FishNet `SceneManager` swaps the
    global scene to the arena → every lobby player is replaced with the real
    game player (`OnClientPresenceChangeEnd`).
- **`CODNetworkDiscovery`**: transport-agnostic LAN discovery over raw UDP
  broadcast (works with any FishNet transport). Server browser entries carry
  room name + player count.
- **`CODLobbyPlayer`**: lightweight player while in the room lobby (StartMenu);
  replaced by the real game player when the host starts the match.
- **`NetCMDs`** (on the player prefab): shooting/weapon-switch replication
  (`ServerRpc` → `ObserversRpc`), body state `SyncVar<T>`s (late joiners sync
  for free), **server-authoritative damage** entry point, kill feed, respawn.
- Player prefab uses FishNet `NetworkObject`, `NetworkTransform` (position +
  torso rotation) and `NetworkAnimator`.
- `Assets/DefaultPrefabObjects.asset` is FishNet's spawnable-prefab collection —
  it is auto-refreshed by FishNet's editor generator on import.

## Dynamic systems (nothing hardcoded)

- **`WeaponDatabase`** (`Resources/Weapons/WeaponDatabase.asset`): every weapon
  in the game, referenced by id. Loadout menu, spawning and sync all read from
  it — add a gun to the database and it appears everywhere.
- **`PlayerLoadout`** (networked): the loadout picked in the menu is applied on
  every client by swapping the guns inside the player's `WeaponController`
  slot rigs at spawn. Slot count is read from the prefab — changing to a
  2-gun inventory requires **zero** scene changes.
- **`CameraShake`** (native, in-house): perlin-noise camera-feel engine with
  per-state presets — fire kick (scaled by weapon class), damage flinch, death
  rumble, landing thud (scaled by fall speed), jump pop, melee whoosh,
  distance-scaled explosions, sustained movement rumble (walk/sprint/tac).
  Cinemachine-safe (composes after the brain), ownership-gated so remote
  players never shake your screen.
- **`WeaponMovementPose`** (COD-style handling): procedural weapon poses for
  movement states, all SmoothDamp-blended — sprint keeps the gun UP and in
  frame; **tac sprint** (double-tap Shift) is a timed speed burst with a
  one-handed muzzle-up pose (off-hand released via IK; pistols use a lighter
  raise), plus subtle hand-wobble vibration while moving.
- **2-weapon loadout** (primary + secondary) with **melee stance** on key 3
  (gun stowed, punch with LMB). Holstered guns are invisible — switching
  (faster, 0.09s blend) pulls the gun up COD-style instead of reaching to the
  back. Locomotion clips are speed-synced (`locomotionSpeed` animator
  parameter) so faster movement never foot-slides.
- **`CharacterSkinLibrary`** + **`PlayerAppearance`** (networked): operator
  selection (Crimson / Cobalt), synced to all players in the match.
  Cobalt's `modelOverride` is wired to `Male_Body_BaseMesh.fbx`: at spawn the
  override model is instantiated and its skinned meshes are **rebound to the
  live player skeleton by bone name**, then the original body is hidden.
  ⚠️ The current FBX is an *unrigged* static mesh (no skin weights), so the
  swap safely skips with a console warning until the model is rigged (Mixamo
  or Blender) using the kit skeleton's bone names — after that it goes live
  with zero code changes.

## Main menu

`CODMainMenu` + `MenuStage` + `OperatorDisplay` (all injected at runtime into
`StartMenu` — no scene surgery):

- Full 3D backdrop: dark set, fog, orange rim light, drifting smoke and dust,
  with the **live operator model** holding the selected primary weapon.
- Tabbed UI in modern-shooter style: **PLAY** (quick play, host, direct
  connect, private solo + live LAN server browser), **OPERATORS** (live
  realtime switching), **LOADOUT** (per-slot weapon cards, updates the 3D
  preview instantly), **BARRACKS** / **STORE** (placeholders for progression
  and cosmetics).

## Importing new guns (GLB pipeline)

1. `.glb` models import natively via the **glTFast** package
   (`com.unity.cloud.gltfast` in `Packages/manifest.json`). Files live in
   `Assets/Models/Weapons/`.
2. Select the imported model → menu **COD / Weapons / Create Weapon From
   Selected Model**. The wizard:
   - strips bloated hierarchy junk (loose bullets, casings, helpers),
   - normalizes real-world scale,
   - adds `Weapon`, hand-IK `WeaponPoint`s, bullet/casing/aim points, audio,
     pickup physics,
   - saves the prefab to `Assets/Prefabs/weapons/` and registers it in the
     `WeaponDatabase` (it then shows up in the loadout menu automatically).
3. Fine-tune the generated grip/muzzle/aim points on the prefab in the editor.

## Repository layout

```
Assets/
├── External/            # Third-party packages
│   ├── FishNet/         # Networking (the base of the game)
│   └── ParrelSync/      # Editor clones for multi-client LAN testing
├── Scripts/             # All game code
│   ├── Camera/          # Cinemachine POV extension, camera switcher
│   ├── EventsSystem/    # Animation/curve event system
│   ├── Network/         # CODNetworkManager, discovery, lobby, health, spawning
│   ├── Player/          # Movement states, body handlers, input, skins
│   ├── Rig/             # Code-driven rig system (aim, lean, procedural anim)
│   ├── UI/              # HUD + MainMenu/ (CODMainMenu, MenuStage, OperatorDisplay)
│   └── Weapons/         # Weapon controller, database, loadout, bullets, wizard
├── Models/
│   ├── Characters/      # Male_Body_BaseMesh.fbx (future Cobalt operator model)
│   └── Weapons/         # Kit gun FBXs + imported .glb guns
├── Materials/  Prefabs/  Shaders/  Sounds/
├── Resources/           # Runtime-loaded assets
│   ├── Network/         # NetworkManager + LobbyPlayer prefabs
│   ├── Weapons/         # WeaponDatabase.asset
│   ├── Character/       # CharacterSkinLibrary.asset
│   └── UI/              # Weapon icons
├── Scenes/              # StartMenu, DMArena1, OfflineTest
└── Settings/            # URP render pipeline, input actions, camera blends
Docs/                    # Kit documentation (PDF + readme)
```

## Roadmap

1. ~~Project cleanup & reorganization~~ ✅
2. ~~Networking base: PUN2 → Mirror conversion~~ ✅ → ~~**Mirror → FishNet migration**~~ ✅
   - internal server for offline play, LAN host/join + discovery, dedicated-server ready
3. ~~Dynamic loadout + operator systems, COD-style 3D main menu~~ ✅
4. Import GLB guns as prefabs via the Weapon Setup Wizard (in-editor step)
5. ~~New arena layout + full map restyle~~ ✅ — **Foundry** (64×64 industrial
   arena: central smelter platform w/ 4 ramps, red/blue container lanes,
   N/S catwalks, corner crate nests; 8 flat-tint URP materials in
   `Assets/Materials/Arena/`; realtime lighting — re-bake if you want baked GI)
6. Swap in Male_Body_BaseMesh as the Cobalt operator model (pipeline done —
   blocked on rigging the FBX, see above)
7. Bots / AI, game modes (TDM, FFA), progression

## Testing multiplayer

Use **ParrelSync** (`Assets/External/ParrelSync`) to open editor clones and run
several clients against one host on the same machine/LAN:

1. Editor A: play `StartMenu` → PLAY → HOST MATCH → lobby → START MATCH.
2. Editor B (clone): play `StartMenu` → PLAY → server appears in LAN SERVERS →
   JOIN (or QUICK PLAY / DIRECT CONNECT with the host's IP).
3. Offline test: just press Play on `DMArena1` — the internal server boots
   automatically and you spawn alone.

> First open after the FishNet migration: let the editor recompile, then FishNet
> auto-generates scene/prefab ids and fills `DefaultPrefabObjects.asset`. If
> spawning complains, run **Tools → Fish-Networking → Rebuild** menu items once.
