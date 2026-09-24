using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotReturn_Ex : RigExtension<LocalRigs.RotationConstrained>
{
    public override void Tick()
    { 
        baseLocalRig.toObject.localRotation = Quaternion.identity;
    }
}
