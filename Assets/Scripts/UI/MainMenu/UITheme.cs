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

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static Image Image(string name, Transform parent, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        return img;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string content, float size,
        Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
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
        Button button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(onClick);

        var text = Text("Label", img.transform, label, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    public static TMP_InputField Input(string name, Transform parent, string placeholder, Vector2 size)
    {
        Image bg = Image(name, parent, PanelSoft);
        bg.rectTransform.sizeDelta = size;

        var input = bg.gameObject.AddComponent<TMP_InputField>();

        RectTransform area = Rect("TextArea", bg.transform);
        Stretch(area);
        area.offsetMin = new Vector2(10, 4);
        area.offsetMax = new Vector2(-10, -4);
        area.gameObject.AddComponent<RectMask2D>();

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
