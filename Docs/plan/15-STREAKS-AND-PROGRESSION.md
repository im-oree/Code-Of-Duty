# 15 — Streaks & Progression

All streaks below are **original designs** for this project (doc 01 §5).

## 1. Streak framework

Three streak types; a player equips three, and they must be of ascending cost.

| Type | Earned by | Resets on death? |
|---|---|---|
| **Killstreak** | consecutive eliminations | yes |
| **Scorestreak** | accumulated score (kills, assists, objectives) | no (default mode setting) |
| **Support** | score, but cheaper and non-lethal | no |

Mode definitions choose the type (`GameModeDefinition.streakMode`), so objective modes can reward
objective play without a code change.

## 2. `StreakDefinition`

```
identity    id, displayName, icon, category, cost, costType(Kills|Score)
behaviour   controllerType, duration, cooldown, maxSimultaneous,
            isPiloted, requiresLineOfSky, blockedOnSmallMaps
delivery    callInTime, deploymentVfx, announcementVoiceLine
counters    counteredBy[] (e.g. Jammer, launcher lock, Hardwired)
```

## 3. v1.0 streak roster (original)

| # | Streak | Cost | Category | Behaviour |
|---|---|---|---|---|
| 1 | **Recon Sweep** | 3 | Support | A single radar sweep every 4 s for 30 s; reveals moving enemies |
| 2 | **Munitions Drop** | 4 | Support | Crate; team resupplies ammo + equipment |
| 3 | **Smoke Screen** | 4 | Support | Line of smoke along a chosen bearing |
| 4 | **Sentry Turret** | 5 | Killstreak | Auto-turret, 120° arc, destructible, 90 s |
| 5 | **Mortar Strike** | 6 | Killstreak | Three timed impacts on a map-selected point |
| 6 | **Hunter Drone** | 7 | Killstreak | Piloted recon/attack drone, 35 s |
| 7 | **Strafing Run** | 8 | Killstreak | A pass along a player-chosen line |
| 8 | **Counter-Recon** | 8 | Support | Disables enemy radar and reveals their streaks for 45 s |
| 9 | **Gun Platform** | 11 | Killstreak | Player-controlled aerial weapons platform, 40 s |
| 10 | **Siege Walker** | 15 | Killstreak | Heavy piloted ground unit, 45 s, destructible |

Design rules:
- Every streak at cost ≥7 has at least one **hard counter** (launcher lock, `Engineer` perk, EMP).
- No streak can win a round on its own.
- Piloted streaks leave the player's body **vulnerable** in the world.
- Streaks obey the map's `blockedStreaks` list — small maps disable air support.

## 4. Streak lifecycle

```
earn → available (HUD) → call in (bind) → [targeting mode] → confirm
     → server spawns controller → active → expire/destroyed → cleanup
```

- **Server owns** spawning, lifetime, damage, and destruction. Clients present.
- Piloted streaks take over the player's camera and input context (doc 05 §4 context stack); the
  player's body stays in the world and can be killed, which ends the streak.
- A **targeting mode** (tablet/map overlay) is used for placed streaks: the client picks a point,
  the server validates it (in-bounds, line of sky, not inside geometry).
- All streaks have a `counteredBy` list checked by the server before applying effects.

## 5. Progression

### Account level
- XP from: eliminations, assists, objectives, streaks, match completion, wins.
- 55 levels, then prestige-style resets (optional, cosmetic-only rewards).
- Unlocks: weapons, perks, equipment, streaks, operators, class slots.

### Weapon level
- Per-weapon, 25 levels, XP from that weapon's eliminations/damage.
- Unlocks attachments (doc 13 §8) — using a gun is how you improve it.

### Challenges
- Daily (3, rotating), weapon-specific, and long-term.
- Reward XP and cosmetics only. **Never gameplay advantage.**

### Persistence
```
ProfileSave (versioned JSON)
  accountXp, level, prestige
  weapons { id -> { xp, level, unlockedAttachments[] } }
  unlocks { perks[], equipment[], streaks[], operators[] }
  loadouts[10]
  settings, keybinds
  stats { kills, deaths, wins, timePlayed, accuracy, … }
```
Saved through one `SaveService` (doc 08 §9), migrated by version, and **never** trusted from the
client in a dedicated-server context — the server holds authoritative progression when one exists;
LAN play trusts the local profile.

## 6. Economy sanity

- A player of average skill should earn their first streak in most lives, and their top streak a
  few times per session. If telemetry (P11) shows the 11-cost streak firing more than ~1 in 8
  lives, costs move.
- Streaks are **not** stacked across lives for killstreak-type (by definition), which keeps the
  ceiling in check.

## 7. UI

- HUD: earned streaks as a small stack near the ammo counter, with the next one's progress.
- Announcements: subtle text + audio sting on earn, on enemy call-in, and on destruction.
- Barracks tab: full progression view — level, weapon levels, challenges, stats.

## 8. Acceptance (P7)

- [ ] Earn, call in, use, and lose each streak; server-authoritative throughout.
- [ ] Each ≥7-cost streak is demonstrably counterable.
- [ ] Piloted streaks leave the body vulnerable; killing the body ends the streak.
- [ ] Progression persists, migrates across a version bump, and never unlocks power.
- [ ] Map `blockedStreaks` respected.
