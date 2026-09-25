using UnityEngine;

/// <summary>
/// Global gameplay configuration asset (Resources/GameConfig.asset).
/// Editable in the inspector AND from the COD &gt; Game Config editor window.
///
/// Server authority: when a match host wants to force a value, it sets the
/// static override (e.g. via a replicated match-rules payload later) — the
/// override always wins over the local asset.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "COD/Game Config")]
public class GameConfig : ScriptableObject
{
    [Header("Ammo")]
    [Tooltip("Weapons never consume ammo. Tick for testing; later this becomes a server-enforced match rule.")]
    public bool infiniteAmmo = true;

    [Tooltip("Full spare magazines a player carries per weapon (used when infinite ammo is OFF).")]
    public int reserveMagazines = 4;

    [Header("Grenades")]
    [Tooltip("Frag grenades carried per life (refilled on respawn).")]
    public int grenadesPerLife = 2;
    public float grenadeDamage = 115f;
    public float grenadeRadius = 6.5f;
    public float grenadeFuse = 3.5f;
    public float grenadeThrowForce = 16f;

    [Header("Player")]
    public int maxHealth = 100;
    public float respawnDelay = 3.5f;

    // ---------------------------------------------------------------- access

    static GameConfig instance;

    public static GameConfig Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<GameConfig>("GameConfig");
            if (instance == null) // safety net so gameplay never NREs
                instance = CreateInstance<GameConfig>();
            return instance;
        }
    }

    /// <summary>Server-pushed override; null = use the asset value.</summary>
    public static bool? ServerInfiniteAmmoOverride;

    public static bool InfiniteAmmo =>
        ServerInfiniteAmmoOverride ?? Instance.infiniteAmmo;
}
