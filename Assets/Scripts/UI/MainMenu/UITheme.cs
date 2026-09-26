using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared visual language of all COD UI + code-driven UI building helpers.
/// Dark tactical palette, orange accent, sharp panels. Every menu element is
/// built through these helpers so the whole game stays consistent.
/// </summary>
public static class UITheme
{
    // palette
    public static readonly Color Background = new Color32(8, 10, 13, 255);
    public static readonly Color Panel = new Color32(14, 17, 22, 235);
    public static readonly Color PanelSoft = new Color32(22, 27, 34, 220);
    public static readonly Color PanelHover = new Color32(32, 39, 48, 235);
    public static readonly Color Accent = new Color32(255, 138, 0, 255);
    public static readonly Color AccentDim = new Color32(160, 90, 10, 255);
    public static readonly Color TextMain = new Color32(233, 236, 240, 255);
    public static readonly Color TextDim = new Color32(140, 148, 158, 255);
    public static readonly Color TextOnAccent = new Color32(18, 14, 8, 255);
    public static readonly Color Positive = new Color32(84, 196, 112, 255);
    public static readonly Color Locked = new Color32(70, 76, 84, 255);

    #region Builders

    // Every builder below is find-or-create.
    //
    // The frontend is saved scene content, and play mode re-runs these methods
    // to re-attach button listeners — closures cannot be serialised, so that
    // part genuinely has to happen every run. If the builders always made a new
    // GameObject, a second TopBar would appear on top of the saved one every
    // time you pressed Play, and the scene you were editing would not be the
    // scene you were looking at.
    //
    // Reusing the existing object also keeps its fileID stable, so inspector
    // references and prefab links into the menu survive.

    /// <summary>
    /// The child named <paramref name="name"/>, with a <typeparamref name="T"/>
    /// on it, creating whichever part is missing.
    /// </summary>
    static T Adopt<T>(string name, Transform parent) where T : Component
    {
        Transform existing = parent != null ? parent.Find(name) : null;

        if (existing == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            existing = go.transform;
        }
        else if (!(existing is RectTransform))
        {
            // A plain Transform where UI is expected cannot be converted in
            // place, and silently returning it would produce a widget that
            // never lays out.
            Debug.LogError($"UITheme: '{name}' under '{(parent != null ? parent.name : "null")}' " +
                           $"is not a RectTransform — UI cannot be built on it.");
            return null;
        }

        return existing.GetComponent<T>() ?? existing.gameObject.AddComponent<T>();
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        return Adopt<RectTransform>(name, parent);
    }

    public static Image Image(string name, Transform parent, Color color)
    {
        var img = Adopt<Image>(name, parent);
        if (img == null) return null;
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string content, float size,
        Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = Adopt<TextMeshProUGUI>(name, parent);
        if (text == null) return null;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = align;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    public static Button Button(string name, Transform parent, string label, float fontSize,
        Color background, Color textColor, UnityEngine.Events.UnityAction onClick)
    {
        Image img = Image(name, parent, background);
        if (img == null) return null;
        Button button = img.GetComponent<Button>() ?? img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        // Clear first: on an adopted button this method runs again every play,
        // and AddListener stacks, so a second run would fire the handler twice.
        button.onClick.RemoveAllListeners();
        if (onClick != null) button.onClick.AddListener(onClick);

        var text = Text("Label", img.transform, label, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    public static TMP_InputField Input(string name, Transform parent, string placeholder, Vector2 size)
    {
        Image bg = Image(name, parent, PanelSoft);
        if (bg == null) return null;
        bg.rectTransform.sizeDelta = size;

        var input = bg.GetComponent<TMP_InputField>() ?? bg.gameObject.AddComponent<TMP_InputField>();

        RectTransform area = Rect("TextArea", bg.transform);
        Stretch(area);
        area.offsetMin = new Vector2(10, 4);
        area.offsetMax = new Vector2(-10, -4);
        if (area.GetComponent<RectMask2D>() == null) area.gameObject.AddComponent<RectMask2D>();

        var ph = Text("Placeholder", area, placeholder, size.y * 0.42f, TextDim, FontStyles.Italic, TextAlignmentOptions.Left);
        Stretch(ph.rectTransform);
        var txt = Text("Text", area, "", size.y * 0.42f, TextMain, FontStyles.Normal, TextAlignmentOptions.Left);
        Stretch(txt.rectTransform);
        txt.raycastTarget = true;

        input.textViewport = area;
        input.placeholder = ph;
        input.textComponent = txt;
        input.caretColor = Accent;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        return input;
    }

    #endregion

    #region Layout helpers

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    /// <summary>Top-left anchored placement (y grows downward).</summary>
    public static void TL(RectTransform rt, float x, float y, float w, float h) =>
        Place(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

    #endregion
}
