# Gunsmith — researched

**Source:** Activision, *The Basics of Call of Duty: Modern Warfare — Loadouts*, 2019-10-29
(primary); Nat Dart (Infinity Ward Senior UI Artist) Gunsmith design portfolio,
natdart.com/modern-warfare (visual/interaction reference).

## Attachment areas (8 on a rifle) + weapon perk
`muzzle`, `laser`, `optic`, `stock`, `rear grip`, `magazine`, `underbarrel`, `barrel`
plus a **weapon perk** slot (no visual change; e.g. FMJ = penetration + damage vs equipment/streaks,
Presence of Mind = hold breath indefinitely).

## Slots
- Default **5 attachments** equipped at once.
- A wildcard ("Gunfighter") raises it to **8**.
- You may equip fewer than five; none is legal too.
- Launchers have **no attachments**.

## Stats (6 axes shown as bar chart)
`accuracy`, `damage`, `range`, `fire rate`, `mobility`, `control`.
Every attachment shows a pro/con and a live delta on these axes (e.g. Merc Scout 3x: +accuracy,
+range, −control, slower ADS).

## Unlock / save model
- Weapons level up as you play; **attachment options unlock per weapon**.
- Attachments available differ by weapon.
- Save a finished build as a **Weapon Mod**; blueprints are prebuilt variants.

## Our decision
- `AttachmentDefinition` ScriptableObject: id, slot, display name, description, pro/con text,
  `WeaponStats` delta, unlockedAtWeaponLevel, optional mesh/material override + socket.
- `WeaponStats` is a struct of the 6 axes; a `WeaponProfile` holds base stats; the loadout
  aggregates attachment deltas with clamping.
- The 3D **gun bench**: live weapon preview that updates per attachment (we already preview
  weapons in the menu via `OperatorDisplay`/`MenuStage`).
- Camo/reticle/charm/sticker customization deferred to the Store/Barracks phase.

## Acceptance test
Equipping an optic changes the ADS sight picture and the stat bars, spawns the correct optic mesh,
persists across sessions, and is visible to other clients after spawn sync.
