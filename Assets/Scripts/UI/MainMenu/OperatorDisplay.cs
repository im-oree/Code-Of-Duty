using UnityEngine;

/// <summary>
/// The live operator model in the main menu: a display-only clone of the game
/// player prefab (all gameplay/network components stripped), tinted with the
/// selected skin, holding the selected primary weapon, breathing idle motion.
/// Changing operator or loadout in the menu updates it instantly.
/// </summary>
public class OperatorDisplay : MonoBehaviour
{
    GameObject model;
    Animator animator;
    Transform rightHand;
    GameObject handWeapon;
    float idleSeed;
    Quaternion baseRotation = Quaternion.identity;
    float groundedLocalY;

    public static OperatorDisplay Create(Transform parent, Vector3 position, Quaternion rotation)
    {
        var holder = new GameObject("OperatorDisplay");
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(position, rotation);

        var display = holder.AddComponent<OperatorDisplay>();
        display.idleSeed = Random.value * 10f;
        display.BuildModel();
        display.RefreshSkin();
        display.RefreshWeapon();
        return display;
    }

    #region Model

    void BuildModel()
    {
        GameObject prefab = ResolvePlayerPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("OperatorDisplay: no player prefab found for the menu preview");
            return;
        }

        // instantiate deactivated so no Awake/OnEnable runs before stripping
        bool wasActive = gameObject.activeSelf;
        gameObject.SetActive(false);
        model = Instantiate(prefab, transform, false);
        model.name = "OperatorModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        StripToDisplayOnly(model);
        gameObject.SetActive(wasActive);

        animator = model.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            rightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            // gun-hold pose layer used for remote players in matches
            animator.Play("GunPickUp", 1, 0f);
        }

        if (rightHand == null) rightHand = FindBoneByName(model.transform, "hand_r", "righthand", "hand.r");

        GroundModel();
        baseRotation = transform.localRotation;
    }

    /// <summary>Drops the model so the lowest visible point sits exactly on y = 0 (no floating).</summary>
    void GroundModel()
    {
        if (model == null) return;
        var renderers = model.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;

        float minY = float.MaxValue;
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            minY = Mathf.Min(minY, r.bounds.min.y);
        }
        if (minY == float.MaxValue) return;

        Vector3 p = model.transform.position;
        p.y -= minY - transform.position.y;
        model.transform.position = p;
        groundedLocalY = model.transform.localPosition.y;
    }

    static GameObject ResolvePlayerPrefab()
    {
        CODNetworkManager manager = CODNetworkManager.EnsureExists();
        if (manager != null && manager.gamePlayerPrefab != null)
            return manager.gamePlayerPrefab.gameObject;
        return null;
    }

    /// <summary>Removes every gameplay, physics and networking piece; keeps visuals + animator.</summary>
    static void StripToDisplayOnly(GameObject root)
    {
        // cameras & audio
        foreach (var cam in root.GetComponentsInChildren<Camera>(true)) DestroyImmediate(cam.gameObject);
        foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);

        // all scripts (includes FishNet NetworkObject/NetworkBehaviours) — multi-pass for RequireComponent chains
        for (int pass = 0; pass < 6; pass++)
        {
            var scripts = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (scripts.Length == 0) break;
            foreach (var script in scripts)
            {
                if (script == null) continue;
                try { DestroyImmediate(script); } catch { /* dependency, next pass */ }
            }
        }

        foreach (var joint in root.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint);
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        foreach (var col in root.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
        foreach (var cc in root.GetComponentsInChildren<CharacterController>(true)) DestroyImmediate(cc);
    }

    static Transform FindBoneByName(Transform root, params string[] names)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string lower = child.name.ToLowerInvariant().Replace(" ", "");
            foreach (string name in names)
            {
                if (lower.Contains(name)) return child;
            }
        }
        return null;
    }

    #endregion

    #region Live refresh (called by the menu)

    public void RefreshSkin()
    {
        if (model == null) return;
        var skin = CharacterSkinLibrary.Instance?.GetByIndex(PlayerAppearance.SavedSkinIndex);
        if (skin != null) PlayerAppearance.ApplySkinTo(model, skin);
    }

    public void RefreshWeapon()
    {
        if (model == null) return;

        // selected primary = first id in the saved loadout (fallback: first rifle in DB)
        string primaryId = null;
        string saved = PlayerLoadout.SavedLoadout;
        if (!string.IsNullOrEmpty(saved)) primaryId = saved.Split(',')[0].Trim();

        var database = WeaponDatabase.Instance;
        if (database == null) return;

        GameObject prefab = database.GetPrefab(primaryId);
        if (prefab == null)
        {
            var rifles = database.GetBySlotType(Weapon.SlotType.rifle);
            if (rifles.Count > 0) prefab = rifles[0].prefab;
        }
        if (prefab == null) return;

        if (handWeapon != null)
        {
            if (handWeapon.name == prefab.name) return; // already showing it
            DestroyImmediate(handWeapon);
        }

        handWeapon = Instantiate(prefab);
        handWeapon.name = prefab.name;

        foreach (var script in handWeapon.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script is Weapon || script is WeaponPoint) continue; // needed for grip alignment
            try { DestroyImmediate(script); } catch { }
        }
        foreach (var body in handWeapon.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        foreach (var col in handWeapon.GetComponentsInChildren<Collider>(true)) col.enabled = false;

        AttachToHand(handWeapon);
        HideHolsteredDuplicate(prefab.name);
    }

    void AttachToHand(GameObject weapon)
    {
        if (rightHand == null)
        {
            // no hand bone: float it beside the operator
            weapon.transform.SetParent(transform, false);
            weapon.transform.localPosition = new Vector3(0.4f, 1.1f, 0.1f);
            return;
        }

        weapon.transform.SetParent(rightHand, false);

        // align the weapon's authored right-hand grip point onto the palm
        Transform grip = null;
        foreach (var point in weapon.GetComponentsInChildren<WeaponPoint>(true))
        {
            if (point.pointType == WeaponPoint.PointType.RightHandDefault) { grip = point.transform; break; }
        }

        if (grip != null)
        {
            Quaternion gripLocalRot = Quaternion.Inverse(weapon.transform.rotation) * grip.rotation;
            weapon.transform.localRotation = Quaternion.Inverse(gripLocalRot);
            Vector3 gripLocalPos = weapon.transform.InverseTransformPoint(grip.position);
            weapon.transform.localPosition = -(weapon.transform.localRotation * gripLocalPos);
        }
        else
        {
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
        }
    }

    void HideHolsteredDuplicate(string weaponName)
    {
        if (model == null) return;

        // the display model's scripts are stripped, so find slot rigs by NAME
        // and hide every baked gun — the operator only holds the loadout gun
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.EndsWith("SlotRig")) continue;
            foreach (Transform holstered in t)
                holstered.gameObject.SetActive(false);
        }
    }

    #endregion

    void Update()
    {
        // subtle breathing / weight-shift so the operator feels alive even if
        // the animator idle is static. ABSOLUTE offsets from the base pose —
        // never accumulated — so the facing can never drift over time.
        float t = Time.time + idleSeed;
        transform.localRotation = baseRotation * Quaternion.Euler(0f, Mathf.Sin(t * 0.22f) * 1.5f, 0f);
        if (model != null)
        {
            Vector3 p = model.transform.localPosition;
            p.y = groundedLocalY + Mathf.Sin(t * 1.1f) * 0.004f;
            model.transform.localPosition = p;
        }
    }
}
