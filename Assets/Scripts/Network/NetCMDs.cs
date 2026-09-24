using Mirror;
using UnityEngine;

/// <summary>
/// Player network hub (Mirror). Lives on the player prefab root next to NetworkIdentity.
/// - Syncs body slope / turn / active weapon (SyncVars, late joiners get state for free)
/// - Replicates shooting and weapon switching (Commands -> ClientRpcs)
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

    [SyncVar] public string playerName = "Player";

    [SyncVar(hook = nameof(OnBodySlopeSynced))] float syncedBodySlope;
    [SyncVar(hook = nameof(OnMomentaryTurnSynced))] bool syncedMomentaryTurn;
    [SyncVar(hook = nameof(OnActiveWeaponSynced))] int syncedActiveWeaponID;

    bool remoteStateInitialized;
    float nextBodySyncTime;
    float lastSentBodySlope = float.MinValue;
    bool lastSentMomentaryTurn;
    int lastSentActiveWeaponID = int.MinValue;

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

    public override void OnStartLocalPlayer()
    {
        CmdSetPlayerName(CODNetworkManager.PlayerName);
    }

    public override void OnStartClient()
    {
        if (!isOwned)
        {
            // snap remote/late-joined players to their current state
            bodySlope_Handler.targetAngle = syncedBodySlope;
            bodyTurnHandler.momentaryTurn = syncedMomentaryTurn;

            if (syncedActiveWeaponID != 0)
            {
                weaponController.activeID = syncedActiveWeaponID;
                weaponController.animator.Play("GunPickUp", 1);
            }
        }

        remoteStateInitialized = true;
    }

    void Update()
    {
        if (!isOwned || !NetworkClient.active) return;
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

            CmdSyncBodyState(slope, turn, weaponID);
        }
    }

    #region Name

    [Command]
    void CmdSetPlayerName(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            playerName = newName.Trim();
    }

    #endregion

    #region Body state sync (owner -> server -> everyone)

    [Command]
    void CmdSyncBodyState(float bodySlope, bool momentaryTurn, int activeWeaponID)
    {
        syncedBodySlope = bodySlope;
        syncedMomentaryTurn = momentaryTurn;
        syncedActiveWeaponID = activeWeaponID;
    }

    void OnBodySlopeSynced(float _, float value)
    {
        if (!isOwned) bodySlope_Handler.targetAngle = value;
    }

    void OnMomentaryTurnSynced(bool _, bool value)
    {
        if (!isOwned) bodyTurnHandler.momentaryTurn = value;
    }

    void OnActiveWeaponSynced(int _, int value)
    {
        if (!isOwned && remoteStateInitialized)
            weaponController.activeID = value;
    }

    #endregion

    #region Shooting

    void ShootSender()
    {
        if (isOwned) CmdShoot();
    }

    [Command]
    void CmdShoot() => RpcShoot();

    [ClientRpc(includeOwner = false)]
    void RpcShoot() => weaponController.StartShoot();

    #endregion

    #region Weapon switching

    void WeaponChangeSender(bool changing)
    {
        if (!changing || !isOwned) return;
        CmdChangeWeapon(weaponController.nextID);
    }

    [Command]
    void CmdChangeWeapon(int nextGunSlotID) => RpcChangeWeapon(nextGunSlotID);

    [ClientRpc(includeOwner = false)]
    void RpcChangeWeapon(int nextGunSlotID) => weaponController.ToChange(nextGunSlotID);

    #endregion

    #region Damage / kill feed

    /// <summary>
    /// Called on the SHOOTER's player object by its owning client when a bullet hits.
    /// Runs on the server, which forwards authoritative damage to the victim.
    /// </summary>
    [Command]
    public void CmdDealDamage(NetworkIdentity target, float damage, bool hitOnTheHead, string weaponName)
    {
        if (target == null) return;

        if (target.TryGetComponent(out NetCMDs victim))
            victim.RpcApplyDamage(damage, netIdentity, hitOnTheHead, weaponName);
    }

    [ClientRpc]
    void RpcApplyDamage(float damage, NetworkIdentity killer, bool hitOnTheHead, string weaponName)
    {
        float health = playerHealth.SetDamage(damage);

        if (health <= 0f && killer != null)
        {
            bool involvesLocalPlayer = killer.isOwned | isOwned;
            string killerName = killer.TryGetComponent(out NetCMDs killerCmds) ? killerCmds.playerName : "Unknown";

            UIManger.instance.killPanel.CreateKillItemUI(killerName, playerName, weaponName, hitOnTheHead, involvesLocalPlayer);
        }
    }

    #endregion

    #region Respawn

    /// <summary>Called by PlayerLifeController on the owning client when the respawn timer ends.</summary>
    public void RequestRespawn()
    {
        if (isOwned) CmdRespawn();
    }

    [Command]
    void CmdRespawn() => RpcRespawn();

    [ClientRpc]
    void RpcRespawn() => playerLifeController.Respawn();

    #endregion
}
