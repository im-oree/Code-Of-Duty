using System.Text.RegularExpressions;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RecoilController : MonoBehaviour
{
    public EventsCenter eventsCenter;
    public CameraController cameraController;
    public WeaponController weaponController;
    public RecoilParametersModel recoilParametersModel;
    public Transform weaponRotationRecoilPivot;
    public Transform weaponPositionRecoilPivot;

    public Vector3 lastPosition;
    public Quaternion lastRotation;

    // Rest pose of the recoil pivots, captured before anything kicks them.
    Vector3 restPosition;
    Quaternion restRotation;
    bool restCaptured;

    // Only the recoil routines, so stopping a kick cannot cancel unrelated work.
    Coroutine cameraRecoilRoutine, rotationRecoilRoutine, positionRecoilRoutine;

    // OnShoot also fires on REMOTE rigs (NetCMDs.ObserversShoot replays shots
    // for visuals) — only the locally-owned rig may shake the local camera.
    FishNet.Object.NetworkObject _netObject;
    bool _netObjectSearched;
    bool IsLocalRig
    {
        get
        {
            if (!_netObjectSearched)
            {
                _netObject = GetComponentInParent<FishNet.Object.NetworkObject>();
                _netObjectSearched = true;
            }
            return _netObject == null || _netObject.IsOwner; // null = offline rig
        }
    }

    private void OnEnable()
    {
        eventsCenter.OnWeaponChange += weaponChangeCheck;
        weaponController.OnShoot += RecoilStarter;
    }
    private void OnDisable()
    {
        eventsCenter.OnWeaponChange -= weaponChangeCheck;
        weaponController.OnShoot -= RecoilStarter;
    }

    private void Start()
    {
        CaptureRestPose();

        var weapon = weaponController.GETCurrentWeapon;
        if (weapon != null) recoilParametersModel = weapon.recoilParametersModel;
    }

    void CaptureRestPose()
    {
        if (restCaptured) return;
        if (weaponPositionRecoilPivot != null) restPosition = weaponPositionRecoilPivot.localPosition;
        if (weaponRotationRecoilPivot != null) restRotation = weaponRotationRecoilPivot.localRotation;
        restCaptured = true;
    }

    void weaponChangeCheck(bool changed)
    {
        if (changed) return;
        var weapon = weaponController.GETCurrentWeapon;
        if (weapon != null) recoilParametersModel = weapon.recoilParametersModel;
    }

    void RecoilStarter()
    {
        var currentWeapon = weaponController.GETCurrentWeapon;
        if (currentWeapon == null) return;

        CaptureRestPose();

        // Stop only the previous kick. StopAllCoroutines() here would also kill anything else
        // this component ever starts, which is a trap waiting for the next person to add one.
        if (cameraRecoilRoutine != null) StopCoroutine(cameraRecoilRoutine);
        if (rotationRecoilRoutine != null) StopCoroutine(rotationRecoilRoutine);
        if (positionRecoilRoutine != null) StopCoroutine(positionRecoilRoutine);

        // Re-base the kick on the rest pose.
        //
        // Each recoil curve takes a full second to travel out and settle back, but a full-auto
        // rifle fires every 0.07s. Every shot therefore interrupted the previous recovery and
        // then started the next curve from wherever the gun had been left -- and since the
        // routines feed their own output back in through lastPosition/lastRotation, the
        // displacement compounded about fourteen times a second. Within a moment the weapon had
        // wandered completely out of the hands and off screen, which reads as the gun vanishing
        // or swapping itself the instant you hold the trigger. Single-shot weapons hid the bug
        // because their curve had time to finish between clicks.
        lastPosition = restPosition;
        lastRotation = restRotation;

        // cosmetic screen-shake layered on top of the aim recoil (local only).
        // heavier weapon classes kick the camera harder.
        if (IsLocalRig)
        {
            Weapon weapon = weaponController.GETCurrentWeapon;
            float strength = 0.5f;
            if (weapon != null)
            {
                switch (weapon.slotType)
                {
                    case Weapon.SlotType.rifle: strength = weapon.singleShoot ? 0.95f : 0.42f; break;
                    case Weapon.SlotType.smg: strength = 0.35f; break;
                    case Weapon.SlotType.pistol: strength = 0.5f; break;
                }
            }
            CameraShake.FireKick(strength);
        }

        var model = currentWeapon.recoilParametersModel;

        cameraRecoilRoutine = StartCoroutine(ApplyCameraRecoil(model.cameraRecoilAxes));
        rotationRecoilRoutine = StartCoroutine(ApplyPositionRecoil(weaponRotationRecoilPivot, model.weaponRotationRecoilAxes, true));
        positionRecoilRoutine = StartCoroutine(ApplyPositionRecoil(weaponPositionRecoilPivot, model.weaponPositionRecoilAxes, false));
    }

    IEnumerator ApplyCameraRecoil(RecoilParametersModel.RecoilAxis[] recoilAxes)
    {
        if (recoilAxes.Length == 0) yield break;

        Vector3 finalState = GetFinalState(recoilAxes);

        float t = 0;
        while (t < 1)
        {
            var verticalRecoil = finalState.x * recoilAxes[0].axisValueCurve.Evaluate(t / 1);
            var horizontalRecoil = recoilAxes.Length > 1 ? finalState.y * recoilAxes[1].axisValueCurve.Evaluate(t / 1) : 0;

            cameraController.SetCameraRotation(verticalRecoil, horizontalRecoil);

            t += Time.deltaTime;

            yield return null;
        }
    }

    IEnumerator ApplyPositionRecoil(Transform pivot, RecoilParametersModel.RecoilAxis[] recoilAxes, bool recoilRotate)
    {
        if (pivot == null || recoilAxes == null || recoilAxes.Length == 0) yield break;

       // var startState = recoilRotate ? GetStartState(pivot.localRotation.eulerAngles, recoilAxes) : GetStartState(pivot.localPosition, recoilAxes);

        //var startState = recoilRotate ? GetStartState(pivot.localRotation.eulerAngles, recoilAxes) : GetStartState(pivot.GetComponent<LocalRigs.PositionConstrained>().fromTransform.lastPostition, recoilAxes);


        var finalPosition = GetFinalState(recoilAxes);

        //var pivotState = recoilRotate ? pivot.localPosition : pivot.localRotation.eulerAngles;

        var recoilValue = Vector3.zero;

        float t = 0;
        while (t < 1)
        {
            recoilValue = new Vector3(
                finalPosition.x * recoilAxes[0].axisValueCurve.Evaluate(t / 1),
                finalPosition.y * (recoilAxes.Length > 1 ? recoilAxes[1].axisValueCurve.Evaluate(t / 1) : 0),
                finalPosition.z * (recoilAxes.Length > 2 ? recoilAxes[2].axisValueCurve.Evaluate(t / 1) : 0)
            );

            var currentState = Vector3.zero;
            var currentRotation = Quaternion.identity;

            if (recoilRotate)
            {
                currentRotation = GetCurrentRotationState(lastRotation, recoilValue, recoilAxes, t);
                pivot.localRotation = currentRotation;
                lastRotation = pivot.localRotation;
            }
            else
            {
                currentState = GetCurrentPositionState(lastPosition, recoilValue, recoilAxes, t);
                pivot.localPosition = currentState;
                lastPosition = currentState;
            }


            t += Time.deltaTime;
            yield return null;
        }

        // Land exactly on rest. Curves that do not quite evaluate to zero at t=1 would otherwise
        // leave a sliver of offset behind on every single shot.
        if (recoilRotate) { pivot.localRotation = restRotation; lastRotation = restRotation; }
        else { pivot.localPosition = restPosition; lastPosition = restPosition; }
    }

    Vector3 GetStartState(Vector3 currentPivotPosition, RecoilParametersModel.RecoilAxis[] recoilAxes)
    {
        var xValue = recoilAxes.Length > 0 && recoilAxes[0].smoothReturnTime > 0 ? currentPivotPosition.x : 0;
        var yValue = recoilAxes.Length > 1 && recoilAxes[1].smoothReturnTime > 0 ? currentPivotPosition.y : 0;
        var zValue = recoilAxes.Length > 2 && recoilAxes[2].smoothReturnTime > 0 ? currentPivotPosition.z : 0;

        return new Vector3(xValue, yValue, zValue);
    }

    Vector3 GetFinalState(RecoilParametersModel.RecoilAxis[] recoilAxes)
    {
        float xValue = 0;
        if (recoilAxes.Length > 0)
        {
            xValue = GetFinalAxisValue(recoilAxes[0]);
        }
        float yValue = 0;
        if (recoilAxes.Length > 1)
        {
            yValue = GetFinalAxisValue(recoilAxes[1]);
        }
        float zValue = 0;
        if (recoilAxes.Length > 2)
        {
            zValue = GetFinalAxisValue(recoilAxes[2]);
        }

        return new Vector3(xValue, yValue, zValue);
    }

    float GetFinalAxisValue(RecoilParametersModel.RecoilAxis recoilAxis)
    {
        float value = 0;

        float force = recoilParametersModel.recoilForce * recoilAxis.percentageOfRecoilValue;
        if (recoilAxis.randomValue)
        {
            value = UnityEngine.Random.Range(recoilAxis.canBeNegative ? force * -1 : 0, force);
        }
        else
        {
            value = force;
        }

        return value;
    }

    Vector3 GetCurrentPositionState(Vector3 startVector, Vector3 currentRecoilVector, RecoilParametersModel.RecoilAxis[] recoilAxis, float recoiledTime)
    {
        float xValue = 0;
        if (recoilAxis.Length > 0)
        {
            xValue = recoilAxis[0].smoothReturnTime > 0 && (recoiledTime / recoilAxis[0].smoothReturnTime) < 1 ? Mathf.Lerp(startVector.x, currentRecoilVector.x, recoiledTime / recoilAxis[0].smoothReturnTime) : currentRecoilVector.x;
        }

        float yValue = 0;
        if (recoilAxis.Length > 1)
        {
            yValue = recoilAxis[1].smoothReturnTime > 0 && (recoiledTime / recoilAxis[1].smoothReturnTime) < 1 ? Mathf.Lerp(startVector.y, currentRecoilVector.y, recoiledTime / recoilAxis[1].smoothReturnTime) : currentRecoilVector.y;
        }

        float zValue = 0;
        if (recoilAxis.Length > 2)
        {
            zValue = recoilAxis[2].smoothReturnTime > 0 && (recoiledTime / recoilAxis[2].smoothReturnTime) < 1 ? Mathf.Lerp(startVector.z, currentRecoilVector.z, recoiledTime / recoilAxis[2].smoothReturnTime) : currentRecoilVector.z;
        }

        return new Vector3(xValue, yValue, zValue);
    }

    Quaternion GetCurrentRotationState(Quaternion startRot, Vector3 currentRecoilVector, RecoilParametersModel.RecoilAxis[] recoilAxis, float recoiledTime)
    {
        Quaternion lerpRot = Quaternion.identity;

        float xValue = 0;
        float yValue = 0;
        float zValue = 0;

        if (recoilAxis.Length > 0)
        {
            float startX = startRot.eulerAngles.x;
            startX += startRot.eulerAngles.x < -180 ? +360f : 0;
            startX += startRot.eulerAngles.x > 180 ? -360f : 0;

            xValue = recoilAxis[0].smoothReturnTime > 0 && (recoiledTime / recoilAxis[0].smoothReturnTime) < 1 ? Mathf.Lerp(startX, currentRecoilVector.x, recoiledTime / recoilAxis[0].smoothReturnTime) : currentRecoilVector.x;
        }

        if (recoilAxis.Length > 1)
        {
            float startY = startRot.eulerAngles.y;
            startY += startRot.eulerAngles.y < -180 ? +360f : 0;
            startY += startRot.eulerAngles.y > 180 ? -360f : 0;

            yValue = recoilAxis[1].smoothReturnTime > 0 && (recoiledTime / recoilAxis[1].smoothReturnTime) < 1 ? Mathf.Lerp(startY, currentRecoilVector.y, recoiledTime / recoilAxis[1].smoothReturnTime) : currentRecoilVector.y;
        }

        if (recoilAxis.Length > 2)
        {
            float startZ = startRot.eulerAngles.z;
            startZ += startRot.eulerAngles.z < -180 ? +360f : 0;
            startZ += startRot.eulerAngles.z > 180 ? -360f : 0;

            zValue = recoilAxis[2].smoothReturnTime > 0 && (recoiledTime / recoilAxis[2].smoothReturnTime) < 1 ? Mathf.Lerp(startZ, currentRecoilVector.z, recoiledTime / recoilAxis[2].smoothReturnTime) : currentRecoilVector.z;
        }

        var q = Quaternion.Euler(xValue, yValue, zValue);

        return q;
    }
}
