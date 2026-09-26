# 12 — Gunplay & Ballistics

Weapons are **data**. This document defines the data schema, the math, and the feel rules.
All names are original in-fiction designations (doc 01 §5).

## 1. `WeaponProfile` schema

```
identity     id, displayName, class, faction, unlockLevel, icon, modelRef, viewmodelRef
fire         fireMode[], rpm, burstCount, burstDelay, firstShotDelay
damage       baseDamage, damageZones{head,upperTorso,lowerTorso,arm,leg}
ballistics   muzzleVelocity, dropFactor, penetrationPower, rangeCurve[]
accuracy     hipSpreadMin/Max, spreadPerShot, spreadRecovery, adsSpread
recoil       patternRef, kickMin/Max, visualKick, recoilRecovery, centeringSpeed
handling     adsInTime, adsOutTime, sprintOutTime, swapInTime, swapOutTime,
             moveSpeedScale, adsMoveSpeedScale, inertiaScale, swayScale
ammo         magazineSize, startingMags, reloadTacTime, reloadEmptyTime, cyclingAction
optics       defaultFov, adsFov, zoomLevels[], scopeOverlayRef
audio        fireLayers[], tailByEnvironment, reloadSet, emptyClick, mechanism
attachments  allowedSlots[], socketMap
feel         cameraShakePreset, springPreset, muzzleFlashScale, casingRef
```

Every field is tunable in the **COD > Game Config** window (already exists) and validated by
`Tools/validate-assets.mjs`.

## 2. Weapon classes (original roster, v1.0)

| Class | Role | RPM | Damage @0m | TTK target | Count v1 |
|---|---|---|---|---|---|
| Assault Rifle | all-round, 10–35 m | 620–760 | 28–34 | ~330 ms | 4 |
| SMG | close, mobile | 780–950 | 22–28 | ~290 ms | 4 |
| LMG | suppression, high mag | 520–680 | 30–36 | ~360 ms | 2 |
| Marksman | semi-auto precision | 320–420 | 48–62 | 2–3 shots | 2 |
| Sniper | one-shot-kill upper torso+ | 45–60 | 90–150 | 1 shot | 2 |
| Shotgun | ≤8 m | 70–180 | 12×8 pellets | 1–2 shots | 2 |
| Pistol | secondary | 400–620 | 26–32 | ~420 ms | 3 |
| Launcher | anti-streak / area | — | splash | — | 2 |
| Melee | fists / blade | — | 135 | 1 hit | 2 |

**TTK philosophy:** 250–450 ms. Fast enough to feel decisive, slow enough that reaction and
positioning matter. Headshot multiplier 1.4× (not instant-kill for automatics) so aim is rewarded
without making body shots pointless.

## 3. Damage model

```
final = base
      × rangeMultiplier(distance)      // from rangeCurve
      × zoneMultiplier(hitZone)
      × penetrationMultiplier(surfacesPassed)
      × (1 - armourAbsorption)
```

**Range curve** is a piecewise-linear array of `(metres, multiplier)` points, e.g. an AR:
`(0,1.0) (26,1.0) (38,0.82) (55,0.68) (∞,0.68)`. Explicit points beat exponential falloff formulas
because designers can see and edit them.

**Hit zones:** head 1.4, upper torso 1.1, lower torso 1.0, arm 0.9, leg 0.85. Hit zones are real
colliders on the rig, lag-compensated server-side (doc 18).

**Penetration:** each surface material has `penetrationCost`. A bullet with `penetrationPower`
remaining passes through and loses damage proportionally. Max 2 surfaces.

## 4. Ballistics

Two modes, chosen per weapon:

| Mode | Used by | Behaviour |
|---|---|---|
| **Hitscan** | pistols, SMGs, ARs, shotguns, LMGs | Instant, but with server-side lag compensation |
| **Projectile** | snipers, marksmen (optional), launchers | Travel time + gravity drop |

Projectile: `muzzleVelocity` 600–900 m/s, `dropFactor` scales gravity (0 = laser, 1 = realistic).
Simulated on the server in fixed steps; the client spawns a visual-only tracer that is corrected on
confirmation. Sniper travel time is a deliberate skill element at long range.

## 5. Spread & bloom

```
spread = base(stance) × stateMul × (1 + bloom)
bloom  += spreadPerShot  per shot
bloom  -= spreadRecovery × dt   (after firstShotRecoveryDelay)
```

| Modifier | Multiplier |
|---|---|
| ADS | 0.0 (pinpoint first shot) |
| Hipfire standing | 1.0 |
| Crouched | 0.82 |
| Prone | 0.65 |
| Moving (scaled by speed) | 1.0 → 1.45 |
| Sliding | 1.5 |
| Airborne | 1.8 |

ADS first shot is **exactly** on the reticle. Every skilled-shooter design depends on that.

## 6. Recoil

Two separate things, often conflated:

1. **Aim punch (gameplay):** the camera actually moves. Follows an authored **recoil pattern** —
   an array of `(horizontal, vertical)` offsets, one per shot, plus randomness bounds. Learnable,
   which is the point.
2. **Visual kick (cosmetic):** the weapon model kicks back and rotates, on a spring (doc 11 §6).
   Does not affect where bullets go.

```
RecoilPattern:  Vector2[] steps; float randomH; float randomV; int loopFrom;
```
After firing stops, the camera **re-centres** toward the pre-fire direction at `centeringSpeed`,
but only by the amount the player didn't already compensate — so pulling down manually isn't
punished.

## 7. ADS

| Parameter | Behaviour |
|---|---|
| ADS-in / out | per weapon, 0.18–0.42 s, eased (not linear) |
| FOV | world FOV lerps to `adsFov`; viewmodel FOV is **separate** so the gun doesn't distort |
| Sensitivity | relative-zoom scaling (doc 08 §4) |
| Sway | ×0.2, plus breathing sway on scopes |
| Hold breath | scopes only; steadies for 3.2 s, 4 s recovery |
| Variable zoom | multi-level optics cycle with a bind; each level has its own overlay + sens |
| Scope overlay | full-screen mask with parallax; edges blur; occludes the rest of the HUD |
| Flinch | taking damage while ADS punches the aim, scaled by damage and reduced by a perk |

## 8. Reloading

- **Tactical reload** (mag not empty) is faster and keeps the chambered round (+1 ammo).
- **Empty reload** is longer and includes the bolt/slide release.
- Both are **cancellable** by sprint or weapon switch, with the mag state preserved correctly
  (ammo is only committed at the `MagIn` animation event — cancel before it and you keep the old
  count; this is the standard behaviour and players rely on it).
- Cycling actions (pump/bolt) are per-shot, cancellable into ADS, and block firing until complete.

## 9. Feedback (the part that sells it)

| Element | Rule |
|---|---|
| Muzzle flash | Additive quad + point light, 40 ms, scaled by weapon; **never** blinds in ADS |
| Tracer | 1 in 3 rounds, velocity-matched to the ballistic solution, fades with distance |
| Impacts | Per-surface decal + particle + sound; decals pooled with a hard cap |
| Casings | Ejected rigidbodies from the port, pooled, despawn after 4 s, sound on first bounce |
| Hitmarker | Shape+colour by outcome: normal / armour / headshot / kill / downed. Audio distinct per outcome. |
| Damage direction | Arc indicator around the reticle |
| Kill confirmation | Reticle pulse + distinct sound + killfeed entry, all within 60 ms of the server confirm |

**Latency rule:** local feedback (muzzle flash, sound, recoil, casing) fires **immediately** on the
client's own input. Only *outcome* feedback (hitmarker, kill) waits for the server. This is what
makes a networked shooter feel responsive.

## 10. Melee

- Quick melee (any weapon): 0.55 s, 2.2 m range, 135 damage — one-hit at full health.
- Dedicated melee stance: 3-hit combo with a `MeleeComboTracker`, faster, with lunge.
- Hit detection is a swept capsule on the server, timed to the `MeleeContact` animation event.

## 11. Server authority

| Decision | Owner |
|---|---|
| Did the shot fire? | Server (validates fire rate, ammo, state) |
| Where did it go? | Server (re-simulates from the client's reported aim + tick) |
| Did it hit? | Server (lag-compensated raycast against rewound colliders) |
| How much damage? | Server |
| Muzzle flash, sound, casing, recoil animation | Client, immediately |

A client that reports an impossible fire rate, an impossible aim delta, or shots while in a
non-firing state is rejected and flagged.

## 12. Acceptance (P3)

- [ ] Every class demonstrably distinct in a firing-range test: TTK, recoil shape, handling.
- [ ] Recoil patterns are learnable — the same pattern produces the same result twice.
- [ ] ADS first shot lands exactly on the reticle at 50 m for every weapon.
- [ ] Range curves produce the documented TTK at 5/15/30/50 m.
- [ ] Hit registration fair at 80 ms RTT in the network harness.
- [ ] No gameplay number exists outside a `WeaponProfile` asset.
