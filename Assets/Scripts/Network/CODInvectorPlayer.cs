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

    /// <summary>The locally controlled player, if any.</summary>
    public static CODInvectorPlayer Local { get; private set; }

    CODThirdPersonController controller;
    CODShooterInput input;
    Rigidbody body;

    void Awake()
    {
        controller = GetComponent<CODThirdPersonController>();
        input = GetComponent<CODShooterInput>();
        body = GetComponent<Rigidbody>();

        // stay inert until ownership is known (or offline fallback kicks in)
        SetControlled(false);
        StartCoroutine(OfflineFallback());
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
    }
}
