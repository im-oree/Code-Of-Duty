using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Input_Handler : MonoBehaviour
{
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private WeaponPickup weaponPickUp;
    [SerializeField] private BodySlope_Handler bodySlope_Handler;
    [SerializeField] private CameraSwitcher cameraSwitcher;
    [SerializeField] private BodyTiltInSprint bodyTiltInSprint;
    [SerializeField] private WeaponSight_hangler weaponSightHandler;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;
    [SerializeField] float maxViewAngle = 80;
    [SerializeField] private float sensitivity = 150;

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

        TryShoot();

        bodySlope_Handler.setInput(-Input.GetAxisRaw("Slope"));

        //bodyTiltInSprint.SetMouseXMove(Input.GetAxis("Mouse X"));

        // 2-weapon loadout: 1 = primary, 2 = secondary, 3 = melee (unarmed)
        if (InputBindings.Down("weapon1"))
            weaponController.ToChange(1);
        if (InputBindings.Down("weapon2"))
            weaponController.ToChange(2);
        if (InputBindings.Down("melee"))
            weaponController.SetMelee(!weaponController.MeleeMode);


        if (InputBindings.Down("interact") && weaponPickUp != null)
        {
            weaponPickUp.PickupCheck();
        }

        if (InputBindings.Down("reload") && !weaponController.MeleeMode)
        {
            var current = weaponController.GETCurrentWeapon;
            if (current != null) current.StartReload();
        }

        if (InputBindings.Down("aim"))
        {
            //cameraSwitcher.AimViewChange();
            weaponSightHandler.AimViewChange();
        }
        if (InputBindings.Down("sightSwitch"))
        {
            //cameraSwitcher.AimViewChange();
            weaponSightHandler.AimSightChange();
        }

        if (InputBindings.Down("viewToggle"))
        {
            cameraSwitcher.ViewChange();
        }

        float sens = sensitivity * GameSettings.MouseSensitivity;
        cameraController.SetCameraRotation(Input.GetAxis("Mouse Y") * -sens, Input.GetAxis("Mouse X") * sens);
    }


    WeaponMovementPose movementPose;
    bool hudAttached;

    void TryShoot()
    {
        if (weaponController.MeleeMode)
        {
            if (InputBindings.Down("fire")) TryMelee();
            return;
        }

        // COD rule: no firing mid-sprint (configurable)
        if (movementPose == null) movementPose = weaponController.GetComponent<WeaponMovementPose>();
        if (movementPose != null && movementPose.IsSprinting &&
            !movementPose.canShootWhileSprinting && !InputBindings.FireWhileSprinting)
            return;

        Weapon current = weaponController.GETCurrentWeapon;
        if (current == null) return;

        bool singleshoot = current.singleShoot;
        if (singleshoot && InputBindings.Down("fire"))
        {
            weaponController.StartShoot();
        }
        else if (!singleshoot && InputBindings.Held("fire"))
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
