# Game modes — researched

**Sources:** Activision loadout/killstreak guides (primary); IGN *How to Play Each Multiplayer
Mode* (MW2019); Call of Duty Wiki game-mode list (verification).

## Respawn modes
| Mode | Rules | Typical limits |
|---|---|---|
| Team Deathmatch | Two teams, kill to score | 75 kills / 10 min (6v6) |
| Free-for-All | Everyone solo, top placers win | 30 kills / 10 min, 8p, top 3 win |
| Domination | Capture + hold A/B/C, score ticks per flag | 200 points |
| Hardpoint | Rotating capture zone | 250 points |
| Headquarters | One contested HQ, no respawns while held | 200 points |
| Kill Confirmed | Collect enemy tags to score | 65 tags |
| Grind | Tags must be banked at a point | 50 |
| Drop Zone | Capture a rotating care-package zone | 100 |

## One-life / round modes
| Mode | Rules |
|---|---|
| Search & Destroy | Attack/defend, bomb plant/defuse, no respawn per round, best of 11 |
| Cyber Attack | Like S&D but you can revive downed teammates |
| Demolition | Attack/defend two bomb sites, respawns on |
| Infected | Survivors vs infected, transferred on death |

## Special
| Mode | Rules |
|---|---|
| Gunfight | 2v2, random identical loadouts each round, small maps |
| Gun Game | Cycled weapon list; each kill advances your weapon |
| Ground War / Invasion | Large-scale, vehicles, many players |
| Realism | No HUD, restricted health model |

## Match flow
Playlist select → lobby → map rotation/vote → pre-match countdown → match → scoreboard →
after-action report → next map. Private/custom matches: host edits rules; client-proposed values
are **sanitised** server-side (the reference repo clamps scoreLimit/time/maxPlayers/respawn).

## Our decision
- `GameModeDefinition` plain data + `MatchSystem` reading only the definition (no per-mode branches).
- Build order: **TDM + FFA** → **Domination + Hardpoint + Kill Confirmed** → **S&D + Cyber Attack** →
  **Gunfight + Grind + Drop Zone + Demolition + Infected** → **Ground War/Invasion + Realism**.
- Custom-match overrides with clamps, mirroring the reference's `RULE_LIMITS`.

## Acceptance test
Each mode ends on its score/time limit, awards the correct winner, and respawn/round rules are
enforced by the server (a client cannot end or extend a match).
