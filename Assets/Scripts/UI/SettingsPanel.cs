using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable, fully code-built settings screen — the SAME panel is embedded in
/// the main menu SETTINGS tab and the in-game pause menu.
///
/// Sections: AUDIO, VIDEO (granular URP), MOUSE & CONTROLS, KEYBINDS.
/// Every control saves instantly through GameSettings / InputBindings.
/// The keybind list is generated from the InputBindings registry, so new
/// actions appear here automatically — this UI never needs touching again.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    RectTransform content;
    float y;

    // rebind state
    string awaitingActionId;
    TextMeshProUGUI awaitingLabel;
    readonly List<(string id, TextMeshProUGUI label)> keyLabels = new List<(string, TextMeshProUGUI)>();

    /// <summary>Builds a settings panel filling <paramref name="parent"/>.</summary>
    public static SettingsPanel Build(Transform parent)
    {
        var root = UITheme.Rect("SettingsPanel", parent);
        UITheme.Stretch(root);
        var panel = root.gameObject.AddComponent<SettingsPanel>();
        panel.BuildUI();
        return panel;
    }

    void BuildUI()
    {
        // ---- scroll view ----
        var scrollGO = UITheme.Rect("Scroll", transform);
        UITheme.Stretch(scrollGO);
        var scroll = scrollGO.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;

        var viewport = UITheme.Rect("Viewport", scrollGO);
        UITheme.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImage = viewport.gameObject.AddComponent<Image>();
        vpImage.color = new Color(0f, 0f, 0f, 0.01f); // raycast catcher for drag

        content = UITheme.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);

        scroll.viewport = viewport;
        scroll.content = content;

        y = 16f;

        // ================= AUDIO =================
        Section("AUDIO");
        SliderRow("Master Volume", 0f, 1f, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, "P0");

        // ================= VIDEO =================
        Section("VIDEO");
        SliderRow("Field of View", 55f, 110f, () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = v, "F0");
        SliderRow("Render Scale", 0.5f, 2f, () => GameSettings.RenderScale, v => GameSettings.RenderScale = v, "P0");
        SliderRow("Shadow Distance", 0f, 150f, () => GameSettings.ShadowDistance, v => GameSettings.ShadowDistance = v, "F0");
        CyclerRow("Quality Preset", () => QualitySettings.names[GameSettings.QualityLevel], dir =>
        {
            int n = QualitySettings.names.Length;
            GameSettings.QualityLevel = (GameSettings.QualityLevel + dir + n) % n;
        });
        CyclerRow("Anti-Aliasing (MSAA)", () => GameSettings.Antialiasing <= 1 ? "OFF" : GameSettings.Antialiasing + "x", dir =>
        {
            int[] steps = { 1, 2, 4, 8 };
            int i = System.Array.IndexOf(steps, GameSettings.Antialiasing);
            GameSettings.Antialiasing = steps[Mathf.Clamp(i + dir, 0, steps.Length - 1)];
        });
        ToggleRow("V-Sync", () => GameSettings.VSync, v => GameSettings.VSync = v);
        ToggleRow("Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);

        // ================= CONTROLS =================
        Section("MOUSE & CONTROLS");
        SliderRow("Mouse Sensitivity", 0.1f, 3f, () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v, "F2");
        ToggleRow("Crouch: Toggle (off = Hold)", () => InputBindings.CrouchIsToggle, v => InputBindings.CrouchIsToggle = v);
        CyclerRow("Tac Sprint Trigger", () => InputBindings.TacSprintMode == 0 ? "DOUBLE-TAP SPRINT" : "AUTO (HOLD SPRINT)",
            dir => InputBindings.TacSprintMode = 1 - InputBindings.TacSprintMode);
        ToggleRow("Fire While Sprinting", () => InputBindings.FireWhileSprinting, v => InputBindings.FireWhileSprinting = v);

        // ================= KEYBINDS =================
        Section("KEYBINDS");
        string lastCategory = null;
        foreach (var action in InputBindings.Actions)
        {
            if (action.category != lastCategory)
            {
                lastCategory = action.category;
                var cat = UITheme.Text("Cat_" + action.category, content, action.category.ToUpperInvariant(), 13,
                    UITheme.TextDim, FontStyles.Bold);
                UITheme.TL(cat.rectTransform, 36, y, 400, 20);
                cat.characterSpacing = 3;
                y += 26;
            }
            KeybindRow(action);
        }

        var reset = UITheme.Button("ResetBinds", content, "RESET KEYBINDS TO DEFAULT", 13,
            UITheme.PanelHover, UITheme.TextMain, () =>
            {
                InputBindings.ResetToDefaults();
                RefreshKeyLabels();
            });
        UITheme.TL((RectTransform)reset.transform, 24, y, 280, 34);
        y += 50;

        content.sizeDelta = new Vector2(0f, y + 20f);
    }

    // ------------------------------------------------------------ widgets

    void Section(string title)
    {
        y += 12f;
        var text = UITheme.Text("Sec_" + title, content, title, 20, UITheme.Accent, FontStyles.Bold);
        UITheme.TL(text.rectTransform, 24, y, 500, 28);
        text.characterSpacing = 4;
        y += 34;
        var line = UITheme.Image("Line", content, new Color(1f, 1f, 1f, 0.08f));
        UITheme.TL(line.rectTransform, 24, y, 640, 1);
        y += 12;
    }

    RectTransform Row(string label)
    {
        var row = UITheme.Rect("Row_" + label, content);
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(0f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = new Vector2(24f, -y);
        row.sizeDelta = new Vector2(660f, 34f);

        var text = UITheme.Text("Label", row, label, 15, UITheme.TextMain);
        UITheme.TL(text.rectTransform, 0, 6, 300, 22);

        y += 40f;
        return row;
    }

    void SliderRow(string label, float min, float max, System.Func<float> get, System.Action<float> set, string format)
    {
        var row = Row(label);

        var value = UITheme.Text("Value", row, "", 14, UITheme.Accent, FontStyles.Bold, TextAlignmentOptions.Right);
        UITheme.TL(value.rectTransform, 560, 6, 100, 22);

        // slider built by hand (background / fill / handle)
        var bg = UITheme.Image("SliderBG", row, UITheme.PanelSoft);
        UITheme.TL(bg.rectTransform, 310, 12, 240, 10);

        var fillArea = UITheme.Rect("FillArea", bg.transform);
        UITheme.Stretch(fillArea);
        var fill = UITheme.Image("Fill", fillArea, UITheme.Accent);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;

        var handleArea = UITheme.Rect("HandleArea", bg.transform);
        UITheme.Stretch(handleArea);
        var handle = UITheme.Image("Handle", handleArea, UITheme.TextMain);
        handle.rectTransform.sizeDelta = new Vector2(12f, 18f);

        var slider = bg.gameObject.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(get());

        string Fmt(float v) => format == "P0" ? Mathf.RoundToInt(v * 100f) + "%" : v.ToString(format);
        value.text = Fmt(get());

        slider.onValueChanged.AddListener(v =>
        {
            set(v);
            value.text = Fmt(v);
        });
    }

    void ToggleRow(string label, System.Func<bool> get, System.Action<bool> set)
    {
        var row = Row(label);
        Button button = null;
        TextMeshProUGUI state = null;

        button = UITheme.Button("Toggle", row, "", 13, UITheme.PanelSoft, UITheme.TextMain, () =>
        {
            set(!get());
            Refresh();
        });
        UITheme.TL((RectTransform)button.transform, 310, 2, 110, 30);
        state = button.GetComponentInChildren<TextMeshProUGUI>();

        void Refresh()
        {
            bool on = get();
            state.text = on ? "ON" : "OFF";
            state.color = on ? UITheme.Accent : UITheme.TextDim;
        }
        Refresh();
    }

    void CyclerRow(string label, System.Func<string> get, System.Action<int> cycle)
    {
        var row = Row(label);

        var value = UITheme.Text("Value", row, get(), 13, UITheme.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        UITheme.TL(value.rectTransform, 350, 6, 210, 22);

        var prev = UITheme.Button("Prev", row, "<", 15, UITheme.PanelSoft, UITheme.TextMain, () => { cycle(-1); value.text = get(); });
        UITheme.TL((RectTransform)prev.transform, 310, 2, 34, 30);
        var next = UITheme.Button("Next", row, ">", 15, UITheme.PanelSoft, UITheme.TextMain, () => { cycle(+1); value.text = get(); });
        UITheme.TL((RectTransform)next.transform, 566, 2, 34, 30);
    }

    void KeybindRow(InputBindings.ActionDef action)
    {
        var row = Row(action.label);

        Button button = null;
        button = UITheme.Button("Bind", row, "", 13, UITheme.PanelSoft, UITheme.TextMain, () =>
        {
            // begin capture
            awaitingActionId = action.id;
            awaitingLabel = button.GetComponentInChildren<TextMeshProUGUI>();
            awaitingLabel.text = "PRESS A KEY...";
            awaitingLabel.color = UITheme.Accent;
        });
        UITheme.TL((RectTransform)button.transform, 310, 2, 250, 30);

        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.text = KeyName(InputBindings.Get(action.id));
        keyLabels.Add((action.id, label));
    }

    void RefreshKeyLabels()
    {
        foreach (var (id, label) in keyLabels)
        {
            label.text = KeyName(InputBindings.Get(id));
            label.color = UITheme.TextMain;
        }
    }

    static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Mouse0: return "LEFT MOUSE";
            case KeyCode.Mouse1: return "RIGHT MOUSE";
            case KeyCode.Mouse2: return "MIDDLE MOUSE";
            case KeyCode.None: return "—";
            default: return key.ToString().ToUpperInvariant();
        }
    }

    void Update()
    {
        // keybind capture
        if (awaitingActionId == null || !Input.anyKeyDown) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            RefreshKeyLabels();
            awaitingActionId = null;
            return;
        }

        foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (!Input.GetKeyDown(key)) continue;

            InputBindings.Set(awaitingActionId, key);
            awaitingActionId = null;
            RefreshKeyLabels();
            break;
        }
    }
}
