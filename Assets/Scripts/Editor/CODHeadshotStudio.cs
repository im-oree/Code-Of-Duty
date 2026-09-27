using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Renders operator headshots from the authored HeadshotStudio scene (fixed,
/// nicely lit camera angles) straight into the CharacterDatabase, so the
/// operator selection cards always use studio-quality portraits.
///
/// For every playable database entry:
///  • the character prefab is instantiated at the studio station
///  • variations get their tint applied (same path the game uses)
///  • the station's authored 'Headshot_*' camera renders a 512² PNG into
///    Assets/UI/Headshots/<id>.png (imported as sprite, assigned to the entry)
/// </summary>
public static class CODHeadshotStudio
{
    const string StudioScene = "Assets/Scenes/HeadshotStudio.unity";
    const string OutputFolder = "Assets/UI/Headshots";
    const int Size = 512;

    [MenuItem("COD/Characters/Render Headshots")]
    public static void RenderAll()
    {
        var db = CharacterDatabase.Instance;
        if (db == null)
        {
            EditorUtility.DisplayDialog("Headshot Studio", "CharacterDatabase not found in Resources/Character.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previousScene = SceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(StudioScene, OpenSceneMode.Single);

        Directory.CreateDirectory(OutputFolder);
        int rendered = 0;

        foreach (var entry in db.characters)
        {
            GameObject prefab = db.GetPrefab(entry.id);
            if (prefab == null) continue;

            Camera cam = FindStationCamera(entry);
            if (cam == null) continue;

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.transform.position = cam.transform.parent != null
                ? cam.transform.parent.position : Vector3.zero;
            model.transform.rotation = Quaternion.identity;

            if (entry.isVariation) OperatorDisplay.ApplyTint(model, entry.tint);

            string file = $"{OutputFolder}/{entry.id}.png";
            Capture(cam, file);
            Object.DestroyImmediate(model);

            AssetDatabase.ImportAsset(file);
            var importer = (TextureImporter)AssetImporter.GetAtPath(file);
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();

            entry.headshot = AssetDatabase.LoadAssetAtPath<Sprite>(file);
            rendered++;
        }

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();

        if (!string.IsNullOrEmpty(previousScene) && previousScene != StudioScene)
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

        EditorUtility.DisplayDialog("Headshot Studio",
            $"Rendered {rendered} headshot(s) into {OutputFolder}.", "OK");
    }

    /// <summary>The authored per-character camera; variations reuse their base station. Falls back to the first studio camera.</summary>
    static Camera FindStationCamera(CharacterDatabase.Entry entry)
    {
        string wanted = "Headshot_" + (entry.isVariation ? entry.baseId : entry.id);
        GameObject go = GameObject.Find(wanted);
        if (go == null)
        {
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.name.StartsWith("Headshot_")) return c;
            return null;
        }
        return go.GetComponent<Camera>();
    }

    static void Capture(Camera cam, string file)
    {
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }
}
