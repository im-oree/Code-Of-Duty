# Movement — researched

**Sources:** MW2019 community mechanics documentation + the reference repo's
`PlayerMovement.ts` / `PlayerState.ts` / `VaultSystem.ts` and `CHARACTER_STATE.md` (behavioural
truth), Call of Duty Wiki (verification).

## Mechanics and intent
| Mechanic | Behaviour |
|---|---|
| Walk / run | Base movement; run is the default forward speed |
| Sprint | Hold sprint; brief delay before you can fire (COD "sprint-out") |
| Tactical sprint | Double-tap sprint within ~0.3 s; faster burst, weapon lowered/raised one-handed, timed |
| Crouch | Hold or toggle; collider + camera lower smoothly; slower move |
| Slide | Sprint + crouch while grounded and moving: burst of speed decaying to a stop, camera dip, collider shrink, cooldown before repeating |
| Jump | Short hop; air control limited; landing dip + reduced accuracy briefly |
| Mantle / vault | **Never automatic.** A prompt arms when geometry qualifies; jump while it is armed begins the climb. Mantle = high ledge (both hands, weapon stowed); vault = waist-high (over the top) |
| Stamina | Sprint drains, regenerates when not sprinting |
| Head bob | Sinusoidal camera bob, amplitude/frequency scale with state; none while ADS/airborne |
| Footsteps | Timed to cadence; surface-tagged (concrete/metal/gravel/wood/dirt) |

## Cross-channel rules (from the reference)
- Traversal (vault/mantle) vetoes all weapon actions, ADS and locomotion requests; forces
  `aim → HIP`, `weaponAction → NONE`, `carry → STOWED`.
- RELOADING / SWITCHING force `aim → HIP`.
- SPRINT forces `aim → HIP`; ADS requests vetoed while sprinting.
- TAC_SPRINT additionally forces `carry → LOWERED`.
- SLIDE vetoes traversal.
- `carry != READY` vetoes ADS.

## Our decision
- Keep the existing `CharacterController`-based kit but move all state through the new
  `CharacterState` authority; add tac-sprint, slide, stamina, surface footsteps, vault/mantle.
- Real COD timings are the baseline; we tune for "smooth, fast, fun" (slightly snappier slide,
  forgiving mantle) rather than strict 1:1.

## Channel ownership (implementation note)
`locomotion` has ONE primary owner — `CharacterMove.SetState` — which reports the physics state
(Idle / Crouch / Slide / Jump / Air). The two sprint states are refined by a **documented second
originator**, `WeaponMovementPose`, because sprint/tac-sprint are input-driven rather than
physics-driven. This mirrors the reference project exactly, where `PlayerController` owns
locomotion while `PlayerMovement.requestTacticalSprint` owns the `TAC_SPRINT` transition.

The refinement only fires while the character is in the standing state, so it can never fight
the physics states:

| Situation | Reported locomotion |
|---|---|
| Standing, not sprinting | `Idle` |
| Standing, sprint held | `Sprint` |
| Standing, tac-sprint active | `TacSprint` |

## Tac-sprint pose (what "looks right" means)
The pose is procedural (no authored clips). Verified problems and their fixes are recorded in
`Docs/research/tac-sprint.md`.

## Acceptance test
Every state change happens only through `CharacterState.request()`, the interaction rules hold
(no ADS while sprinting, weapon stowed while mantling), and rejecting a request leaves state unchanged.
