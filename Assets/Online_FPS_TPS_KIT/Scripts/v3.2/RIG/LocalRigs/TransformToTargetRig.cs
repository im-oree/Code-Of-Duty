using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformToTargetRig : LocalRig
{
    [SerializeField] private Transform useTransform;
    [SerializeField] private float accuracy = 0.001f;
    [SerializeField] private float changeRate = 20f;

    [SerializeField] private bool applyTargetPosition;
    [SerializeField] private bool applyTargetRotation;

    public Vector3 targetPosition { get; private set; }
    public Quaternion targetRotation { get; private set; }

    [SerializeField] private bool useLocal;

    public void SetPositionTarget(Vector3 position)
    {
        targetPosition = position;
    }

    public void SetRotationTarget(Quaternion rotation)
    {
        targetRotation = rotation;
    }

    public override void Execute()
    {
        if (applyTargetPosition && Vector3.Distance(useLocal ? useTransform.localPosition : useTransform.position, targetPosition) > accuracy)
        {
            if (useLocal) useTransform.localPosition = Vector3.Lerp(useTransform.localPosition, targetPosition, changeRate * Time.deltaTime);
            else useTransform.position = Vector3.Lerp(useTransform.position, targetPosition, changeRate * Time.deltaTime);
        }


        if (applyTargetRotation && Quaternion.Angle(useLocal ? useTransform.localRotation : useTransform.rotation, targetRotation) > accuracy)
        {
            if (useLocal) useTransform.localRotation = Quaternion.Slerp(useTransform.localRotation, targetRotation, changeRate * Time.deltaTime);
            else useTransform.rotation = Quaternion.Slerp(useTransform.rotation, targetRotation, changeRate * Time.deltaTime);
        }
    }
}
