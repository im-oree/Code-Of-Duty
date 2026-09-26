# Scorestreaks (MW2019) — researched

**Source:** Activision, *The Basics of Call of Duty: Modern Warfare — Killstreaks*, 2019-10-31
(primary). Kill costs confirmed against community summaries.

Up to **3** streaks equipped. Earned by kills without dying; **progress resets on death**.
Hardline reduces cost by 1; Kill Chain lets streaks contribute. There is also a hidden fourth slot.

| Kills | Streak | Behaviour |
|---|---|---|
| 3 | Personal Radar | Escort drone, personal mini-map pings; fragile, can be shot down |
| 3 | Shield Turret | Manually-operated shielded .50 cal emplacement |
| 4 | Counter UAV | Jams enemy mini-maps + progressively disrupts HUD in radius |
| 4 | UAV | Team-wide mini-map sweeps; negated by the Ghost perk |
| 4 | Care Package | Throw marker → drop with a random streak (weighted to cheap ones) |
| 5 | Cluster Strike | Laser-designated mortar barrage in a circular zone |
| 5 | Cruise Missile | Tablet-launched, player-steered missile with boost |
| 5 | Precision Airstrike | Designate a line; two jets strafe it |
| 7 | Wheelson | Remote-controlled UGV with airburst turret |
| 7 | Infantry Assault Vehicle | Light tank; driver cannon + turret gunner .50 cal (large maps only, else Wheelson) |
| 7 | Sentry Gun | Automated turret, swivels ~90° either side |
| 8 | Emergency Airdrop | Three care packages in one drop |
| 8 | VTOL Jet | Initial missile barrage, then hovers as a defensive support platform (repositionable) |
| 10 | Chopper Gunner | Player-piloted gunship run: cannon + hydra rockets + thermal |
| 10 | White Phosphorus | Carpet line of incendiaries; heavy disorient + burn, affects owner too |
| 11 | Support Helo | AI-piloted circling helo with two auto-firing gunners |
| 12 | Gunship | 105mm / 40mm / 25mm selectable, thermal, orbits the map |
| 12 | Advanced UAV | Reveals enemies **and facing direction** to the whole team |
| 15 | Juggernaut | Care-package suit: heavy armour, minigun, limited mobility, togglable music |
| 30 (hidden) | Tactical Nuke | 30 kills in one life with loadout weapons only → ends the match immediately |

**Counters:** Counter UAV, Ghost, launchers (can destroy air streaks), shooting down drones;
Cruise Missile can be steered into an enemy streak.

## Our decision
- Port the whole set with real costs. Server owns earning + activation; clients own cameras/VFX
  (the reference repo's "events not objects" rule).
- Ship in waves: **wave 1** = UAV, Counter UAV, Personal Radar, Care Package, Precision Airstrike,
  Cluster Strike, Sentry Gun; **wave 2** = Chopper Gunner, VTOL, Support Helo, Gunship, Advanced UAV;
  **wave 3** = Wheelson, IAV, Juggernaut, Cruise Missile, Emergency Airdrop, White Phosphorus, Nuke.
- Low-poly proxies for every vehicle/aircraft streak; cinematic camera per piloted streak.

## Acceptance test
UAV activation on the server reveals enemies on every client's minimap; Ghost blocks it; CUAV
disables it; a launcher can destroy the UAV; kill counts reset correctly on death.
