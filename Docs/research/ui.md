# UI/UX — researched

**Sources:** Nat Dart (Infinity Ward Senior UI Artist) MW2019 portfolio — "minimalist aesthetic that
let the weaponry take center stage", plus the UI Style Guide he authored (natdart.com/modern-warfare);
Activision loadout guide for menu structure; UI reference galleries
(interfaceingame.com/games/call-of-duty-modern-warfare, gameuidatabase.com).

## Reference principles we adopt
- **Minimalist chrome, weapon-forward.** Dark, low-noise surfaces; the 3D weapon/operator is the hero.
- **Horizontal top tab bar** for the front-end modules, consistent on every screen.
- **The "gun bench"**: loadout/Gunsmith is a 3D inspection space, not a form.
- **Six-axis stat bars** for weapons; pro/con text on every attachment.
- **Consistent hierarchy**: category → item → detail, with a persistent back/confirm affordance.
- High contrast for readability; large hit areas; gamepad-first focus order.

## Screen inventory (target)
**Front-end:** Hub, Play (playlists / quick play / LAN browser / private / custom rules / Warzone),
Operators, Loadout + Gunsmith (gun bench, attachment tree, stat bars, mods/blueprints),
Barracks (rank, challenges, camos, records, stats), Store/Battle Pass, Social (party/friends/invites),
Settings (video/audio/controls/keybinds/accessibility), Loading.

**In-match:** dynamic crosshair, hitmarker + kill marker, ammo/mag/reserve, health + low-health
vignette, minimap/radar (normal / UAV / CUAV / Advanced UAV states), compass, objective tracker,
streak HUD, equipment HUD, match score bar + timer, killfeed, scoreboard, damage-direction
indicators, grenade/streak warnings, disorient overlays (flash/stun/gas), traversal + interaction
prompts, vehicle HUD, scope overlay, death overlay, killcam, spectator bar, pause, after-action report.

## Our decision
- One `ScreenFramework`: persistent header (logo + tabs) + content region + status bar, identical
  everywhere; all screens assembled from shared `UITheme` widgets.
- Extend the existing code-built uGUI/TMP system; add a focus-navigation layer for controllers with
  visible focus ring and gamepad glyph hints.
- Reference screenshots are attached to each screen's implementation note for side-by-side comparison.

## Acceptance test
Every screen loads through the framework, shares identical chrome, is fully navigable with a
controller alone, and matches its reference layout within the documented tolerances.
