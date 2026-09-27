using UnityEngine;

/// <summary>
/// The live operator model in the main menu: a display-only clone of the
/// selected character's prefab (all gameplay/network components stripped),
/// tinted with the character's variation color, holding the selected primary
/// weapon, breathing idle motion. Changing operator or loadout in the menu
/// updates it instantly. Characters come from the CharacterDatabase.
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
    string modelCharacterId;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>
    /// Resolves the authored preview already stored below <paramref name="parent"/>.
    /// The menu intentionally does not spawn an operator at runtime: an absent
    /// preview is a scene-authoring error and is reported instead of hidden by a
    /// generated replacement.
    /// </summary>
    public static OperatorDisplay EnsureUnder(Transform parent, Vector3 position, Quaternion rotation)
    {
        var existing = parent != null ? parent.GetComponentInChildren<OperatorDisplay>(true) : null;
        if (existing == null)
        {
            Debug.LogError("[OperatorDisplay] MenuStage has no authored OperatorDisplay.");
            return null;
        }

        existing.Adopt();
        return existing;
    }

    /// <summary>
    /// Wires up the model that is already in the scene.
    ///
    /// The menu operator is saved scene content — a real, editable GameObject
    /// with a skeleton and a SkinnedMeshRenderer, not something instantiated on
    /// play. What cannot be saved is the component references this class keeps
    /// (animator, hand bone), so those are re-resolved, and the skin and weapon
    /// are re-applied from current player data.
    /// </summary>
    public void Adopt()
    {
        if (model == null)
        {
            var found = transform.Find("OperatorModel");
            if (found != null) model = found.gameObject;
        }

        if (model == null)
        {
            Debug.LogError("[OperatorDisplay] StartMenu is missing the authored OperatorModel child.");
            return;
        }

        ConfigureAnimator();
        if (rightHand == null) rightHand = FindBoneByName(model.transform, "hand_r", "righthand", "hand.r");
        baseRotation = transform.localRotation;

        if (idleSeed <= 0f) idleSeed = Random.value * 10f;

        // show the character the player actually selected (database-driven);
        // the authored model is only the edit-mode stand-in
        var selected = CharacterDatabase.Instance != null ? CharacterDatabase.Instance.GetSelected() : null;
        if (selected != null && Application.isPlaying)
        {
            SetCharacter(selected.id);
            return;
        }

        RefreshSkin();
        RefreshWeapon();
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
        UnpackIfPrefabInstance(model); // edit-mode bake: components can't be stripped off a linked prefab instance
        model.name = "OperatorModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        StripToDisplayOnly(model);
        gameObject.SetActive(wasActive);

        ConfigureAnimator();

        if (rightHand == null) rightHand = FindBoneByName(model.transform, "hand_r", "righthand", "hand.r");

        GroundModel();
        baseRotation = transform.localRotation;
    }

    /// <summary>
    /// Points the model's Animator at the menu idle controller.
    ///
    /// Shared by the adopt and the build paths: the controller reference is a
    /// serialized field on the Animator and survives a scene save, but the
    /// fallback checks below depend on runtime state, so they have to run
    /// either way.
    /// </summary>
    void ConfigureAnimator()
    {
        if (model == null) return;

        animator = model.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            rightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            // the gameplay controller needs live movement params (otherwise it
            // sits in its falling/locomotion default) — the menu uses a
            // dedicated single-state idle controller instead: standing aim-idle, no SMBs.
            var originalController = animator.runtimeAnimatorController;
            var menuController = Resources.Load<RuntimeAnimatorController>("Character/MenuIdleAnimator");
            if (menuController != null)
            {
                animator.runtimeAnimatorController = menuController;
                animator.Update(0f);

                // T-pose defense: if the idle clip failed to bind (missing/renamed
                // sub-asset), fall back to the gameplay controller frozen grounded.
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                if (clips == null || clips.Length == 0 || clips[0].clip == null
                    || clips[0].clip.name.Contains("T-Pose"))
                {
                    Debug.LogWarning("OperatorDisplay: menu idle clip missing — falling back to gameplay controller");
                    animator.runtimeAnimatorController = originalController;
                    animator.SetBool("isGrounded", true);
                }
            }
            else
            {
                // fallback: freeze the gameplay controller into its grounded idle
                animator.SetBool("isGrounded", true);
                animator.Play("GunPickUp", 1, 0f);
            }
            animator.Update(0f);
        }
    }

    /// <summary>Drops the model so the lowest visible point sits exactly on y = 0 (no floating).</summary>
    void GroundModel()
    {
        if (model == null) return;

        // pose the skeleton first so bounds are real, not bind-pose garbage
        if (animator != null) animator.Update(0f);

        float minY = float.MaxValue;

        // prefer foot bones (exact), fall back to renderer bounds
        if (animator != null && animator.isHuman)
        {
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lf != null) minY = Mathf.Min(minY, lf.position.y - 0.08f);
            if (rf != null) minY = Mathf.Min(minY, rf.position.y - 0.08f);
        }

        if (minY == float.MaxValue)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer) continue;
                minY = Mathf.Min(minY, r.bounds.min.y);
            }
        }
        if (minY == float.MaxValue) return;

        // sanity-clamped shift: never teleport the model around
        float shift = Mathf.Clamp(minY - transform.position.y, -1f, 1f);
        Vector3 p = model.transform.position;
        p.y -= shift;
        model.transform.position = p;
        groundedLocalY = model.transform.localPosition.y;
    }

    static GameObject ResolvePlayerPrefab()
    {
        // selected character from the database; falls back to the default
        // player prefab so an empty database still shows something
        var entry = CharacterDatabase.Instance != null ? CharacterDatabase.Instance.GetSelected() : null;
        if (entry != null)
        {
            var prefab = CharacterDatabase.Instance.GetPrefab(entry.id);
            if (prefab != null) return prefab;
        }
        return CODNetworkManager.PlayerPrefabAsset;
    }

    /// <summary>
    /// Swaps the preview to another character from the database (menu card
    /// click). Replaces the current display model with a stripped clone of the
    /// character's prefab and applies its variation tint.
    /// </summary>
    public void SetCharacter(string characterId)
    {
        var db = CharacterDatabase.Instance;
        var entry = db != null ? db.Get(characterId) : null;
        if (entry == null) return;

        GameObject prefab = db.GetPrefab(characterId);
        if (prefab == null) return;

        if (model != null && modelCharacterId != null &&
            db.GetPrefab(modelCharacterId) == prefab)
        {
            // same base prefab (color variation) — just re-tint
            modelCharacterId = characterId;
            RefreshSkin();
            return;
        }

        if (model != null) DestroyImmediate(model);
        handWeapon = null;
        rightHand = null;

        bool wasActive = gameObject.activeSelf;
        gameObject.SetActive(false);
        model = Instantiate(prefab, transform, false);
        UnpackIfPrefabInstance(model);
        model.name = "OperatorModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        StripToDisplayOnly(model);
        gameObject.SetActive(wasActive);

        modelCharacterId = characterId;
        ConfigureAnimator();
        if (rightHand == null) rightHand = FindBoneByName(model.transform, "hand_r", "righthand", "hand.r");
        GroundModel();

        RefreshSkin();
        RefreshWeapon();
    }

    /// <summary>Removes every gameplay, physics and networking piece; keeps visuals + animator.</summary>
    /// <summary>In edit mode an Instantiate keeps the prefab link, and Unity forbids
    /// destroying components of a prefab instance — unpack completely first.</summary>
    static void UnpackIfPrefabInstance(GameObject go)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && go != null && UnityEditor.PrefabUtility.IsPartOfPrefabInstance(go))
            UnityEditor.PrefabUtility.UnpackPrefabInstance(
                go, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
#endif
    }

    static void StripToDisplayOnly(GameObject root)
    {
        // cameras & audio
        foreach (var cam in root.GetComponentsInChildren<Camera>(true)) DestroyImmediate(cam.gameObject);
        foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);

        // embedded UI (inventory / HUD canvases inside the character prefab)
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null && canvas.gameObject != root) DestroyImmediate(canvas.gameObject);
        }

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

        var db = CharacterDatabase.Instance;
        var entry = db != null ? db.Get(modelCharacterId ?? string.Empty) ?? db.GetSelected() : null;
        if (entry == null) return;

        ApplyTint(model, entry.tint);
    }

    /// <summary>Tints every mesh via MaterialPropertyBlock — no material instances leak in edit mode.</summary>
    public static void ApplyTint(GameObject root, Color tint)
    {
        var block = new MaterialPropertyBlock();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(block);
        }
    }

    public void RefreshWeapon()
    {
        if (model == null) return;

        // FIRST: hide every baked slot gun, no matter what happens below.
        // (previously an early-return could leave a stray gun lying visible)
        HideHolsteredDuplicate("");

        // selected primary = first id in the saved loadout (fallback: first rifle in DB)
        string primaryId = null;
        string saved = PlayerPrefs.GetString(CODLoadout.LoadoutPref, CODLoadout.DefaultLoadout);
        if (!string.IsNullOrEmpty(saved)) primaryId = saved.Split(',')[0].Trim();

        var database = WeaponDatabase.Instance;
        if (database == null) return;

        GameObject prefab = database.GetPrefab(primaryId);
        if (prefab == null)
        {
            var rifles = database.GetBySlotType(WeaponDatabase.SlotType.rifle);
            if (rifles.Count > 0) prefab = rifles[0].prefab;
        }
        if (prefab == null) return;

        // The saved scene may already hold the weapon in the operator's hand.
        // handWeapon is a plain field, not serialized, so after a scene load it
        // is null even though the object is right there — instantiating on that
        // basis gives the operator two rifles.
        if (handWeapon == null && rightHand != null)
        {
            foreach (Transform child in rightHand)
            {
                if (child.name == prefab.name) { handWeapon = child.gameObject; break; }
            }
        }

        if (handWeapon != null)
        {
            if (handWeapon.name == prefab.name)
            {
                AttachToHand(handWeapon); // re-seat the grip; cheap and idempotent
                return;
            }
            DestroyImmediate(handWeapon);
        }

        handWeapon = Instantiate(prefab);
        UnpackIfPrefabInstance(handWeapon);
        handWeapon.name = prefab.name;

        // display only: strip behaviours and physics from the preview gun
        foreach (var script in handWeapon.GetComponentsInChildren<MonoBehaviour>(true))
        {
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

        // Invector weapons sit at zero local pose on the hand's NATIVE equip
        // handler ("defaultHandler", from the inventory template hierarchy)
        Transform mount = rightHand;
        foreach (var t in rightHand.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "defaultHandler") { mount = t; break; }
        }

        weapon.transform.SetParent(mount, false);
        weapon.transform.localPosition = Vector3.zero;
        weapon.transform.localRotation = Quaternion.identity;
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
