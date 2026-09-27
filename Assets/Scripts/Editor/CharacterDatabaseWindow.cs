using UnityEditor;
using UnityEngine;

/// <summary>
/// COD > Character Database — the roster editor. Every playable character and
/// variation lives here: prefab, headshot, variation metadata, tint,
/// playability. The same asset drives the operator menu, spawning and the
/// headshot studio at runtime and in the editor.
/// </summary>
public class CharacterDatabaseWindow : EditorWindow
{
    Vector2 scroll;
    int selected = -1;

    [MenuItem("COD/Character Database")]
    static void Open()
    {
        var window = GetWindow<CharacterDatabaseWindow>("Character Database");
        window.minSize = new Vector2(560, 420);
    }

    CharacterDatabase Database => CharacterDatabase.Instance;

    void OnGUI()
    {
        var db = Database;
        if (db == null)
        {
            EditorGUILayout.HelpBox("CharacterDatabase asset not found at Resources/Character/CharacterDatabase.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawList(db);
        DrawDetails(db);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Character"))
        {
            Undo.RecordObject(db, "Add Character");
            db.characters.Add(new CharacterDatabase.Entry { id = "NewCharacter", displayName = "New Character" });
            selected = db.characters.Count - 1;
            EditorUtility.SetDirty(db);
        }
        if (GUILayout.Button("Render Headshots (studio scene)"))
        {
            CODHeadshotStudio.RenderAll();
        }
        if (GUILayout.Button("Setup Selected Scene Object", GUILayout.Width(200)))
        {
            CODCharacterSetupTool.SetupSelected();
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawList(CharacterDatabase db)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(210));
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < db.characters.Count; i++)
        {
            var e = db.characters[i];
            string label = string.IsNullOrEmpty(e.displayName) ? e.id : e.displayName;
            if (e.isVariation) label = "  ↳ " + label;
            if (!e.playable) label += "  (locked)";

            GUI.color = i == selected ? new Color(0.6f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button(label, EditorStyles.miniButton)) selected = i;
        }
        GUI.color = Color.white;
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawDetails(CharacterDatabase db)
    {
        EditorGUILayout.BeginVertical();
        if (selected < 0 || selected >= db.characters.Count)
        {
            EditorGUILayout.HelpBox("Select a character on the left.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        var e = db.characters[selected];
        EditorGUI.BeginChangeCheck();

        e.id = EditorGUILayout.TextField("Id", e.id);
        e.displayName = EditorGUILayout.TextField("Display Name", e.displayName);
        e.description = EditorGUILayout.TextArea(e.description, GUILayout.Height(48));
        e.prefab = (GameObject)EditorGUILayout.ObjectField("Character Prefab", e.prefab, typeof(GameObject), false);
        e.headshot = (Sprite)EditorGUILayout.ObjectField("Headshot", e.headshot, typeof(Sprite), false);

        EditorGUILayout.Space(4);
        e.isVariation = EditorGUILayout.Toggle("Is Variation", e.isVariation);
        if (e.isVariation)
        {
            e.baseId = EditorGUILayout.TextField("Base Character Id", e.baseId);
            e.tint = EditorGUILayout.ColorField("Tint", e.tint);
        }
        e.playable = EditorGUILayout.Toggle("Playable", e.playable);

        if (e.headshot != null)
        {
            Rect r = GUILayoutUtility.GetRect(128, 128, GUILayout.Width(128));
            GUI.DrawTexture(r, e.headshot.texture, ScaleMode.ScaleToFit);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(db, "Edit Character");
            EditorUtility.SetDirty(db);
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Remove This Character", GUILayout.Width(180)))
        {
            Undo.RecordObject(db, "Remove Character");
            db.characters.RemoveAt(selected);
            selected = -1;
            EditorUtility.SetDirty(db);
        }
        EditorGUILayout.EndVertical();
    }
}
