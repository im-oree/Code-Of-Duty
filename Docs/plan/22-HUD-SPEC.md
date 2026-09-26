# 22 — In-Match HUD

## 1. Philosophy

The HUD answers four questions instantly and nothing else:
**Where am I? · What's my state? · What can I shoot with? · What just happened?**

Rules:
- Nothing in the centre 40% of the screen except the reticle and transient hit feedback.
- Elements appear when relevant and fade when not. A full-health, full-ammo, idle player sees a
  nearly empty screen.
- Every element scriptable to off/opacity/scale in settings (competitive players will strip it).
- All colours/sizes from the theme (doc 20). Friendly blue / enemy red, always.

## 2. Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [minimap]         ◄── compass strip ──►         [score  timer  score]    │
│                                                                          │
│                                                                          │
│                              ✛  reticle                                  │
│                          (hitmarker, damage arc)                         │
│                                                                          │
│                                                                          │
│ [streaks]                                        [killfeed              ]│
│ [equipment]                                      [                      ]│
│ [health]                                              [weapon][30/240]   │
└──────────────────────────────────────────────────────────────────────────┘
```

## 3. Elements

| Element | Position | Behaviour |
|---|---|---|
| **Reticle** | centre | Per-weapon shape; **expands with live spread** (doc 12 §5) so it tells the truth; hidden in ADS (replaced by the sight) |
| **Hitmarker** | centre | Distinct shape + sound for normal / armour / headshot / kill. 120 ms. |
| **Damage direction** | around centre | Arc at the incoming bearing, intensity by damage, 1.2 s fade |
| **Health** | bottom-left | Hidden at full; appears on damage; regen shimmer; low-health vignette + heartbeat |
| **Ammo** | bottom-right | `mag / reserve`, tabular numerals; mag turns `state/warning` under 25%, `state/danger` under 10% |
| **Weapon** | bottom-right | Silhouette of the current weapon with equipped optic; fire-mode indicator |
| **Equipment** | bottom-left | Lethal / tactical / field-upgrade icons with counts and charge |
| **Streaks** | left | Earned streaks stacked, next-streak progress |
| **Minimap** | top-left | Rotating or fixed (setting); teammates, objectives, recon contacts, gunfire pings; heights indicated by icon style |
| **Compass** | top-centre | Bearing strip with objective and teammate markers |
| **Match bar** | top-centre | Team scores + timer; mode-specific (Dom flags, Hardpoint rotation timer, S&D round pips) |
| **Killfeed** | top-right | `killer [weapon] victim`, team-coloured, callout context, 5 s, max 5 rows |
| **Objective** | contextual | Capture progress rings, plant/defuse bars, zone-rotation warnings |
| **Nameplates** | world | Friendly only (enemy names never shown); distance-faded; occlusion-aware |
| **Prompts** | lower-centre | Interact, mantle, pick-up — `KeyHint` component, device-aware |
| **Scope overlay** | fullscreen | On ADS with a scoped optic; masks the rest of the HUD |
| **Status effects** | fullscreen | Flash whiteout, concussion blur, suppression vignette |
| **Spectator bar** | bottom | When dead: who you're watching, respawn countdown, cycle hints |

## 4. Reticle-tells-the-truth rule

The reticle's gap equals the actual current spread cone projected at a reference distance. It grows
when you move, jump, or fire, and shrinks as bloom recovers. This is a **fairness feature**: the
player can always see their real accuracy instead of learning it by frustration.

## 5. Minimap

- Generated from the level asset (doc 17 §7); never hand-drawn.
- Modes: rotating (default) / fixed north (setting).
- Shows: teammates (arrows with facing), objectives, own equipment, recon contacts, unsuppressed
  gunfire pings, last-known-enemy markers from the `Tracker` perk.
- Height disambiguation: icons above/below your level are drawn smaller with an up/down chevron.
- Enemy positions appear **only** from a legitimate source (recon streak, gunfire, perks) — never
  a permanent enemy display.

## 6. Feedback timing (must be exact)

| Event | Latency budget | Source |
|---|---|---|
| Muzzle flash, fire sound, recoil | 0 ms (same frame as input) | local |
| Reticle bloom | 0 ms | local |
| Hitmarker | on server confirm | server |
| Kill confirmation | on server confirm, ≤60 ms after hitmarker | server |
| Damage direction | on damage event | server |
| Killfeed entry | on kill event | server |

Local feel is never gated on the network; outcomes always are. This is the rule that makes a
networked shooter feel crisp without lying to the player.

## 7. Death & spectate

1. Death: brief desaturation + slump camera, killer's name, their remaining health, and their
   weapon.
2. Killcam (P10) if enabled by the mode.
3. Respawn countdown or, in no-respawn modes, spectate: free-cycle teammates, free camera when
   allowed.

## 8. Scalability & settings

- HUD scale 75%–125%; opacity 50%–100%.
- Per-element toggles: minimap, compass, killfeed, nameplates, damage direction, hitmarkers,
  reticle, health.
- Safe-area inset respected (doc 20 §4).
- **Competitive preset** that strips to reticle + ammo + health + match bar.

## 9. Implementation notes

- HUD is built from the same component library as the menus (doc 20 §5).
- Every element subscribes to events; none polls gameplay state in `Update` except the reticle
  (which needs per-frame spread).
- Elements are individually disableable with zero cost when off.
- All world-anchored UI (nameplates, prompts, objective markers) goes through one
  `WorldAnchorLayer` with shared occlusion and distance culling — not per-element canvases.

## 10. Acceptance (P3)

- [ ] Every element present, themed, and individually toggleable.
- [ ] Reticle spread matches the real spread value (verified numerically).
- [ ] Feedback latency budget met (measured in the harness by frame counting).
- [ ] Minimap generated from the level asset for all maps.
- [ ] Competitive preset produces a clean screen with no orphaned elements.
- [ ] Full HUD screenshot set at 75%/100%/125% scale in `Artifacts/p3/hud/`.
