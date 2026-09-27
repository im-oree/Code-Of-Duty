# 11 — Animation System

## 1. The problem this solves

Today the project animates in three unrelated ways: a legacy Animator on the body, a procedural
`Rig`/`LocalRigs` system, and ad-hoc transform writes from `WeaponMovementPose`. They fight each
other (doc 10 F1/F2/F8). We replace this with **one compositor** that every animated thing goes
through.

The reference implementation's structure is the right one (doc 02 §5) and we adopt its shape:
**layers → composer → final pose**, with an explicit **perspective sync** stage.

## 2. Architecture

```
        ┌───────────────── inputs ──────────────────┐
        │ CharacterState (5 channels) · velocity ·  │
        │ stride phase · weapon profile · IK targets│
        └───────────────────┬───────────────────────┘
                            ▼
   ┌─────────────────── PoseComposer ───────────────────┐
   │ L0  BaseLocomotion   blend tree, speed-synced      │
   │ L1  AdditivePose     lean/pitch/tac-sprint torso   │
   │ L2  OneShotAction    reload, switch, melee, mantle │
   │ L3  ProceduralIK     hands, feet, look-at, elbows  │
   │ L4  ProceduralSpring weapon inertia, sway, recoil  │
   └───────────────────┬────────────────────────────────┘
                       ▼
              ┌── PerspectiveSync ──┐      ONE pose, TWO consumers
              ▼                     ▼
      ViewmodelPoseDriver     BodyPoseDriver
      (first person)          (third person)
```

**Priority table** resolves conflicts: a one-shot action (L2) suppresses conflicting additive (L1);
IK (L3) always runs last on the skeleton; springs (L4) are applied to the weapon transform only.

## 3. Layer definitions

| Layer | Source | Mask | Blend | Examples |
|---|---|---|---|---|
| **L0 Base locomotion** | Animator blend tree | full body | replace | idle, walk8, run8, sprint, crouch, prone, slide |
| **L1 Additive pose** | authored additive clips + procedural | spine/arms/head | additive | aim pitch, lean, tac-sprint torso lean, injured |
| **L2 One-shot action** | clips with events | upper body (usually) | override w/ in-out blend | reload (tac/empty), weapon switch, melee combo, throw, mantle, inspect |
| **L3 Procedural IK** | code | hands/feet/head | post-process | hand→grip, hand→ledge, foot planting, look-at |
| **L4 Procedural spring** | code | weapon transform | additive | sway, bob, inertia, recoil kick, landing dip |

### Speed sync (no foot sliding)
`L0` playback speed = `horizontalSpeed / clipReferenceSpeed`, clamped 0.7–1.4, with the blend tree
choosing the clip pair. Each locomotion clip asset records its authored reference speed. This is
the only correct way to avoid sliding and it's cheap.

### Stride phase
One authoritative `float stridePhase01` advanced by **distance travelled**, not time. Everything
that must sync to footfalls reads it: footstep audio, head bob, the tac-sprint off-hand pump,
camera roll. Because it's distance-based, it stays locked at any speed.

## 4. First-person / third-person parity (`PerspectiveSync`)

This is the module the project is missing and the reason tac sprint looks wrong.

**Rule:** the viewmodel and the body are two *views of the same pose*, never two independent
animations.

```csharp
public struct CompositePose {
    public float   StridePhase;
    public float   TorsoPitch, HipYaw, NeckPitch;
    public Vector3 WeaponLocalPos;  public Quaternion WeaponLocalRot;
    public Vector3 DominantHandPos, OffHandPos;  // rig-normalised
    public Quaternion DominantHandRot, OffHandRot;
    public float   AimBlend, SprintBlend, TacBlend;
}
```

`PerspectiveSync.Current` is computed once in `LateUpdate` (after the Animator, before rendering)
and consumed by both drivers. Switching perspective mid-action is therefore seamless — the pose is
already the same.

**Scale bridging:** the viewmodel rig is typically authored larger/closer than the world rig. All
shared quantities are **rig-normalised** (fractions of arm/spine length), and each driver
multiplies by its own rig's measurements. This is also what lets a user-supplied model drop in
(doc 23) without re-tuning every pose.

## 5. Hands: the standing requirement

From the user: hands must be visible and correct, first person and third, in every state and every
transition. Rules that enforce it:

1. **One skeleton owns the hands.** The FP viewmodel arms and the TP arms are driven from the same
   `CompositePose` hand targets. No separate "FP hands rig" with its own animation set.
2. **IK is always on.** Hand IK weight never drops to 0 during a transition; targets *move*, weights
   don't blend out. Blending out weight is what produces the limp/detached look (doc 10 F6).
3. **Arced interpolation.** Hand targets travel along a quadratic Bézier whose control point is
   pushed away from the body, so a hand never passes through the weapon or the chest.
4. **Wrist from arm, not from grip.** Free-hand wrist rotation is derived from the forearm
   direction plus a pose-specified twist — never inherited from a weapon socket.
5. **Elbow hints** on both arms at all times; elbows may not invert or clip the torso.
6. **Automated check.** The harness samples FP captures for hand pixels in the expected screen
   regions and fails the shot if a frame has none (doc 10 §6).

## 6. Weapon springs (L4)

Everything that makes a gun feel alive, as critically-damped springs (not lerps):

| Effect | Driver | Notes |
|---|---|---|
| Sway | look delta | positional + rotational, reduced 80% in ADS |
| Inertia / lag | camera angular velocity | weapon trails the camera then catches up |
| Bob | stride phase | figure-eight, killed in ADS |
| Recoil kick | fire event | per-weapon pattern, visual kick separate from aim-punch |
| Landing dip | impact velocity | shared with camera |
| Step-up / vault | traversal | weapon lowers and returns |

One `SpringDamper` utility (stiffness, damping, mass) used for all of them so the feel is
coherent. Presets per weapon class in `WeaponProfile`.

## 7. Animation authoring plan (we have almost no clips)

The project has procedural rigs and a small kit clip set. We need a real library. Three sources,
in order of preference:

1. **Procedural where procedural is genuinely better** — sway, bob, inertia, recoil, lean, aim
   pitch, look-at, foot planting, ledge hands. These *should* be code; authored clips would be
   worse. This covers L1/L3/L4 almost entirely.
2. **Generated clips from our own pipeline** (doc 23) — the mesh/rig tool also emits simple,
   correct, original locomotion cycles (idle, walk, run, sprint, crouch) as GLB animation tracks,
   authored as keyframe curves by code. Not beautiful, but correct, in-scale, and ours.
3. **Hand-refined clips** — the user replaces (2) with better animations later; because everything
   is rig-normalised and clip-referenced by name, this is a drop-in.

**Clip naming contract** (so replacement is trivial):
`Loco_Idle`, `Loco_Walk_F/B/L/R`, `Loco_Run_F/B/L/R`, `Loco_Sprint`, `Loco_TacSprint`,
`Loco_CrouchIdle`, `Loco_CrouchWalk_F/B/L/R`, `Loco_Prone*`, `Loco_Slide`,
`Act_Reload_Tac_<class>`, `Act_Reload_Empty_<class>`, `Act_Switch_In/Out`, `Act_Melee_1/2/3`,
`Act_Throw`, `Act_Mantle_Low/High`, `Act_Vault`, `Add_AimPitch`, `Add_Lean_L/R`,
`Add_TacSprint_Upper`.

An editor verifier lists missing clips per class so gaps are visible, and substitutes a safe
fallback rather than T-posing.

## 8. Retargeting & model swap

- Characters use Unity's **Humanoid** rig so clips retarget across models.
- `OperatorDefinition` references a model + an optional bone-name remap table.
- At spawn, if `modelOverride` is set, its skinned meshes are rebound to the live skeleton by bone
  name (this mechanism already exists in the project and works).
- Rig measurements (spine length, arm length, hand size, eye height) are sampled from the Avatar at
  spawn into a `RigMetrics` struct that every pose consumer multiplies by.
- **Known blocker in the repo:** `Male_Body_BaseMesh.fbx` is an unrigged static mesh. Doc 23
  covers rigging it (or generating a rigged replacement).

## 9. Events

Animation events drive gameplay-adjacent moments: `MagOut`, `MagIn`, `BoltRelease`, `WeaponHidden`,
`WeaponShown`, `MeleeContact`, `ThrowRelease`, `FootL`, `FootR`, `MantleHandPlant`.

They are declared in a per-clip event asset (not embedded in the FBX) so replacing a clip doesn't
lose them, and an editor tool re-times them against a new clip's length.

## 10. Debug tooling

- **Pose inspector overlay** (in-game, dev builds): current state per channel, active layers and
  weights, IK weights, stride phase, blend values.
- **Harness contact sheets** (doc 07 §4): the primary way animation is judged.
- **Skeleton draw mode** in the Unity Web Clone viewer: bone axes + IK targets + elbow hints, so a
  bad wrist rotation is immediately visible.

## 11. Acceptance (P2)

- [ ] One compositor; no script writes a bone or weapon transform outside a layer.
- [ ] `PerspectiveSync` exists and both drivers consume it; switching perspective mid-sprint,
      mid-reload and mid-mantle shows no pop.
- [ ] Zero foot sliding across the speed range (measured: foot-plant world position drift < 2 cm).
- [ ] Hands-visible test passes for sprint, tac sprint, reload, switch, mantle, throw — FP and TP.
- [ ] Every missing clip has a logged fallback; no T-poses in any state.
- [ ] Contact sheets for each locomotion state approved and stored as baselines.
