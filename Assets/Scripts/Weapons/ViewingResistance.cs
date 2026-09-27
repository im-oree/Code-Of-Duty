using UnityEngine;
using CodeOfDuty.Input;

/// <summary>
/// Weapon sway: the gun lags behind the view, so whipping the camera around throws the muzzle
/// off-axis before it settles. Driven by this character's look intent rather than by the mouse,
/// so a bot's weapon sways exactly like a player's instead of sitting unnaturally rigid.
/// </summary>
public class ViewingResistance : MonoBehaviour, ILocalOnly
{
    public WeaponController weaponController;
    public EventsCenter eventsCenter;
    public Transform pivot;
    public float resistanceForce;
    public float resistanceSmoothing;

    CharacterInput characterInput;

    private void OnEnable()
    {
        eventsCenter.OnWeaponChange += WeaponChangeCheck;
    }
    private void OnDisable()
    {
        eventsCenter.OnWeaponChange -= WeaponChangeCheck;
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

        pivot.localRotation = Quaternion.Lerp(
            pivot.localRotation,
            Quaternion.Euler(-look.y * resistanceForce, look.x * resistanceForce, 0f),
            resistanceSmoothing * Time.deltaTime);
    }
}
