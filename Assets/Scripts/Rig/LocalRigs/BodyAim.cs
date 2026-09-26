using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BodyAim : LocalRig
{
    [Serializable]
    public struct BoneLookModel
    {
        public Transform bone;
        [Range(0, 1)] public float verticalWeight;
        [Range(0, 1)] public float horizontalWeight;

        public Quaternion lookRotation;
    }

    [SerializeField] private Transform _rootBone;
    [SerializeField] private BoneLookModel[] _bonesLook;

    [SerializeField] private float _verticalViewAngle;
    [SerializeField] private float _horizontalViewAngle;
    [SerializeField] private float _maxVerticalAngle = 80f;


    public void SetViewAngles(float verticalAngle, float horizontalAngle)
    {
        _verticalViewAngle = verticalAngle;
        _horizontalViewAngle = horizontalAngle;
    }

    public void AddViewAngle(float verticalAngle, float horizontalAngle)
    {
        _verticalViewAngle += verticalAngle;
        _horizontalViewAngle += horizontalAngle;

        _verticalViewAngle = Mathf.Clamp(_verticalViewAngle, -_maxVerticalAngle, _maxVerticalAngle);
    }

    public override void Execute()
    {
        for (int i = 0; i < _bonesLook.Length; i++)
        {
            _bonesLook[i].lookRotation = boonLookRotationCalculate(_bonesLook[i].bone, _bonesLook[i].verticalWeight, _bonesLook[i].horizontalWeight);
        }

        for (int i = 0; i < _bonesLook.Length; i++)
        {
            _bonesLook[i].bone.rotation = _bonesLook[i].lookRotation;
        }
    }


    public Quaternion boonLookRotationCalculate(Transform bone, float verticalWeight, float horizontalWeight)
    {
        var verticalAngleCalculate = Mathf.Lerp(0, _verticalViewAngle, verticalWeight);
        var horizontalAngleCalculate = Mathf.Lerp(0, _horizontalViewAngle, horizontalWeight);

        var horizontalRotate = Quaternion.AngleAxis(horizontalAngleCalculate, _rootBone.up);
        var finalHorizontalRotation = horizontalRotate * bone.rotation;

        var boneForwardOffsetCalculate = Quaternion.Euler(_rootBone.eulerAngles.x, _rootBone.eulerAngles.y + horizontalAngleCalculate, 0);

        var verticalRotate = Quaternion.AngleAxis(verticalAngleCalculate, boneForwardOffsetCalculate * Vector3.right);

        var finalRotation = verticalRotate * finalHorizontalRotation;

        return finalRotation;
    }
}
