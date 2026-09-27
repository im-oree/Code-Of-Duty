# 25 — Audio Design

Audio is half of how a shooter feels, and it is the most common thing left to last. It isn't here.
All audio is original or open-licensed (doc 01 §5).

## 1. Mix architecture

```
Master
├── SFX
│   ├── Weapons        (own layer set, §3)
│   ├── Impacts
│   ├── Foley          (footsteps, gear, movement)
│   ├── Equipment
│   └── World          (doors, destructibles, streaks)
├── Ambience
├── Voice              (callouts, operator lines, announcer)
├── Music              (menu, match start/end, stings)
└── UI
```

Each bus has a settings slider. **Ducking rules**: an explosion ducks Ambience −6 dB for 900 ms;
announcer ducks SFX −3 dB; the player's own weapon ducks distant gunfire so your gun always cuts
through.

## 2. Priority & voice management

Hard cap on concurrent voices (64). Priority order when over budget:
`own weapon > incoming damage > nearby enemy gunfire > teammate gunfire > footsteps > impacts >
casings > ambience`. Distance and occlusion reduce priority. A dropped sound never produces a
click — it fades over 30 ms.

## 3. Weapon audio — layered, not one file

Each shot is assembled from layers so weapons sound distinct without hundreds of recordings:

| Layer | Purpose |
|---|---|
| `mechanism` | The action: click, bolt, hammer. Identity at close range. |
| `body` | The main transient + tone. The "character". |
| `sub` | Low-frequency punch. Dropped at distance. |
| `tail` | Environment reverb tail — **swapped by environment** (indoor/outdoor/tunnel/open) |
| `distance` | A separate crack/slap sample used beyond ~25 m |

Rules:
- Your own weapon has a dedicated close mix with more `sub` and less `tail`.
- Enemy weapons at distance are dominated by `distance` + `tail`, which is what makes gunfire
  directionally readable.
- Pitch/level randomised ±2% / ±1.5 dB per shot to avoid machine-gun sameness.
- Suppressors change layers, not just volume.
- Every weapon also has: reload (multi-part, synced to animation events), swap in/out, empty
  click, fire-mode switch, ADS-in/out foley.

## 4. Footsteps & foley

- Surface-driven (doc 09 §12), triggered by the **stride phase**, never a timer.
- Per surface: `walk`, `run`, `sprint`, `land_soft`, `land_hard`, `slide`, `crouch`, `mantle`.
- Own footsteps mixed quieter than others' — you need to hear *them*, not yourself.
- Gear foley (straps, magazines, plates) as a separate quieter layer scaled by speed; it's what
  makes tac sprint sound urgent.
- **Audible radius is a gameplay value** (doc 09 §12) and must match the actual attenuation curve —
  if a player can hear you at 30 m, the design doc says 30 m.

## 5. Spatialisation & occlusion

- All world SFX are 3D with custom rolloff curves per category (gunfire carries far; footsteps
  fall off fast).
- HRTF-style binaural option in settings.
- **Occlusion:** a cheap raycast between source and listener; a blocked path applies a low-pass
  and attenuation. Partial occlusion via a 3-ray sample. Budgeted and round-robined, not per-frame
  for every source.
- Reverb zones per map area (doc 17 §2) drive the `tail` layer selection.

## 6. Ambience

Per-map bed + randomised one-shots (wind, distant machinery, birds, creaks) positioned in the
world, not stereo-baked. Intensity shifts with match state (quieter during S&D rounds, fuller in
warmup).

## 7. UI audio

One set, defined once in the design system (doc 20 §8): focus tick, press, confirm, back, error,
tab change, equip, unlock. Short, dry, low-frequency-light so they never mask gameplay.

## 8. Voice

- **Announcer**: match start, score events, round results, streak warnings. Sparse.
- **Operator callouts**: reload, grenade, enemy spotted, low health, objective. Cooldowns per
  category so it never becomes chatter.
- **Bots use the same callouts** as players would (doc 19) — part of what makes them read as human.
- Subtitles for everything, on by default in accessibility.

## 9. Dynamic range & presets

| Preset | Use |
|---|---|
| `Home Theatre` | Full dynamic range |
| `TV` | Compressed |
| `Headphones` | Compressed + HRTF + boosted footsteps |
| `Night` | Heavy compression, reduced low end |

## 10. Technical

- Compressed in memory for short SFX; streamed for music/ambience.
- Pooled `AudioSource`s; zero runtime allocation in the audio path.
- `SoundLibrary` asset maps logical ids → clip sets with randomisation; **gameplay code plays ids,
  never clips**.
- Audio events are triggered by animation events and gameplay events, never by polling.
- All timings for animation-synced audio come from the clip's event asset (doc 11 §9), so replacing
  an animation keeps audio in sync.

## 11. Acceptance (P7)

- [ ] Every weapon has the full layer set and is identifiable by ear alone.
- [ ] Footstep audible radius matches the documented gameplay values (measured).
- [ ] Occlusion audibly works and stays within CPU budget.
- [ ] No gameplay code references an `AudioClip` directly.
- [ ] Voice cap never exceeded; no clicks under stress (a 12-bot firefight test).
- [ ] All four dynamic-range presets produce a sane mix.
