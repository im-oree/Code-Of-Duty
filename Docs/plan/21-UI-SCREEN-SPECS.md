# 21 — UI Screen Specifications

Built entirely on the design system in doc 20. Every screen uses the shared header + footer.

## 1. Information architecture

```
Boot ─► Frontend Hub
          ├─ PLAY        quick play · host · join · server browser · solo/bots
          ├─ OPERATORS   roster · skins · preview
          ├─ LOADOUT     classes ─► GUNSMITH (in-place) · perks · equipment · streaks
          ├─ BARRACKS    progression · challenges · stats
          ├─ STORE       cosmetics (placeholder, honest about it)
          └─ SETTINGS    gameplay · controls · video · audio · accessibility
        ─► Lobby ─► Loading ─► MATCH (HUD, doc 22) ─► End of Match ─► Frontend
```

## 2. Hard requirement: the menu is real scene content

> *"Every main menu thing is meant to be in scene, not as prefab, and must be editable — I can see
> it in pause or play."*

This is a structural rule, not a preference:

| Rule | Consequence |
|---|---|
| The `Frontend` scene contains the **actual, saved** canvas hierarchy, 3D stage, operator, lights and camera | You can select and tune any of it in the Inspector |
| Runtime code **binds** to existing objects; it never creates, destroys or rebuilds the menu | Inspector edits survive Play |
| No `[ExecuteAlways]` component that mutates and dirties the scene | No bake/purge races (doc 03 D1) |
| Exactly one component owns each subsystem, found by an explicit serialized reference | No `FindObjectOfType` scavenging |
| If a required object is missing, log a clear error and **offer an editor command to build it** — never silently rebuild at runtime | Deterministic |

A one-time editor command `COD / UI / Generate Frontend Scene` builds the hierarchy from the design
system when a screen is added or restructured. It runs **on demand only**, from a menu item, with a
confirmation — never automatically.

## 3. Header (shared, 88 px)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [MARK] CODE OF DUTY   PLAY  OPERATORS  LOADOUT  BARRACKS  STORE   ⚙  ⏻   │
│                       ────                                    [Lv 23 ▰▰▱]│
└──────────────────────────────────────────────────────────────────────────┘
```
- Left: original wordmark.
- Centre: `TabBar`. Active tab: `text/primary` + a 3 px `accent/primary` underline that slides
  between tabs over 180 ms. Inactive: `text/secondary`.
- Right: player card (operator portrait, name, level + XP bar), settings, quit.
- `LB`/`RB` cycle tabs. Tab order never changes.

## 4. Footer (shared, 56 px)

Contextual `KeyHint`s, right-aligned, device-aware glyphs, e.g.
`[A] Select   [B] Back   [Y] Inspect   [LB/RB] Tabs`. Updated by the focused widget, not
hand-written per screen.

## 5. PLAY

```
CONTENT (12 col)
┌──────────────────────────────┬───────────────────────────────┐
│  col 1-7                      │  col 8-12                     │
│  ┌─────────────┬────────────┐ │  LAN SERVERS           [↻]    │
│  │ QUICK PLAY  │ HOST MATCH │ │  ┌──────────────────────────┐ │
│  │ large card  │ large card │ │  │ Room · Map · Mode  4/12  │ │
│  └─────────────┴────────────┘ │  │ Room · Map · Mode  8/12  │ │
│  ┌─────────────┬────────────┐ │  │ …                        │ │
│  │ SOLO + BOTS │ DIRECT IP  │ │  └──────────────────────────┘ │
│  └─────────────┴────────────┘ │  status: "Searching LAN…"     │
│                               │                               │
│  MODE  [Team Deathmatch  ▾]   │  PLAYER NAME [__________]     │
│  MAP   [Any             ▾]    │                               │
└──────────────────────────────┴───────────────────────────────┘
```
- Cards use art + title + one line of description. Selected card gets the accent bar.
- Server browser rows show version-mismatch state greyed with a reason (doc 18 §3).
- `HOST MATCH` opens an inline panel (room name, max players, mode, map, private toggle) — a
  panel, not a new screen.
- Behind everything: the **3D stage** with the live operator (§10).

## 6. OPERATORS

- Left: operator roster grid (`Card` with portrait, name, faction, lock state).
- Centre: the live 3D operator on the stage, rotatable, in the current loadout's primary.
- Right: skin variants, operator bio, unlock requirement if locked.
- Changing selection updates the 3D model instantly — no reload, no flicker.

## 7. LOADOUT

- Left rail: 10 class slots, renameable, with a small primary-weapon silhouette.
- Centre: the class sheet —
  `Primary ▸`, `Secondary ▸`, `Perk 1/2/3`, `Lethal`, `Tactical`, `Field Upgrade`, `Streaks ×3`.
- Right: the 3D preview of the operator holding the current primary.
- Selecting a weapon row opens the **Gunsmith in place** (doc 13 §7) — the header stays, the
  content area transitions. It is a state of this screen, not a new destination.

## 8. BARRACKS

Sub-tabs: `PROGRESSION · CHALLENGES · STATS · WEAPONS`.
- Progression: account level, XP bar, recent unlocks, next unlock preview.
- Challenges: daily + long-term with progress bars.
- Stats: K/D, W/L, accuracy, favourite weapon, time played, per-mode breakdown.
- Weapons: per-weapon level, attachment unlock tree, usage stats.

## 9. STORE

Placeholder, and **honest about it**: a clear "Coming soon" state with a short description of what
will live here. Never a fake shop. Same layout language so it doesn't look unfinished — it looks
*planned*.

## 10. SETTINGS (shared by menu and pause)

**One component**, embedded in the SETTINGS tab and in the pause menu. Sub-tabs:

| Sub-tab | Contents |
|---|---|
| Gameplay | FOV, ADS mode, crouch mode, tac-sprint trigger, auto-mantle, auto-reload, fire-while-sprinting |
| Controls | Device selector, sensitivity, ADS multiplier, response curve, deadzone, invert, aim assist, full rebinding for both devices |
| Video | Resolution, mode, v-sync, frame cap, quality preset, render scale, MSAA, shadows, textures, effects |
| Audio | Master, SFX, music, voice, UI; dynamic range; subtitle toggle |
| Accessibility | Colour-blind mode, text scale, reduced motion, HUD scale/opacity, TV-safe area, hold-vs-toggle |

Every row is a library component. Changes apply **live** with an Apply/Revert pair only for
destructive video changes.

## 11. The 3D stage (menu backdrop)

Real, saved scene content in `Frontend`:
- Dark industrial set, volumetric fog, one warm rim light, one cool key, one fill.
- Drifting dust and slow smoke.
- The **operator model**, idling, holding the selected primary weapon.
- A dedicated `MenuCamera` with a slow parallax drift and a per-tab framing (PLAY = mid-shot,
  OPERATORS = full body, LOADOUT = weapon-focused three-quarter).
- Tab changes move the camera between authored framings over 280 ms.

**Fixes required here** (doc 03 D1/D3): exactly one operator, driven by an idle animation, weapon
attached to the hand socket, no duplicate stage.

## 12. LOBBY

Player list with team assignment, ready state, operator portraits; room settings summary; host
controls (start, kick, change map/mode); chat. Shown as an overlay over the PLAY tab, not a
separate scene.

## 13. LOADING

Map art, map name + mode, a determinate progress bar driven by real load progress, a rotating tip,
and the player list filling in as clients report ready.

## 14. PAUSE (in-match)

Dim overlay; `RESUME · LOADOUT · SETTINGS · SCOREBOARD · LEAVE MATCH`. Same components, same
header treatment (compact variant). Leaving requires hold-to-confirm.

## 15. END OF MATCH

Victory/defeat banner, final score, MVP with the operator model and the reason, the full
scoreboard (the **same** scoreboard widget as in-match), XP summary with animated bars, unlocks
earned, then auto-return to the lobby with a countdown and a skip.

## 16. Screen inventory & build order (P1)

| Order | Screen | Depends on |
|---|---|---|
| 1 | Theme + component library | — |
| 2 | Screen framework + header/footer | 1 |
| 3 | PLAY | 2 |
| 4 | SETTINGS (shared) | 2 |
| 5 | OPERATORS | 2 + stage |
| 6 | LOADOUT | 2 + stage |
| 7 | LOBBY + LOADING | 2 |
| 8 | PAUSE | 4 |
| 9 | BARRACKS / STORE | 2 |
| 10 | END OF MATCH | 2 + scoreboard |

## 17. Acceptance (P1)

- [ ] All screens use the shared header/footer and library components only.
- [ ] Menu content is saved scene objects; Inspector edits survive entering Play.
- [ ] Exactly one operator, animated, holding the selected weapon.
- [ ] Every screen reachable and operable with a gamepad alone.
- [ ] Settings is literally the same component in the menu and in pause.
- [ ] Screenshot of every screen in `Artifacts/p1/ui/` from the harness.
