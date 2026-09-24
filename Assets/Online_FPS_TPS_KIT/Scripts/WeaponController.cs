using UnityEngine;
using System.Collections;
using System.Linq;
using Photon.Pun;

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
        if (activeID == nextGunSlotID) return;

        string animaName = "PutSlot" + activeID;
        this.nextID = nextGunSlotID;

        animator.CrossFadeInFixedTime(animaName, 0.25f, 1);
    }
}
