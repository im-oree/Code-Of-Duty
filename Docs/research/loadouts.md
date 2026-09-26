# Loadouts, perks, equipment, field upgrades — researched

**Source:** Activision, *The Basics of Call of Duty: Modern Warfare — Loadouts*, 2019-10-29 (primary).

## Loadout anatomy
- **Primary** weapon — Assault Rifle, SMG, Shotgun, LMG, Marksman Rifle, Sniper Rifle,
  Melee (Riot Shield).
- **Secondary** — Handgun, Launcher, Melee (Combat Knife).
- **Perks** — three colour categories (Blue / Red / Yellow), **one per category**.
  Blue examples: Double Time (longer tac sprint + faster crouch move), Quick Fix, Sleight of Hand,
  Scavenger, Cold-Blooded, E.O.D. Red examples: Restock, Hardline, Ghost, High Alert, Kill Chain,
  Tracker, Overkill. Yellow examples: Tune Up, Amped, Battle Hardened, Shrapnel, Spotter.
  *(Exact per-slot assignment varies by title — pinned during implementation.)*
- **Lethal** — Frag, Semtex, Molotov, Thermite, C4, Claymore, Proximity Mine, Throwing Knife.
- **Tactical** — Flash, Stun, Smoke, Snapshot, Stim, Decoy, Gas, Heartbeat Sensor.
- **Field Upgrade** — Munitions Box, Stopping Power Rounds, Trophy System, Deployable Cover,
  Recon Drone, Dead Silence, Tactical Insertion.
- **Specialist mode** — forgo streaks; gain an extra perk at **2, 4 and 6** kills without dying.
- Multiple custom loadout slots; editable mid-match from the pause menu.

## Our decision
- `LoadoutDefinition` (primary, secondary, perk1/2/3, lethal, tactical, fieldUpgrade, specialist flag)
  persisted locally now, synced to server on spawn.
- Perks are `PerkDefinition` data with hook points (`OnSpawn`, `OnKill`, `ModifyRegen`,
  `ModifyTacSprint`, `HideFromRadar`, …) so adding one is data + a hook, never a branch.
- Specialist implemented as the same perk hooks granted at kill thresholds.
- Mid-match editing writes the same definition and takes effect on respawn.

## Acceptance test
A loadout selected in the menu spawns the correct weapons/perks/equipment on every client; Ghost
hides the player from UAV; Hardline reduces streak cost; Specialist grants perks at 2/4/6 kills.
