# 20 — UI Design System

> User requirement: *"use the best UI for this kind of game … the UI must be consistent on all
> screens … nice layout, all UI redesigned fully, modern and reactive."*

One system. Every screen is built from it. No screen invents its own spacing, colour, or
interaction.

## 2. Art direction

**Military-technical, dark, high-contrast, confident.** Information-dense without clutter.
Sharp geometry, thin rules, generous negative space, one hot accent colour used sparingly so it
always means "this is the thing".

Explicitly **not**: neon/cyber, rounded-friendly, glassmorphism, or busy textures behind text.

All art is original vector/procedural (doc 01 §5). No third-party UI kits or extracted game art.

## 3. Design tokens (`UIThemeAsset`)

Every value below is a field on one ScriptableObject. **No literal colours or sizes in UI code.**

### Colour
| Token | Value | Use |
|---|---|---|
| `bg/base` | `#0B0D10` | App background |
| `bg/raised` | `#12151A` | Panels |
| `bg/overlay` | `#0B0D10` @ 88% | Modals, pause |
| `surface/card` | `#171B21` | Cards, rows |
| `surface/cardHover` | `#1E242C` | Hover/focus fill |
| `line/subtle` | `#242A33` | Dividers |
| `line/strong` | `#39414D` | Card borders |
| `text/primary` | `#F2F5F8` | Headings, values |
| `text/secondary` | `#9AA5B1` | Labels |
| `text/disabled` | `#5A626C` | Locked items |
| `accent/primary` | `#FF7A1A` | Selection, focus, primary action |
| `accent/dim` | `#B2540F` | Pressed accent |
| `team/friendly` | `#4FA3FF` | Friendly everywhere |
| `team/enemy` | `#FF4F4F` | Enemy everywhere |
| `state/success` | `#48D18A` | Positive stat delta |
| `state/warning` | `#FFC24B` | Caution |
| `state/danger` | `#FF4F4F` | Negative stat delta, destructive |

Colour-blind alternates for `team/*` and `state/*` ship from day one as a settings toggle
(deuteranopia, protanopia, tritanopia sets).

### Type
One family, three weights. Original or open-licensed only.

| Token | Size | Weight | Tracking | Use |
|---|---|---|---|---|
| `display` | 48 | Bold | +2% | Screen titles |
| `h1` | 32 | Bold | +2% | Section headers |
| `h2` | 24 | Semibold | +1% | Card titles |
| `body` | 17 | Regular | 0 | Descriptions |
| `label` | 14 | Semibold | +6% UPPER | Field labels, tabs |
| `caption` | 12 | Regular | +4% | Hints, metadata |
| `numeric` | 20 | Semibold tabular | 0 | Stats, ammo, timers |

### Spacing — 4 px base, 8 px rhythm
`xs 4 · sm 8 · md 16 · lg 24 · xl 32 · xxl 48 · section 64`

### Geometry & motion
- Radius: `0` (sharp) for panels, `2` for cards, `999` only for pills/avatars.
- Borders: 1 px `line/strong`; **left accent bar 3 px** marks selection.
- Elevation by contrast, not shadow.
- Motion: `fast 120 ms`, `base 180 ms`, `slow 280 ms`; easing `cubic-bezier(0.2, 0, 0, 1)`.
  Hover 120 ms, focus 120 ms, screen transitions 180 ms, modal 220 ms.
- **Reduced-motion setting** disables all non-essential animation.

## 4. Layout grid

- 12-column grid, 24 px gutters, 48 px page margins at 1920×1080.
- Reference resolution 1920×1080, `CanvasScaler` scales with screen height, matching 0.5.
- **Safe area:** all interactive content inside a 5% inset when TV-safe mode is on.
- Standard page structure, identical on every screen:

```
┌────────────────────────────────────────────────────────────┐
│ HEADER   logo · tabs · player card · currency · settings   │  88 px
├────────────────────────────────────────────────────────────┤
│                                                            │
│  CONTENT   (screen-specific, on the 12-col grid)           │
│                                                            │
├────────────────────────────────────────────────────────────┤
│ FOOTER   contextual button hints (device-aware glyphs)     │  56 px
└────────────────────────────────────────────────────────────┘
```

The header and footer are **one shared prefab-free component** instantiated by the screen
framework — that is what makes consistency automatic rather than a discipline problem.

## 5. Component library

Each is a script + a theme-driven builder, controller-navigable, with defined states
(`default / hover / focused / pressed / selected / disabled / locked`).

| Component | Notes |
|---|---|
| `Button` | primary / secondary / ghost / danger; optional glyph; hold-to-confirm variant |
| `TabBar` | top-level nav; bumper-switchable; animated underline |
| `SubTabBar` | secondary nav; trigger-switchable |
| `Card` | the workhorse: icon/art, title, subtitle, meta, lock state, accent bar when selected |
| `ListRow` | dense list item with leading icon, label, value, chevron |
| `StatBar` | six-axis stat display with ghost-overlay delta (doc 13 §5) |
| `Slider` | value + live numeric readout; pad: left/right nudge, hold to accelerate |
| `Toggle` | on/off with a sliding indicator |
| `Dropdown` | opens an overlay list, never an inline expansion |
| `Stepper` | discrete option cycling (the pad-friendly default for enums) |
| `TextField` | with validation state and an on-screen keyboard hook for pads |
| `Modal` | dim + centred panel, focus-trapped, `B`/Esc closes |
| `Toast` | transient bottom-right notification, stacked, auto-dismiss |
| `ProgressBar` | determinate + indeterminate |
| `Scroller` | auto-scrolls to keep focus visible with margin |
| `Tooltip` | on focus (pad) and hover (mouse) |
| `KeyHint` | device-aware glyph + label; the only way button prompts are drawn |
| `Avatar` / `PlayerCard` | operator portrait, name, level, rank |
| `3DViewport` | a render-texture panel for operator/weapon previews |

## 6. Screen framework

```csharp
abstract class UIScreen {
    ScreenId Id;
    bool     BlocksInput;       // pushes an input context (doc 05 §4)
    void     Build(UIContext ctx);
    void     OnShow(); void OnHide();
    IFocusable DefaultFocus;
}
```
- A **screen stack** with a global back action. `B`/Esc always pops. No screen implements its own
  back logic.
- Transitions are owned by the framework (fade+slide 180 ms), not by screens.
- Screens are **built in-scene** as real objects (see doc 21 §2) — not spawned from code at
  runtime — so they are inspectable and editable, which is an explicit user requirement.

## 7. Focus & navigation

Covered in doc 08 §7. Key points restated because they are design constraints, not just input:
- Explicit focus graph; visible focus ring; bumpers change tabs; `A`/`B` confirm/back everywhere.
- **Every screen must be fully operable with a gamepad**, verified by an automated reachability
  test.
- Mouse and pad can be used interchangeably without mode switching.

## 8. Feedback rules

| Interaction | Response |
|---|---|
| Focus change | 120 ms fill + accent bar + soft tick sound |
| Press | 60 ms scale to 0.98 + firmer click |
| Confirm | accent flash + confirm sound |
| Invalid | 180 ms horizontal shake + error tone + reason text |
| Load | skeleton placeholders, never a blank panel |
| Destructive | hold-to-confirm with a radial fill |

Audio and visual feedback are defined **once** here and reused; screens do not pick their own
sounds.

## 9. Accessibility (from day one, not retrofitted)

- Colour-blind palettes; never colour alone to convey meaning (always pair with shape/icon/text).
- Minimum contrast 4.5:1 for body text, 3:1 for large text — enforced by a theme validator.
- Text scale setting 85%–130%; layouts must not break (tested at both extremes).
- Reduced motion; subtitle support; remappable everything; hold-vs-toggle for every hold action.
- Minimum touch/focus target 44 px.

## 10. Localisation

All strings via `Loc.Get("screen.key")` from a table asset. No literals in UI code. Layouts use
flexible sizing and are tested against a pseudo-locale with +40% string length.

## 11. Enforcement

A `COD / UI / Audit` editor command that fails on: hardcoded colours/sizes, missing focus
neighbours, contrast violations, unlocalised strings, components not from the library, and screens
missing header/footer. Consistency that isn't enforced decays.

## 12. Acceptance (P1)

- [ ] `UIThemeAsset` exists; zero hardcoded colours/sizes in UI code (audit clean).
- [ ] All components implemented with every state.
- [ ] Header/footer shared and identical on every screen.
- [ ] Pad-navigable everywhere; reachability test passes.
- [ ] Contrast, text-scale and colour-blind checks pass.
- [ ] Screenshot set of every screen and every component state in `Artifacts/p1/ui/`.
