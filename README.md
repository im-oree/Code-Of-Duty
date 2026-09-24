# Code Of Duty

A Call of Duty: Modern Warfare / Warzone style FPS built in Unity, based on the
Online FPS/TPS Kit (procedural, code-driven animations — no separate hands rig,
character + weapons all in one), being converted from **Photon PUN2** to
**Mirror** with a server-authoritative architecture.

## Architecture goal

Networking is the base of everything — all game logic, physics and gameplay run
through the server:

- **Mirror** is the one and only networking layer (PUN2 is being removed).
- **Server-authoritative**: the server owns logic, physics, hit registration.
- **Host model like a real game**: offline/solo play starts an internal local
  server, LAN play hosts/joins on the local network, and dedicated servers use
  the exact same code path. One flow for everything.

## Repository layout

```
Assets/
├── External/            # Third-party packages
│   ├── Mirror/          # Networking (the base of the game)
│   └── ParrelSync/      # Editor clones for multi-client LAN testing
├── Photon/              # LEGACY — deleted once the Mirror conversion compiles
├── Scripts/             # All game code
│   ├── Camera/          # Cinemachine POV extension, camera switcher
│   ├── EventsSystem/    # Animation/curve event system
│   ├── Network/         # Spawning, health, game manager, menus (PUN → Mirror WIP)
│   ├── Player/          # Movement states, body handlers, input, camera controller
│   ├── Rig/             # Code-driven rig system (aim, lean, procedural anim)
│   ├── UI/              # Menu + HUD scripts
│   └── Weapons/         # Weapon controller, bullets, recoil, sights, pickups
├── Materials/  Models/  Prefabs/  Shaders/  Sounds/
├── Resources/           # Runtime-loaded assets (net player prefab, UI)
├── Scenes/              # StartMenu, DMArena1, OfflineTest
├── Settings/            # URP render pipeline, input actions, camera blends
└── ScriptTemplates/     # Mirror script templates (must stay at Assets root)
Docs/                    # Kit documentation (PDF + readme)
```

## Roadmap

1. ~~Project cleanup & reorganization~~ ✅
2. **Networking base**: full PUN2 → Mirror conversion, then delete `Assets/Photon`
   - internal server for offline play, LAN host/join, dedicated server support
3. Character: movement (sprint, slide, crouch, lean), camera, code-driven animations
4. Weapons: gunplay, ADS/sights, recoil, pickups — all server-authoritative
5. Maps & game modes (TDM, FFA → Warzone-style BR later)
6. Bots / AI
7. Polish: UI, killfeed, loadouts, progression

## Testing multiplayer

Use **ParrelSync** (`Assets/External/ParrelSync`) to open editor clones and run
several clients against one host on the same machine/LAN.
