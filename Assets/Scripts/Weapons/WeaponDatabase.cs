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
    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public GameObject prefab;
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

    /// <summary>All weapons whose Weapon.slotType matches (for the loadout menu).</summary>
    public List<Entry> GetBySlotType(Weapon.SlotType slotType)
    {
        var result = new List<Entry>();
        foreach (var entry in weapons)
        {
            if (entry?.prefab == null) continue;
            var weapon = entry.prefab.GetComponent<Weapon>();
            if (weapon != null && weapon.slotType == slotType) result.Add(entry);
        }
        return result;
    }
}
