using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AimReturn_Extension : RigExtension<LocalRigs.Aim>
{
    public override void Tick()
    {
        transform.localRotation = baseLocalRig.startRotation;
    }
}
