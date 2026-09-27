using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CodeOfDuty.Input;

/// <summary>
/// Turns this character's intent into weapon, camera and pose commands.
///
/// Reads <see cref="CharacterInput"/>, never a device, so a bot issues the same commands
/// through the same code. Note that <c>Input</c> here refers to the namespace
/// <c>CodeOfDuty.Input</c>; the legacy <c>UnityEngine.Input</c> class is deliberately not used
/// anywhere in this file.
/// </summary>
public class Input_Handler : MonoBehaviour
{
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private WeaponPickup weaponPickUp;
    [SerializeField] private BodySlope_Handler bodySlope_Handler;
    [SerializeField] private CameraSwitcher cameraSwitcher;
    [SerializeField] private BodyTiltInSprint bodyTiltInSprint;
    [SerializeField] private WeaponSight_hangler weaponSightHandler;

    [Header("Input")]
    [Tooltip("Resolved from this character if left empty.")]
    [SerializeField] private CharacterInput characterInput;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;
    [SerializeField] float maxViewAngle = 80;
    [SerializeField] private float sensitivity = 150;

    private void Awake()
    {
        if (characterInput == null) characterInput = CharacterInput.For(this);
    }

    private void Start()
    {
        weaponController.activeID = 1;
        weaponController.animator.Play("GunPickUp", 1);
    }

    void Update()
    {
        // pause menu swallows gameplay input (look stays frozen, no fire-through-UI)
        if (PauseMenu.IsOpen)
        {
            cameraController.SetCameraRotation(0f, 0f);
            return;
        }

        // local player HUD (live ownership check — never trust Start-time ownership)
        if (!hudAttached && NetOwnership.IsLocal(this))
        {
            AmmoHUD.Attach(weaponController);
            hudAttached = true;
        }

        var input = characterInput.Source;

        TryShoot(input);

        bodySlope_Handler.setInput(-input.Lean);

        // 2-weapon loadout: 1 = primary, 2 = secondary, 3 = melee (unarmed)
        if (input.Pressed(InputActionId.SlotPrimary))
            weaponController.ToChange(1);
        if (input.Pressed(InputActionId.SlotSecondary))
            weaponController.ToChange(2);
        if (input.Pressed(InputActionId.SlotMelee))
            weaponController.SetMelee(!weaponController.MeleeMode);

        if (input.Pressed(InputActionId.Interact) && weaponPickUp != null)
        {
            weaponPickUp.PickupCheck();
        }

        if (input.Pressed(InputActionId.Reload) && !weaponController.MeleeMode)
        {
            var current = weaponController.GETCurrentWeapon;
            if (current != null) current.StartReload();
        }

        if (input.Pressed(InputActionId.Aim))
        {
            weaponSightHandler.AimViewChange();
        }
        if (input.Pressed(InputActionId.SwitchSight))
        {
            weaponSightHandler.AimSightChange();
        }

        if (input.Pressed(InputActionId.ToggleView))
        {
            cameraSwitcher.ViewChange();
        }

        // Look arrives as a rate; the camera integrates it against delta time.
        float sens = sensitivity * GameSettings.MouseSensitivity;
        Vector2 look = input.Look;
        cameraController.SetCameraRotation(look.y * -sens, look.x * sens);
    }


    WeaponMovementPose movementPose;
    bool hudAttached;

    void TryShoot(IInputSource input)
    {
        if (weaponController.MeleeMode)
        {
            if (input.Pressed(InputActionId.Fire) || input.Pressed(InputActionId.Melee)) TryMelee();
            return;
        }

        // COD rule: no firing mid-sprint (configurable)
        if (movementPose == null) movementPose = weaponController.GetComponent<WeaponMovementPose>();
        if (movementPose != null && movementPose.IsSprinting &&
            !movementPose.canShootWhileSprinting && !InputBindings.FireWhileSprinting)
            return;

        // canShoot is false while a draw/holster animation owns the gun.
        if (!weaponController.canShoot) return;

        Weapon current = weaponController.GETCurrentWeapon;
        if (current == null) return;

        bool singleshoot = current.singleShoot;
        if (singleshoot && input.Pressed(InputActionId.Fire))
        {
            weaponController.StartShoot();
        }
        else if (!singleshoot && input.Held(InputActionId.Fire))
        {
            weaponController.StartShoot();
        }
    }

    float nextMeleeTime;

    void TryMelee()
    {
        if (Time.time < nextMeleeTime) return;
        nextMeleeTime = Time.time + 0.6f;

        Camera cam = Camera.main;
        if (cam == null) return;

        CameraShake.MeleeSwing(); // punch feedback

        var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 2.4f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.root == transform.root) continue; // own body

            var victim = hit.collider.GetComponentInParent<FishNet.Object.NetworkObject>();
            var self = GetComponentInParent<NetCMDs>();
            if (victim != null && self != null)
            {
                self.ServerDealDamage(victim, 45f, false, "Melee");
            }
            else
            {
                var health = hit.collider.GetComponentInParent<PlayerHealth>();
                if (health != null) health.SetDamage(45f);
            }
            break;
        }
    }
}
