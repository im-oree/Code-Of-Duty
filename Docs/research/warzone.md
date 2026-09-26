# Warzone-class systems — researched

**Sources:** Call of Duty Warzone official patch notes + community guides (primary); Call of Duty
Wiki (verification). Targets Warzone 1.0-era systems, scaled down to what this stack can run.

## Core systems to port
| System | Behaviour |
|---|---|
| Armor | Plate model: 3 plates over base health; plates break in stages, refilled from loot/buy station |
| Loot | Ground loot with rarity tiers, cash, armor plates, streaks, weapons |
| Cash economy | Loot + contracts → cash; spend at buy stations |
| Buy Stations | Buy loadout drop, armor plates bundle, killstreaks, self-revive, redeploy |
| Loadout Drop | Scheduled drops each circle phase, or purchased; grants your chosen loadout |
| Contracts | Recon (reveal next circle), Bounty (kill a marked player), Scavenger, Supply Run |
| Gulag | On first death, 1v1 to redeploy; winner returns, loser can be bought back |
| Gas circle | Phased contraction, damage over time outside; random final circles |
| Redeploy | Squad buy-back, redeploy drones, resurgence modes |
| Vehicles | Ground + air, damage states, fuel |
| Ping system | Context pings for loot/enemies/objectives/squad comms |
| Squad revive | Downed state → revive window |

## Our decision
- Implement **after** MW multiplayer modes are solid (Phase 12), because it needs loot, inventory,
  vehicles, larger maps and longer-lived sessions.
- Scale playercount to what FishNet + this project can hold reliably; document the compromise.
- Reuse the SAME `GameModeDefinition`/`MatchSystem` seam: Battle Royale is a mode with phases, not
  a separate game mode engine.
- Low-poly map built from the existing arena/level metadata pipeline; circle + Gulag are server systems.

## Acceptance test
A full BR match: drop → loot → contract → circle phases → death → Gulag → redeploy → final circle → win.
