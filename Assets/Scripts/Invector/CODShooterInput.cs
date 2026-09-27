using Invector.vCharacterController;
using UnityEngine;

/// <summary>
/// Code Of Duty player input — the Invector shooter/melee input with:
///  • all bindings routed through our rebindable <see cref="InputBindings"/>
///  • native first person view toggling (V by default) using the camera's
///    built-in "FirstPerson" state (see vThirdPersonCamera FP integration)
///  • loadout slot switching (primary / secondary / melee)
/// The component starts disabled on networked prefabs; CODInvectorPlayer
/// enables it for the owning client only.
/// </summary>
public class CODShooterInput : Invector.vCharacterController.vShooterMeleeInput
{
    [Header("Code Of Duty")]
    [Tooltip("Camera state used for first person view (must exist in the camera state list)")]
    public string firstPersonState = "FirstPerson";
    [Tooltip("Camera state used for third person view")]
    public string thirdPersonState = "Default";
    [Tooltip("Start in first person when the saved preference says so")]
    public bool restoreViewFromPrefs = true;

    public const string ViewModePref = "ViewMode"; // 0 = TPS, 1 = FPS

    /// <summary>True while the player is using the first person view.</summary>
    public bool IsFirstPerson { get; private set; }

    CODFirstPersonBody fpBody;
    Invector.vCharacterController.vHeadTrack headTrack;
    CODLoadoutEquipper loadout;
    bool fpStateApplied;

    protected override void Start()
    {
        base.Start();

        fpBody = GetComponent<CODFirstPersonBody>();
        headTrack = GetComponent<Invector.vCharacterController.vHeadTrack>();
        loadout = GetComponent<CODLoadoutEquipper>();

        ApplyBindings();

        if (restoreViewFromPrefs && PlayerPrefs.GetInt(ViewModePref, 0) == 1)
        {
            SetFirstPerson(true);
        }
    }

    /// <summary>
    /// Routes our rebindable InputBindings into Invector's GenericInput slots.
    /// Call again after the player rebinds keys in the settings menu.
    /// </summary>
    public virtual void ApplyBindings()
    {
        // locomotion
        sprintInput.keyboard = InputBindings.Get("sprint").ToString();
        jumpInput.keyboard = InputBindings.Get("jump").ToString();
        crouchInput.keyboard = InputBindings.Get("crouch").ToString();
        rollInput.useInput = false;               // CoD-style: no combat roll
        strafeInput.useInput = false;             // strafe is driven by aim/FP

        // shooter
        shotInput.keyboard = InputBindings.Get("fire").ToString();
        aimInput.keyboard = InputBindings.Get("aim").ToString();
        reloadInput.keyboard = InputBindings.Get("reload").ToString();
        switchCameraSideInput.useInput = false;   // Tab is the scoreboard

        // melee
        weakAttackInput.keyboard = InputBindings.Get("meleeAttack").ToString();
        strongAttackInput.useInput = false;
        blockInput.useInput = false;

        // parachute add-on (part of the standard COD character setup)
        var parachute = GetComponentInChildren<vParachuteController>(true);
        if (parachute != null)
        {
            parachute.openCloseParachute.keyboard = InputBindings.Get("parachute").ToString();
        }
    }

    public override void InputHandle()
    {
        base.InputHandle();

        if (lockInput || cc == null || cc.isDead)
        {
            return;
        }

        // first person toggle
        if (InputBindings.Down("viewToggle"))
        {
            SetFirstPerson(!IsFirstPerson);
        }

        // loadout slots
        if (loadout != null)
        {
            if (InputBindings.Down("weapon1")) loadout.SelectSlot(0);
            else if (InputBindings.Down("weapon2")) loadout.SelectSlot(1);
            else if (InputBindings.Down("melee")) loadout.SelectSlot(2);
        }

        SyncFirstPersonState();
    }

    /// <summary>Switch between first and third person view.</summary>
    public virtual void SetFirstPerson(bool value)
    {
        IsFirstPerson = value;
        PlayerPrefs.SetInt(ViewModePref, value ? 1 : 0);

        if (tpCamera != null)
        {
            ChangeCameraState(value ? firstPersonState : thirdPersonState, true);
        }
    }

    /// <summary>
    /// Keeps character-side FP behaviour in sync with what the camera is
    /// actually doing (states blend over a few frames).
    /// </summary>
    protected virtual void SyncFirstPersonState()
    {
        bool fpActive = tpCamera != null && tpCamera.isFirstPersonActive;
        if (fpActive == fpStateApplied)
        {
            return;
        }

        fpStateApplied = fpActive;

        // rotate the body with the camera and disable head-track wander
        SetStrafeLocomotion(fpActive || IsAiming);
        if (headTrack != null) headTrack.enabled = !fpActive;
        if (fpBody != null) fpBody.SetFirstPerson(fpActive);
    }

    public override void UpdateCameraStates()
    {
        base.UpdateCameraStates();

        // if the camera was found after Start (network spawn order), make sure
        // the saved view preference is applied once
        if (IsFirstPerson && tpCamera != null && !tpCamera.isFirstPersonActive && !fpStateApplied)
        {
            ChangeCameraState(firstPersonState, true);
        }
    }
}
