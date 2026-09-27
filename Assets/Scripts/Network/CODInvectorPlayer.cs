using System.Collections;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Network glue for an Invector-based Code Of Duty character.
///
/// Owner client: runs the full Invector stack (input, motor, camera pickup).
/// Remote copies: input and physics disabled — NetworkTransform moves the
/// root and NetworkAnimator mirrors the animator, so remote players show the
/// exact Invector locomotion/aim/shoot animations.
/// Offline (object not network-spawned): behaves like a plain local Invector
/// character so authored test scenes still work.
/// </summary>
[RequireComponent(typeof(CODThirdPersonController))]
public class CODInvectorPlayer : NetworkBehaviour
{
    public readonly SyncVar<string> playerName = new SyncVar<string>("Player");

    /// <summary>Mirrors the owner's parachute state so remote players see the canopy.</summary>
    public readonly SyncVar<bool> parachuteOpen = new SyncVar<bool>(false);

    /// <summary>The locally controlled player, if any.</summary>
    public static CODInvectorPlayer Local { get; private set; }

    CODThirdPersonController controller;
    CODShooterInput input;
    Rigidbody body;
    vParachuteController parachute;

    void Awake()
    {
        controller = GetComponent<CODThirdPersonController>();
        input = GetComponent<CODShooterInput>();
        body = GetComponent<Rigidbody>();
        parachute = GetComponentInChildren<vParachuteController>(true);
        parachuteOpen.OnChange += OnParachuteChanged;

        // stay inert until ownership is known (or offline fallback kicks in)
        SetControlled(false);
        StartCoroutine(OfflineFallback());
    }

    void OnDestroy()
    {
        parachuteOpen.OnChange -= OnParachuteChanged;
    }

    void Update()
    {
        // owner: publish parachute state changes
        if (base.IsSpawned && base.IsOwner && parachute != null &&
            parachute.usingParachute != parachuteOpen.Value)
        {
            ServerSetParachute(parachute.usingParachute);
        }
    }

    [ServerRpc]
    void ServerSetParachute(bool open) => parachuteOpen.Value = open;

    void OnParachuteChanged(bool _, bool open, bool __)
    {
        // remote proxies: the controller logic doesn't run, so toggle the
        // canopy visual directly (pose comes from the NetworkAnimator)
        if (base.IsOwner || parachute == null || parachute.parachuteTilt == null) return;
        parachute.parachuteTilt.SetActive(open);
    }

    /// <summary>If the object was never network-spawned (offline scene), activate local control.</summary>
    IEnumerator OfflineFallback()
    {
        yield return null;
        if (!base.IsSpawned && input != null && !input.enabled)
        {
            SetControlled(true);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        SetControlled(base.IsOwner);

        if (base.IsOwner)
        {
            Local = this;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // dedicated server (no client for this object): run the motor for
        // authoritative physics but never the input/camera side
        if (!base.IsClientInitialized)
        {
            SetControlled(false);
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (Local == this) Local = null;
    }

    /// <summary>Enable/disable the pieces only the controlling machine should run.</summary>
    void SetControlled(bool controlled)
    {
        if (input != null) input.enabled = controlled;
        if (controller != null) controller.enabled = controlled;

        if (body != null)
        {
            body.isKinematic = !controlled;
            body.interpolation = controlled ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }

        // remote copies must not fight the NetworkAnimator with IK/headtrack updates
        var headTrack = GetComponent<Invector.vCharacterController.vHeadTrack>();
        if (headTrack != null) headTrack.enabled = controlled;

        // the native inventory (vItemManager) and the embedded UI canvases
        // (inventory, HUD) belong to the LOCAL player only. They stay off
        // here; CODLoadout enables them for the owner after it has written
        // the loadout into the native start-items.
        if (!controlled)
        {
            var itemManager = GetComponent<Invector.vItemManager.vItemManager>();
            if (itemManager != null) itemManager.enabled = false;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                canvas.gameObject.SetActive(false);
        }
    }
}
