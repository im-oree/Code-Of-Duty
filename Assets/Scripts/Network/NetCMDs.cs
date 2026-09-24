using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Player network hub (FishNet). Lives on the player prefab root next to NetworkObject.
/// - Syncs body slope / turn / active weapon (SyncVars, late joiners get state for free)
/// - Replicates shooting and weapon switching (ServerRpc -> ObserversRpc)
/// - Server-authoritative damage entry point + kill feed
/// - Respawn round trip
/// </summary>
public class NetCMDs : NetworkBehaviour
{
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private BodySlope_Handler bodySlope_Handler;
    [SerializeField] private BodyTurnHandler bodyTurnHandler;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerLifeController playerLifeController;
    [SerializeField] private EventsCenter eventsCenter;

    [Header("Sync")]
    [Tooltip("How many times per second the owner uploads body state (slope/turn/weapon)")]
    [SerializeField] private float bodySyncRate = 15f;

    public readonly SyncVar<string> playerName = new SyncVar<string>("Player");

    readonly SyncVar<float> syncedBodySlope = new SyncVar<float>();
    readonly SyncVar<bool> syncedMomentaryTurn = new SyncVar<bool>();
    readonly SyncVar<int> syncedActiveWeaponID = new SyncVar<int>();

    bool remoteStateInitialized;
    float nextBodySyncTime;
    float lastSentBodySlope = float.MinValue;
    bool lastSentMomentaryTurn;
    int lastSentActiveWeaponID = int.MinValue;

    void Awake()
    {
        syncedBodySlope.OnChange += OnBodySlopeSynced;
        syncedMomentaryTurn.OnChange += OnMomentaryTurnSynced;
        syncedActiveWeaponID.OnChange += OnActiveWeaponSynced;
    }

    void OnEnable()
    {
        weaponController.OnShoot += ShootSender;
        eventsCenter.OnWeaponChange += WeaponChangeSender;
    }

    void OnDisable()
    {
        weaponController.OnShoot -= ShootSender;
        eventsCenter.OnWeaponChange -= WeaponChangeSender;
    }

    void OnDestroy()
    {
        syncedBodySlope.OnChange -= OnBodySlopeSynced;
        syncedMomentaryTurn.OnChange -= OnMomentaryTurnSynced;
        syncedActiveWeaponID.OnChange -= OnActiveWeaponSynced;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            ServerSetPlayerName(CODNetworkManager.PlayerName);
        }
        else
        {
            // snap remote/late-joined players to their current state
            bodySlope_Handler.targetAngle = syncedBodySlope.Value;
            bodyTurnHandler.momentaryTurn = syncedMomentaryTurn.Value;

            if (syncedActiveWeaponID.Value != 0)
            {
                weaponController.activeID = syncedActiveWeaponID.Value;
                weaponController.animator.Play("GunPickUp", 1);
            }
        }

        remoteStateInitialized = true;
    }

    void Update()
    {
        if (!IsOwner || !InstanceFinder.IsClientStarted) return;
        if (Time.time < nextBodySyncTime) return;

        float slope = bodySlope_Handler.targetAngle;
        bool turn = bodyTurnHandler.momentaryTurn;
        int weaponID = weaponController.activeID;

        if (!Mathf.Approximately(slope, lastSentBodySlope) ||
            turn != lastSentMomentaryTurn ||
            weaponID != lastSentActiveWeaponID)
        {
            nextBodySyncTime = Time.time + 1f / Mathf.Max(1f, bodySyncRate);
            lastSentBodySlope = slope;
            lastSentMomentaryTurn = turn;
            lastSentActiveWeaponID = weaponID;

            ServerSyncBodyState(slope, turn, weaponID);
        }
    }

    #region Name

    [ServerRpc]
    void ServerSetPlayerName(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            playerName.Value = newName.Trim();
    }

    #endregion

    #region Body state sync (owner -> server -> everyone)

    [ServerRpc]
    void ServerSyncBodyState(float bodySlope, bool momentaryTurn, int activeWeaponID)
    {
        syncedBodySlope.Value = bodySlope;
        syncedMomentaryTurn.Value = momentaryTurn;
        syncedActiveWeaponID.Value = activeWeaponID;
    }

    void OnBodySlopeSynced(float _, float value, bool asServer)
    {
        if (asServer) return;
        if (!IsOwner) bodySlope_Handler.targetAngle = value;
    }

    void OnMomentaryTurnSynced(bool _, bool value, bool asServer)
    {
        if (asServer) return;
        if (!IsOwner) bodyTurnHandler.momentaryTurn = value;
    }

    void OnActiveWeaponSynced(int _, int value, bool asServer)
    {
        if (asServer) return;
        if (!IsOwner && remoteStateInitialized)
            weaponController.activeID = value;
    }

    #endregion

    #region Shooting

    void ShootSender()
    {
        if (IsOwner) ServerShoot();
    }

    [ServerRpc]
    void ServerShoot() => ObserversShoot();

    [ObserversRpc(ExcludeOwner = true)]
    void ObserversShoot() => weaponController.StartShoot();

    #endregion

    #region Weapon switching

    void WeaponChangeSender(bool changing)
    {
        if (!changing || !IsOwner) return;
        ServerChangeWeapon(weaponController.nextID);
    }

    [ServerRpc]
    void ServerChangeWeapon(int nextGunSlotID) => ObserversChangeWeapon(nextGunSlotID);

    [ObserversRpc(ExcludeOwner = true)]
    void ObserversChangeWeapon(int nextGunSlotID) => weaponController.ToChange(nextGunSlotID);

    #endregion

    #region Damage / kill feed

    /// <summary>
    /// Called on the SHOOTER's player object by its owning client when a bullet hits.
    /// Runs on the server, which forwards authoritative damage to the victim.
    /// </summary>
    [ServerRpc]
    public void ServerDealDamage(NetworkObject target, float damage, bool hitOnTheHead, string weaponName)
    {
        if (target == null) return;

        if (target.TryGetComponent(out NetCMDs victim))
            victim.ObserversApplyDamage(damage, NetworkObject, hitOnTheHead, weaponName);
    }

    [ObserversRpc]
    void ObserversApplyDamage(float damage, NetworkObject killer, bool hitOnTheHead, string weaponName)
    {
        float health = playerHealth.SetDamage(damage);

        // screen-shake feedback for the LOCAL player only
        if (IsOwner)
        {
            if (health <= 0f) CameraShake.Death();
            else CameraShake.HitFlinch(damage);
        }

        if (health <= 0f && killer != null)
        {
            bool involvesLocalPlayer = killer.IsOwner | IsOwner;
            string killerName = killer.TryGetComponent(out NetCMDs killerCmds) ? killerCmds.playerName.Value : "Unknown";

            UIManger.instance.killPanel.CreateKillItemUI(killerName, playerName.Value, weaponName, hitOnTheHead, involvesLocalPlayer);
        }
    }

    #endregion

    #region Respawn

    /// <summary>Called by PlayerLifeController on the owning client when the respawn timer ends.</summary>
    public void RequestRespawn()
    {
        if (IsOwner) ServerRespawn();
    }

    [ServerRpc]
    void ServerRespawn() => ObserversRespawn();

    [ObserversRpc]
    void ObserversRespawn() => playerLifeController.Respawn();

    #endregion
}
