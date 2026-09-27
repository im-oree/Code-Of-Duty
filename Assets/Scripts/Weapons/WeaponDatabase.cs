using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry of every weapon in the game. Lives in Resources/Weapons so
/// nothing is hardcoded: menus, loadouts and spawning reference weapons by id.
/// Add new guns here (the Weapon Setup Wizard does it automatically) and every
/// system — loadout menu, spawning, sync — picks them up.
/// </summary>
[CreateAssetMenu(menuName = "COD/Weapon Database", fileName = "WeaponDatabase")]
public class WeaponDatabase : ScriptableObject
{
    /// <summary>Loadout slot category of a weapon.</summary>
    public enum SlotType
    {
        rifle = 1,
        smg = 2,
        pistol = 3,
        melee = 4,
        grenade = 5
    }

    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        [Tooltip("Invector shooter weapon prefab (vShooterWeapon), usable on any character")]
        public GameObject prefab;
        public SlotType slotType = SlotType.rifle;

        [Header("Invector item integration (native vItemManager)")]
        [Tooltip("Item id inside the vItemListData (ShooterMelee list). -1 = not an inventory item")]
        public int itemId = -1;
        [Tooltip("How many of the item to give (grenades > 1)")]
        public int itemAmount = 1;
        [Tooltip("Equip area index inside the Invector inventory (0 = weapons, grenades use the throw area)")]
        public int equipAreaIndex = 0;
        [Tooltip("Ammo item id to give alongside this weapon (-1 = none)")]
        public int ammoItemId = -1;
        public int ammoAmount = 60;
        [Tooltip("Invector item icon shown in the loadout menu")]
        public Sprite icon;
    }

    public List<Entry> weapons = new List<Entry>();

    static WeaponDatabase cached;

    public static WeaponDatabase Instance
    {
        get
        {
            if (cached == null) cached = Resources.Load<WeaponDatabase>("Weapons/WeaponDatabase");
            return cached;
        }
    }

    public Entry Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != null && weapons[i].id == id) return weapons[i];
        }
        return null;
    }

    public GameObject GetPrefab(string id) => Get(id)?.prefab;

    /// <summary>All weapons of a slot category (for the loadout menu).</summary>
    public List<Entry> GetBySlotType(SlotType slotType)
    {
        var result = new List<Entry>();
        foreach (var entry in weapons)
        {
            if (entry?.prefab == null) continue;
            if (entry.slotType == slotType) result.Add(entry);
        }
        return result;
    }
}
