using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// COD > Game Config — the ONE place to manage gameplay configuration
/// (MFPS-style). Sidebar sections:
///
///   GENERAL   — GameConfig asset (infinite ammo, reserve mags, health…)
///   GUNS      — every WeaponDatabase gun: stats, ammo, recoil + icon capture
///               (wide-aspect render that frames long rifles properly)
///   MAPS      — build scene list management
///   KEYBINDS  — default bindings registry + local override tools
///
/// Everything edits the underlying ASSETS (GameConfig.asset, gun prefabs,
/// WeaponDatabase.asset), so values persist and ship with the game.
/// </summary>
public class GameConfigWindow : EditorWindow
{
    static readonly string[] Sections = { "GENERAL", "GUNS", "MAPS", "KEYBINDS", "GAMEMODES" };

    int section;
    int selectedGun;
    Vector2 sidebarScroll, mainScroll;
    Texture2D iconPreview;

    [MenuItem("COD/Game Config")]
    static void Open()
    {
        var window = GetWindow<GameConfigWindow>("COD Game Config");
        window.minSize = new Vector2(860f, 520f);
    }

    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawSidebar();
        DrawMain();
        EditorGUILayout.EndHorizontal();
    }

    // ================================================================ sidebar

    void DrawSidebar()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(180f));
        sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);

        GUILayout.Space(10f);
        var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        GUILayout.Label("  CODE OF DUTY", title);
        GUILayout.Space(10f);

        for (int i = 0; i < Sections.Length; i++)
        {
            bool disabled = Sections[i] == "GAMEMODES";
            using (new EditorGUI.DisabledScope(disabled))
            {
                var style = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 34f,
                    fontStyle = i == section ? FontStyle.Bold : FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, 4, 0, 0)
                };
                GUI.backgroundColor = i == section ? new Color(1f, 0.62f, 0.1f) : Color.white;
                if (GUILayout.Button(disabled ? Sections[i] + "  (soon)" : Sections[i], style))
                    section = i;
                GUI.backgroundColor = Color.white;
            }
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.Width(1f), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(line, new Color(0f, 0f, 0f, 0.4f));
    }

    void DrawMain()
    {
        EditorGUILayout.BeginVertical();
        mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
        GUILayout.Space(12f);

        switch (Sections[section])
        {
            case "GENERAL": DrawGeneral(); break;
            case "GUNS": DrawGuns(); break;
            case "MAPS": DrawMaps(); break;
            case "KEYBINDS": DrawKeybinds(); break;
        }

        GUILayout.Space(24f);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    static void Header(string text, string sub = null)
    {
        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
        GUILayout.Label(text, style);
        if (!string.IsNullOrEmpty(sub))
            EditorGUILayout.LabelField(sub, EditorStyles.miniLabel);
        GUILayout.Space(8f);
    }

    // ================================================================ general

    void DrawGeneral()
    {
        Header("GENERAL", "Global gameplay rules — stored in Assets/Resources/GameConfig.asset");

        var config = GameConfig.Instance;
        if (config == null || !AssetDatabase.Contains(config))
        {
            EditorGUILayout.HelpBox("Resources/GameConfig.asset not found.", MessageType.Error);
            return;
        }

        var so = new SerializedObject(config);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("infiniteAmmo"));
        EditorGUILayout.PropertyField(so.FindProperty("reserveMagazines"));
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Grenades", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("grenadesPerLife"));
        EditorGUILayout.PropertyField(so.FindProperty("grenadeDamage"));
        EditorGUILayout.PropertyField(so.FindProperty("grenadeRadius"));
        EditorGUILayout.PropertyField(so.FindProperty("grenadeFuse"));
        EditorGUILayout.PropertyField(so.FindProperty("grenadeThrowForce"));
        GUILayout.Space(6f);
        EditorGUILayout.PropertyField(so.FindProperty("maxHealth"));
        EditorGUILayout.PropertyField(so.FindProperty("respawnDelay"));
        if (so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        GUILayout.Space(10f);
        EditorGUILayout.HelpBox(
            "Infinite ammo is read live by every weapon. Later, the match host can force it via " +
            "GameConfig.ServerInfiniteAmmoOverride (replicated match rules).", MessageType.Info);
    }

    // ================================================================== guns

    void DrawGuns()
    {
        Header("GUN MANAGER", "Stats live on each gun prefab's Weapon component; identity lives in the WeaponDatabase.");

        var db = WeaponDatabase.Instance;
        if (db == null)
        {
            EditorGUILayout.HelpBox("WeaponDatabase not found at Resources/Weapons/WeaponDatabase.", MessageType.Error);
            return;
        }

        EditorGUILayout.BeginHorizontal();

        // ---- gun list ----
        EditorGUILayout.BeginVertical(GUILayout.Width(190f));
        for (int i = 0; i < db.weapons.Count; i++)
        {
            var entry = db.weapons[i];
            GUI.backgroundColor = i == selectedGun ? new Color(1f, 0.62f, 0.1f) : Color.white;
            if (GUILayout.Button(string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName,
                    GUILayout.Height(28f)))
            {
                selectedGun = i;
                iconPreview = LoadIconTexture(entry.id);
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = Color.white;
        }
        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Add guns via COD > Weapon\nSetup Wizard.", EditorStyles.miniLabel, GUILayout.Height(26f));
        EditorGUILayout.EndVertical();

        GUILayout.Space(14f);

        // ---- selected gun editor ----
        EditorGUILayout.BeginVertical();
        if (db.weapons.Count == 0)
        {
            EditorGUILayout.HelpBox("No weapons registered.", MessageType.Info);
        }
        else
        {
            selectedGun = Mathf.Clamp(selectedGun, 0, db.weapons.Count - 1);
            DrawGunEditor(db, db.weapons[selectedGun]);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    void DrawGunEditor(WeaponDatabase db, WeaponDatabase.Entry entry)
    {
        // identity (database asset)
        EditorGUILayout.LabelField("IDENTITY", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField("Display Name", entry.displayName);
        string newDesc = EditorGUILayout.TextField("Description", entry.description);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Id", entry.id);
            EditorGUILayout.ObjectField("Prefab", entry.prefab, typeof(GameObject), false);
        }
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(db, "Edit Weapon Entry");
            entry.displayName = newName;
            entry.description = newDesc;
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
        }

        if (entry.prefab == null)
        {
            EditorGUILayout.HelpBox("Entry has no prefab.", MessageType.Warning);
            return;
        }

        var weapon = entry.prefab.GetComponentInChildren<Invector.vShooter.vShooterWeapon>(true);
        if (weapon == null)
        {
            EditorGUILayout.HelpBox("Prefab has no vShooterWeapon component (Invector).", MessageType.Warning);
            return;
        }

        GUILayout.Space(10f);
        EditorGUILayout.LabelField("COMBAT (Invector vShooterWeapon)", EditorStyles.boldLabel);
        var so = new SerializedObject(weapon);
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("automaticWeapon"), new GUIContent("Automatic"));
        EditorGUILayout.PropertyField(so.FindProperty("_shootFrequency"), new GUIContent("Fire Interval (s)"));
        EditorGUILayout.PropertyField(so.FindProperty("_maxDamage"), new GUIContent("Damage"));
        EditorGUILayout.PropertyField(so.FindProperty("velocity"), new GUIContent("Bullet Velocity"));

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("AMMO", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("_clipSize"), new GUIContent("Clip Size"));
        EditorGUILayout.PropertyField(so.FindProperty("reloadTime"));
        EditorGUILayout.PropertyField(so.FindProperty("ammoID"));

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("RECOIL", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("recoilUp"));
        EditorGUILayout.PropertyField(so.FindProperty("recoilRight"));
        EditorGUILayout.PropertyField(so.FindProperty("recoilLeft"));

        if (so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssets(); // persists straight into the prefab asset
        }

        // ------- icon -------
        GUILayout.Space(12f);
        EditorGUILayout.LabelField("ICON", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        var rect = GUILayoutUtility.GetRect(192f, 96f, GUILayout.Width(192f), GUILayout.Height(96f));
        EditorGUI.DrawRect(rect, new Color(0.09f, 0.1f, 0.12f));
        if (iconPreview == null) iconPreview = LoadIconTexture(entry.id);
        if (iconPreview != null)
            GUI.DrawTexture(rect, iconPreview, ScaleMode.ScaleToFit, true);
        else
            GUI.Label(rect, "  no icon", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.BeginVertical();
        if (GUILayout.Button("Generate Icon (2:1 side view)", GUILayout.Height(30f)))
        {
            GenerateGunIcon(entry);
            iconPreview = LoadIconTexture(entry.id);
        }
        EditorGUILayout.LabelField("Renders the gun side-on at 512×256 —\nlong rifles fill the frame instead of\nbeing squashed into a square.",
            EditorStyles.miniLabel, GUILayout.Height(40f));
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    static Texture2D LoadIconTexture(string id) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Resources/UI/{id}_UI.png");

    // ------------------------------------------------------------ icon render

    void GenerateGunIcon(WeaponDatabase.Entry entry)
    {
        const int W = 512, H = 256;

        var preview = new PreviewRenderUtility();
        try
        {
            preview.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 50f;
            preview.lights[0].intensity = 1.35f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            if (preview.lights.Length > 1) preview.lights[1].intensity = 0.9f;
            preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);

            var instance = preview.InstantiatePrefabInScene(entry.prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            // strip particles/trails that pollute bounds
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                Object.DestroyImmediate(ps.gameObject);

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Debug.LogWarning("No renderers on " + entry.id); return; }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // side view down +X so the gun's length (z) spans the image width
            preview.camera.transform.position = bounds.center + Vector3.right * (bounds.extents.magnitude + 2f);
            preview.camera.transform.rotation = Quaternion.LookRotation(Vector3.left);

            // fit with 8% padding, honoring the 2:1 aspect
            float halfW = bounds.extents.z * 1.08f;
            float halfH = bounds.extents.y * 1.08f;
            preview.camera.orthographicSize = Mathf.Max(halfH, halfW * ((float)H / W));

            preview.BeginPreview(new Rect(0f, 0f, W, H), GUIStyle.none);
            preview.camera.Render();
            var result = preview.EndPreview() as RenderTexture;

            var prevActive = RenderTexture.active;
            RenderTexture.active = result;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            string dir = "Assets/Resources/UI";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = $"{dir}/{entry.id}_UI.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Debug.Log($"[COD] Icon generated: {path}");
        }
        finally
        {
            preview.Cleanup();
        }
    }

    // ================================================================== maps

    void DrawMaps()
    {
        Header("MAPS", "Scenes in the build — the menu scene must stay first.");

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int remove = -1;

        for (int i = 0; i < scenes.Count; i++)
        {
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label(i == 0 ? "MENU" : "MAP", EditorStyles.miniBoldLabel, GUILayout.Width(44f));
            scenes[i].enabled = EditorGUILayout.Toggle(scenes[i].enabled, GUILayout.Width(20f));
            GUILayout.Label(Path.GetFileNameWithoutExtension(scenes[i].path));
            GUILayout.FlexibleSpace();
            GUILayout.Label(scenes[i].path, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(i == 0))
            {
                if (GUILayout.Button("✕", GUILayout.Width(24f))) remove = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (remove >= 0) scenes.RemoveAt(remove);

        GUILayout.Space(8f);
        if (GUILayout.Button("Add Currently Open Scene", GUILayout.Height(28f), GUILayout.Width(220f)))
        {
            string path = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(path) && scenes.FindIndex(s => s.path == path) < 0)
                scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // =============================================================== keybinds

    void DrawKeybinds()
    {
        Header("KEYBINDS", "Registry defaults live in InputBindings.cs — players override them in Settings (PlayerPrefs).");

        string category = null;
        foreach (var action in InputBindings.Actions)
        {
            if (action.category != category)
            {
                category = action.category;
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(category.ToUpperInvariant(), EditorStyles.boldLabel);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(action.label, GUILayout.Width(220f));
            GUILayout.Label("default: " + action.defaultKey, EditorStyles.miniLabel, GUILayout.Width(140f));

            var currentKey = InputBindings.Get(action.id);
            var newKey = (KeyCode)EditorGUILayout.EnumPopup(currentKey, GUILayout.Width(160f));
            if (newKey != currentKey) InputBindings.Set(action.id, newKey);
            EditorGUILayout.EndHorizontal();
        }

        GUILayout.Space(10f);
        if (GUILayout.Button("Reset Local Overrides To Defaults", GUILayout.Width(260f), GUILayout.Height(26f)))
            InputBindings.ResetToDefaults();

        GUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            "To CHANGE a default for all players, edit the Register(...) line in InputBindings.cs — " +
            "new actions registered there appear here and in the in-game rebind UI automatically.",
            MessageType.Info);
    }
}
