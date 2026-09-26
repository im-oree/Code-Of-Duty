# 16 — Game Modes & Match Flow

## 1. Match state machine (server-owned)

```
   Lobby ──► Loading ──► Warmup ──► Countdown ──► Live ──► RoundEnd ──► Intermission
                                                    │           │            │
                                                    │           └──► (next round)
                                                    ▼
                                               MatchEnd ──► Scoreboard ──► Lobby
```

| State | Duration | Behaviour |
|---|---|---|
| `Lobby` | until host starts | Players join, pick loadouts, ready up |
| `Loading` | until all clients report ready (timeout 25 s) | Scene + asset load, progress bar |
| `Warmup` | 20 s (skippable when full) | Free play, no score, respawn instantly |
| `Countdown` | 5 s | Spawn locked, camera on the spawn view, mode intro |
| `Live` | mode timer | The match |
| `RoundEnd` | 6 s | Round result, final-kill replay (doc 10 of roadmap / P10) |
| `Intermission` | 12 s | Team switch, loadout change (round-based modes) |
| `MatchEnd` | 10 s | Victory/defeat, MVP, XP summary |
| `Scoreboard` | 15 s | Final stats, then back to lobby |

Every transition is a server event; clients react. A late-joining client is told the current state
and its remaining time, and catches up correctly.

## 2. `GameModeDefinition`

```
identity      id, displayName, description, icon
teams         teamCount(1|2), teamSize, friendlyFire, teamBalance
scoring       scoreLimit, timeLimitSeconds, scorePerKill, scorePerAssist,
              scorePerObjective, winCondition
rounds        roundBased, roundsToWin, roundTimeSeconds, switchSidesAt
respawn       respawnMode(Instant|Delayed|Wave|None), respawnDelay,
              waveIntervalSeconds, spawnProtectionSeconds
rules         streakMode, allowStreaks, allowFieldUpgrades, hardcore,
              minimapMode, killcamEnabled
objectives    objectiveType, objectiveCount, captureTime, rotationSeconds
overrides     sanitised custom-match overrides with clamps
```

## 3. v1.0 mode list

### Free-For-All
8 players, no teams. First to **30** eliminations or highest at **10:00**. Top 3 "win".
Instant respawn. Spawn logic must avoid the recent-death area.

### Team Deathmatch
6v6. First team to **75** or highest at **10:00**. Instant respawn.

### Domination
6v6, three capture points A/B/C. Ticks **1 point per held flag per 5 s**; score limit **200**,
time limit **10:00**. Capture time 6 s (faster with more players, capped at 3× rate).
Round-based: two halves with a side switch.

### Hardpoint
6v6, a single rotating objective. **1 point per second** held; score limit **250**, time **10:00**.
The zone rotates every **60 s** through an ordered list defined per map. Contested = no score.

### Search & Destroy
6v6, round-based, **no respawns**. Attackers plant on one of two sites (plant 5 s), defenders
defuse (7.5 s). Bomb timer 45 s. Round time 1:45. First to **6** rounds, sides switch at 6 total.
Killcam disabled; the spectator follows teammates.

**Why these five:** they cover the full spectrum — pure slayer (FFA/TDM), area control (Dom),
rotation pressure (Hardpoint), and tactical/no-respawn (S&D). Each stresses different systems
(spawns, objectives, timers, spectating) so building them proves the framework.

## 4. Spawn selection (the most under-appreciated system)

Bad spawns ruin a shooter. Our selector scores every spawn point each time someone respawns:

```
score = 0
  − enemyProximityPenalty      (steep under 18 m; lethal under 10 m)
  − enemyLineOfSightPenalty    (raycast from each known enemy position)
  − recentDeathPenalty         (your own death location, decaying over 12 s)
  − spawnTrafficPenalty        (how recently this point was used, by anyone)
  + teammateProximityBonus     (peaks around 15–25 m — near, not on top of)
  + objectiveRelevanceBonus    (mode-specific: your flag, the hardpoint's next zone)
  + zoneOwnershipBonus         (the map half your team currently controls)
  + randomJitter               (small, prevents deterministic spawn camping)
```

Then pick from the **top 15%** at random (not the single best — that is predictable).

Extra rules:
- **Spawn flipping** in TDM/FFA: when a team's control of a map region passes a threshold, the
  valid spawn set flips wholesale rather than drifting.
- Hard block: never spawn within 12 m of an enemy's current view frustum.
- Spawn protection: `spawnProtectionSeconds` of invulnerability, cancelled the instant you fire or
  ADS.
- S&D uses fixed team spawn sets, no scoring.
- Every map declares spawn **groups** with a team affinity and an objective association
  (doc 17 §4).

## 5. Teams & balance

- Auto-balance on join by score, never mid-round in round-based modes.
- Party/pre-made grouping is respected where possible (LAN: trivial; online: later).
- Team colours are **friendly = one hue, enemy = another**, consistently everywhere (nameplates,
  minimap, killfeed, objectives, outlines). Colour-blind-safe alternates are a setting from day one.

## 6. Scoring & the scoreboard

Per-player tracked: eliminations, deaths, assists, score, objective score, longest streak,
accuracy, damage dealt, headshots, distance travelled, time alive.

Scoreboard shows the essentials by default with an expanded view on hold. It is the **same widget**
in-match (hold Tab) and at match end (doc 20 consistency rule).

MVP at match end = highest weighted combination of score, objective contribution, and survival —
displayed with the operator model and the reason ("Top Objective Score").

## 7. Objectives framework

Objectives are components implementing `IObjective`:

```
IObjective:
  OnTick(server, dt)
  OnPlayerEnter/Exit(player)
  GetState() -> { ownerTeam, progress01, contested, activeUntil }
```

`CapturePoint`, `Hardpoint`, `BombSite`, `Payload` (future) all implement it. `MatchSystem` doesn't
know about specific objectives — it reads state and applies mode scoring. Adding a mode that uses
a new objective type requires no `MatchSystem` change.

## 8. Custom matches

Hosts can override: score limit, time limit, respawn mode/delay, streaks on/off, friendly fire,
health, team size, hardcore. Every override is **clamped** to a sane range declared on the mode
definition, so a bad value can't break the match state machine.

## 9. Networking

- `MatchSystem` runs only on the server. Clients receive a compact `MatchStateSnapshot` at 5 Hz
  plus immediate events for transitions.
- Objective state is delta-synced.
- Scoreboard data is sent at 2 Hz, and in full on request (Tab press).
- Late join: the server sends a full state bundle; the client reconstructs without special-casing.

## 10. Acceptance (P5)

- [ ] All five modes playable start-to-finish with bots and humans mixed.
- [ ] Round-based modes correctly switch sides, preserve score, and handle disconnects.
- [ ] Spawn selector never spawns a player in an enemy's view; measured over 500 spawns in a
      simulated match.
- [ ] Late join produces correct state in every phase, including mid-round S&D (as a spectator).
- [ ] Custom overrides cannot produce an invalid match.
- [ ] `MatchSystem` contains no mode-specific `if` chains.
