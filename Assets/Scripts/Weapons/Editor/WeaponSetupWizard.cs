using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns an imported gun model (FBX/GLB via glTFast) into a game-ready weapon
/// prefab wired for all existing systems (Weapon, WeaponPoints for hand IK,
/// bullet/casing spawns, audio, pickup physics) and registers it in the
/// WeaponDatabase so it appears in the loadout menu automatically.
///
/// Usage: select the model in the Project window ->
///        COD / Weapons / Create Weapon From Selected Model.
/// Then fine-tune the generated points on the prefab (grips, muzzle, aim).
/// </summary>
public static class WeaponSetupWizard
{
    const string PrefabFolder = "Assets/Prefabs/weapons";
    const string DatabasePath = "Assets/Resources/Weapons/WeaponDatabase.asset";

    [MenuItem("COD/Weapons/Create Weapon From Selected Model")]
    public static void CreateFromSelection()
    {
        GameObject model = Selection.activeGameObject;
        if (model == null)
        {
            EditorUtility.DisplayDialog("Weapon Setup",
                "Select a gun model (FBX/GLB) in the Project window first.", "OK");
            return;
        }

        string weaponName = CleanName(model.name);
        GameObject root = new GameObject(weaponName);

        // visual model as child, normalized transform
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (visual == null) visual = Object.Instantiate(model);
        visual.name = "Model";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        RemoveBloat(visual.transform);
        NormalizeSize(visual);

        // gameplay components (mirrors the kit's gun prefab layout)
        Weapon weapon = root.AddComponent<Weapon>();
        weapon.slotType = GuessSlotType(weaponName);
        weapon.playerDamage = weapon.slotType == Weapon.SlotType.pistol ? 15 : 10;
        weapon.shotTemp = weapon.slotType == Weapon.SlotType.smg ? 0.08f : 0.12f;
        weapon.bulletStartSpeed = 200f;
        weapon.bulletForce = 30f;
        weapon.accuracy = 1f;

        AudioSource audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;

        BoxCollider pickupCollider = root.AddComponent<BoxCollider>();
        FitColliderToRenderers(root, pickupCollider);
        pickupCollider.enabled = false; // enabled when dropped
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;

        // standard point hierarchy used by the IK/shooting systems
        Transform points = new GameObject("Points").transform;
        points.SetParent(root.transform, false);

        weapon.weaponPoints = new WeaponPoint[]
        {
            CreatePoint(points, "RightHandDefault", WeaponPoint.PointType.RightHandDefault, new Vector3(0f, -0.03f, -0.12f)),
            CreatePoint(points, "LeftHandDefault", WeaponPoint.PointType.LeftHandDefault, new Vector3(0f, -0.02f, 0.15f)),
            CreatePoint(points, "LeftHandguard", WeaponPoint.PointType.LeftHandguard, new Vector3(0f, -0.02f, 0.2f)),
        };

        weapon.bulletSpawnPoint = CreateChild(root.transform, "BulletSpawnPoint", new Vector3(0, 0.05f, 0.5f));
        weapon.casingSpawnPoint = CreateChild(root.transform, "CasingSpawnPoint", new Vector3(0, 0.05f, 0.1f));
        weapon.aimPoint = CreateChild(root.transform, "AimPoint", new Vector3(0, 0.08f, 0.2f));

        // reuse existing bullet/muzzle prefabs so shooting works out of the box
        weapon.bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BulletNetwork.prefab");
        weapon.muzzleFlash = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RifleParticle.prefab");
        weapon.casingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Casing.prefab");

        // save prefab
        Directory.CreateDirectory(PrefabFolder);
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{weaponName}.prefab");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        // register in database
        WeaponDatabase database = AssetDatabase.LoadAssetAtPath<WeaponDatabase>(DatabasePath);
        if (database != null && database.Get(weaponName) == null)
        {
            database.weapons.Add(new WeaponDatabase.Entry
            {
                id = weaponName,
                displayName = weaponName.Replace("_", " "),
                description = "Imported weapon.",
                prefab = prefab
            });
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
        }

        Selection.activeObject = prefab;
        Debug.Log($"Weapon '{weaponName}' created at {prefabPath} and registered in the WeaponDatabase. " +
                  "Now position the hand points, muzzle, casing and aim point on the prefab.");
    }

    static string CleanName(string raw)
    {
        string name = raw.Replace("low-poly_", "").Replace("low_poly_", "").Replace(" ", "_");
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    /// Imported store models often ship with bloated hierarchies: loose bullets,
    /// magazines duplicated, casings, scene helpers. Strip the obvious junk.
    /// </summary>
    static void RemoveBloat(Transform root)
    {
        string[] junk = { "bullet", "casing", "shell", "ammo_", "round", "cartridge", "empty", "helper", "icosphere", "camera", "light" };
        var toKill = new System.Collections.Generic.List<GameObject>();

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == root) continue;
            string lower = child.name.ToLowerInvariant();
            foreach (string word in junk)
            {
                if (lower.Contains(word)) { toKill.Add(child.gameObject); break; }
            }
        }

        foreach (var go in toKill)
        {
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    /// <summary>Rescales the model so the gun is a sane real-world size (~0.4-1.1m long).</summary>
    static void NormalizeSize(GameObject visual)
    {
        Bounds bounds = CalculateBounds(visual);
        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest <= 0.0001f) return;

        // target: rifles ~0.9m, anything else proportional
        float target = 0.9f;
        float factor = target / longest;
        if (factor < 0.95f || factor > 1.05f)
            visual.transform.localScale = visual.transform.localScale * factor;
    }

    static void FitColliderToRenderers(GameObject root, BoxCollider collider)
    {
        Bounds bounds = CalculateBounds(root);
        if (bounds.size == Vector3.zero) return;
        collider.center = root.transform.InverseTransformPoint(bounds.center);
        collider.size = bounds.size;
    }

    static Bounds CalculateBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static Weapon.SlotType GuessSlotType(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.Contains("pistol") || lower.Contains("g17") || lower.Contains("glock") || lower.Contains("mos")) return Weapon.SlotType.pistol;
        if (lower.Contains("smg") || lower.Contains("sa-9") || lower.Contains("sa_9") || lower.Contains("mp5") || lower.Contains("uzi")) return Weapon.SlotType.smg;
        return Weapon.SlotType.rifle;
    }

    static WeaponPoint CreatePoint(Transform parent, string name, WeaponPoint.PointType type, Vector3 localPos)
    {
        var point = new GameObject(name).AddComponent<WeaponPoint>();
        point.pointType = type;
        point.transform.SetParent(parent, false);
        point.transform.localPosition = localPos;
        return point;
    }

    static Transform CreateChild(Transform parent, string name, Vector3 localPos)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = localPos;
        return child;
    }
}
