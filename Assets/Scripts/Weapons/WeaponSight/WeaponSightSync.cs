using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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

    readonly SyncVar<Vector3> syncedTargetPosition = new SyncVar<Vector3>();
    readonly SyncVar<Quaternion> syncedTargetRotation = new SyncVar<Quaternion>(Quaternion.identity);

    float nextSendTime;
    Vector3 lastSentPosition;
    Quaternion lastSentRotation;

    void Awake()
    {
        syncedTargetPosition.OnChange += OnPositionSynced;
        syncedTargetRotation.OnChange += OnRotationSynced;
    }

    void OnDestroy()
    {
        syncedTargetPosition.OnChange -= OnPositionSynced;
        syncedTargetRotation.OnChange -= OnRotationSynced;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
        {
            weaponSightPositionGetter.execute = false;

            transformToTargetRig.SetPositionTarget(syncedTargetPosition.Value);
            transformToTargetRig.SetRotationTarget(syncedTargetRotation.Value);
        }
    }

    void Update()
    {
        if (!IsOwner || !InstanceFinder.IsClientStarted) return;
        if (Time.time < nextSendTime) return;

        Vector3 position = transformToTargetRig.targetPosition;
        Quaternion rotation = transformToTargetRig.targetRotation;

        if (position != lastSentPosition || rotation != lastSentRotation)
        {
            nextSendTime = Time.time + 1f / Mathf.Max(1f, sendRate);
            lastSentPosition = position;
            lastSentRotation = rotation;

            ServerSyncSightTarget(position, rotation);
        }
    }

    [ServerRpc]
    void ServerSyncSightTarget(Vector3 position, Quaternion rotation)
    {
        syncedTargetPosition.Value = position;
        syncedTargetRotation.Value = rotation;
    }

    void OnPositionSynced(Vector3 _, Vector3 value, bool asServer)
    {
        if (!asServer && !IsOwner) transformToTargetRig.SetPositionTarget(value);
    }

    void OnRotationSynced(Quaternion _, Quaternion value, bool asServer)
    {
        if (!asServer && !IsOwner) transformToTargetRig.SetRotationTarget(value);
    }
}
