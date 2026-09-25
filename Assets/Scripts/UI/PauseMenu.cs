using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game ESC pause menu (COD-style). Self-installing — lives on a persistent
/// object, only reacts while a match scene is active.
///
///  RESUME       — closes, re-locks the cursor.
///  SETTINGS     — embeds the SAME reusable SettingsPanel used by the main menu.
///  LEAVE MATCH  — confirmation first (warns the host their server stops for
///                 everyone), then CODNetworkManager.Leave() tears the session
///                 down properly and returns to the main menu.
///
/// NOTE: multiplayer — the game does NOT freeze while paused.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    static PauseMenu instance;

    Canvas canvas;
    RectTransform homeView;
    RectTransform settingsView;
    RectTransform confirmView;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("PauseMenu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PauseMenu>();
    }

    bool InMatch => SceneManager.GetActiveScene().name != "StartMenu";

    void Update()
    {
        if (!InMatch)
        {
            if (IsOpen) Close();
            return;
        }

        if (InputBindings.Down("pause"))
        {
            if (!IsOpen) Open();
            else if (settingsView != null && settingsView.gameObject.activeSelf) ShowHome();
            else if (confirmView != null && confirmView.gameObject.activeSelf) ShowHome();
            else Close();
        }
    }

    // ------------------------------------------------------------ open/close

    void Open()
    {
        if (canvas == null) BuildUI();
        EnsureEventSystem(); // arena scenes have no EventSystem — without one, nothing is clickable
        canvas.enabled = true;
        ShowHome();
        IsOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    void Close()
    {
        if (canvas != null) canvas.enabled = false;
        IsOpen = false;

        if (InMatch)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void ShowHome()
    {
        homeView.gameObject.SetActive(true);
        settingsView.gameObject.SetActive(false);
        confirmView.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------ UI

    void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var dim = UITheme.Image("Dim", transform, new Color(0f, 0f, 0f, 0.78f));
        UITheme.Stretch(dim.rectTransform);

        // ---------------- home view ----------------
        homeView = UITheme.Rect("Home", dim.transform);
        UITheme.Stretch(homeView);

        var title = UITheme.Text("Title", homeView, "GAME PAUSED", 44, UITheme.TextMain,
            FontStyles.Bold | FontStyles.Italic);
        UITheme.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(120f, -140f), new Vector2(600f, 60f));
        title.characterSpacing = 4;

        var accent = UITheme.Image("TitleAccent", homeView, UITheme.Accent);
        UITheme.Place(accent.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(120f, -205f), new Vector2(320f, 3f));

        MenuButton(homeView, "RESUME", 0, Close);
        MenuButton(homeView, "SETTINGS", 1, () =>
        {
            homeView.gameObject.SetActive(false);
            settingsView.gameObject.SetActive(true);
        });
        MenuButton(homeView, "LEAVE MATCH", 2, () =>
        {
            homeView.gameObject.SetActive(false);
            confirmView.gameObject.SetActive(true);
        });

        // ---------------- settings view ----------------
        settingsView = UITheme.Rect("Settings", dim.transform);
        UITheme.Stretch(settingsView);

        var sHeader = UITheme.Text("Header", settingsView, "SETTINGS", 34, UITheme.TextMain, FontStyles.Bold);
        UITheme.Place(sHeader.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(120f, -110f), new Vector2(500f, 48f));

        var host = UITheme.Image("SettingsHost", settingsView, UITheme.Panel);
        host.rectTransform.anchorMin = new Vector2(0f, 0f);
        host.rectTransform.anchorMax = new Vector2(0f, 1f);
        host.rectTransform.pivot = new Vector2(0f, 1f);
        host.rectTransform.anchoredPosition = new Vector2(120f, -170f);
        host.rectTransform.sizeDelta = new Vector2(780f, -260f);
        SettingsPanel.Build(host.transform);

        var back = UITheme.Button("Back", settingsView, "< BACK", 16, UITheme.PanelSoft, UITheme.TextMain, ShowHome);
        UITheme.Place((RectTransform)back.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(120f, 40f), new Vector2(160f, 44f));

        // ---------------- leave confirmation ----------------
        confirmView = UITheme.Rect("Confirm", dim.transform);
        UITheme.Stretch(confirmView);

        var box = UITheme.Image("Box", confirmView, UITheme.Panel);
        UITheme.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(560f, 240f));

        var cTitle = UITheme.Text("Title", box.transform, "LEAVE MATCH?", 26, UITheme.TextMain, FontStyles.Bold,
            TextAlignmentOptions.Center);
        UITheme.Place(cTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(520f, 34f));

        var cBody = UITheme.Text("Body", box.transform,
            "You will exit to the main menu.\nIf you are the host, the server stops and ALL players are disconnected.",
            14, UITheme.TextDim, FontStyles.Normal, TextAlignmentOptions.Center);
        UITheme.Place(cBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 8f), new Vector2(500f, 60f));
        cBody.textWrappingMode = TextWrappingModes.Normal;

        var stay = UITheme.Button("Stay", box.transform, "CANCEL", 15, UITheme.PanelSoft, UITheme.TextMain, ShowHome);
        UITheme.Place((RectTransform)stay.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-110f, 24f), new Vector2(180f, 44f));

        var leave = UITheme.Button("Leave", box.transform, "LEAVE", 15, UITheme.Accent, UITheme.TextOnAccent, () =>
        {
            Close();
            var manager = CODNetworkManager.Instance;
            if (manager != null) manager.Leave();
            else SceneManager.LoadScene("StartMenu");
        });
        UITheme.Place((RectTransform)leave.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(110f, 24f), new Vector2(180f, 44f));
    }

    void MenuButton(Transform parent, string label, int index, UnityEngine.Events.UnityAction onClick)
    {
        var button = UITheme.Button("Btn_" + label, parent, label, 19,
            index == 0 ? UITheme.Accent : UITheme.PanelSoft,
            index == 0 ? UITheme.TextOnAccent : UITheme.TextMain, onClick);
        UITheme.Place((RectTransform)button.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(120f, -260f - index * 66f), new Vector2(340f, 54f));
        var text = button.GetComponentInChildren<TextMeshProUGUI>();
        text.characterSpacing = 3;
        text.alignment = TextAlignmentOptions.Center;
    }
}
