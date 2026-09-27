using System;
using System.Collections;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Invector;
using UnityEngine;

/// <summary>
/// Server-authoritative health for Invector characters.
///
/// Flow: any client whose projectile/melee hit lands calls TakeDamage on the
/// victim's CODThirdPersonController → InterceptDamage sends it to the server
/// → the server applies it to its own copy (real health bookkeeping) and
/// replicates the damage event to every client, where the stock Invector
/// damage pipeline runs (hit reaction, death animation/ragdoll). The synced
/// health value keeps late joiners and drift in check.
///
/// The server despawns the body and respawns a fresh character a few seconds
/// after death (CODNetworkManager owns spawning).
/// </summary>
public class CODNetworkHealth : NetworkBehaviour
{
    [Tooltip("Seconds between death and respawn")]
    public float respawnDelay = 5f;

    public readonly SyncVar<float> health = new SyncVar<float>(100f);

    /// <summary>(attackerName, victimName) — raised on every client when someone dies.</summary>
    public static event Action<string, string> PlayerKilled;

    CODThirdPersonController controller;
    bool applyingReplicatedDamage;
    bool deathHandled;

    void Awake()
    {
        controller = GetComponent<CODThirdPersonController>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (controller != null)
        {
            health.Value = controller.currentHealth;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // late joiner / drift correction: adopt the server's value
        if (!base.IsServerInitialized && controller != null && !Mathf.Approximately(controller.currentHealth, health.Value))
        {
            controller.ChangeHealth((int)(health.Value - controller.currentHealth));
        }
    }

    /// <summary>
    /// Called by CODThirdPersonController before applying damage locally.
    /// Returns true when the damage was routed through the network instead.
    /// </summary>
    public bool InterceptDamage(vDamage damage)
    {
        if (applyingReplicatedDamage) return false;   // replication → run locally
        if (!base.IsSpawned) return false;            // offline → run locally

        if (base.IsServerInitialized)
        {
            ServerApplyDamage(damage.damageValue, damage.hitPosition, damage.reaction_id,
                damage.recoil_id, damage.hitReaction, damage.activeRagdoll,
                damage.sender != null ? damage.sender.root.name : string.Empty,
                damage.damageType ?? string.Empty);
        }
        else
        {
            RequestDamage(damage.damageValue, damage.hitPosition, damage.reaction_id,
                damage.recoil_id, damage.hitReaction, damage.activeRagdoll,
                damage.sender != null ? damage.sender.root.name : string.Empty,
                damage.damageType ?? string.Empty);
        }
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDamage(float value, Vector3 hitPosition, int reactionId, int recoilId,
        bool hitReaction, bool activeRagdoll, string attackerName, string damageType)
    {
        ServerApplyDamage(value, hitPosition, reactionId, recoilId, hitReaction, activeRagdoll, attackerName, damageType);
    }

    [Server]
    void ServerApplyDamage(float value, Vector3 hitPosition, int reactionId, int recoilId,
        bool hitReaction, bool activeRagdoll, string attackerName, string damageType)
    {
        if (controller == null || controller.isDead) return;

        ApplyLocal(value, hitPosition, reactionId, recoilId, hitReaction, activeRagdoll, damageType);
        health.Value = controller.currentHealth;

        ReplicateDamage(value, hitPosition, reactionId, recoilId, hitReaction, activeRagdoll, damageType);

        if (controller.currentHealth <= 0f && !deathHandled)
        {
            deathHandled = true;
            AnnounceKill(attackerName, GetComponent<CODInvectorPlayer>()?.playerName.Value ?? name);
            StartCoroutine(RespawnRoutine());
        }
    }

    [ObserversRpc(ExcludeServer = true)]
    void ReplicateDamage(float value, Vector3 hitPosition, int reactionId, int recoilId,
        bool hitReaction, bool activeRagdoll, string damageType)
    {
        ApplyLocal(value, hitPosition, reactionId, recoilId, hitReaction, activeRagdoll, damageType);
    }

    void ApplyLocal(float value, Vector3 hitPosition, int reactionId, int recoilId,
        bool hitReaction, bool activeRagdoll, string damageType)
    {
        if (controller == null) return;

        vDamage damage = new vDamage((int)value)
        {
            hitPosition = hitPosition,
            reaction_id = reactionId,
            recoil_id = recoilId,
            hitReaction = hitReaction,
            activeRagdoll = activeRagdoll,
            damageType = damageType,
            receiver = controller.transform
        };

        applyingReplicatedDamage = true;
        controller.ApplyDamageDirect(damage);
        applyingReplicatedDamage = false;
    }

    [ObserversRpc(RunLocally = true)]
    void AnnounceKill(string attacker, string victim)
    {
        PlayerKilled?.Invoke(attacker, victim);
    }

    [Server]
    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (base.Owner != null && base.Owner.IsActive && CODNetworkManager.Instance != null)
        {
            CODNetworkManager.Instance.RespawnPlayer(base.Owner);
        }

        base.Despawn();
    }
}
