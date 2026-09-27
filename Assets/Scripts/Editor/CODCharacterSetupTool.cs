using System.IO;
using Invector;
using Invector.vCharacterController;
using Invector.vShooter;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click COD character setup — the glue between Invector's character
/// creator and our systems. Takes any humanoid character (e.g. one made with
/// Invector's "Create New Character" wizard, or a plain rigged model) and
/// applies the FULL standard COD stack:
///
///   Invector:  CODThirdPersonController, CODShooterInput (owner-only),
///              vShooterManager, vAmmoManager, vMeleeManager, vHeadTrack
///   COD:       CODFirstPersonBody (native first person),
///              CODInvectorPlayer, CODNetworkHealth, CODLoadoutEquipper (FishNet)
///   Parachute: 'Body Snap Control' child + the add-on's Parachute prefab
///              (animator states live in Invector@ShooterMelee.controller)
///
/// then saves it as a prefab in Assets/Prefabs/Characters, registers it in
/// the CharacterDatabase and renders its headshot in the HeadshotStudio
/// scene so the operator menu immediately shows it.
/// </summary>
public static class CODCharacterSetupTool
{
    const string AnimatorControllerPath =
        "Assets/Invector-3rdPersonController/Shooter/Animator/Invector@ShooterMelee.controller";
    const string AmmoListPath =
        "Assets/Invector-3rdPersonController/Shooter/Scripts/Shooter/Shooter_AmmoListData.asset";
    const string ParachutePrefabPath =
        "Assets/Invector-3rdPersonController/Add-ons/Parachute/Prefabs/Parachute.prefab";
    const string PrefabFolder = "Assets/Prefabs/Characters";
    const string DatabasePath = "Assets/Resources/Character/CharacterDatabase.asset";

    [MenuItem("COD/Characters/Setup Selected As COD Character", true)]
    static bool ValidateSetup() => Selection.activeGameObject != null;

    [MenuItem("COD/Characters/Setup Selected As COD Character")]
    public static void SetupSelected()
    {
        GameObject go = Selection.activeGameObject;
        Animator animator = go.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
        {
            EditorUtility.DisplayDialog("COD Character Setup",
                "The selected object needs a Humanoid Animator (rig type Humanoid).", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(go, "COD Character Setup");
        ApplyFullStack(go, animator);

        GameObject prefab = SaveAsPrefab(go);
        RegisterInDatabase(prefab);

        EditorUtility.DisplayDialog("COD Character Setup",
            $"'{go.name}' is now a full COD character.\n\n" +
            "• Invector controller + shooter/melee stack\n" +
            "• Native first person + FishNet sync\n" +
            "• Parachute add-on (animator already contains the states)\n" +
            "• Registered in the Character Database\n\n" +
            "Render its headshot via COD > Characters > Render Headshots.", "OK");
    }

    /// <summary>Ensures every component of the standard COD character stack.</summary>
    public static void ApplyFullStack(GameObject go, Animator animator)
    {
        go.tag = "Player";
        go.layer = LayerMask.NameToLayer("Player");

        // animator: shared shooter/melee controller (includes the Parachute substate)
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
        if (controller != null) animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = true;

        // physics
        var body = Ensure<Rigidbody>(go);
        body.mass = 50;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var capsule = Ensure<CapsuleCollider>(go);
        if (capsule.height <= 1f)
        {
            capsule.center = new Vector3(0, 0.9f, 0);
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
        }

        // Invector gameplay stack (COD subclasses where we extend Invector)
        Ensure<CODThirdPersonController>(go);
        var input = Ensure<CODShooterInput>(go);
        input.enabled = false; // CODInvectorPlayer enables it for the owner
        var shooter = Ensure<vShooterManager>(go);
        var ammo = Ensure<Invector.vItemManager.vAmmoManager>(go);
        if (ammo.ammoListData == null)
            ammo.ammoListData = AssetDatabase.LoadAssetAtPath<Invector.vItemManager.vAmmoListData>(AmmoListPath);
        Ensure<Invector.vMelee.vMeleeManager>(go);
        Ensure<vHeadTrack>(go);

        // COD layer
        Ensure<CODFirstPersonBody>(go);
        Ensure<CODInvectorPlayer>(go);
        Ensure<CODNetworkHealth>(go);
        Ensure<CODLoadoutEquipper>(go);
        Ensure<FishNet.Object.NetworkObject>(go);
        Ensure<FishNet.Component.Transforming.NetworkTransform>(go);
        Ensure<FishNet.Component.Animating.NetworkAnimator>(go);

        SetupParachute(go);
    }

    /// <summary>Parachute add-on, per its documentation (steps 2 and 3).</summary>
    public static void SetupParachute(GameObject go)
    {
        // step 2 — BodySnappingControl on an empty child (humanoid: auto bones)
        if (go.GetComponentInChildren<vBodySnappingControl>(true) == null)
        {
            var snap = new GameObject("Body Snap Control");
            snap.transform.SetParent(go.transform, false);
            snap.layer = go.layer;
            snap.AddComponent<vBodySnappingControl>();
        }

        // step 3 — the Parachute prefab inside the character
        if (go.GetComponentInChildren<vParachuteController>(true) == null)
        {
            var parachutePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ParachutePrefabPath);
            if (parachutePrefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(parachutePrefab);
                instance.transform.SetParent(go.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
        }
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static GameObject SaveAsPrefab(GameObject go)
    {
        if (PrefabUtility.IsPartOfPrefabAsset(go)) return go;

        Directory.CreateDirectory(PrefabFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{PrefabFolder}/{go.name}.prefab");

        // already an instance of one of our character prefabs? apply instead
        var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (source != null && AssetDatabase.GetAssetPath(source).StartsWith(PrefabFolder))
        {
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            return source;
        }

        return PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
    }

    static void RegisterInDatabase(GameObject prefab)
    {
        var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DatabasePath);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<CharacterDatabase>();
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            AssetDatabase.CreateAsset(db, DatabasePath);
        }

        string id = prefab.name;
        var entry = db.Get(id);
        if (entry == null)
        {
            entry = new CharacterDatabase.Entry
            {
                id = id,
                displayName = ObjectNames.NicifyVariableName(id),
                description = "Custom operator created with the COD character setup.",
                playable = true
            };
            db.characters.Add(entry);
        }
        entry.prefab = prefab;
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
    }
}
