using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponSight : MonoBehaviour
{
    [SerializeField] private Vector3 roation;

    public Vector3 getRotation => roation;

    [SerializeField] private WeaponSightEnabler weaponSightEnabler;

    public void Activate()
    {
        if (weaponSightEnabler == null) return;
        if (weaponSightEnabler.enabled == false) return;
        weaponSightEnabler.Activate();
    }

    public void Deactivate()
    {
        if (weaponSightEnabler == null) return;
        if (weaponSightEnabler.enabled == false) return;
        weaponSightEnabler.Deactivate();
    }
}
