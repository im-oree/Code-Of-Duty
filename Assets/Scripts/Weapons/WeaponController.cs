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
    public WeaponSlotRig GETCurrentSlot => slots[activeID - 1];
    public Weapon GETCurrentWeapon => slots[activeID - 1].GetComponentInChildren<Weapon>();


    /*  public TwoBoneIK rightHandIK;
     public TwoBoneIK leftHandIK; */
    public HandIK rightHandIK;
    public HandIK leftHandIK;


    public int activeID; // activeGun
    public int nextID;
    public Transform offsetForGun; // transform forOffset 

    [Header("COD-style handling")]
    [Tooltip("Guns not in hand are invisible (pulled 'from below' on switch) instead of showing on the body.")]
    public bool hideHolsteredGuns = true;
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
        if (GETCurrentWeapon.Shoot())
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

    void ApplyGunOffsetRelativeToParent(int handId, int applyOffset) => GETCurrentSlot.ApplyHandOffset(handId, applyOffset == 1);

    void ApplyGunPositionOffsetInHands(float active) => offsetForGun.localPosition = GETCurrentWeapon.inHandsPositionOffset * active;

    void ApplyRightHandIkWeight(float weight) => rightHandIK.weight = weight;

    void ApplyLeftHandIkWeight(float weight) => leftHandIK.weight = weight;

    void ApplyGunParent(float handActive) => GETCurrentSlot.HandActive = handActive;

    void ApplyGunActiveWeight(float weight) => GETCurrentSlot.weight = weight;

    void ApplyHandsIKTarget(int handId, string pointName)
    {
        var handIk = handId > 0 ? leftHandIK : rightHandIK;

        switch (pointName)
        {
            case "RightHandDefault":
                handIk.target = (from f in GETCurrentWeapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.RightHandDefault
                                 select f).SingleOrDefault()?.transform;
                break;
            case "LeftHandDefault":
                handIk.target = (from f in GETCurrentWeapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.LeftHandDefault
                                 select f).SingleOrDefault()?.transform;
                break;
            case "RightHandguard":
                handIk.target = (from f in GETCurrentWeapon.weaponPoints
                                 where f.GetComponent<WeaponPoint>().pointType == WeaponPoint.PointType.RightHandguard
                                 select f).SingleOrDefault()?.transform;
                break;
            case "LeftHandguard":
                handIk.target = (from f in GETCurrentWeapon.weaponPoints
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

    #region COD-style visibility & melee

    void LateUpdate()
    {
        if (!hideHolsteredGuns || slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;

            Weapon weapon = slot.GetComponentInChildren<Weapon>(true);
            if (weapon == null) continue;

            int id = i + 1;
            bool visible = !MeleeMode && (id == activeID || (changed && id == nextID));
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
