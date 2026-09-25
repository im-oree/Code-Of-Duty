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
        TryShoot();

        bodySlope_Handler.setInput(-Input.GetAxisRaw("Slope"));

        //bodyTiltInSprint.SetMouseXMove(Input.GetAxis("Mouse X"));

        // 2-weapon loadout: 1 = primary, 2 = secondary, 3 = melee (unarmed)
        if (Input.GetKeyDown(KeyCode.Alpha1))
            weaponController.ToChange(1);
        if (Input.GetKeyDown(KeyCode.Alpha2))
            weaponController.ToChange(2);
        if (Input.GetKeyDown(KeyCode.Alpha3))
            weaponController.SetMelee(!weaponController.MeleeMode);


        if (Input.GetKeyDown(KeyCode.F) && weaponPickUp != null)
        {
            weaponPickUp.PickupCheck();
        }

        if (Input.GetMouseButtonDown(1))
        {
            //cameraSwitcher.AimViewChange();
            weaponSightHandler.AimViewChange();
        }
        if (Input.GetMouseButtonDown(2))
        {
            //cameraSwitcher.AimViewChange();
            weaponSightHandler.AimSightChange();
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            cameraSwitcher.ViewChange();
        }

        cameraController.SetCameraRotation(Input.GetAxis("Mouse Y") * -sensitivity, Input.GetAxis("Mouse X") * sensitivity);
    }


    WeaponMovementPose movementPose;

    void TryShoot()
    {
        if (weaponController.MeleeMode)
        {
            if (Input.GetMouseButtonDown(0)) TryMelee();
            return;
        }

        // COD rule: no firing mid-sprint (configurable)
        if (movementPose == null) movementPose = weaponController.GetComponent<WeaponMovementPose>();
        if (movementPose != null && movementPose.IsSprinting && !movementPose.canShootWhileSprinting)
            return;

        Weapon current = weaponController.GETCurrentWeapon;
        if (current == null) return;

        bool singleshoot = current.singleShoot;
        if (singleshoot && Input.GetMouseButtonDown(0))
        {
            weaponController.StartShoot();
        }
        else if (!singleshoot && Input.GetMouseButton(0))
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
