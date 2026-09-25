using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-right COD-style ammo counter for the LOCAL player.
/// Attached by Input_Handler (which only runs for the owning client).
/// Shows magazine / reserve, ∞ when infinite ammo is on, RELOADING state,
/// and hides itself in melee mode.
/// </summary>
public class AmmoHUD : MonoBehaviour
{
    static AmmoHUD instance;

    WeaponController weaponController;
    TextMeshProUGUI magText;
    TextMeshProUGUI reserveText;
    TextMeshProUGUI stateText;
    TextMeshProUGUI grenadeText;

    public static void Attach(WeaponController controller)
    {
        if (instance == null)
        {
            var go = new GameObject("AmmoHUD");
            instance = go.AddComponent<AmmoHUD>();
            instance.BuildUI();
        }
        instance.weaponController = controller;
    }

    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var plate = UITheme.Image("Plate", transform, new Color(0f, 0f, 0f, 0.35f));
        UITheme.Place(plate.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 36f), new Vector2(240f, 84f));

        magText = UITheme.Text("Mag", plate.transform, "30", 46, UITheme.TextMain, FontStyles.Bold,
            TextAlignmentOptions.Right);
        UITheme.Place(magText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(64f, 6f), new Vector2(120f, 56f));

        reserveText = UITheme.Text("Reserve", plate.transform, "/ 120", 22, UITheme.TextDim, FontStyles.Bold,
            TextAlignmentOptions.Left);
        UITheme.Place(reserveText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-16f, 0f), new Vector2(84f, 30f));

        stateText = UITheme.Text("State", plate.transform, "", 13, UITheme.Accent, FontStyles.Bold,
            TextAlignmentOptions.Right);
        UITheme.Place(stateText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-16f, 10f), new Vector2(200f, 18f));
        stateText.characterSpacing = 2;

        grenadeText = UITheme.Text("Grenades", plate.transform, "", 15, UITheme.TextDim, FontStyles.Bold,
            TextAlignmentOptions.Right);
        UITheme.Place(grenadeText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-16f, 22f), new Vector2(200f, 20f));
        grenadeText.characterSpacing = 1;
    }

    void Update()
    {
        if (weaponController == null)
        {
            // owner despawned (death/leave) — hide until re-attached
            if (magText != null) transform.GetChild(0).gameObject.SetActive(false);
            return;
        }

        Weapon weapon = weaponController.GETCurrentWeapon;
        bool show = weapon != null && !weaponController.MeleeMode;
        transform.GetChild(0).gameObject.SetActive(show);
        if (!show) return;

        magText.text = weapon.CurrentAmmo.ToString();
        magText.color = weapon.CurrentAmmo == 0 ? new Color(1f, 0.35f, 0.25f)
            : weapon.CurrentAmmo <= Mathf.Max(1, weapon.magazineSize / 4) ? UITheme.Accent
            : UITheme.TextMain;

        reserveText.text = GameConfig.InfiniteAmmo
            ? "/ \u221E"
            : "/ " + (weapon.ReserveMags * weapon.magazineSize);

        stateText.text = weapon.Reloading ? "RELOADING..." : "";

        var thrower = GrenadeThrower.Local;
        grenadeText.text = thrower == null ? ""
            : GameConfig.InfiniteAmmo ? "FRAG x \u221E"
            : "FRAG x " + thrower.Remaining;
    }
}
