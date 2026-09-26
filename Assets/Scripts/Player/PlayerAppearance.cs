using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Networked operator skin (FishNet). The operator chosen in the main menu is
/// synced to every client and applied to all skinned meshes of the player
/// (color tint now, full model swap later via CharacterSkinLibrary.modelOverride).
/// </summary>
public class PlayerAppearance : NetworkBehaviour
{
    public const string PrefKey = "SkinIndex";

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    static readonly int ColorId = Shader.PropertyToID("_Color");         // legacy/standard

    readonly SyncVar<int> skinIndex = new SyncVar<int>(-1);

    int appliedIndex = -1;

    public static int SavedSkinIndex
    {
        get => PlayerPrefs.GetInt(PrefKey, 0);
        set => PlayerPrefs.SetInt(PrefKey, value);
    }

    void Awake()
    {
        skinIndex.OnChange += OnSkinSynced;
    }

    void OnDestroy()
    {
        skinIndex.OnChange -= OnSkinSynced;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner) ServerSetSkin(SavedSkinIndex);
        if (skinIndex.Value >= 0) ApplySkin(skinIndex.Value);
    }

    [ServerRpc]
    void ServerSetSkin(int index)
    {
        var library = CharacterSkinLibrary.Instance;
        if (library == null || library.Count == 0) return;

        skinIndex.Value = Mathf.Clamp(index, 0, library.Count - 1);
    }

    void OnSkinSynced(int _, int index, bool asServer)
    {
        if (!asServer) ApplySkin(index);
    }

    void ApplySkin(int index)
    {
        if (index < 0 || index == appliedIndex) return;

        var skin = CharacterSkinLibrary.Instance != null ? CharacterSkinLibrary.Instance.GetByIndex(index) : null;
        if (skin == null) return;

        ApplySkinTo(gameObject, skin);
        appliedIndex = index;
    }

    /// <summary>Tints every skinned mesh under root (also used by the menu preview).</summary>
    public static void ApplySkinTo(GameObject root, CharacterSkinLibrary.Skin skin)
    {
        if (root == null || skin == null) return;

        if (skin.modelOverride != null)
            TrySwapModel(root, skin.modelOverride);

        var block = new MaterialPropertyBlock();
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, skin.tint);
            block.SetColor(ColorId, skin.tint);
            renderer.SetPropertyBlock(block);
        }
    }

    const string OverrideNodeName = "BodyModelOverride";

    /// <summary>
    /// Full body-model swap: instantiates the override model and rebinds its
    /// skinned meshes onto the player's existing skeleton by bone NAME, then
    /// hides the original body renderers. Works with any mesh rigged to the
    /// kit skeleton (same bone names). Unrigged meshes are skipped with a
    /// warning — they cannot animate.
    /// </summary>
    static void TrySwapModel(GameObject root, GameObject overridePrefab)
    {
        Animator animator = root.GetComponentInChildren<Animator>(true);
        Transform skeletonRoot = animator != null ? animator.transform : root.transform;

        // already swapped?
        foreach (Transform child in skeletonRoot)
        {
            if (child.name == OverrideNodeName) return;
        }

        var sourceRenderers = overridePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (sourceRenderers.Length == 0)
        {
            Debug.LogWarning($"[PlayerAppearance] '{overridePrefab.name}' has no skinned mesh (unrigged model?). " +
                             "Rig it to the kit skeleton (same bone names) and it will swap in automatically.");
            return;
        }

        // bone lookup of the live skeleton
        var boneMap = new System.Collections.Generic.Dictionary<string, Transform>();
        foreach (Transform bone in skeletonRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!boneMap.ContainsKey(bone.name)) boneMap[bone.name] = bone;
        }

        GameObject instance = Instantiate(overridePrefab, skeletonRoot, false);
        instance.name = OverrideNodeName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var newBones = new Transform[renderer.bones.Length];
            for (int i = 0; i < newBones.Length; i++)
            {
                if (renderer.bones[i] == null || !boneMap.TryGetValue(renderer.bones[i].name, out newBones[i]))
                {
                    Debug.LogWarning($"[PlayerAppearance] Bone '{(renderer.bones[i] != null ? renderer.bones[i].name : "null")}' " +
                                     $"not found on the player skeleton — model swap aborted for '{overridePrefab.name}'.");
                    Destroy(instance);
                    return;
                }
            }
            renderer.bones = newBones;
            if (renderer.rootBone != null && boneMap.TryGetValue(renderer.rootBone.name, out Transform newRoot))
                renderer.rootBone = newRoot;
        }

        // hide the original body meshes (they stay for ragdoll/death systems)
        foreach (var renderer in skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.transform.IsChildOf(instance.transform))
                renderer.enabled = false;
        }
    }
}
