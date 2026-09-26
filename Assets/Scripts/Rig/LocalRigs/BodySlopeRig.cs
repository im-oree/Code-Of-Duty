using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BodySlopeRig : LocalRig
{
    public bool canSlope = true;
    public Transform constrainedObject;
    public Transform bodyTransform;
    public float weight = 1;
    public float slopeAngle;
    private Quaternion startRotation;

    private void Start()
    {
        startRotation = constrainedObject.localRotation;
    }

    public override void Execute()
    {
        if (!canSlope) return;

        Quaternion defRot = constrainedObject.rotation;
        Quaternion bodyRotation = bodyTransform.rotation;

        transform.localRotation = Quaternion.Euler(0, 0, slopeAngle);

        Quaternion rotationDelta = Quaternion.Euler(transform.localRotation.eulerAngles + bodyRotation.eulerAngles)
                                   * Quaternion.Euler(defRot.eulerAngles - bodyRotation.eulerAngles);

        constrainedObject.rotation = Quaternion.Lerp(defRot, rotationDelta, weight);
    }
}
