using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Invector.vItemManager;
using UnityEngine;

/// <summary>
/// COD bridge into Invector's NATIVE item/equipment system (vItemManager).
/// We do not re-implement weapons or slots — the inventory template already
/// gives us equip areas (2+ weapon slots, extensible), grenades via the
/// ThrowManager equip area, item icons, hand handlers and holsters. This
/// component only:
///
///  • owner: converts the saved menu loadout (WeaponDatabase ids) into
///    vItemManager.startItems (weapon + its ammo, auto-equipped) and then
///    enables the item manager — everything after that is stock Invector
///  • network: replicates which item is in each hand so REMOTE players see
///    the correct weapon (Invector has no netcode of its own); remote copies
///    never run the inventory — they just mirror the equipped item's model
///    into the same native equip handler
///
/// CODInvectorPlayer disables vItemManager + the embedded UI canvases in
/// Awake; this component re-enables them for the owning client only.
/// </summary>
[RequireComponent(typeof(vItemManager))]
public class CODLoadout : NetworkBehaviour
{
    public const string LoadoutPref = "Loadout";
    public const string DefaultLoadout = "AssaultRifle,Handgun,ShortKatana,FragGrenade";

    [Tooltip("Item list shared with the vItemManager (used to resolve mirrored weapons)")]
    public vItemListData itemList;

    public readonly SyncVar<string> loadoutCsv = new SyncVar<string>(string.Empty);
    /// <summary>Item id currently in the right hand (-1 = unarmed) — mirrored on remote copies.</summary>
    public readonly SyncVar<int> rightItemId = new SyncVar<int>(-1);
    /// <summary>Item id currently in the left hand (-1 = none).</summary>
    public readonly SyncVar<int> leftItemId = new SyncVar<int>(-1);

    vItemManager itemManager;
    Invector.vShooter.vShooterManager shooterManager;
    Invector.vMelee.vMeleeManager meleeManager;
    Animator animator;

    GameObject rightMirror, leftMirror;
    float pollTimer;
    bool configured;

    void Awake()
    {
        itemManager = GetComponent<vItemManager>();
        shooterManager = GetComponent<Invector.vShooter.vShooterManager>();
        meleeManager = GetComponent<Invector.vMelee.vMeleeManager>();
        animator = GetComponent<Animator>();
        if (itemList == null) itemList = itemManager.itemListData;

        rightItemId.OnChange += OnRightChanged;
        leftItemId.OnChange += OnLeftChanged;
    }

    void OnDestroy()
    {
        rightItemId.OnChange -= OnRightChanged;
        leftItemId.OnChange -= OnLeftChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (base.IsOwner)
        {
            string csv = PlayerPrefs.GetString(LoadoutPref, DefaultLoadout);
            ServerSetLoadout(csv);
            ConfigureAndEnable(csv);
        }
        else
        {
            // late joiner: apply whatever is already equipped
            OnRightChanged(-1, rightItemId.Value, false);
            OnLeftChanged(-1, leftItemId.Value, false);
        }
    }

    void Start()
    {
        // offline scene (no network): behave like the owning player
        if (!base.IsSpawned && !configured)
        {
            ConfigureAndEnable(PlayerPrefs.GetString(LoadoutPref, DefaultLoadout));
        }
    }

    [ServerRpc]
    void ServerSetLoadout(string csv) => loadoutCsv.Value = csv ?? string.Empty;

    [ServerRpc]
    void ServerSetHands(int right, int left)
    {
        rightItemId.Value = right;
        leftItemId.Value = left;
    }

    /// <summary>
    /// Owner/offline: feed the menu loadout into the NATIVE start-items flow
    /// and let Invector do everything else (equip, slots, UI, icons, ammo).
    /// </summary>
    public void ConfigureAndEnable(string csv)
    {
        if (configured || itemManager == null) return;
        configured = true;

        var db = WeaponDatabase.Instance;
        itemManager.startItems.Clear();

        bool equippedFirst = false;
        foreach (string raw in (csv ?? string.Empty).Split(','))
        {
            string id = raw.Trim();
            var entry = db != null ? db.Get(id) : null;
            if (entry == null || entry.itemId < 0) continue;

            itemManager.startItems.Add(new ItemReference(entry.itemId)
            {
                amount = Mathf.Max(1, entry.itemAmount),
                autoEquip = !equippedFirst && entry.slotType != WeaponDatabase.SlotType.grenade,
                addToEquipArea = true,
                indexArea = entry.equipAreaIndex
            });
            if (entry.slotType != WeaponDatabase.SlotType.grenade) equippedFirst = true;

            if (entry.ammoItemId >= 0)
            {
                itemManager.startItems.Add(new ItemReference(entry.ammoItemId)
                {
                    amount = Mathf.Max(1, entry.ammoAmount),
                    autoEquip = false,
                    addToEquipArea = false
                });
            }
        }

        // native flow takes over from here
        itemManager.enabled = true;
        foreach (var canvas in GetComponentsInChildren<Canvas>(true))
            canvas.gameObject.SetActive(true);
    }

    void Update()
    {
        // owner: publish what is actually in the hands (equip/holster/switch)
        if (!base.IsSpawned || !base.IsOwner) return;

        pollTimer += Time.unscaledDeltaTime;
        if (pollTimer < 0.25f) return;
        pollTimer = 0f;

        int right = ResolveItemId(CurrentRightWeaponName());
        int left = ResolveItemId(CurrentLeftWeaponName());
        if (right != rightItemId.Value || left != leftItemId.Value)
        {
            ServerSetHands(right, left);
        }
    }

    string CurrentRightWeaponName()
    {
        if (shooterManager != null && shooterManager.rWeapon != null && shooterManager.rWeapon.gameObject.activeInHierarchy)
            return shooterManager.rWeapon.gameObject.name;
        if (meleeManager != null && meleeManager.rightWeapon != null && meleeManager.rightWeapon.gameObject.activeInHierarchy)
            return meleeManager.rightWeapon.gameObject.name;
        return null;
    }

    string CurrentLeftWeaponName()
    {
        if (shooterManager != null && shooterManager.lWeapon != null && shooterManager.lWeapon.gameObject.activeInHierarchy)
            return shooterManager.lWeapon.gameObject.name;
        if (meleeManager != null && meleeManager.leftWeapon != null && meleeManager.leftWeapon.gameObject.activeInHierarchy)
            return meleeManager.leftWeapon.gameObject.name;
        return null;
    }

    int ResolveItemId(string weaponObjectName)
    {
        if (string.IsNullOrEmpty(weaponObjectName) || itemList == null) return -1;
        string clean = weaponObjectName.Replace("(Clone)", "").Trim();
        foreach (var item in itemList.items)
        {
            if (item != null && item.originalObject != null && item.originalObject.name == clean)
                return item.id;
        }
        return -1;
    }

    // ------------------------- remote mirroring -------------------------- //

    void OnRightChanged(int _, int id, bool __)
    {
        if (base.IsOwner) return;
        Mirror(ref rightMirror, id, true);
    }

    void OnLeftChanged(int _, int id, bool __)
    {
        if (base.IsOwner) return;
        Mirror(ref leftMirror, id, false);
    }

    void Mirror(ref GameObject current, int id, bool rightHand)
    {
        if (current != null) Destroy(current);
        current = null;
        if (id < 0 || itemList == null) return;

        vItem item = itemList.items.Find(i => i != null && i.id == id);
        if (item == null || item.originalObject == null) return;

        Transform handler = FindHandler(rightHand);
        if (handler == null) return;

        current = Instantiate(item.originalObject, handler);
        current.transform.localPosition = Vector3.zero;
        current.transform.localRotation = Quaternion.identity;

        // display only on remote copies
        foreach (var col in current.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in current.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
    }

    /// <summary>The native Invector equip handler under the hand bone.</summary>
    Transform FindHandler(bool rightHand)
    {
        Transform hand = animator != null && animator.isHuman
            ? animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand)
            : null;
        if (hand == null) return null;

        foreach (var t in hand.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "defaultHandler") return t;
        }
        return hand;
    }
}
