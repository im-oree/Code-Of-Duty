# 14 — Loadout, Perks & Equipment

## 1. Loadout structure

Ten custom classes per profile. One class is:

```
LoadoutDefinition
  name
  primary     { weaponId, attachments[5], camoId }
  secondary   { weaponId, attachments[5], camoId }
  perk1 perk2 perk3
  lethal      { id, count }
  tactical    { id, count }
  fieldUpgrade
  streaks[3]          (see doc 15)
  operatorId
```

Stored per-profile as versioned JSON, synced to the server on spawn, validated server-side against
unlock state.

## 2. Perks — design rules

Three slots, each with a distinct **category** so builds can't stack one dimension:

| Slot | Category | Answers |
|---|---|---|
| Perk 1 | **Survivability / stealth** | How do I stay alive or stay hidden? |
| Perk 2 | **Combat / handling** | How do I win the gunfight? |
| Perk 3 | **Awareness / utility** | What do I know that others don't? |

Hard rules:
- No perk is strictly better than another in its slot.
- No perk removes a core skill expression (nothing that auto-aims, nothing that removes recoil).
- Every perk that affects what another player experiences must be **counterable** by a perk or a
  behaviour.
- Perk effects are implemented as **hooks** on a `PerkContext`, never as `if (perkId == …)`
  scattered through gameplay code.

### v1.0 perk list (original)

**Slot 1 — Survivability / stealth**
| Perk | Effect |
|---|---|
| `Muffled` | Footsteps 50% quieter; invisible to recon sweeps |
| `Plated` | +25 effective health, but −4% movement speed |
| `Resilient` | 60% less aim flinch; immune to concussion slow |
| `Scavenger` | Resupply ammo and equipment from eliminations |

**Slot 2 — Combat / handling**
| Perk | Effect |
|---|---|
| `Quickhands` | −25% reload and swap time |
| `Steady` | −20% recoil, +15% hold-breath duration |
| `Lightfoot` | −20% sprint-out time, +10% tac-sprint duration |
| `Overkill` | Two primary weapons (secondary slot becomes primary) |

**Slot 3 — Awareness / utility**
| Perk | Effect |
|---|---|
| `Spotter` | See enemy equipment and streaks through walls at 18 m |
| `Tracker` | Enemies leave brief footprints; see their last-known marker |
| `Hardwired` | Immune to enemy recon and disruption effects |
| `Engineer` | Faster objective capture; +30% damage to streaks |

## 3. Lethal equipment

| Item | Behaviour |
|---|---|
| `Frag` | Cookable, 3.5 s fuse, bounces, 6 m lethal / 9 m falloff |
| `Semtex` | Sticks to surfaces and players, 2.5 s fixed fuse |
| `Thermite` | Burning area denial, 5 s, damage over time |
| `Claymore` | Directional proximity mine, 1 s arm, 140° cone |
| `Throwing Knife` | One-hit kill, retrievable |

## 4. Tactical equipment

| Item | Behaviour |
|---|---|
| `Flash` | Vision whiteout scaled by angle and distance, 1.5–4 s |
| `Concussion` | Slows movement and turn rate 3 s, blurs edges |
| `Smoke` | Volumetric cover, blocks recon, 12 s |
| `Snapshot` | Pulse showing enemy silhouettes for 2 s |
| `Stim` | Instant health regen + brief speed boost, self-use |

## 5. Throwing mechanics

- **Cook** (hold) for fragmentation types; releasing early throws a longer arc.
- Three arcs: overhand (default), underhand (tap crouch, short low toss), lob (look up).
- The **trajectory is server-simulated**; the client shows a predicted arc preview while cooking.
- Bounce uses real physics with a damped restitution per surface.
- Damage is radius-falloff with **line-of-sight checks** — a wall between you and a grenade
  protects you. This is essential for fairness and is a server-side occlusion test.
- Kill credit, assists, and the killfeed all flow from the server's damage event.

## 6. Field upgrades

Charge over time (and via objective play), one per life, activated with a bind:

| Upgrade | Effect |
|---|---|
| `Deployable Cover` | A ballistic shield wall |
| `Recon Pulse` | A single wide sweep revealing enemies for 2 s |
| `Munitions Box` | Resupplies ammo/equipment for the team |
| `Jammer` | Disables enemy HUD and minimap in a radius |
| `Trophy System` | Destroys incoming projectiles in a small radius |

Charge rate is per-upgrade so strong ones take longer.

## 7. Implementation shape

```
PerkContext        // passed to hooks, holds player, weapon, damage event etc.
IPerkHook          // OnSpawn, OnDamageDealt, OnDamageTaken, OnReloadStart,
                   // OnSprintStart, OnKill, OnTick, ModifyStat(statId, value)
PerkRuntime        // resolves the 3 equipped perks into a hook list once at spawn
```

Stat-modifying perks feed the **same** `WeaponStatsResolver` as attachments (doc 13 §4) so effects
compose predictably and the UI can show the real resulting numbers.

Equipment is a small class hierarchy behind `IEquipmentBehaviour` with server-side simulation and
client-side presentation, mirroring the existing grenade implementation (which is already
server-simulated and is the right template).

## 8. Loadout UI

- Class selector rail (10 slots, renameable).
- Weapon rows open the gunsmith (doc 13) in place, not as a separate screen.
- Perk row: three cards, each opening a category list with clear descriptions and counters.
- Equipment row: lethal / tactical / field upgrade.
- **Live 3D preview** of the operator holding the current primary, updating instantly on change —
  this already exists in the project's menu and is preserved.
- Everything controller-navigable (doc 08 §7).

## 9. Server validation

On spawn the server checks: class index valid, every id exists, every item unlocked for that
profile, attachment count ≤5, slot legality, no duplicate perks. Invalid loadouts fall back to a
default class and log — never kick.

## 10. Acceptance (P4)

- [ ] 10 classes persist across sessions and are synced correctly to all clients.
- [ ] Each perk has a test proving its effect and its counter.
- [ ] Grenade LOS damage check verified: a wall between player and blast prevents damage.
- [ ] Equipment counts, refills, and respawn behaviour correct.
- [ ] No `if (perkId == …)` anywhere outside `PerkRuntime`.
