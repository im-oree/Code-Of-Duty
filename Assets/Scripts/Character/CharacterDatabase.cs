using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry of every playable character (operator): the Invector-based
/// prefab, its headshot for the selection UI, and variation metadata (a skin
/// tint or model variation of a base character).
///
/// Lives at Resources/Character/CharacterDatabase so runtime code can load it
/// anywhere. Edited through the "COD > Character Database" editor window; the
/// character creator wizard registers new characters automatically.
/// </summary>
[CreateAssetMenu(menuName = "COD/Character Database", fileName = "CharacterDatabase")]
public class CharacterDatabase : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Stable id saved in player prefs / sent over the network")]
        public string id;
        public string displayName;
        [TextArea] public string description;

        [Tooltip("Invector character prefab (networked player). Empty = not playable yet")]
        public GameObject prefab;
        [Tooltip("Headshot rendered in the headshot studio")]
        public Sprite headshot;

        [Header("Variation")]
        [Tooltip("True when this entry is a variation (skin/tint) of another character")]
        public bool isVariation;
        [Tooltip("Id of the base character when this is a variation")]
        public string baseId;
        [Tooltip("Tint applied to the body materials for simple color variations")]
        public Color tint = Color.white;

        [Tooltip("Selectable in the operator menu")]
        public bool playable = true;
    }

    public List<Entry> characters = new List<Entry>();

    public const string ResourcePath = "Character/CharacterDatabase";
    public const string SelectedPref = "CharacterId";

    static CharacterDatabase instance;
    public static CharacterDatabase Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<CharacterDatabase>(ResourcePath);
            }
            return instance;
        }
    }

    public Entry Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return characters.Find(c => c.id == id);
    }

    public Entry GetAt(int index)
    {
        return index >= 0 && index < characters.Count ? characters[index] : null;
    }

    public int IndexOf(string id)
    {
        return characters.FindIndex(c => c.id == id);
    }

    /// <summary>Resolved spawn prefab for a character id (variations fall back to their base prefab).</summary>
    public GameObject GetPrefab(string id)
    {
        Entry entry = Get(id);
        if (entry == null) return null;
        if (entry.prefab != null) return entry.prefab;
        if (entry.isVariation) return GetPrefab(entry.baseId);
        return null;
    }

    /// <summary>The locally selected character (player prefs), falling back to the first playable entry.</summary>
    public Entry GetSelected()
    {
        Entry entry = Get(PlayerPrefs.GetString(SelectedPref, string.Empty));
        if (entry != null && entry.playable) return entry;
        return characters.Find(c => c.playable && c.prefab != null);
    }

    public static void SetSelected(string id)
    {
        PlayerPrefs.SetString(SelectedPref, id);
    }
}
