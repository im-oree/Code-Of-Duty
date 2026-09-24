using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BodyAimHandler : MonoBehaviour
{
    [SerializeField] private Transform rootBone;
    [SerializeField] private Transform viewCamera;

    [SerializeField] private float horizontalAngle;
    [SerializeField] private float verticalAngle;

    public BodyAim boneAimCore;

    public Vector3 xAngle;
    public float signedAngel;

    private void LateUpdate()
    {
        if (rootBone == null) return;
        if (viewCamera == null) return;
        
        verticalAngle = Vector3.SignedAngle(viewCamera.up, rootBone.up, viewCamera.right) * -1;

        int horizontalCross = Vector3.Cross(rootBone.right, viewCamera.right).y > 0 ? 1 : -1;
        horizontalAngle = Vector3.Angle(rootBone.right, viewCamera.right) * horizontalCross;

        boneAimCore.SetViewAngles(verticalAngle, horizontalAngle);
    }
}
