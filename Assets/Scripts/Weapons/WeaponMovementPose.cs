using UnityEngine;

/// <summary>
/// COD-style procedural weapon handling for movement states — no authored
/// animations needed, everything blends smoothly (SmoothDamp, never snaps):
///
/// - SPRINT: the gun is kept UP and readable on screen (slight raise + tilt
///   toward center) instead of dropping out of frame.
/// - TAC SPRINT (double-tap Sprint): one-handed pose — muzzle up, gun pulled
///   in, off-hand released from the handguard — plus a speed boost for a
///   limited burst. Pistols and other one-handed guns use a lighter raise
///   (they're already one-handed, like in COD).
/// - Subtle weapon wobble/vibration while moving, scaled by state.
/// - Drives the sustained camera movement rumble (walk/sprint/tac).
///
/// Purely local + cosmetic except the tac-sprint speed multiplier, which the
/// server sees through the normal movement sync.
/// </summary>
public class WeaponMovementPose : MonoBehaviour
{
    public WeaponController weaponController;
    public CharacterMove characterMove;

    [Header("Sprint pose (two-handed jog, gun stays in frame)")]
    public Vector3 sprintPositionOffset = new Vector3(-0.02f, 0.09f, -0.05f);
    public Vector3 sprintEulerOffset = new Vector3(-18f, 8f, 12f);

    [Header("Rules")]
    [Tooltip("Allow firing while sprinting (COD default: no — firing breaks the sprint).")]
    public bool canShootWhileSprinting = false;

    [Header("Tac-sprint pose (one-handed, muzzle up)")]
    public Vector3 tacPositionOffset = new Vector3(-0.035f, 0.11f, -0.06f);
    public Vector3 tacEulerOffset = new Vector3(-35f, 8f, 20f);
    [Tooltip("Lighter raise for guns that are already one-handed (pistols).")]
    public Vector3 tacOneHandedEulerOffset = new Vector3(-20f, 5f, 8f);

    [Header("Tac-sprint rules")]
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
    float preTacLeftHandWeight = 1f;
    bool leftHandOverridden;
    FishNet.Object.NetworkObject netObject;
    WeaponSlotRig lastPosedSlot;

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

    void Update()
    {
        if (!IsLocal || weaponController == null || characterMove == null) return;

        // ---------------- state detection ----------------
        bool movingForward = Input.GetAxisRaw("Vertical") > 0.1f;
        bool sprintHeld = InputBindings.Held("sprint");
        bool sprinting = sprintHeld && movingForward && characterMove.isGrounded && !weaponController.MeleeMode;

        if (InputBindings.Down("sprint"))
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

        // tac sprint breaks on: stopping, timer, firing, aiming, melee
        if (tacActive && (!sprinting || Time.time > tacEndTime || InputBindings.Held("fire") || InputBindings.Held("aim")))
            tacActive = false;

        // ---------------- blending (never snaps) ----------------
        float sprintTarget = sprinting && !tacActive ? 1f : 0f;
        float tacTarget = sprinting && tacActive ? 1f : 0f;
        sprintBlend = Mathf.SmoothDamp(sprintBlend, sprintTarget, ref sprintVel, 1f / blendSpeed);
        tacBlend = Mathf.SmoothDamp(tacBlend, tacTarget, ref tacVel, 1f / blendSpeed);

        // ---------------- speed ----------------
        characterMove.sprintSpeedMultiplier = 1f + (tacSprintSpeedMultiplier - 1f) * tacBlend;

        // ---------------- weapon pose ----------------
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

        if (slot != null)
        {
            Weapon weapon = weaponController.GETCurrentWeapon;
            bool oneHanded = weapon != null && weapon.slotType == Weapon.SlotType.pistol;
            Vector3 tacEuler = oneHanded ? tacOneHandedEulerOffset : tacEulerOffset;

            // hand-shake wobble: tiny perlin vibration that scales with effort
            float effort = sprintBlend * 0.5f + tacBlend;
            float t = Time.time * wobbleFrequency;
            Vector3 wobble = new Vector3(
                (Mathf.PerlinNoise(t, 0.3f) - 0.5f),
                (Mathf.PerlinNoise(t, 7.7f) - 0.5f),
                (Mathf.PerlinNoise(t, 13.1f) - 0.5f)) * (wobbleAmount * effort);

            slot.poseLocalPosition = sprintPositionOffset * sprintBlend + tacPositionOffset * tacBlend;
            slot.poseLocalEuler = sprintEulerOffset * sprintBlend + tacEuler * tacBlend + wobble;
        }

        // ---------------- off-hand release in tac sprint ----------------
        if (weaponController.leftHandIK != null)
        {
            if (tacBlend > 0.02f)
            {
                if (!leftHandOverridden)
                {
                    preTacLeftHandWeight = weaponController.leftHandIK.weight;
                    leftHandOverridden = true;
                }
                weaponController.leftHandIK.weight = Mathf.Min(
                    weaponController.leftHandIK.weight, preTacLeftHandWeight * (1f - tacBlend));
            }
            else if (leftHandOverridden)
            {
                weaponController.leftHandIK.weight = preTacLeftHandWeight;
                leftHandOverridden = false;
            }
        }

        // ---------------- camera movement rumble ----------------
        Vector3 v = characterMove.characterController != null
            ? characterMove.characterController.velocity : Vector3.zero;
        float groundSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        float rumble = characterMove.isGrounded
            ? Mathf.InverseLerp(1.3f, 7.2f, groundSpeed) * (0.35f + 0.2f * tacBlend)
            : 0f;
        CameraShake.SetMovementRumble(rumble);
    }
}
