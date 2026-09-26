# 09 — Movement Specification

All numbers are **starting values** to be tuned in P11. They are written down so tuning is a diff,
not an archaeology exercise. Units: metres, seconds, degrees.

## 1. Speed table

| State | Speed (m/s) | Notes |
|---|---|---|
| Walk (analogue partial) | 0 → 3.2 | stick magnitude scales continuously |
| Run (default forward) | 4.6 | the baseline |
| Strafe | 4.1 | 0.89× forward |
| Backpedal | 3.4 | 0.74× forward |
| Sprint | 6.2 | forward-arc only (±55°) |
| **Tactical sprint** | 7.8 | 1.26× sprint, forward-arc only (±35°) |
| Crouch walk | 2.3 | |
| Prone crawl | 1.0 | |
| ADS walk | 2.6 | scaled per weapon `adsMoveSpeed` |
| Slide entry | inherits ≥5.6 | requires sprint/tac-sprint |
| Air | horizontal control 0.35× | limited steering |

**Weapon movement multiplier:** each `WeaponProfile` has `moveSpeedScale` (0.88–1.02). Applied to
every ground state. Heavy weapons feel heavy.

## 2. Acceleration model

Ground movement is **acceleration-based with high accel**, not instant velocity — instant feels
robotic, physics-y feels sluggish. Target: reach 90% of top speed in ~110 ms.

```
accel        = 55 m/s²       (ground)
decel        = 70 m/s²       (ground, no input)
airAccel     = 14 m/s²
counterAccel = 90 m/s²       (input opposes velocity — snappy direction changes)
```

Direction changes use `counterAccel`, which is what makes strafing feel crisp.

## 3. Sprint

| Parameter | Value |
|---|---|
| Sprint-in time (ramp to sprint speed) | 0.18 s |
| **Sprint-out time** (sprint → able to fire) | **0.20 s** base, modified per weapon |
| Forward arc | ±55° from facing; outside the arc sprint drops to run |
| Breaks on | fire, ADS, reload, melee, slide end, no forward input, ground loss >0.4 s |
| Crouch while sprinting | → slide if speed ≥5.6, else crouch |

Sprint-out time is the single most important gunfight-balance number in the game. It is per-weapon
(`WeaponProfile.sprintOutTime`, 0.15–0.42 s) and it is what makes SMGs aggressive and LMGs
committal.

## 4. Tactical sprint

Full treatment in `10-TACTICAL-SPRINT-REWORK.md` (pose/animation). Mechanics here:

| Parameter | Value |
|---|---|
| Trigger | double-tap Sprint within 0.30 s, **or** hold Sprint 1.1 s (setting), **or** off |
| Duration | 3.6 s at full stamina |
| Speed | 7.8 m/s (1.26× sprint) |
| Forward arc | ±35° — tighter than sprint; you commit to a direction |
| Cooldown | none, but stamina must regenerate |
| **Sprint-out penalty** | **0.38 s** (nearly 2× normal) — the cost of the speed |
| Breaks on | fire, ADS, reload, melee, slide, stamina empty, direction outside arc, timer |
| On break | decays to Sprint (if still sprinting) or Run, never a hard stop |

**Design intent:** tac sprint is a *commitment*. You cross open ground faster but you are helpless
for nearly 400 ms if you meet someone. That trade is the entire mechanic.

## 5. Stamina

| Parameter | Value |
|---|---|
| Max | 100 |
| Tac-sprint drain | 28/s (→ 3.6 s from full) |
| Sprint drain | 0 (sprint is free; only tac sprint costs) |
| Regen delay after tac sprint | 1.2 s |
| Regen rate | 22/s |
| Minimum to engage tac sprint | 25 |

Stamina is shown on the HUD only while non-full (a thin arc under the reticle), so it never adds
permanent clutter.

## 6. Crouch & prone

- Crouch: hold or toggle (setting). Capsule height 1.80 → 1.20 m over 0.16 s, camera follows with
  a slight lag (0.20 s) so it feels weighted.
- Stand-up is blocked if the head would clip; the request is queued and fires when clear.
- Prone: 1.80 → 0.55 m over 0.45 s. Getting up takes 0.60 s. Prone is deliberately slow to
  discourage abuse.
- Crouch spread/recoil bonus: ×0.82 spread, ×0.88 recoil. Prone: ×0.65 / ×0.72.

## 7. Jump

| Parameter | Value |
|---|---|
| Apex height | 1.05 m |
| Rise time | 0.31 s |
| Gravity | −22 m/s² (rise), −28 m/s² (fall) — asymmetric, feels better |
| Coyote time | 0.10 s |
| Input buffer | 0.12 s |
| Jump fatigue | 2nd consecutive jump −25% height, 3rd −45%, resets after 1.5 s grounded |
| Landing dip | camera dip scaled by impact speed, 0.06–0.18 m, recovers over 0.24 s |
| Fall damage | none below 8 m/s; scales to lethal at 22 m/s |

## 8. Slide

| Parameter | Value |
|---|---|
| Entry requirement | ground speed ≥ 5.6 (i.e. sprinting) + Crouch |
| Entry boost | ×1.18 for the first 0.15 s |
| Duration | 0.85 s or until speed < 2.8 |
| Friction | 6.5 m/s² (higher on slopes against you, lower downhill) |
| Steering | 45°/s max — you can curve a slide but not turn around |
| Camera | 0.20 m drop + 4° roll into the slide direction |
| **Slide cancel** | Jump during a slide → immediate stand + jump, costs the jump-fatigue counter |
| Can fire | yes, with ×1.5 spread and no ADS until the slide ends |
| Exit | to crouch if Crouch held, else to run |

Slide-cancel is intentionally preserved — it is a skill expression and removing it makes movement
feel dead. It is *balanced* by jump fatigue rather than removed.

## 9. Mantle / vault

Triggered by **Jump into a ledge** (and optionally auto, a setting).

Detection (runs only when moving toward a surface at >1.5 m/s):
1. Forward capsule cast at chest height finds a blocking surface within 0.75 m.
2. Downward cast from above the hit finds a walkable top within the height band.
3. Clearance check: is there room for the player capsule on top?

| Ledge height | Result | Duration | Control |
|---|---|---|---|
| 0.30–0.75 m | **Step-up** — no animation lock, just a smooth capsule lift | 0.15 s | full |
| 0.75–1.40 m | **Vault** — hands on the edge, body swings over | 0.45 s | look only |
| 1.40–2.30 m | **Mantle** — full climb | 0.75 s | look only |
| >2.30 m | rejected | — | — |

Rules:
- Weapon lowers to a one-handed carry during vault/mantle; **both hands remain visible** (one on
  the ledge, one on the weapon). Same requirement as tac sprint — see doc 10 §6.
- Motion is a root-motion-style curve, not a teleport; it is simulated on the server and predicted
  on the client with the same curve.
- A HUD prompt appears when a mantle is available and auto-mantle is off.
- Cancellable in the first 0.12 s only.

## 10. Lean (optional, off by default)

`Q`/`E` or stick-click modifiers. 18° roll, 0.22 m lateral camera offset, 0.18 s blend. Peeking
does not change the hitbox — the *body* leans in third person so the peek is honest.

## 11. Head bob & camera motion

Bob is procedural, driven by a phase accumulator tied to distance travelled (never to time — that
is what makes bob desync from footsteps).

| State | Vertical | Lateral | Roll | Frequency |
|---|---|---|---|---|
| Walk | 0.018 m | 0.012 m | 0.25° | per-step |
| Run | 0.032 m | 0.022 m | 0.45° | per-step |
| Sprint | 0.048 m | 0.036 m | 0.90° | per-step |
| Tac sprint | 0.062 m | 0.048 m | 1.35° | per-step, +8% frequency |
| Crouch walk | 0.012 m | 0.008 m | 0.15° | per-step |

All bob amplitudes are multiplied by a player setting (0–1) and forced to 0 while ADS.

## 12. Footsteps

Surface is sampled from the physics material / terrain layer under the foot at the step phase
crossing (not on a timer). Each surface has walk/run/sprint/land/slide variants.

Audible radius by state: crouch 6 m, walk 12 m, run 22 m, sprint 28 m, tac sprint 34 m.
Tac sprint being **loud** is part of its cost. A perk reduces these (doc 14).

## 13. State machine

Movement states map onto `CharacterState.Locomotion` (doc 05 §5) and nothing else owns them.

```
        ┌──────────────────────────────── Idle ◄───────────────┐
        │                                  │                   │
        ▼                                  ▼                   │
      Walk ◄──────► Run ────► Sprint ────► TacSprint            │
        │            │          │             │                │
        │            │          └── Crouch ───┤                │
        ▼            ▼                  ▼     ▼                │
     CrouchWalk ◄─ Crouch ──────────► Slide ──┴────────────────►┘
        │                                │
        ▼                                ▼
      Prone                           (stand/crouch)
```
Traversal (jump/fall/vault/mantle/land) is a **separate orthogonal channel** — you can be
`Sprint` + `Jumping` simultaneously, which is exactly how it should be modelled and is why the
5-channel design matters.

## 14. Networking notes

- Movement is simulated on the server from `InputFrame`s (doc 18).
- Client predicts locally and reconciles; prediction error > 0.15 m triggers a smoothed correction
  over 0.20 s, never a snap.
- Tac sprint, slide and mantle are **server-validated**: the client requests, the server checks
  preconditions (stamina, speed, geometry) and authorises. A client cannot self-grant speed.
- Remote players are interpolated 100 ms in the past with extrapolation capped at 120 ms.

## 15. Acceptance (P2)

- [ ] Movement reel captured in FP and TP: idle → walk → run → sprint → tac sprint → slide →
      slide-cancel → jump → mantle → crouch → prone → stand.
- [ ] No foot sliding at any speed (locomotion animator speed is velocity-synced).
- [ ] Every state transition is legal per `CharacterState`; the rejection log is empty in a clean
      2-minute session.
- [ ] Prediction error stays under 0.15 m at 80 ms RTT in the network harness.
- [ ] All numbers above live in one `MovementTuning` ScriptableObject, not in code.
