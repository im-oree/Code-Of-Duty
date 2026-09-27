using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Invector.vShooter;
using UnityEngine;

/// <summary>
/// Networked loadout for Invector characters. Reads the locally saved loadout
/// (WeaponDatabase ids, same PlayerPrefs format as the menu), replicates it,
/// and equips Invector shooter weapons into the character's right hand — on
/// every client, so remote players hold the correct guns.
///
/// Weapons are instantiated from WeaponDatabase prefabs (vShooterWeapon-based,
/// usable on ANY character) under the right-hand handler and registered with
/// vShooterManager. Slot 0/1 = primary/secondary, slot 2 = melee (bare hands
/// for now).
/// </summary>
[RequireComponent(typeof(vShooterManager))]
public class CODLoadoutEquipper : NetworkBehaviour
{
    public const string LoadoutPref = "Loadout";

    [Tooltip("Optional explicit equip point; found from the humanoid right hand when empty")]
    public Transform rightHandler;

    public readonly SyncVar<string> loadoutCsv = new SyncVar<string>(string.Empty);
    public readonly SyncVar<int> activeSlot = new SyncVar<int>(0);

    vShooterManager shooterManager;
    readonly List<GameObject> spawnedWeapons = new List<GameObject>();
    string appliedCsv;
    int appliedSlot = -1;

    void Awake()
    {
        shooterManager = GetComponent<vShooterManager>();
        loadoutCsv.OnChange += OnLoadoutChanged;
        activeSlot.OnChange += OnSlotChanged;
    }

    void OnDestroy()
    {
        loadoutCsv.OnChange -= OnLoadoutChanged;
        activeSlot.OnChange -= OnSlotChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (base.IsOwner)
        {
            ServerSetLoadout(PlayerPrefs.GetString(LoadoutPref, string.Empty));
        }

        // apply whatever is already synced (late join)
        ApplyLoadout(loadoutCsv.Value);
        ApplyActiveSlot(activeSlot.Value);
    }

    void Start()
    {
        // offline: no network state incoming — equip from local prefs
        if (!base.IsSpawned)
        {
            ApplyLoadout(PlayerPrefs.GetString(LoadoutPref, string.Empty));
            ApplyActiveSlot(0);
        }
    }

    /// <summary>Owner-side slot selection (bound to weapon1/weapon2/melee keys).</summary>
    public void SelectSlot(int slot)
    {
        if (!base.IsSpawned)
        {
            ApplyActiveSlot(slot);
            return;
        }
        if (base.IsOwner)
        {
            ServerSetSlot(slot);
        }
    }

    [ServerRpc]
    void ServerSetLoadout(string csv) => loadoutCsv.Value = csv ?? string.Empty;

    [ServerRpc]
    void ServerSetSlot(int slot) => activeSlot.Value = Mathf.Clamp(slot, 0, 2);

    void OnLoadoutChanged(string _, string next, bool __) => ApplyLoadout(next);
    void OnSlotChanged(int _, int next, bool __) => ApplyActiveSlot(next);

    // ------------------------------------------------------------------ //

    void ApplyLoadout(string csv)
    {
        if (csv == appliedCsv) return;
        appliedCsv = csv;

        foreach (GameObject go in spawnedWeapons)
        {
            if (go != null) Destroy(go);
        }
        spawnedWeapons.Clear();

        Transform handler = ResolveHandler();
        if (handler == null || WeaponDatabase.Instance == null) return;

        foreach (string id in (csv ?? string.Empty).Split(','))
        {
            string trimmed = id.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            GameObject prefab = WeaponDatabase.Instance.GetPrefab(trimmed);
            if (prefab == null) continue;

            GameObject weapon = Instantiate(prefab, handler);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localEulerAngles = Vector3.zero;
            weapon.SetActive(false);
            spawnedWeapons.Add(weapon);
        }

        appliedSlot = -1; // force re-equip
        ApplyActiveSlot(base.IsSpawned ? activeSlot.Value : 0);
    }

    void ApplyActiveSlot(int slot)
    {
        if (slot == appliedSlot) return;
        appliedSlot = slot;

        GameObject active = null;
        for (int i = 0; i < spawnedWeapons.Count; i++)
        {
            bool isActive = i == slot && spawnedWeapons[i] != null;
            if (spawnedWeapons[i] != null) spawnedWeapons[i].SetActive(isActive);
            if (isActive) active = spawnedWeapons[i];
        }

        if (shooterManager != null)
        {
            if (active != null && active.GetComponent<vShooterWeapon>() != null)
            {
                shooterManager.SetRightWeapon(active);
            }
            else
            {
                shooterManager.rWeapon = null; // melee slot / empty → bare hands
            }
        }

        // weapon changed → refresh first person body filter
        var fpBody = GetComponent<CODFirstPersonBody>();
        if (fpBody != null) fpBody.Refresh();
    }

    Transform ResolveHandler()
    {
        if (rightHandler != null) return rightHandler;

        var animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return null;

        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return null;

        // Invector convention: a "weaponHandler" child marks the grip point
        foreach (Transform child in hand)
        {
            if (child.name.ToLower().Contains("handler")) { rightHandler = child; return child; }
        }
        rightHandler = hand;
        return hand;
    }
}
