# 08 — Input & Controller Support

**Principle:** gamepad is not a port. Keyboard+mouse and gamepad are two implementations of one
contract, shipped together from day one, tested together, and never allowed to diverge.

## 1. The contract

Everything downstream of input reads `InputFrame` (doc 05 §4). Nothing else. The enforced rule:

> A file outside `Assets/Scripts/Input/` containing `Input.`, `Keyboard.current`, `Gamepad.current`
> or `Mouse.current` is a lint failure.

This one rule is what makes bots possible (doc 19), replays possible (doc 10 of the reference),
and controller support automatic — a system that reads `InputFrame` works on every device by
construction.

## 2. Action set

Actions are declared once in `InputActionId` and bound per-device. Adding an action is one enum
entry plus one binding row; it then appears in the rebind UI automatically.

| Action | KB+M default | Gamepad default | Notes |
|---|---|---|---|
| Move | `WASD` | Left stick | analogue on pad, digital on KB |
| Look | Mouse | Right stick | different curves — see §4 |
| Fire | `LMB` | Right trigger | |
| Aim | `RMB` (hold/toggle) | Left trigger | |
| Reload | `R` | `X` / Square | |
| Jump | `Space` | `A` / Cross | |
| Crouch | `C` (hold/toggle) | `B` / Circle | |
| Prone | `Z` | `B` hold | |
| Sprint | `Shift` | `L3` | |
| **Tac Sprint** | double-tap `Shift` *or* auto | double-click `L3` *or* auto | mode is a setting |
| Slide | `Shift`+`C` while running | `B` while sprinting | |
| Melee | `V` / `MMB` | `R3` | |
| Interact | `F` | `Y` / Triangle hold | |
| Switch Weapon | `1` `2` / `Q` | `Y` / Triangle | |
| Lethal | `G` | `R1` | |
| Tactical | `Q`(alt) / `4` | `L1` | |
| Field Upgrade | `X` | `L1`+`R1` | |
| Streak | `5`–`7` | D-pad | |
| Scoreboard | `Tab` (hold) | Touchpad / `Back` | |
| Pause | `Esc` | `Start` | |
| Ping | `MMB` alt | D-pad up | |

## 3. Device detection & glyph switching

- The active device is whichever produced the **last non-idle input**, with hysteresis (a stray
  mouse bump must not flip glyphs mid-gunfight: require >2 px mouse delta or a button press).
- `InputService.ActiveDevice` fires an event; the UI swaps every prompt glyph.
- Glyph sets: `KBM`, `XboxStyle`, `PlayStationStyle`, `Generic`. Chosen by device name, overridable
  in settings.
- Glyphs are **original vector art** in a TMP sprite atlas — no third-party button art.

## 4. Aim feel (the part that decides whether a pad feels good)

Mouse and stick need genuinely different processing. Getting this wrong is the most common reason
a PC shooter's controller support feels bad.

### Mouse
```
lookDelta = rawDelta * sensitivity * (adsMultiplier if ADS)
```
- **No acceleration** by default; optional, off.
- ADS multiplier is **relative**: `monitorDistanceRatio` style, so muscle memory transfers across
  zoom levels. Default coefficient 1.33.
- Raw input, polled at the highest available rate, applied at render rate (never at tick rate).

### Stick — the model
```
1. deadzone:     radial inner deadzone (default 0.08), outer 0.95, normalised after
2. response:     v' = v^exponent        (default exponent 1.8 — fine control near centre)
3. base rate:    turnRate = v' * maxTurnRate      (deg/s, default 220 hip / 120 ADS)
4. acceleration: ramp from 0 → 1 over accelTime (default 0.18 s) while held near full deflection
5. aim assist:   see §5
6. ADS scaling:  per-zoom multiplier, same relative model as mouse
```
Every constant above is exposed in Settings with sane presets (`Default`, `Precision`, `Fast`).

### Response curve settings the player gets
`Sensitivity` (0.1–10), `ADS sensitivity multiplier`, `Aim response curve`
(Standard / Linear / Dynamic), `Deadzone`, `Aim acceleration`, `Invert Y`.

## 5. Aim assist (gamepad only, and honest about it)

Three separate mechanisms, all **server-agnostic and purely local** (they modify *input*, so they
are naturally fair — they cannot do anything a player couldn't):

| Mechanism | What it does | Default |
|---|---|---|
| **Rotational assist** | When an enemy is near the reticle, camera rotation follows their movement | 0.35 strength, 6° cone |
| **Slowdown ("friction")** | Stick turn rate reduced while the reticle is over an enemy | 0.60 multiplier, 4.5° cone |
| **Precision/snap** | Small one-shot pull toward target on ADS press | 0.25 strength, 3° cone, 120 ms |

Rules: never active while hipfiring at >30 m; never targets downed/dead players; disabled in
KB+M; configurable and disclosable in settings; a server-side telemetry flag records that a client
is using pad input (for fair matchmaking later, never for gameplay effect).

## 6. Rebinding

- `InputBindings` (existing) becomes a thin layer over Input System's `InputActionAsset` with
  runtime rebinding (`PerformInteractiveRebinding`).
- Both devices are rebindable, independently, including composite bindings.
- Conflict detection with a clear resolution prompt.
- Persisted as JSON in `PlayerPrefs` (path already established), versioned so we can migrate.
- Control-style preferences that are *not* bindings live beside them: crouch hold/toggle, ADS
  hold/toggle, tac-sprint trigger (double-tap / auto / off), sprint hold/toggle,
  fire-while-sprinting, auto-reload, auto-mantle.

## 7. UI navigation (controller-first menus)

The failure mode to avoid: a menu that *technically* accepts a gamepad but is miserable to use.

**Model:** an explicit **focus graph**, not Unity's automatic navigation (which picks nonsense
neighbours in complex layouts).

- Every focusable widget declares `up/down/left/right` neighbours (auto-generated for grids and
  lists, hand-authored for irregular layouts).
- One **focus ring** visual, from the theme, always visible when the active device is a pad,
  hidden on mouse hover-based interaction.
- **Bumpers change top-level tabs** (`LB`/`RB`), **triggers change sub-tabs** where present.
  This is the genre convention and it must work everywhere.
- `A` = confirm, `B` = back (a global back stack, never a per-screen hack), `Y` = contextual,
  `X` = contextual, `Start` = apply/close.
- **Hold-to-confirm** for destructive actions, with a radial fill.
- Scroll views auto-scroll to keep focus visible with a margin.
- Every screen must be completable with the pad alone — an automated test walks the focus graph
  and asserts every interactive widget is reachable.

## 8. Haptics & feedback

- Rumble events routed through one `HapticService` with intensity/duration presets: fire (scaled
  by weapon class), hit taken, explosion (distance-scaled), streak ready, low health.
- Respects a global haptics slider (0 = off).
- Trigger effects (adaptive triggers) abstracted behind the same service so a console build can
  implement them without touching gameplay.

## 9. Cross-platform readiness

We are not shipping consoles now, but nothing may *preclude* them:

| Concern | Rule |
|---|---|
| Input | Only ever `InputFrame`. No platform branches in gameplay. |
| UI scale | Layout in a resolution-independent grid; safe-area margins respected from day one (5% TV safe area toggle in settings). |
| Text | All strings in a localisation table, never literals in UI code. |
| File IO | Through one `SaveService`; no direct `Application.dataPath` writes. |
| Frame pacing | No `Update`-order dependencies; no fixed 60 Hz assumptions. |
| Storage | Settings/progression serialised as versioned JSON with migration. |

## 10. Phase 1 acceptance

- [ ] `PlayerInputSource` produces `InputFrame` from both devices, with §4 processing.
- [ ] `Input_Handler`, `CharacterMove`, `WeaponController`, `WeaponMovementPose` read only
      `IInputSource`. Lint passes with zero banned-token hits.
- [ ] Tac sprint is triggerable on a gamepad (it currently is not — doc 03 D5).
- [ ] The entire front-end is navigable with a pad: every tab, every card, start a solo match,
      open settings, rebind a key, back out — without touching mouse or keyboard.
- [ ] Glyphs switch device correctly with hysteresis.
- [ ] Automated focus-graph reachability test passes on every screen.
