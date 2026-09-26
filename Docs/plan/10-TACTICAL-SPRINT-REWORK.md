# 10 — Tactical Sprint Rework

> User report: *"TAC SPRINT LOOKS GENUINELY WRONG … this is something you have to manually animate
> and look at to ensure it looks nice and animated. In transition hand is visible and everything.
> The character and first person look good in both modes."*

This document is the diagnosis, the target pose, the implementation, and the acceptance shots.

## 1. Diagnosis — why it looks wrong

Read of `Assets/Scripts/Weapons/WeaponMovementPose.cs` (288 LOC). Eight distinct faults:

### F1 — The third-person pose is applied cumulatively, with no base
```csharp
thirdPersonWeapon.rotation = poseRotation * thirdPersonWeapon.rotation;   // line 232
thirdPersonWeapon.position += (right*p.x + up*p.y + fwd*p.z) * scale;     // line 233
```
There is no cached rest pose. Each frame multiplies onto **last frame's already-posed** transform.
While the blend is changing, the error integrates; the weapon drifts and spins. This alone makes
third person look broken.

### F2 — Pose is written in `Update()`, animation is evaluated after
`Update()` runs *before* the Animator writes the skeleton. In third person the animated arms
overwrite the pose immediately. Procedural pose must be written in **`LateUpdate`** (or via
`OnAnimatorIK`), never `Update`.

### F3 — The muzzle clamp is applied before the wobble
```csharp
tacEuler.x = Mathf.Min(tacEuler.x, maxMuzzleUpDegrees);   // clamp…
…
return sprintEuler*sprintBlend + tacEuler*tacBlend + wobble;   // …then add wobble
```
`maxMuzzleUpDegrees` is not a ceiling. The muzzle can exceed it by the wobble amplitude.

### F4 — Tac sprint is unreachable on a gamepad
```csharp
bool movingForward = Input.GetAxisRaw("Vertical") > 0.1f;   // legacy input
```
Legacy axis + `InputBindings` key reads. A pad never satisfies the condition. Violates doc 08.

### F5 — Movement state lives in a cosmetic component
`WeaponMovementPose` owns `tacActive`, the timer, the speed multiplier, and *writes* to
`CharacterState`. A presentation script is the authority for a movement state. This is backwards
(doc 05 §5) and it means `CharacterMove`, the server, and the animation system cannot reason about
tac sprint.

### F6 — The off-hand tuck is a hardcoded point with no rotation
```csharp
public Vector3 tacOffHandTuckLocal = new Vector3(0.19f, 1.12f, 0.20f);
tuckTarget.rotation = offHandOriginalTarget.rotation;   // weapon-grip rotation, on a free hand
```
`y = 1.12` is an absolute height in character space — correct only for the kit's exact rig. And
the hand keeps the *handguard* orientation while floating free, so the wrist reads twisted and
broken. This is very likely the single most visible artefact.

### F7 — Threshold mismatch
`IsSprinting` uses `> 0.35`; the `CharacterState` report uses `> 0.5`. Between 0.35 and 0.5 the
player cannot fire but is not reported as sprinting — an invisible dead zone.

### F8 — There is no actual animation
There are no arm/hand poses, no clip, no additive layer. Third person gets a *weapon transform
nudge* and nothing else: the body keeps running its normal sprint cycle while the gun tilts. The
pose cannot possibly read as a tactical sprint.

## 2. Ownership split (the structural fix)

| Concern | New owner |
|---|---|
| Is tac sprint requested / active / expiring? | `CharacterMove` (sim) |
| Legal transitions | `CharacterState.Locomotion` |
| Speed, stamina, arc, sprint-out penalty | `MovementTuning` + server validation |
| First-person viewmodel pose | `ViewmodelPoseDriver` (presentation) |
| Third-person body pose | `BodyPoseDriver` + additive animation layer |
| Keeping FP and TP identical | **`PerspectiveSync`** (new — the reference has this, we don't) |

`WeaponMovementPose` is deleted and replaced by these. No presentation script ever writes state.

## 3. The target pose — what a tactical sprint actually looks like

The read we want, described so it can be judged from a screenshot:

**Body**
- Torso pitched forward **~18°**, shoulders leading, spine curved not rigid.
- Stride long and low; the head tracks a shallow figure-of-eight, not a pogo bounce.
- Hips rotate ±6° opposing the shoulders — counter-rotation is what sells running.
- Head stays level-ish (eyes on the horizon), so neck counter-pitches ~8° against the torso.

**Weapon arm (dominant hand)**
- Weapon held **one-handed at the grip**, pulled in close to the chest/shoulder.
- Muzzle **up and slightly outboard**, between 30° and 42° — never past 46°.
- Elbow tucked, upper arm close to the ribs. The weapon moves *with* the torso, not independently.

**Off hand — the critical part**
- **Pumping in a natural running arc**, not tucked against the chest and not hanging limp.
- Elbow bent ~75–95°, hand travelling from hip to sternum height, fingers relaxed and curled.
- Wrist orientation follows the arm, **not the weapon grip** (this is fault F6).
- The hand must be **inside the camera frustum for part of the cycle in first person** — the user
  explicitly requires hands visible. In FP the off-hand swings up into the lower-left of frame on
  each stride.

**First person specifically**
- Weapon occupies the lower-right, canted, muzzle rising out of the top-right of frame.
- Visible: the dominant hand on the grip, the forearm, and the off-hand arcing through the
  lower-left. At no point is the frame empty of hands.
- Camera: +8% bob frequency, 0.062 m vertical, 1.35° roll (doc 09 §11), plus a subtle 2.5° FOV
  widening that eases in over 0.25 s and out over 0.18 s.

**Transitions (where it currently breaks)**
- Sprint → tac sprint: 0.22 s ease-in. The muzzle rises and the off-hand *releases the handguard
  and swings down into the pump* — it must never teleport.
- Tac sprint → anything: 0.16 s ease-out, off-hand returns to the handguard along an arc.
- Because the off-hand is IK-driven, both transitions are **target interpolation along a curved
  path**, not a straight lerp (a straight lerp passes the hand through the weapon).

## 4. Implementation

### 4.1 State (sim)
```
CharacterMove:
  TryBeginTacSprint(InputFrame f)
    require Locomotion == Sprint
    require stamina >= 25
    require |angle(moveDir, facing)| <= 35°
    require CharacterState.Request(Locomotion, TacSprint) accepted
  UpdateTacSprint(dt)
    drain stamina; break on: fire | aim | reload | melee | slide | timer | arc | stamina
    on break: Request(Locomotion, Sprint or Run)
```
Speed and sprint-out penalty come from `MovementTuning`; the server validates and applies.

### 4.2 Pose source (one, shared)
```
TacSprintPose  (a ScriptableObject — tunable without recompiling)
  torsoPitch, hipYaw, neckPitch
  weaponLocalPos, weaponLocalEuler        (dominant-hand space)
  offHandArc: { hipPoint, sternumPoint, outSwing, bendDeg }
  cycleFrequencyScale
```
`PerspectiveSync` samples this **once per frame** and hands the identical result to both the
viewmodel driver and the body driver. One source ⇒ the two perspectives cannot disagree.

### 4.3 Application rules (fixes F1, F2)
```csharp
// Captured once, when the slot/weapon changes — never read back from the posed transform.
Vector3 _baseLocalPos; Quaternion _baseLocalRot;

void LateUpdate() {                     // AFTER the Animator (fixes F2)
    var pose = PerspectiveSync.Current;
    target.localPosition = _baseLocalPos + pose.Position;          // absolute (fixes F1)
    target.localRotation = _baseLocalRot * pose.Rotation;          // absolute
}
```

### 4.4 Third-person arms
The body needs real animation, not a transform nudge (fixes F8). Two layers on the Animator:

1. **Base layer** — locomotion blend tree, speed-synced (`locomotionSpeed` parameter already
   exists in the project).
2. **`TacSprintUpper` additive layer**, avatar-masked to spine + both arms + head, weight driven by
   `tacBlend`. Provides the torso pitch, the one-handed carry, and the off-hand pump cycle phase-
   locked to the stride.

On top of that, **IK in `OnAnimatorIK`**:
- Right hand → weapon grip point (always).
- Left hand → a point sampled along `offHandArc` at the current stride phase, with the **wrist
  rotation derived from the arm direction**, not the grip (fixes F6).
- Hint/elbow targets so elbows never invert.

### 4.5 Clamp order (fixes F3)
```csharp
euler = Clamp(baseEuler + wobble, maxMuzzleUp);   // wobble first, clamp last
```

### 4.6 Input (fixes F4)
All reads via `IInputSource`. Double-tap detection on `InputActionId.Sprint` edge-downs within
`doubleTapWindow`, plus the hold-to-engage and off modes from settings. Works on pad by
construction.

### 4.7 Thresholds (fixes F7)
One constant: `TacSprintPose.engagedThreshold = 0.5`. `IsSprinting`, the fire block, and the state
report all read it.

## 5. Tuning values (initial)

| Parameter | Value |
|---|---|
| Blend in / out | 0.22 s / 0.16 s |
| Torso pitch | 18° |
| Hip counter-yaw | ±6° |
| Neck counter-pitch | −8° |
| Muzzle up (rifles) | 36° |
| Muzzle up (pistols/one-handed) | 24° |
| Muzzle up hard ceiling | 46° |
| Weapon local offset | `(0.035, 0.085, 0.045)` m |
| Off-hand elbow bend | 82° |
| Off-hand arc height | hip → sternum (rig-relative, **normalised to spine length**, not absolute metres — fixes F6) |
| Stride frequency scale | 1.08× |
| Wobble amplitude / frequency | 0.30 / 7.5 Hz |
| FOV widen | +2.5° in 0.25 s, out 0.18 s |

**Rig-relative measurement** is important: every offset is expressed as a fraction of the
character's spine or arm length, sampled from the Avatar at runtime. The pose then survives a
model swap — which matters because the user intends to drop in their own model (doc 23).

## 6. Hands-visible requirement (explicit)

The user called this out twice. It becomes a **hard acceptance test**, not a nicety:

- [ ] FP, tac sprint steady state: dominant hand + forearm visible; off-hand enters frame on every
      stride. Assert by pixel-sampling the lower-left and lower-right regions of the FP capture for
      skin/glove material IDs.
- [ ] FP, sprint → tac sprint transition: hands visible in **every** sampled frame of the 0.22 s
      blend. No frame where the frame is empty of hands.
- [ ] FP, tac sprint → sprint: same, across 0.16 s.
- [ ] TP, all of the above: both hands visible and attached — never intersecting the torso, never
      detached from the weapon when they should be on it.

## 7. Acceptance shots (the harness sequence)

`UnityWeb/harness/tacsprint.json` (format in doc 07 §4) produces:

| Shot | Camera | What it proves |
|---|---|---|
| `fp-contact.png` | first person | The full blend range in FP; hands present in every cell |
| `tp-contact.png` | orbit yaw 35° | Body pose reads as a tactical sprint from the side |
| `tp_front-contact.png` | orbit yaw 195° | Off-hand pump and torso lean read from the front |
| `tp_top.png` | top-down | Weapon does not clip the torso; elbows not inverted |
| `blend-in.png` / `blend-out.png` | FP, 10 frames each | Transitions are smooth and hands never vanish |

**The work is not done until these images look right.** They are re-rendered after every change
and the approved set becomes the regression baseline (doc 07 §5).

## 8. Definition of done

- [ ] F1–F8 all fixed, each with a note in the commit message.
- [ ] `WeaponMovementPose.cs` deleted; replaced by `ViewmodelPoseDriver`, `BodyPoseDriver`,
      `PerspectiveSync`, and tac-sprint state in `CharacterMove`.
- [ ] Zero raw input reads (lint clean).
- [ ] All tuning in `TacSprintPose` + `MovementTuning` assets.
- [ ] The five acceptance shots rendered, read, and judged good.
- [ ] Verified in first **and** third person, moving **and** in both transitions.
- [ ] Works identically on keyboard and gamepad.
