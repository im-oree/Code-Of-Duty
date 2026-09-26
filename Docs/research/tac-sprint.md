# Tactical sprint — findings, fix, and review checklist

## What it should be (COD reference)
Double-tap sprint within ~0.3 s → a short speed burst with a one-handed weapon carry:
**muzzle up**, gun pulled in toward the body, **off-hand off the handguard**, timed
(≈3.5 s), broken by stopping, firing or aiming. Sprint (single) is the two-handed jog where
the gun stays up and readable.

## What was actually wrong
Read from `Assets/Scripts/Weapons/WeaponMovementPose.cs`:

1. **The feature was switched off.** `tacSprintEnabled` shipped `false` with the tooltip
   *"disabled for now while animations are polished"*. `tacActive` was forced false every
   frame, so `tacBlend` never left 0 — tac sprint did nothing. This is the primary reason it
   "looks genuinely wrong".
2. **The off-hand was released, not moved.** The old code only reduced the left-hand IK
   `weight` toward 0. Dropping IK weight does not tuck the arm — it stops solving it, so the
   arm hangs limp / reads as detached. That is the "hand not visible / looks wrong" symptom.
3. **No ceiling on the muzzle-up rotation.** `tacEulerOffset` was a raw 55° pitch with nothing
   stopping it from swinging the weapon out of frame if tuned further.
4. **No third-person story.** The component only posed the first-person slot rig, so TP showed
   a normal jog with no weapon handling at all.

## The fix (this pass)
| Problem | Fix |
|---|---|
| Disabled | `tacSprintEnabled` defaults **true** |
| Limp off-hand | Off-hand IK **target is retargeted** to a tuck point (character space `tacOffHandTuckLocal`, default `(0.19, 1.12, 0.20)` near the chest/waist) blended by `tacBlend`, with IK weight kept. The original target is restored when the blend ends |
| Unbounded rotation | `maxMuzzleUpDegrees` (default 46°) clamps the pitch; the tac pose defaults were also pulled back from 55°/10°/8° to 38°/8°/7° |
| No TP parity | `thirdPersonWeapon` (optional) receives the same pose at `thirdPersonPoseScale` (0.65); `SprintBlend` / `TacBlend` are public so a body-side rig can consume them |
| Silent state | Sprint/tac-sprint are now reported to `CharacterState` (documented second originator, see `movement.md`) |

## Not verified — needs eyes
I have **no way to view the Unity viewport** in this session: there is no Unity MCP server
registered, the `onion` capture server is unreachable, and I cannot open image files. So the
numeric pose values above are reasoned, not observed. They must be confirmed visually.

## Review checklist (run these, then tune)
Use **`COD ▸ Debug ▸ Capture Game Screenshot`** (Cmd/Ctrl+Shift+S) in play mode; files land in
`<project>/Screenshots/`.

1. **FP, tac-sprint peak** — muzzle up but the weapon fully inside the frame; both hands
   readable; no clipping through the camera.
2. **TP, tac-sprint peak** — same story as FP: gun raised, off-hand tucked, no limp arm.
3. **Blend in/out** — entry and exit are smooth with no snap (SmoothDamp at `blendSpeed`).
4. **Sprint (single tap)** — two-handed jog, gun stays up.
5. **Break conditions** — firing and aiming both drop tac sprint immediately.
6. **Pistol** — uses the lighter one-handed raise, still fully in frame.

Tune in this order: `tacEulerOffset.x` → `tacPositionOffset` → `tacOffHandTuckLocal` →
`thirdPersonPoseScale`.

## Next session requirement
To make this genuinely "look right", this thread needs live visual feedback. Either:
- enable the Unity Editor MCP bridge (`Docs/MCP_SETUP.md`) so screenshots return to me, or
- run the capture tool and share the frames.
