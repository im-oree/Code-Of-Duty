using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Networked, fully dynamic weapon loadout (FishNet). On spawn, the guns chosen
/// in the main menu are instantiated into the player's weapon slot rigs on every
/// client (and the server), replacing the prefab's default guns.
///
/// Nothing is hardcoded:
/// - slot count comes from WeaponController.slots (2 slots later? still works)
/// - weapons come from the WeaponDatabase by id
/// - unknown/empty ids keep the slot's default gun
/// </summary>
public class PlayerLoadout : NetworkBehaviour
{
    public const string PrefKey = "Loadout";

    [SerializeField] private WeaponController weaponController;

    readonly SyncVar<string> loadoutCsv = new SyncVar<string>("");

    string appliedCsv;

    public static string SavedLoadout
    {
        get => PlayerPrefs.GetString(PrefKey, "");
        set => PlayerPrefs.SetString(PrefKey, value);
    }

    void Awake()
    {
        if (weaponController == null)
            weaponController = GetComponentInChildren<WeaponController>(true);

        loadoutCsv.OnChange += OnLoadoutSynced;
    }

    void OnDestroy()
    {
        loadoutCsv.OnChange -= OnLoadoutSynced;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            string saved = SavedLoadout;
            if (!string.IsNullOrEmpty(saved)) ServerSetLoadout(saved);
        }

        // late joiners: SyncVar already carries the value
        ApplyLoadout(loadoutCsv.Value);
    }

    [ServerRpc]
    void ServerSetLoadout(string csv)
    {
        if (string.IsNullOrEmpty(csv) || csv.Length > 256) return;

        loadoutCsv.Value = csv;

        // dedicated server needs the swap too (hitbox/ragdoll consistency)
        if (!InstanceFinder.IsClientStarted) ApplyLoadout(csv);
    }

    void OnLoadoutSynced(string _, string csv, bool asServer)
    {
        if (!asServer) ApplyLoadout(csv);
    }

    void ApplyLoadout(string csv)
    {
        if (string.IsNullOrEmpty(csv) || csv == appliedCsv) return;
        if (weaponController == null || weaponController.slots == null) return;

        var database = WeaponDatabase.Instance;
        if (database == null) return;

        string[] ids = csv.Split(',');

        for (int slotIndex = 0; slotIndex < weaponController.slots.Length && slotIndex < ids.Length; slotIndex++)
        {
            string id = ids[slotIndex].Trim();
            if (string.IsNullOrEmpty(id) || id == "-") continue; // keep default gun

            GameObject prefab = database.GetPrefab(id);
            if (prefab == null) continue;

            ReplaceSlotWeapon(weaponController.slots[slotIndex], prefab);
        }

        appliedCsv = csv;
    }

    public static void ReplaceSlotWeapon(WeaponSlotRig slot, GameObject weaponPrefab)
    {
        if (slot == null || weaponPrefab == null) return;

        Weapon current = slot.GetComponentInChildren<Weapon>(true);
        if (current != null)
        {
            // already the right gun?
            if (current.name.StartsWith(weaponPrefab.name)) return;

            Transform old = current.transform;
            old.gameObject.SetActive(false);
            old.SetParent(null);
            Destroy(old.gameObject);
        }

        GameObject weapon = Instantiate(weaponPrefab, slot.transform, false);
        weapon.name = weaponPrefab.name;
        weapon.transform.SetSiblingIndex(0); // systems expect the gun as first child
    }
}
