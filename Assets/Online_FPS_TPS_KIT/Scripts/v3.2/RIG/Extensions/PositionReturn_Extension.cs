using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PositionReturn_Extension : RigExtension<LocalRigs.PositionConstrained>
{
    public override void Tick()
    {
        baseLocalRig.toTransform.transformObject.localPosition = Vector3.zero;
    }
}
