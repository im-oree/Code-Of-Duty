using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScopeSightEnabler : WeaponSightEnabler
{
    [SerializeField] private Camera _cam;

    public override void Activate()
    {
        _cam.enabled = true;
    }

    public override void Deactivate()
    {
        _cam.enabled = false;
    }
}
