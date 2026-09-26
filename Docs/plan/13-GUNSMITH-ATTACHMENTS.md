# 13 — Gunsmith & Attachments

## 1. Goal

Deep weapon customisation where every choice is a **visible trade-off**, the model physically
changes, and nothing is hardcoded.

## 2. Slots

Eight slot areas; a weapon may equip **five** at once. That constraint is the whole design — it
forces identity instead of a strictly-best build.

| Slot | Typical effects |
|---|---|
| Muzzle | recoil, sound suppression, range, ADS speed |
| Barrel | range, velocity, recoil control, mobility, hipfire |
| Optic | zoom, precision, ADS speed, screen clarity |
| Stock | recoil control, ADS/sprint-out speed, aim walking |
| Underbarrel | recoil, hipfire, aim stability, mobility |
| Magazine | capacity, reload speed, mobility |
| Rear Grip | recoil kick, ADS speed, flinch resistance |
| Ammunition | damage range, penetration, velocity |
| *Weapon Perk* | a 9th, weapon-specific slot; counts toward the five |

## 3. `AttachmentDefinition`

```
identity   id, displayName, slot, compatibleWeapons[] | compatibleClasses[],
           unlockLevel, icon, meshRef, socketOverrides
stats      StatModifier[] { statId, mode(Add|Mul|Override), value }
visual     socket, localPosition, localRotation, scale,
           hidesParts[]           // e.g. an extended mag hides the stock mag
functional muzzleFlashScale, soundProfileOverride, opticOverlayRef,
           zoomLevels[], laserRef, lightRef, bipodBehaviour
```

## 4. Stat aggregation

Order matters and is fixed:

```
1. start from WeaponProfile base stats
2. apply all Add modifiers      (sum)
3. apply all Mul modifiers      (product)
4. apply Override modifiers     (last wins, e.g. an optic setting adsFov)
5. clamp to the weapon's declared min/max per stat
```

Deterministic and order-independent within each phase, so the UI preview always matches the
runtime result. A shared `WeaponStatsResolver` is used by **both** the gunsmith UI and the spawn
code — never two implementations.

## 5. The six-axis display

Player-facing stats are an honest projection of the real numbers, not decoration:

| Axis | Derived from |
|---|---|
| Damage | baseDamage × effective range multiplier at 25 m |
| Accuracy | inverse of ADS spread + recoil magnitude |
| Range | distance at which damage drops below 80% |
| Fire Rate | rpm |
| Mobility | moveSpeedScale × sprintOut × swapIn, weighted |
| Control | recoil centering + pattern deviation |

The gunsmith shows the **delta** (green/red bar segment) for the hovered attachment before
equipping, computed by running the resolver with a hypothetical build.

## 6. Sockets on the model

Each weapon prefab carries named empty transforms:
`Socket_Muzzle`, `Socket_Barrel`, `Socket_Optic`, `Socket_Stock`, `Socket_Underbarrel`,
`Socket_Magazine`, `Socket_RearGrip`, `Socket_Laser`, plus function points `Muzzle_Point`,
`Eject_Point`, `Aim_Point`, `Grip_R`, `Grip_L`.

Rules:
- Attaching = instantiate the attachment mesh at its socket with its local offsets.
- `hidesParts` disables named child objects on the base weapon (a folding stock replaces the
  fixed one, not overlaps it).
- **Optics move `Aim_Point`.** The ADS camera aligns to `Aim_Point`, so a new optic automatically
  produces the correct sight picture with zero per-optic code.
- **Left-hand IK follows the underbarrel.** A foregrip moves `Grip_L`, so the hand re-grips
  correctly — no manual re-posing (this is exactly the kind of thing doc 11's rig-normalised IK
  makes free).
- The Weapon Setup Wizard (already in the repo) is extended to auto-generate sockets on import.

## 7. Gunsmith UI

Layout (built on the design system, doc 20):
- **Left:** slot list, each showing the equipped attachment and its net effect.
- **Centre:** large 3D weapon preview on a turntable, real lighting, real attachments. Inspect
  controls (orbit/zoom) on both mouse and stick.
- **Right:** the attachment list for the selected slot, with per-item delta preview on hover/focus.
- **Bottom:** the six-axis chart with a ghost overlay of the current build vs the hovered build.
- **Header:** the standard app header (doc 21) — the gunsmith is a screen, not a special case.

Controller: bumpers change slot, stick scrolls the list, `A` equips, `X` clears, `Y` inspects.

## 8. Progression gating

Attachments unlock by **weapon level** (using that gun), not account level. Each weapon has 25
levels; unlocks are spread so the first 5 levels are quick and meaningful. `unlockLevel` lives on
the definition; the UI shows locked items with their requirement rather than hiding them.

## 9. Networking

- A build is `{weaponId, attachmentId[]}` — tiny, sent once on spawn/loadout change.
- The server resolves stats through the same `WeaponStatsResolver` and uses **its** numbers for all
  damage decisions. The client's resolved stats are for prediction and UI only.
- Attachment meshes are instantiated on every client so remote players visibly carry their build.
- Server validates: slot legality, ≤5 attachments, ownership/unlock status.

## 10. Extensibility

Adding an attachment is: create the asset, set slot + modifiers + mesh + socket offsets, done.
It appears in the gunsmith, on the model, in the stat math, and over the network with no code
change. That is the test for whether this system is built correctly.

## 11. Acceptance (P4)

- [ ] Build a weapon with 5 attachments; the model visibly changes; stats match the preview
      exactly; spawning with it produces identical server-side numbers.
- [ ] Equipping an optic changes the sight picture with no optic-specific code.
- [ ] Equipping a foregrip re-poses the left hand correctly (FP and TP screenshots).
- [ ] The 5-of-8 rule is enforced in UI and re-validated on the server.
- [ ] `WeaponStatsResolver` has a unit test covering add/mul/override/clamp ordering.
