using UnityEngine;
using System.Collections;
using System.Linq;

public class WeaponController : MonoBehaviour
{
    public Animator animator;
    public EventsCenter eventsCenter;
    //public SlotController[] slots;
    //public SlotController GETCurrentSlot => slots[activeID - 1];
    public WeaponSlotRig[] slots;

    /// <summary>
    /// The slot currently in hand, or null if <see cref="activeID"/> has not been set up yet.
    /// Bounds-checked: activeID is 1-based and is 0 until something assigns it, which used to
    /// index slots[-1] and throw before any of the null guards downstream got a chance to run.
    /// </summary>
    public WeaponSlotRig GETCurrentSlot =>
        slots != null && activeID >= 1 && activeID <= slots.Length ? slots[activeID - 1] : null;

    /// <summary>
    /// The gun currently in hand, or null if the slot is empty.
    /// Includes inactive objects, because holstered guns are switched off in COD-style
    /// visibility mode and a holstered gun is still the gun that lives in that slot.
    /// </summary>
    public Weapon GETCurrentWeapon
    {
        get
        {
            var slot = GETCurrentSlot;
            return slot != null ? slot.GetComponentInChildren<Weapon>(true) : null;
        }
    }


    /*  public TwoBoneIK rightHandIK;
     public TwoBoneIK leftHandIK; */
    public HandIK rightHandIK;
    public HandIK leftHandIK;


    public int activeID; // activeGun
    public int nextID;
    public Transform offsetForGun; // transform forOffset 

    [Header("COD-style handling")]
    [Tooltip("Guns not in hand are invisible (pulled 'from below' on switch) instead of showing on the body.")]
    public bool hideHolsteredGuns = false; // COD style: guns stay visible on back/hip mounts
    [Tooltip("Cross-fade time into the weapon-switch animation. Lower = snappier COD-style swaps.")]
    public float switchBlendTime = 0.09f;

    /// <summary>True while the player is unarmed (melee stance).</summary>
    public bool MeleeMode { get; private set; }

    // shoot event, called when fired
    public delegate void Shoot();
    public event Shoot OnShoot;
    public bool changed; // true if weapon changed
    public bool canShoot;

    void OnEnable()
    {
        var gunchangeSMBs = animator.GetBehaviours<GunChange_SMB>(); // get gunchange state machine behaviours from animator
        foreach (var gunchangeSMB in gunchangeSMBs)
        {
            gunchangeSMB.setWeaponController(this); // set this as gunchangers in state machine behaviours from animator
        }

        eventsCenter = animator.transform.GetComponent<EventsCenter>();

        // subscribes on events
        eventsCenter.OnRightHandIKWeightUpdate += ApplyRightHandIkWeight;
        eventsCenter.OnLeftHandIKWeightUpdate += ApplyLeftHandIkWeight;
        eventsCenter.OnGunWeightUpdate += ApplyGunActiveWeight;
        eventsCenter.OnGunOffsetRelativeToParent += ApplyGunOffsetRelativeToParent;
        eventsCenter.OnGunParentChange += ApplyGunParent;
        eventsCenter.OnHandIKTargetChange += ApplyHandsIKTarget;
        eventsCenter.OnApplyGunPositionOffset += ApplyGunPositionOffsetInHands;
        eventsCenter.OnWeaponChange += GunChangeCheck;
    }

    private void OnDisable()
    {
        eventsCenter.OnRightHandIKWeightUpdate -= ApplyRightHandIkWeight;
        eventsCenter.OnLeftHandIKWeightUpdate -= ApplyLeftHandIkWeight;
        eventsCenter.OnGunWeightUpdate -= ApplyGunActiveWeight;
        eventsCenter.OnGunOffsetRelativeToParent -= ApplyGunOffsetRelativeToParent;
        eventsCenter.OnGunParentChange -= ApplyGunParent;
        eventsCenter.OnHandIKTargetChange -= ApplyHandsIKTarget;
        eventsCenter.OnApplyGunPositionOffset -= ApplyGunPositionOffsetInHands;
        eventsCenter.OnWeaponChange -= GunChangeCheck;
    }

    public void StartShoot()
    {
        // Deliberately NOT gated on canShoot: this is also the entry point the network uses to
        // replay a remote player's shots for visuals, and a remote rig whose draw animation is
        // a few frames behind would silently swallow them. The "don't fire mid-switch" rule
        // belongs to the local input path, which is where the intent originates.
        var weapon = GETCurrentWeapon;
        if (weapon == null) return;

        if (weapon.Shoot())
            OnShoot?.Invoke();
    }

    IEnumerator setLHandIkWeight(float t, float pause)
    {
        yield return new WaitForSeconds(pause);
        float startWeight = leftHandIK.weight;

        float t00 = 0;
        while (t00 < 0.1f)
        {
            leftHandIK.weight = Mathf.Lerp(startWeight, 0, t00 / 0.1f);
            t00 += Time.deltaTime;

            yield return null;
        }

        ApplyHandsIKTarget(1, "LeftHandDefault");

        float t0 = 0;
        while (t0 < t)
        {
            leftHandIK.weight = Mathf.Lerp(0, 1, t0 / t);
            t0 += Time.deltaTime;

            yield return null;
        }
        leftHandIK.weight = 1;

    }

    void GunChangeCheck(bool changing)
    {
        canShoot = !changing;
        this.changed = changing;

        // if (!changing && GETCurrentWeapon.aimPoint != null) aimPointEffector.getFromTransform = GETCurrentWeapon.aimPoint.transform;
    }

    /* Animation-event handlers.
     *
     * These are driven by the animator, which knows nothing about whether a gun is actually in
     * the slot right now -- a loadout swap, a melee stance or a half-initialised spawn can all
     * leave it briefly empty. Each one therefore checks before it dereferences. They used to
     * assume a weapon was always present, and the resulting null reference did far more damage
     * than losing one frame of a cosmetic offset: it aborted the rest of the draw sequence and
     * left the player empty-handed. */

    void ApplyGunOffsetRelativeToParent(int handId, int applyOffset)
    {
        var slot = GETCurrentSlot;
        if (slot == null) return;
        slot.ApplyHandOffset(handId, applyOffset == 1);
    }

    void ApplyGunPositionOffsetInHands(float active)
    {
        var weapon = GETCurrentWeapon;
        if (weapon == null) { WarnMissingWeaponOnce(); return; }
        if (offsetForGun == null) return;
        offsetForGun.localPosition = weapon.inHandsPositionOffset * active;
    }

    bool warnedMissingWeapon;

    /// <summary>
    /// Say so, once, when a draw event runs against an empty slot. The guards above keep the
    /// rest of the sequence alive, but an empty slot is still a real problem worth seeing --
    /// silently coping with it would just hide the next version of this bug.
    /// </summary>
    void WarnMissingWeaponOnce()
    {
        if (warnedMissingWeapon) return;
        warnedMissingWeapon = true;

        int slotCount = slots != null ? slots.Length : 0;
        var slot = GETCurrentSlot;
        Debug.LogWarning(
            $"{name}: weapon draw ran with no Weapon in the active slot " +
            $"(activeID={activeID}, nextID={nextID}, slots={slotCount}, " +
            $"slotObject={(slot != null ? slot.name : "null")}). " +
            "The draw will continue so the gun is not left holstered, but this slot should " +
            "contain a weapon. Most likely a loadout swap destroyed one without replacing it.",
            this);
    }

    void ApplyRightHandIkWeight(float weight)
    {
        if (rightHandIK != null) rightHandIK.weight = weight;
    }

    void ApplyLeftHandIkWeight(float weight)
    {
        if (leftHandIK != null) leftHandIK.weight = weight;
    }

    void ApplyGunParent(float handActive)
    {
        var slot = GETCurrentSlot;
        if (slot != null) slot.HandActive = handActive;
    }

    void ApplyGunActiveWeight(float weight)
    {
        var slot = GETCurrentSlot;
        if (slot != null) slot.weight = weight;
    }

    void ApplyHandsIKTarget(int handId, string pointName)
    {
        var handIk = handId > 0 ? leftHandIK : rightHandIK;
        if (handIk == null) return;

        var weapon = GETCurrentWeapon;
        if (weapon == null || weapon.weaponPoints == null) { handIk.target = null; return; }

        switch (pointName)
        {
            case "RightHandDefault":
                handIk.target = (from f in weapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.RightHandDefault
                                 select f).SingleOrDefault()?.transform;
                break;
            case "LeftHandDefault":
                handIk.target = (from f in weapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.LeftHandDefault
                                 select f).SingleOrDefault()?.transform;
                break;
            case "RightHandguard":
                handIk.target = (from f in weapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.RightHandguard
                                 select f).SingleOrDefault()?.transform;
                break;
            case "LeftHandguard":
                handIk.target = (from f in weapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.LeftHandguard
                                 select f).SingleOrDefault()?.transform;
                break;
            default:
                Debug.Log("Not Correct Name: " + pointName);
                break;
        }
    }

    public void ToChange(int nextGunSlotID)
    {
        if (changed) return;
        if (nextGunSlotID < 1 || nextGunSlotID > slots.Length) return;

        if (MeleeMode) SetMelee(false); // drawing a gun leaves melee stance

        if (activeID == nextGunSlotID) return;

        string animaName = "PutSlot" + activeID;
        this.nextID = nextGunSlotID;

        animator.CrossFadeInFixedTime(animaName, switchBlendTime, 1);
    }

    /// <summary>
    /// Re-point everything that cached something from the gun that was just replaced.
    ///
    /// The loadout system destroys a slot's default weapon and instantiates the chosen one in
    /// its place. Nothing else knew that happened, so the hand IK went on aiming at
    /// <see cref="WeaponPoint"/> transforms inside a destroyed object, and the fresh gun never
    /// received the parenting, weight and offset events that the draw animation fires. The
    /// result was a player holding nothing, with hands frozen in a pose belonging to a gun that
    /// no longer existed -- and a stream of null-reference exceptions from the IK solver.
    ///
    /// Safe to call at any time, and cheap, so callers do not have to reason about whether a
    /// swap actually changed anything.
    /// </summary>
    public void RebindAfterWeaponSwap()
    {
        if (slots == null || slots.Length == 0) return;

        // A swap cancels any switch that was in flight; its target may no longer exist.
        nextID = 0;
        activeID = Mathf.Clamp(activeID <= 0 ? 1 : activeID, 1, slots.Length);
        changed = false;
        canShoot = !MeleeMode;

        if (GETCurrentWeapon == null) return;

        // Re-resolve the hand IK onto the new gun's points.
        ApplyHandsIKTarget(0, "RightHandDefault");
        ApplyHandsIKTarget(1, "LeftHandDefault");

        // Let the listeners that cache per-weapon values (recoil model, view resistance, sights)
        // read the new gun.
        if (eventsCenter != null) eventsCenter.InvokeWeaponChange(false);

        // Replay the draw so the animation events re-apply gun parenting, slot weight and the
        // in-hand position offset to the weapon that is actually there now.
        if (animator != null) animator.Play("GunPickUp", 1, 0f);
    }

    #region COD-style visibility & melee

    void LateUpdate()
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;

            Weapon weapon = slot.GetComponentInChildren<Weapon>(true);
            if (weapon == null) continue;

            int id = i + 1;
            // visible always (holstered guns rest on their inactiveSlot mounts);
            // legacy hide mode kept behind the flag
            bool visible = !hideHolsteredGuns
                || (!MeleeMode && (id == activeID || (changed && id == nextID)));
            if (weapon.gameObject.activeSelf != visible)
                weapon.gameObject.SetActive(visible);
        }
    }

    /// <summary>Unarmed stance: gun stowed (hidden), hands released from the weapon IK.</summary>
    public void SetMelee(bool active)
    {
        if (MeleeMode == active || changed) return;

        MeleeMode = active;
        canShoot = !active;
        StopCoroutine(nameof(MeleeBlend));
        StartCoroutine(nameof(MeleeBlend), active);
    }

    IEnumerator MeleeBlend(bool active)
    {
        var slot = GETCurrentSlot;
        if (slot == null || rightHandIK == null || leftHandIK == null) yield break;

        float startSlot = slot.weight;
        float startR = rightHandIK.weight;
        float startL = leftHandIK.weight;
        float end = active ? 0f : 1f;

        float t = 0f;
        const float duration = 0.16f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            slot.weight = Mathf.Lerp(startSlot, end, k);
            rightHandIK.weight = Mathf.Lerp(startR, end, k);
            leftHandIK.weight = Mathf.Lerp(startL, end, k);
            yield return null;
        }

        slot.weight = end;
        rightHandIK.weight = end;
        leftHandIK.weight = end;
    }

    #endregion
}
