using UnityEngine;
using CodeOfDuty.Input;

/// <summary>
/// Weapon sway: the gun lags behind the view, so whipping the camera around throws the muzzle
/// off-axis before it settles. Driven by this character's look intent rather than by the mouse,
/// so a bot's weapon sways exactly like a player's instead of sitting unnaturally rigid.
/// </summary>
public class ViewingResistance : MonoBehaviour
{
    public WeaponController weaponController;
    public EventsCenter eventsCenter;
    public Transform pivot;
    public float resistanceForce;
    public float resistanceSmoothing;

    CharacterInput characterInput;

    private void Awake()
    {
        // Keep the component safe on scene-spawned characters and older prefabs where one
        // reference was not serialized. These are cosmetic references, so the weapon rig should
        // degrade gracefully instead of taking down the animation event chain.
        if (weaponController == null) weaponController = GetComponentInParent<WeaponController>();
        if (eventsCenter == null) eventsCenter = GetComponentInParent<EventsCenter>();
        if (pivot == null) pivot = transform;
    }

    private void OnEnable()
    {
        if (eventsCenter != null) eventsCenter.OnWeaponChange += WeaponChangeCheck;
        WeaponChangeCheck(false);
    }
    private void OnDisable()
    {
        if (eventsCenter != null) eventsCenter.OnWeaponChange -= WeaponChangeCheck;
    }

    void WeaponChangeCheck(bool changing)
    {
        if (changing) return;

        // The slot can legitimately be empty for a frame around a loadout swap.
        var weapon = weaponController != null ? weaponController.GETCurrentWeapon : null;
        if (weapon == null) return;

        resistanceForce = weapon.resistanceForce;
        resistanceSmoothing = weapon.resistanceSmoothing;
    }

    private void Update()
    {
        if (characterInput == null) characterInput = CharacterInput.For(this);

        Vector2 look = characterInput.Look;

        if (pivot == null) return;

        pivot.localRotation = Quaternion.Lerp(
            pivot.localRotation,
            Quaternion.Euler(-look.y * resistanceForce, look.x * resistanceForce, 0f),
            Mathf.Clamp01(resistanceSmoothing * Time.deltaTime));
    }
}
