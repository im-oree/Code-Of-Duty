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

        var block = new MaterialPropertyBlock();
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, skin.tint);
            block.SetColor(ColorId, skin.tint);
            renderer.SetPropertyBlock(block);
        }

        // future: full model swap once skin.modelOverride is assigned (rigged mesh)
    }
}
