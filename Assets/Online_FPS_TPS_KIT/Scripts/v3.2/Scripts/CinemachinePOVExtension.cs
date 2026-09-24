using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;


public class CinemachinePOVExtension : Unity.Cinemachine.CinemachineExtension
{
    [SerializeField] float maxViewAngle = 80;
    [SerializeField] private Vector3 cameraRotation;

    protected override void PostPipelineStageCallback(Unity.Cinemachine.CinemachineVirtualCameraBase vcam, Unity.Cinemachine.CinemachineCore.Stage stage, ref Unity.Cinemachine.CameraState state, float deltaTime)
    {
        if (vcam.Follow)
        {
            if (stage == Unity.Cinemachine.CinemachineCore.Stage.Aim)
            {
                if (cameraRotation == null)
                {
                    cameraRotation = transform.localRotation.eulerAngles;
                }

                state.RawOrientation = Quaternion.Euler(cameraRotation);
            }
        }
    }

    public void SetCameraRotation(Vector3 rotation)
    {
        cameraRotation = rotation;
    }
}
