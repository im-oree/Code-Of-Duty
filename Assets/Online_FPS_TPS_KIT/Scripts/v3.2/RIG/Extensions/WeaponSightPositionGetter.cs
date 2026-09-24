using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponSightPositionGetter : RigExtension<TransformToTargetRig>
{
    public bool execute = true;
    public bool getPostion = false;

    [SerializeField] private Transform pivot;
    [SerializeField] private Transform eyePoint;
    [SerializeField] private Transform sightPoint;

    [SerializeField] private Vector3 actualPosition;

    public void SetSightRotation(Quaternion rotation)
    {
        baseLocalRig.SetRotationTarget(rotation);
    }

    public void SetSightPoint(Transform point)
    {
        sightPoint = point;
    }

    public override void Tick()
    {
        if (!execute) return;
        
        actualPosition = getPostion ? pivot.localPosition + sightPoint.transform.InverseTransformPoint(eyePoint.position) : Vector3.zero;

        baseLocalRig.SetPositionTarget(actualPosition);
    }
}
