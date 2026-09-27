using UnityEngine;

/// <summary>
/// COD-style procedural weapon handling for movement states — no authored clips needed,
/// everything blends smoothly (SmoothDamp, never snaps):
///
/// - SPRINT: gun kept UP and readable on screen (slight raise + tilt toward center).
/// - TAC SPRINT (double-tap Sprint): one-handed pose — muzzle up, gun pulled in, off-hand
///   moved to a TUCK point rather than simply released (releasing it leaves a limp,
///   detached-looking arm, which is the bug this replaces). Plus a timed speed burst.
///
/// ## Both perspectives
/// This component poses the FIRST-PERSON viewmodel (through the active <see cref="WeaponSlotRig"/>).
/// In THIRD person the body is driven by the rig system, so the blends are exposed as
/// <see cref="SprintBlend"/> / <see cref="TacBlend"/> for a body-side rig to consume, and an
/// optional <see cref="thirdPersonWeapon"/> transform receives the same pose at
/// <see cref="thirdPersonPoseScale"/> strength so both perspectives agree.
///
/// Purely local + cosmetic except the tac-sprint speed multiplier, which the server sees
/// through the normal movement sync.
/// </summary>
/// <remarks>
/// EXECUTION ORDER MATTERS HERE, and getting it wrong is what broke the third-person pose.
/// The frame goes: Update -> animator writes bones -> LateUpdate. <see cref="RigExecutor"/>
/// runs at the default order (0) in LateUpdate and is what actually places the weapon.
///
/// So the work is split. <see cref="Update"/> only produces DATA (the blends, and the pose
/// handed to <see cref="WeaponSlotRig"/>), which is safe because the rig reads it later the
/// same frame. Anything that writes a TRANSFORM waits for <see cref="LateUpdate"/>, which at
/// order 100 runs after the animator and after the rig, so it poses a body that has already
/// been placed for this frame instead of one still holding last frame's values.
/// </remarks>
[DefaultExecutionOrder(100)]
public class WeaponMovementPose : MonoBehaviour
{
    public WeaponController weaponController;
    public CharacterMove characterMove;

    [Header("Sprint pose (two-handed jog, gun stays in frame)")]
    public Vector3 sprintPositionOffset = new Vector3(0f, 0.05f, 0.03f); // char space: x right, y UP, z fwd
    public Vector3 sprintEulerOffset = new Vector3(12f, 0f, 6f); // +x = muzzle up

    [Header("Rules")]
    [Tooltip("Allow firing while sprinting (COD default: no — firing breaks the sprint).")]
    public bool canShootWhileSprinting = false;

    [Header("Tac-sprint pose (one-handed, muzzle up)")]
    public Vector3 tacPositionOffset = new Vector3(0.03f, 0.10f, 0.05f);
    public Vector3 tacEulerOffset = new Vector3(38f, 8f, 7f); // muzzle-up, yaw inward, slight roll
    [Tooltip("Lighter raise for guns that are already one-handed (pistols).")]
    public Vector3 tacOneHandedEulerOffset = new Vector3(24f, 4f, 4f);
    [Tooltip("Hard ceiling on the muzzle-up rotation so the weapon can never swing out of frame.")]
    public float maxMuzzleUpDegrees = 46f;

    [Header("Tac-sprint off-hand (keeps the hand readable, not limp)")]
    [Tooltip("Character space: x = right, y = up, z = forward. Where the off-hand tucks to.")]
    public Vector3 tacOffHandTuckLocal = new Vector3(0.19f, 1.12f, 0.20f);
    [Tooltip("Optional authored tuck target. If empty one is created under the character at runtime.")]
    public Transform tacOffHandTuckTarget;
    [Tooltip("Blend the off-hand off the handguard toward the tuck point across the tac-sprint ramp.")]
    public bool retargetOffHand = true;

    [Header("Third person")]
    [Tooltip("Optional TP weapon transform to pose so both perspectives read the same.")]
    public Transform thirdPersonWeapon;
    [Range(0f, 1f)]
    [Tooltip("How much of the FP pose the TP weapon receives.")]
    public float thirdPersonPoseScale = 0.65f;

    // Tac-sprint rules (enable, double-tap window, duration, speed) now live on MovementTuning.

    [Header("Feel")]
    public float blendSpeed = 7f;
    public float wobbleAmount = 0.35f;
    public float wobbleFrequency = 7.5f;

    float sprintBlend, tacBlend;
    float sprintVel, tacVel;
    FishNet.Object.NetworkObject netObject;
    WeaponSlotRig lastPosedSlot;

    HandIK offHand;
    Transform offHandOriginalTarget;
    Transform tuckTarget;

    // Rest pose of the third-person weapon, so the pose above can be absolute.
    Transform tpBaseOwner;
    Vector3 tpBaseLocalPosition;
    Quaternion tpBaseLocalRotation;

    /// <summary>
    /// The pose for this frame, in character space, computed once in <see cref="Update"/> and
    /// read by every consumer. First person, third person and the off-hand all take THIS, so
    /// the perspectives cannot drift apart: there is one answer per frame, not one per reader.
    /// </summary>
    public Vector3 PosePosition { get; private set; }

    /// <inheritdoc cref="PosePosition"/>
    public Vector3 PoseEuler { get; private set; }

    /// <summary>0..1 sprint (non-tac) pose weight — exposed for the third-person body rig.</summary>
    public float SprintBlend => sprintBlend;

    /// <summary>0..1 tactical-sprint pose weight — exposed for the third-person body rig.</summary>
    public float TacBlend => tacBlend;

    /// <summary>
    /// True while the player is sprinting. Forwarded from <see cref="CharacterMove"/> so that
    /// callers blocking fire get the same answer the movement system acted on -- this used to
    /// threshold the cosmetic blend at 0.35 while the state report thresholded it at 0.5, so
    /// there was a window where the player counted as sprinting for one and not the other.
    /// </summary>
    public bool IsSprinting => characterMove != null && characterMove.IsSprinting;

    void Awake()
    {
        if (weaponController == null) weaponController = GetComponent<WeaponController>();
        if (characterMove == null) characterMove = GetComponentInParent<CharacterMove>();
        netObject = GetComponentInParent<FishNet.Object.NetworkObject>();
    }

    // NOTE: ownership must be checked LIVE — FishNet assigns it after Start(),
    // so caching a bool there would leave this system dead forever.
    bool IsLocal => netObject == null || netObject.IsOwner;

    Transform CharacterRoot =>
        characterMove != null ? characterMove.transform : transform.root;

    void Update()
    {
        if (!IsLocal || weaponController == null || characterMove == null) return;

        // ---------------- read the movement decision ----------------
        // This component used to make this decision itself, from its own copy of the input and
        // its own timers. It is a cosmetic component, so that put the rules that decide how fast
        // the character runs inside the thing that tilts the gun -- and the two copies of the
        // rules disagreed about how far forward the stick had to be. CharacterMove owns it now;
        // this just draws the result.
        bool sprinting = characterMove.IsSprinting;
        bool tacActive = characterMove.IsTacSprinting;

        // ---------------- blending (never snaps) ----------------
        float sprintTarget = sprinting && !tacActive ? 1f : 0f;
        float tacTarget = sprinting && tacActive ? 1f : 0f;
        sprintBlend = Mathf.SmoothDamp(sprintBlend, sprintTarget, ref sprintVel, 1f / blendSpeed);
        tacBlend = Mathf.SmoothDamp(tacBlend, tacTarget, ref tacVel, 1f / blendSpeed);

        // ---------------- weapon pose (first person) ----------------
        // Data only: WeaponSlotRig consumes this from RigExecutor.LateUpdate, later this frame.
        Vector3 posePos;
        Vector3 poseEuler = ComputePoseEuler(out posePos);
        PosePosition = posePos;
        PoseEuler = poseEuler;
        ApplyToSlot(posePos, poseEuler);

        // ---------------- camera movement rumble ----------------
        Vector3 v = characterMove.characterController != null
            ? characterMove.characterController.velocity : Vector3.zero;
        float groundSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float rumble = characterMove.isGrounded
            ? Mathf.InverseLerp(1.0f, 6.2f, groundSpeed) * (0.85f + 0.35f * tacBlend)
            : 0f;
        CameraShake.SetMovementRumble(rumble);
    }

    /// <summary>
    /// Everything that writes a transform. Runs after the animator and after the rig (see the
    /// execution-order note on the class), so it poses a skeleton that is already in this
    /// frame's position rather than last frame's.
    /// </summary>
    void LateUpdate()
    {
        if (!IsLocal || weaponController == null || characterMove == null) return;

        // Reuse this frame's pose rather than recomputing it. Recomputing would re-sample the
        // wobble noise and hand the two perspectives slightly different answers.
        ApplyThirdPersonPose(PosePosition, PoseEuler);
        UpdateOffHand();
    }

    /// <summary>Blends sprint and tac-sprint offsets, clamps the muzzle-up, adds movement wobble.</summary>
    Vector3 ComputePoseEuler(out Vector3 position)
    {
        Weapon weapon = weaponController.GETCurrentWeapon;
        bool oneHanded = weapon != null && weapon.slotType == Weapon.SlotType.pistol;
        Vector3 tacEuler = oneHanded ? tacOneHandedEulerOffset : tacEulerOffset;

        float effort = sprintBlend * 0.5f + tacBlend;
        float t = Time.time * wobbleFrequency;
        Vector3 wobble = new Vector3(
            Mathf.PerlinNoise(t, 0.3f) - 0.5f,
            Mathf.PerlinNoise(t, 7.7f) - 0.5f,
            Mathf.PerlinNoise(t, 13.1f) - 0.5f) * (wobbleAmount * effort);

        position = sprintPositionOffset * sprintBlend + tacPositionOffset * tacBlend;

        Vector3 result = sprintEulerOffset * sprintBlend + tacEuler * tacBlend + wobble;

        // Clamp the ANSWER. This used to clamp `tacEuler.x` before blending and before the
        // wobble was added, which is not a ceiling at all: the sprint offset and the wobble
        // were both free to push the muzzle back past it. maxMuzzleUpDegrees exists so the
        // weapon can never swing out of frame, so it has to be the last word.
        result.x = Mathf.Min(result.x, maxMuzzleUpDegrees);
        return result;
    }

    void ApplyToSlot(Vector3 position, Vector3 euler)
    {
        WeaponSlotRig slot = null;
        if (weaponController.slots != null &&
            weaponController.activeID >= 1 && weaponController.activeID <= weaponController.slots.Length)
            slot = weaponController.slots[weaponController.activeID - 1];

        if (lastPosedSlot != null && lastPosedSlot != slot)
        {
            lastPosedSlot.poseLocalPosition = Vector3.zero;
            lastPosedSlot.poseLocalEuler = Vector3.zero;
        }
        lastPosedSlot = slot;

        if (slot == null) return;
        slot.poseLocalPosition = position;
        slot.poseLocalEuler = euler;
    }

    /// <summary>
    /// Mirrors the pose onto a third-person weapon transform (character-space rotation, scaled
    /// down) so the body tells the same story as the viewmodel.
    /// </summary>
    void ApplyThirdPersonPose(Vector3 position, Vector3 euler)
    {
        if (thirdPersonWeapon == null) return;

        // Re-establish the rest pose before posing.
        //
        // This is the bug that made the third-person weapon drift off into space. Writing
        // `.rotation` writes the LOCAL rotation, and the animator only rewrites the parent
        // BONE, never this child -- so last frame's pose was still sitting in the local
        // rotation when we multiplied this frame's on top of it, once per frame, forever.
        // Restoring the captured rest pose first makes the result depend only on the current
        // blend, which is what "pose" has to mean if it is going to blend back out again.
        if (tpBaseOwner != thirdPersonWeapon)
        {
            tpBaseOwner = thirdPersonWeapon;
            tpBaseLocalPosition = thirdPersonWeapon.localPosition;
            tpBaseLocalRotation = thirdPersonWeapon.localRotation;
        }
        thirdPersonWeapon.localPosition = tpBaseLocalPosition;
        thirdPersonWeapon.localRotation = tpBaseLocalRotation;

        Vector3 up = CharacterRoot.up;
        Vector3 right = CharacterRoot.right;
        Vector3 forward = CharacterRoot.forward;

        Quaternion poseRotation =
            Quaternion.AngleAxis(-euler.x * thirdPersonPoseScale, right) *
            Quaternion.AngleAxis(euler.y * thirdPersonPoseScale, up) *
            Quaternion.AngleAxis(euler.z * thirdPersonPoseScale, forward);

        thirdPersonWeapon.rotation = poseRotation * thirdPersonWeapon.rotation;
        thirdPersonWeapon.position += (right * position.x + up * position.y + forward * position.z)
                                      * thirdPersonPoseScale;
    }

    /// <summary>
    /// Moves the off-hand IK TARGET toward the tuck point instead of dropping its weight.
    /// Keeping the weight means the arm still solves, so the hand reads as deliberately
    /// tucked rather than hanging limp. Restores the original target when the blend ends.
    /// </summary>
    void UpdateOffHand()
    {
        if (!retargetOffHand) return;

        if (offHand == null)
        {
            offHand = weaponController.leftHandIK;
            if (offHand == null) return;
        }

        // Track whatever the rest of the game points the off-hand at, but never adopt our own
        // tuck transform as "the original".
        //
        // The old code captured the original once and only refreshed it when it was null. Swap
        // weapons mid tac-sprint and two things went wrong: the captured target still belonged
        // to the previous gun, and if the capture happened while the hand was tucked it
        // captured the tuck transform itself -- so "restore the original" restored the tuck and
        // the left hand never went back to the handguard for the rest of the life.
        if (offHand.target != null && offHand.target != tuckTarget)
            offHandOriginalTarget = offHand.target;
        if (offHandOriginalTarget == null) return;

        if (tuckTarget == null)
        {
            if (tacOffHandTuckTarget != null)
            {
                tuckTarget = tacOffHandTuckTarget;
            }
            else
            {
                var go = new GameObject("TacSprintOffHandTuck");
                go.transform.SetParent(CharacterRoot, false);
                tuckTarget = go.transform;
            }
        }

        if (tacBlend > 0.01f)
        {
            Vector3 tuckWorld = CharacterRoot.TransformPoint(tacOffHandTuckLocal);
            tuckTarget.position = Vector3.Lerp(offHandOriginalTarget.position, tuckWorld, tacBlend);
            tuckTarget.rotation = offHandOriginalTarget.rotation;
            offHand.target = tuckTarget;
        }
        else if (offHand.target == tuckTarget)
        {
            offHand.target = offHandOriginalTarget;
        }
    }
}
