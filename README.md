# Code Of Duty

A Call of Duty: Modern Warfare / Warzone style FPS built in Unity, based on the
Online FPS/TPS Kit (procedural, code-driven animations — no separate hands rig,
character + weapons all in one), fully converted from Photon PUN2 to **Mirror**
with a server-authoritative architecture.

## Networking architecture (Mirror)

Networking is the base of everything — all game logic flows through the server:

- **`CODNetworkManager`** (`Resources/Network/NetworkManager` prefab, spawned on
  demand by any scene via `EnsureExists()`):
  - *Offline / solo*: starts an **internal host** (server + client in one process)
    — opening the arena scene directly auto-boots it, like a real game.
  - *LAN host*: same host, advertised on the local network.
  - *LAN client*: joins via the server browser, quick-match, or direct IP.
  - *Dedicated server*: headless builds auto-start the server (same code path).
- **`CODNetworkDiscovery`**: LAN broadcast — replaces the Photon lobby. Rooms
  list shows server name + player count from discovery responses.
- **`CODLobbyPlayer`**: lightweight player while in the room lobby (StartMenu);
  replaced by the real game player when the host starts the match.
- **`NetCMDs`** (on the player prefab): shooting/weapon-switch replication
  (Commands → ClientRpcs), body state SyncVars (late joiners sync for free),
  **server-authoritative damage** entry point, kill feed, respawn.
- Player prefab uses `NetworkIdentity`, `NetworkTransform` (position + torso
  rotation) and `NetworkAnimator` — direct replacements of the old Photon views.

## Repository layout

```
Assets/
├── External/            # Third-party packages
│   ├── Mirror/          # Networking (the base of the game)
│   └── ParrelSync/      # Editor clones for multi-client LAN testing
├── Scripts/             # All game code
│   ├── Camera/          # Cinemachine POV extension, camera switcher
│   ├── EventsSystem/    # Animation/curve event system
│   ├── Network/         # CODNetworkManager, discovery, lobby, health, spawning
│   ├── Player/          # Movement states, body handlers, input, camera controller
│   ├── Rig/             # Code-driven rig system (aim, lean, procedural anim)
│   ├── UI/              # Menu + HUD scripts
│   └── Weapons/         # Weapon controller, bullets, recoil, sights, pickups
├── Materials/  Models/  Prefabs/  Shaders/  Sounds/
├── Resources/           # Runtime-loaded assets (player prefab, UI)
│   └── Network/         # NetworkManager + LobbyPlayer prefabs
├── Scenes/              # StartMenu, DMArena1, OfflineTest
├── Settings/            # URP render pipeline, input actions, camera blends
└── ScriptTemplates/     # Mirror script templates (must stay at Assets root)
Docs/                    # Kit documentation (PDF + readme)
```

## Roadmap

1. ~~Project cleanup & reorganization~~ ✅
2. ~~Networking base: full PUN2 → Mirror conversion (Photon deleted)~~ ✅
   - internal server for offline play, LAN host/join + discovery, dedicated-server ready
3. Character: movement (sprint, slide, crouch, lean), camera, code-driven animations
4. Weapons: gunplay, ADS/sights, recoil, pickups — all server-authoritative
5. Maps & game modes (TDM, FFA → Warzone-style BR later)
6. Bots / AI
7. Polish: UI, killfeed, loadouts, progression

## Testing multiplayer

Use **ParrelSync** (`Assets/External/ParrelSync`) to open editor clones and run
several clients against one host on the same machine/LAN:

1. Editor A: play `StartMenu` → login → Create Room → Start Game.
2. Editor B (clone): play `StartMenu` → login → Rooms → the LAN room appears →
   Join (or Quick Game → enter the host's IP → Connect).
3. Offline test: just press Play on `DMArena1` — the internal server boots
   automatically and you spawn alone.
