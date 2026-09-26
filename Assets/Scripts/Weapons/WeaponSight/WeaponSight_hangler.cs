using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponSight_hangler : MonoBehaviour
{
    [SerializeField] private bool canAim = true;
    [SerializeField] private CameraSwitcher cameraSwitcher;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private EventsCenter eventsCenter;
    [SerializeField] private WeaponCollisionRig weaponCollisionRig;
    [SerializeField] private WeaponSightPositionGetter weaponSightPositionGetter;

    [SerializeField] private int activeId = 0;
    [SerializeField] private List<WeaponSight> weaponSights;

    [SerializeField] private bool inAiming;

    private void OnEnable()
    {
        eventsCenter.OnWeaponChange += WeaponChangeListener;
    }

    private void OnDisable()
    {
        eventsCenter.OnWeaponChange -= WeaponChangeListener;
    }

    private void Update() {
        if (weaponCollisionRig.inСollision) weaponSightPositionGetter.getPostion = false;
        else
        {
            if (inAiming) weaponSightPositionGetter.getPostion = true;
        }
    }

    void WeaponChangeListener(bool change)
    {
        canAim = !change;

        if (change)
        {
            foreach (var sight in weaponSights)
            {
                sight.Deactivate();
            }
        }

        DeactivateAimState();
        weaponSights?.Clear();
        activeId = 0;

        if (!change)
        {
            UpdateSights();
        }
    }

    void UpdateSights()
    {
        weaponSights.AddRange(weaponController.GETCurrentWeapon.weaponSights);

        foreach (var sight in weaponSights)
        {
            sight.Activate();
        }
    }

    public void AimViewChange()
    {
        if (!canAim) return;
        if (!inAiming) ActivateScoupe();
        else DeactivateAimState();
    }

    public void AimSightChange()
    {
        if (!inAiming) return;
        activeId = weaponSights.Count <= activeId + 1 ? 0 : activeId + 1;
        ActivateScoupe();
    }

    void ActivateScoupe()
    {
        cameraSwitcher.AimViewChange(true);
        inAiming = true;
        weaponSightPositionGetter.SetSightPoint(weaponSights[activeId].transform);
        weaponSightPositionGetter.getPostion = true;
        weaponSightPositionGetter.SetSightRotation(Quaternion.Euler(weaponSights[activeId].getRotation));
    }

    void DeactivateAimState()
    {
        cameraSwitcher.AimViewChange(false);
        inAiming = false;
        weaponSightPositionGetter.getPostion = false;
        weaponSightPositionGetter.SetSightRotation(Quaternion.identity);
    }

}
