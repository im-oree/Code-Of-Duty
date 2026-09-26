using UnityEngine;
using CodeOfDuty.Input;

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

    [Header("Tac-sprint rules")]
    [Tooltip("Master switch for tactical sprint.")]
    public bool tacSprintEnabled = true;
    public float doubleTapWindow = 0.35f;
    public float tacSprintDuration = 3.5f;
    public float tacSprintSpeedMultiplier = 1.22f;

    [Header("Feel")]
    public float blendSpeed = 7f;
    public float wobbleAmount = 0.35f;
    public float wobbleFrequency = 7.5f;

    float sprintBlend, tacBlend;
    float sprintVel, tacVel;
    bool tacActive;
    float lastSprintTap = -10f;
    float sprintHoldStart = -10f;
    float tacEndTime;
    FishNet.Object.NetworkObject netObject;
    WeaponSlotRig lastPosedSlot;

    HandIK offHand;
    Transform offHandOriginalTarget;
    Transform tuckTarget;

    /// <summary>0..1 sprint (non-tac) pose weight — exposed for the third-person body rig.</summary>
    public float SprintBlend => sprintBlend;

    /// <summary>0..1 tactical-sprint pose weight — exposed for the third-person body rig.</summary>
    public float TacBlend => tacBlend;

    /// <summary>True while the player is sprint/tac-sprint moving (used to block firing).</summary>
    public bool IsSprinting => sprintBlend > 0.35f || tacBlend > 0.35f;

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

    CharacterInput characterInput;

    void Update()
    {
        if (!IsLocal || weaponController == null || characterMove == null) return;

        if (characterInput == null) characterInput = CharacterInput.For(this);
        var input = characterInput.Source;

        // ---------------- state detection ----------------
        // Reads intent, not the keyboard: this used to be Input.GetAxisRaw("Vertical"), which
        // meant a stick-forward player on a gamepad could sprint but could never tac-sprint.
        bool movingForward = input.Move.y > 0.1f;
        bool sprintHeld = input.Held(InputActionId.Sprint);
        bool sprinting = sprintHeld && movingForward && characterMove.isGrounded
            && !weaponController.MeleeMode
            && characterMove.currentState == characterMove.standState; // never pose while crouched/sliding

        if (input.Pressed(InputActionId.Sprint))
        {
            if (InputBindings.TacSprintMode == 0 &&
                Time.time - lastSprintTap <= doubleTapWindow && movingForward)
            {
                tacActive = true;
                tacEndTime = Time.time + tacSprintDuration;
            }
            lastSprintTap = Time.time;
            sprintHoldStart = Time.time;
        }

        // auto mode: tac sprint engages after sprinting continuously for a moment
        if (InputBindings.TacSprintMode == 1 && sprinting && !tacActive &&
            Time.time - sprintHoldStart > 1.1f)
        {
            tacActive = true;
            tacEndTime = Time.time + tacSprintDuration;
        }

        // tac sprint breaks on: stopping, timer, firing, aiming
        if (tacActive && (!sprinting || Time.time > tacEndTime ||
            input.Held(InputActionId.Fire) || input.Held(InputActionId.Aim)))
            tacActive = false;

        if (!tacSprintEnabled) tacActive = false;

        // report to the single authority (weapon handling state)
        if (characterMove.characterState != null)
        {
            var wanted = tacBlend > 0.5f ? CodeOfDuty.Character.LocomotionState.TacSprint
                       : sprintBlend > 0.5f ? CodeOfDuty.Character.LocomotionState.Sprint
                       : CodeOfDuty.Character.LocomotionState.Idle;
            if (characterMove.currentState == characterMove.standState)
                characterMove.characterState.RequestLocomotion(wanted, "WeaponMovementPose.sprint");
        }

        // ---------------- blending (never snaps) ----------------
        float sprintTarget = sprinting && !tacActive ? 1f : 0f;
        float tacTarget = sprinting && tacActive ? 1f : 0f;
        sprintBlend = Mathf.SmoothDamp(sprintBlend, sprintTarget, ref sprintVel, 1f / blendSpeed);
        tacBlend = Mathf.SmoothDamp(tacBlend, tacTarget, ref tacVel, 1f / blendSpeed);

        // ---------------- speed ----------------
        characterMove.sprintSpeedMultiplier = 1f + (tacSprintSpeedMultiplier - 1f) * tacBlend;

        // ---------------- weapon pose (first person) ----------------
        Vector3 posePos;
        Vector3 poseEuler = ComputePoseEuler(out posePos);
        ApplyToSlot(posePos, poseEuler);

        // ---------------- weapon pose (third person) ----------------
        ApplyThirdPersonPose(posePos, poseEuler);

        // ---------------- off-hand ----------------
        UpdateOffHand();

        // ---------------- camera movement rumble ----------------
        Vector3 v = characterMove.characterController != null
            ? characterMove.characterController.velocity : Vector3.zero;
        float groundSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float rumble = characterMove.isGrounded
            ? Mathf.InverseLerp(1.0f, 6.2f, groundSpeed) * (0.85f + 0.35f * tacBlend)
            : 0f;
        CameraShake.SetMovementRumble(rumble);
    }

    /// <summary>Blends sprint and tac-sprint offsets, clamps the muzzle-up, adds movement wobble.</summary>
    Vector3 ComputePoseEuler(out Vector3 position)
    {
        Weapon weapon = weaponController.GETCurrentWeapon;
        bool oneHanded = weapon != null && weapon.slotType == Weapon.SlotType.pistol;
        Vector3 tacEuler = oneHanded ? tacOneHandedEulerOffset : tacEulerOffset;
        tacEuler.x = Mathf.Min(tacEuler.x, maxMuzzleUpDegrees);

        float effort = sprintBlend * 0.5f + tacBlend;
        float t = Time.time * wobbleFrequency;
        Vector3 wobble = new Vector3(
            Mathf.PerlinNoise(t, 0.3f) - 0.5f,
            Mathf.PerlinNoise(t, 7.7f) - 0.5f,
            Mathf.PerlinNoise(t, 13.1f) - 0.5f) * (wobbleAmount * effort);

        position = sprintPositionOffset * sprintBlend + tacPositionOffset * tacBlend;
        return sprintEulerOffset * sprintBlend + tacEuler * tacBlend + wobble;
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
            offHandOriginalTarget = offHand.target;
        }
        if (offHandOriginalTarget == null) offHandOriginalTarget = offHand.target;
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
