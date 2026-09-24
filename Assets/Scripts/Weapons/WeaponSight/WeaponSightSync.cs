using Mirror;
using UnityEngine;

/// <summary>
/// Syncs the aim target of the weapon sight rig from the owning client to everyone else.
/// </summary>
public class WeaponSightSync : NetworkBehaviour
{
    [SerializeField] private TransformToTargetRig transformToTargetRig;
    [SerializeField] private WeaponSightPositionGetter weaponSightPositionGetter;

    [Header("Sync")]
    [Tooltip("How many times per second the owner uploads the sight target")]
    [SerializeField] private float sendRate = 20f;

    [SyncVar(hook = nameof(OnPositionSynced))] Vector3 syncedTargetPosition;
    [SyncVar(hook = nameof(OnRotationSynced))] Quaternion syncedTargetRotation;

    float nextSendTime;
    Vector3 lastSentPosition;
    Quaternion lastSentRotation;

    public override void OnStartClient()
    {
        if (!isOwned)
        {
            weaponSightPositionGetter.execute = false;

            transformToTargetRig.SetPositionTarget(syncedTargetPosition);
            transformToTargetRig.SetRotationTarget(syncedTargetRotation);
        }
    }

    void Update()
    {
        if (!isOwned || !NetworkClient.active) return;
        if (Time.time < nextSendTime) return;

        Vector3 position = transformToTargetRig.targetPosition;
        Quaternion rotation = transformToTargetRig.targetRotation;

        if (position != lastSentPosition || rotation != lastSentRotation)
        {
            nextSendTime = Time.time + 1f / Mathf.Max(1f, sendRate);
            lastSentPosition = position;
            lastSentRotation = rotation;

            CmdSyncSightTarget(position, rotation);
        }
    }

    [Command]
    void CmdSyncSightTarget(Vector3 position, Quaternion rotation)
    {
        syncedTargetPosition = position;
        syncedTargetRotation = rotation;
    }

    void OnPositionSynced(Vector3 _, Vector3 value)
    {
        if (!isOwned) transformToTargetRig.SetPositionTarget(value);
    }

    void OnRotationSynced(Quaternion _, Quaternion value)
    {
        if (!isOwned) transformToTargetRig.SetRotationTarget(value);
    }
}
