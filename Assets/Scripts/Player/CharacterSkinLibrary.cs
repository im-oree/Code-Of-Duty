using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of selectable operators. Each operator is a color tint and an
/// optional full model override (for imported character meshes like
/// Male_Body_BaseMesh — drop the rigged prefab into modelOverride when ready).
/// Lives in Resources/Character so menus and players load it anywhere.
/// </summary>
[CreateAssetMenu(menuName = "COD/Character Skin Library", fileName = "CharacterSkinLibrary")]
public class CharacterSkinLibrary : ScriptableObject
{
    [Serializable]
    public class Skin
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public Color tint = Color.white;

        [Tooltip("Optional replacement character model (skinned, same rig). Empty = tint the default model.")]
        public GameObject modelOverride;
    }

    public List<Skin> skins = new List<Skin>();

    static CharacterSkinLibrary cached;

    public static CharacterSkinLibrary Instance
    {
        get
        {
            if (cached == null) cached = Resources.Load<CharacterSkinLibrary>("Character/CharacterSkinLibrary");
            return cached;
        }
    }

    public Skin GetByIndex(int index)
    {
        if (skins.Count == 0) return null;
        return skins[Mathf.Clamp(index, 0, skins.Count - 1)];
    }

    public int Count => skins.Count;
}
