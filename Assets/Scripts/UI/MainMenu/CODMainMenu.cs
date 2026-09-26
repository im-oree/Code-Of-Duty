using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu of CODE OF DUTY. Tabbed layout in the style of modern AAA
/// shooters: top tab bar, big mode cards, operator gallery with realtime
/// switching, loadout screen, placeholder tabs for future features.
///
/// ## The scene is the single source of truth
///
/// The canvas, the tabs, the panels and the 3D stage are ordinary saved
/// GameObjects in StartMenu.unity. You can select them, move them and edit
/// them, in edit mode and in play mode, and what you see is what ships.
///
/// <see cref="Start"/> adopts them. The builder methods below are find-or-create
/// throughout (see <see cref="UITheme"/>), so running them against a scene that
/// already contains the menu updates properties and re-attaches listeners
/// without replacing a single object. Listeners are the reason they run at all:
/// a C# closure cannot be serialised, so button wiring is the one thing that
/// genuinely has to be re-established every play.
///
/// Nothing here destroys scene content, and nothing constructs the menu behind
/// your back. Regenerating the layout from code is an explicit editor command,
/// COD / Main Menu / Generate Frontend Scene.
/// </summary>
public class CODMainMenu : MonoBehaviour
{
    /// <summary>Root all UI is built under (child object, so editor previews can be swapped cleanly).</summary>
    [SerializeField] Transform uiRoot;

    #region Editor tools — the scene is the source of truth

    // There is no bake here, and nothing rebuilds the menu behind your back.
    //
    // This class used to have four separate construction paths: an
    // [ExecuteAlways] bake on OnEnable, a purge in Awake, a rebuild in Start,
    // and a [RuntimeInitializeOnLoadMethod] that spawned a second menu object
    // if it could not find one. They all wrote to the same scene objects, in an
    // order nobody controlled, and two of them destroyed what the others had
    // just made. That is where the second operator came from: DestroyPreview
    // removes objects with DestroyImmediate, DedupePreview removes them with
    // Destroy (deferred to end of frame), and MenuStage.Create() ran in between
    // — so it counted the not-yet-destroyed copy as absent and made another.
    //
    // The menu is now plain saved scene content. Play mode adopts it and
    // attaches behaviour; it never creates or destroys it. Regeneration is an
    // explicit editor command, below, because regenerating a scene is a thing
    // you should have to ask for.

#if UNITY_EDITOR

    [UnityEditor.MenuItem("COD/Main Menu/Diagnose Scene")]
    static void DiagnoseScene()
    {
        var menus = FindObjectsByType<CODMainMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var stages = FindObjectsByType<MenuStage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var displays = FindObjectsByType<OperatorDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var report = new System.Text.StringBuilder();
        report.AppendLine("[CODMainMenu] SCENE DIAGNOSIS (" + SceneManager.GetActiveScene().name + ")");
        report.AppendLine($"  CODMainMenu instances : {menus.Length}  {(menus.Length == 1 ? "OK" : "<-- SHOULD BE 1")}");
        report.AppendLine($"  MenuStage instances   : {stages.Length}  {(stages.Length == 1 ? "OK" : "<-- SHOULD BE 1")}");
        report.AppendLine($"  OperatorDisplay       : {displays.Length}  {(displays.Length == 1 ? "OK" : "<-- SHOULD BE 1")}");
        report.AppendLine($"  Canvas instances      : {canvases.Length}");

        foreach (var d in displays)
            report.AppendLine($"    - operator '{d.name}' under '{(d.transform.parent != null ? d.transform.parent.name : "ROOT")}'");

        // A RectTransform at the scene root is invisible: uGUI only draws inside
        // a Canvas, and it does not warn about UI parked outside one.
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.transform is RectTransform)
                report.AppendLine($"  ORPHANED UI           : '{root.name}' is a RectTransform at the scene root and will NOT be drawn");
        }

        var menu = menus.Length > 0 ? menus[0] : null;
        if (menu != null)
        {
            report.AppendLine($"  menu canvas           : {(menu.canvas != null ? menu.canvas.name : "NULL <-- UI WILL BE MISSING")}");
            if (menu.canvas != null && menu.canvas.transform.childCount == 0)
                report.AppendLine("  canvas has NO CHILDREN <-- the panels are somewhere else");
        }

        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Rebuilds the menu into the open scene from the code below.
    ///
    /// Destructive and deliberately manual: it exists so a code change to the
    /// layout can be materialised into the scene, after which the scene is
    /// again the source of truth. Save the scene to keep the result.
    /// </summary>
    [UnityEditor.MenuItem("COD/Main Menu/Generate Frontend Scene")]
    static void GenerateFrontendScene()
    {
        var menu = FindAnyObjectByType<CODMainMenu>(FindObjectsInactive.Include);
        if (menu == null)
        {
            Debug.LogError("[CODMainMenu] no CODMainMenu in the open scene — nothing to generate into.");
            return;
        }

        if (!UnityEditor.EditorUtility.DisplayDialog(
                "Generate Frontend Scene",
                "This replaces the menu UI in the open scene with a fresh build from code. " +
                "Any hand edits to the generated objects are lost.\n\nContinue?",
                "Generate", "Cancel"))
            return;

        menu.AdoptCanvas();
        menu.BuildTopBar();
        menu.BuildTabs();
        menu.BuildLobbyOverlay();
        menu.SelectTab("PLAY");
        menu.stage = MenuStage.EnsureInScene();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        Debug.Log("[CODMainMenu] frontend generated into the open scene — save the scene to persist it.");
    }

#endif

    #endregion

    [SerializeField] MenuStage stage;
    [SerializeField] Canvas canvas;
    CODNetworkDiscovery discovery;

    // tabs (rebuilt from the baked hierarchy on every startup)
    readonly List<(string id, Button button, TextMeshProUGUI label, Image underline)> tabButtons = new();
    readonly Dictionary<string, RectTransform> tabPanels = new();
    [SerializeField] string activeTab;

    // play tab
    TMP_InputField nameInput;
    TMP_InputField hostRoomInput;
    TMP_InputField hostMaxInput;
    TMP_InputField directIpInput;
    RectTransform browserContent;
    TextMeshProUGUI browserStatus;
    readonly Dictionary<long, CODServerResponse> foundServers = new();
    bool quickSearching;

    // lobby overlay
    CanvasGroup lobbyGroup;
    RectTransform lobbyList;
    TextMeshProUGUI lobbyTitle;
    Button lobbyStartButton;

    // operators / loadout live state (rebuilt from the baked cards on every run)
    readonly List<(int index, Image frame)> operatorCards = new();
    readonly List<List<(string id, Image frame)>> loadoutCards = new();
    List<List<WeaponDatabase.Entry>> loadoutOptions;
    List<int> loadoutSelection;

    /// <summary>
    /// Adopts the menu that is already in the scene and attaches behaviour to it.
    ///
    /// Nothing here creates or destroys scene content. Button listeners and
    /// event subscriptions are C# closures, which cannot be serialised, so they
    /// are the one thing that genuinely has to be re-established every run —
    /// and re-attaching a listener does not require rebuilding the object it
    /// belongs to.
    ///
    /// If a piece is missing this says which one and stops, rather than
    /// silently building a replacement. A menu that half-works is harder to
    /// diagnose than one that refuses to start and names the problem.
    /// </summary>
    void Start()
    {
        if (!AdoptCanvas())
        {
            Debug.LogError(
                "[CODMainMenu] no Canvas found under this object. The frontend lives in the " +
                "scene: run COD / Main Menu / Diagnose Scene, or regenerate it with " +
                "COD / Main Menu / Generate Frontend Scene.");
            enabled = false;
            return;
        }

        stage = MenuStage.EnsureInScene();

        BuildTopBar();
        BuildTabs();
        BuildLobbyOverlay();

        var manager = CODNetworkManager.EnsureExists();
        discovery = manager != null ? manager.discovery : null;

        CODLobbyPlayer.LobbyChanged += RefreshLobby;
        CODLobbyPlayer.LocalPlayerJoined += ShowLobby;
        CODNetworkManager.ClientError += OnClientError;

        SelectTab(string.IsNullOrEmpty(activeTab) ? "PLAY" : activeTab);
        StartBrowserDiscovery();
    }

    void OnDestroy()
    {
        CODLobbyPlayer.LobbyChanged -= RefreshLobby;
        CODLobbyPlayer.LocalPlayerJoined -= ShowLobby;
        CODNetworkManager.ClientError -= OnClientError;
        if (discovery != null) discovery.OnServerFound.RemoveListener(OnServerFound);
    }

    #region Canvas & top bar

    /// <summary>
    /// Takes ownership of the Canvas already in the scene, creating one only if
    /// the scene has none.
    ///
    /// The Canvas and its panels are saved scene objects: they keep their
    /// fileIDs, their inspector edits, and their place in the hierarchy across
    /// play mode. Replacing them every run is what made the menu impossible to
    /// edit — you were always looking at something the editor was about to
    /// throw away.
    /// </summary>
    bool AdoptCanvas()
    {
        canvas = GetComponentInChildren<Canvas>(true);

        if (canvas == null)
        {
            var root = new GameObject("MenuUI");
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            Debug.LogWarning("[CODMainMenu] scene had no menu Canvas — created one. " +
                             "Run COD / Main Menu / Generate Frontend Scene and save.");
        }

        uiRoot = canvas.transform;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        return true;
    }

    void BuildTopBar()
    {
        // full-width strip behind the tab bar (stretch-anchored so children
        // measure from the real screen edge on every resolution)
        var bar = UITheme.Image("TopBar", uiRoot, new Color(0f, 0f, 0f, 0.55f));
        var barRect = bar.rectTransform;
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.offsetMin = new Vector2(0f, -86f);
        barRect.offsetMax = Vector2.zero;

        var title = UITheme.Text("Logo", bar.transform, "CODE OF DUTY", 30, UITheme.TextMain,
            FontStyles.Bold | FontStyles.Italic);
        UITheme.TL(title.rectTransform, 48, 24, 420, 40);
        title.characterSpacing = 2;

        var accent = UITheme.Image("LogoAccent", bar.transform, UITheme.Accent);
        UITheme.TL(accent.rectTransform, 48, 66, 258, 3);

        // tab buttons
        string[] tabs = { "PLAY", "OPERATORS", "LOADOUT", "BARRACKS", "STORE", "SETTINGS" };
        float x = 520;
        foreach (string tab in tabs)
        {
            string id = tab;
            var img = UITheme.Image($"Tab_{tab}", bar.transform, Color.clear);
            UITheme.TL(img.rectTransform, x, 18, 170, 52);
            var button = img.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => SelectTab(id));

            var label = UITheme.Text("Label", img.transform, tab, 21, UITheme.TextDim,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UITheme.Stretch(label.rectTransform);
            label.characterSpacing = 4;

            var underline = UITheme.Image("Underline", img.transform, Color.clear);
            UITheme.Place(underline.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 2f), new Vector2(120, 3));

            tabButtons.Add((id, button, label, underline));
            x += 178;
        }

        // player identity, right side
        var nameLabel = UITheme.Text("NameLabel", bar.transform, "OPERATOR ID", 12, UITheme.TextDim, FontStyles.Bold);
        UITheme.Place(nameLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-250, -16), new Vector2(200, 16));

        nameInput = UITheme.Input("NameInput", bar.transform, "Player", new Vector2(200, 34));
        UITheme.Place(((RectTransform)nameInput.transform), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -34), new Vector2(200, 34));
        nameInput.text = CODNetworkManager.PlayerName;
        nameInput.onEndEdit.AddListener(value =>
        {
            if (!string.IsNullOrWhiteSpace(value)) CODNetworkManager.PlayerName = value.Trim();
        });

        // bottom bar: version + quit (full-width, stretch-anchored)
        var bottom = UITheme.Image("BottomBar", uiRoot, new Color(0f, 0f, 0f, 0.45f));
        var bottomRect = bottom.rectTransform;
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0f);
        bottomRect.pivot = new Vector2(0.5f, 0f);
        bottomRect.offsetMin = Vector2.zero;
        bottomRect.offsetMax = new Vector2(0f, 46f);

        var hint = UITheme.Text("Hint", bottom.transform, "LAN OPERATIONS  //  SERVER-AUTHORITATIVE  //  ALPHA BUILD", 13,
            UITheme.TextDim, FontStyles.Normal, TextAlignmentOptions.Left);
        UITheme.TL(hint.rectTransform, 48, 14, 800, 20);
        hint.characterSpacing = 3;

        UITheme.Place(((RectTransform)UITheme.Button("Quit", bottom.transform, "QUIT GAME", 14,
            new Color(0.5f, 0.12f, 0.1f, 0.9f), UITheme.TextMain, Application.Quit).transform),
            new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(130, 32));
    }

    void SelectTab(string id)
    {
        activeTab = id;
        foreach (var (tabId, _, label, underline) in tabButtons)
        {
            bool active = tabId == id;
            label.color = active ? UITheme.TextMain : UITheme.TextDim;
            underline.color = active ? UITheme.Accent : Color.clear;
        }
        foreach (var pair in tabPanels)
        {
            if (pair.Value.gameObject.activeSelf != (pair.Key == id))
                pair.Value.gameObject.SetActive(pair.Key == id);
            if (pair.Key == id)
            {
                if (Application.isPlaying) StartCoroutine(FadeIn(pair.Value));
                else
                {
                    var g = pair.Value.GetComponent<CanvasGroup>();
                    if (g != null) g.alpha = 1f;
                    pair.Value.anchoredPosition = Vector2.zero;
                }
            }
        }
    }

    IEnumerator FadeIn(RectTransform panel)
    {
        var group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
        Vector2 basePos = Vector2.zero;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 5f;
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            group.alpha = eased;
            panel.anchoredPosition = basePos + new Vector2(0f, -18f * (1f - eased));
            yield return null;
        }
        group.alpha = 1f;
        panel.anchoredPosition = basePos;
    }

    RectTransform CreateTabPanel(string id)
    {
        var panel = UITheme.Rect($"Panel_{id}", uiRoot);
        UITheme.Stretch(panel);
        panel.offsetMin = new Vector2(0, 46);
        panel.offsetMax = new Vector2(0, -86);
        panel.gameObject.SetActive(false);
        tabPanels[id] = panel;
        return panel;
    }

    #endregion

    #region PLAY tab

    void BuildTabs()
    {
        // These are pure construction from constants — no scene lookups, no
        // player data, nothing that can fail for an environmental reason. The
        // old code wrapped each one in a try/catch that logged and continued,
        // which turned "this tab has a bug" into "the menu is subtly missing
        // something" and left a half-built hierarchy for the next pass to trip
        // over. If one of these throws, the exception and its stack trace are
        // the most useful thing that can happen.
        BuildPlayTab();
        BuildOperatorsTab();
        BuildLoadoutTab();
        BuildBarracksTab();
        BuildStoreTab();
        BuildSettingsTab();
    }

    void BuildSettingsTab()
    {
        var panel = CreateTabPanel("SETTINGS");

        var header = UITheme.Text("Header", panel, "SETTINGS", 30, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 48, 36, 600, 40);
        header.characterSpacing = 3;

        // reusable panel (same one the in-game pause menu embeds)
        var host = UITheme.Image("SettingsHost", panel, UITheme.Panel);
        host.rectTransform.anchorMin = new Vector2(0f, 0f);
        host.rectTransform.anchorMax = new Vector2(0f, 1f);
        host.rectTransform.pivot = new Vector2(0f, 1f);
        host.rectTransform.anchoredPosition = new Vector2(48f, -100f);
        host.rectTransform.sizeDelta = new Vector2(760f, -160f);
        SettingsPanel.Build(host.transform);
    }

    void BuildPlayTab()
    {
        var panel = CreateTabPanel("PLAY");

        // left column: mode cards
        float y = 40;
        y = AddModeCard(panel, y, "QUICK PLAY", "Find and join the first LAN match available.", OnQuickPlay);
        y = AddModeCard(panel, y, "HOST MATCH", "Create a LAN room others can join.", null, card =>
        {
            hostRoomInput = UITheme.Input("RoomName", card, "Room name", new Vector2(190, 34));
            UITheme.TL((RectTransform)hostRoomInput.transform, 24, 88, 190, 34);
            hostMaxInput = UITheme.Input("MaxPlayers", card, "Max", new Vector2(64, 34));
            UITheme.TL((RectTransform)hostMaxInput.transform, 224, 88, 64, 34);
            hostMaxInput.text = "8";
            var host = UITheme.Button("Host", card, "HOST", 16, UITheme.Accent, UITheme.TextOnAccent, OnHostMatch);
            UITheme.TL((RectTransform)host.transform, 300, 88, 110, 34);
        }, 140);
        y = AddModeCard(panel, y, "DIRECT CONNECT", "Join a server by IP address.", null, card =>
        {
            directIpInput = UITheme.Input("Ip", card, "192.168.x.x", new Vector2(266, 34));
            UITheme.TL((RectTransform)directIpInput.transform, 24, 88, 266, 34);
            var join = UITheme.Button("Join", card, "JOIN", 16, UITheme.Accent, UITheme.TextOnAccent, OnDirectConnect);
            UITheme.TL((RectTransform)join.transform, 300, 88, 110, 34);
        }, 140);
        AddModeCard(panel, y, "PRIVATE MATCH", "Solo warm-up against the arena. Internal server, no LAN.", () =>
        {
            CODNetworkManager.EnsureExists()?.StartSoloGame();
        });

        // right column: server browser
        var browser = UITheme.Image("Browser", panel, UITheme.Panel);
        UITheme.Place(browser.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -40), new Vector2(420, 560));

        var header = UITheme.Text("Header", browser.transform, "LAN SERVERS", 20, UITheme.Accent, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 22, 18, 300, 26);
        header.characterSpacing = 3;

        browserStatus = UITheme.Text("Status", browser.transform, "Scanning local network...", 14, UITheme.TextDim);
        UITheme.TL(browserStatus.rectTransform, 22, 50, 360, 20);

        browserContent = UITheme.Rect("List", browser.transform);
        UITheme.Stretch(browserContent);
        browserContent.offsetMin = new Vector2(14, 14);
        browserContent.offsetMax = new Vector2(-14, -80);
    }

    float AddModeCard(RectTransform parent, float y, string title, string subtitle,
        UnityEngine.Events.UnityAction onClick, System.Action<RectTransform> extraContent = null, float height = 108)
    {
        var card = UITheme.Image($"Card_{title}", parent, UITheme.Panel);
        UITheme.TL(card.rectTransform, 48, y, 430, height);

        var edge = UITheme.Image("Edge", card.transform, UITheme.Accent);
        UITheme.Place(edge.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4, height));

        var titleText = UITheme.Text("Title", card.transform, title, 24, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(titleText.rectTransform, 24, 18, 380, 30);
        titleText.characterSpacing = 2;

        var subText = UITheme.Text("Sub", card.transform, subtitle, 14, UITheme.TextDim);
        UITheme.TL(subText.rectTransform, 24, 52, 386, 40);
        subText.textWrappingMode = TextWrappingModes.Normal;

        if (onClick != null)
        {
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            colors.pressedColor = new Color(2f, 2f, 2f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
        }

        extraContent?.Invoke(card.rectTransform);
        return y + height + 18;
    }

    void StartBrowserDiscovery()
    {
        if (discovery == null || CODNetworkManager.SessionActive) return;
        discovery.OnServerFound.AddListener(OnServerFound);
        discovery.StartDiscovery();
    }

    void OnServerFound(CODServerResponse info)
    {
        foundServers[info.serverId] = info;

        if (quickSearching)
        {
            quickSearching = false;
            CODNetworkManager.Instance?.JoinGame(info.address);
            return;
        }

        RebuildBrowser();
    }

    void RebuildBrowser()
    {
        foreach (Transform child in browserContent) Destroy(child.gameObject);
        browserStatus.text = foundServers.Count == 0
            ? "Scanning local network..."
            : $"{foundServers.Count} match(es) found";

        float y = 0;
        foreach (var info in foundServers.Values)
        {
            var row = UITheme.Image($"Server_{info.serverId}", browserContent, UITheme.PanelSoft);
            UITheme.TL(row.rectTransform, 0, y, 392, 56);

            var name = UITheme.Text("Name", row.transform, info.serverName, 17, UITheme.TextMain, FontStyles.Bold);
            UITheme.TL(name.rectTransform, 14, 8, 240, 22);
            var players = UITheme.Text("Players", row.transform, $"{info.players}/{info.maxPlayers} OPERATORS  ·  {info.address}", 12, UITheme.TextDim);
            UITheme.TL(players.rectTransform, 14, 32, 300, 16);

            string address = info.address;
            var join = UITheme.Button("Join", row.transform, "JOIN", 14, UITheme.Accent, UITheme.TextOnAccent,
                () => CODNetworkManager.Instance?.JoinGame(address));
            UITheme.Place((RectTransform)join.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(72, 34));

            y += 62;
        }
    }

    void OnQuickPlay()
    {
        if (CODNetworkManager.SessionActive) return;

        if (foundServers.Count > 0)
        {
            foreach (var info in foundServers.Values)
            {
                CODNetworkManager.Instance?.JoinGame(info.address);
                return;
            }
        }

        quickSearching = true;
        browserStatus.text = "Searching for a match...";
        if (discovery != null) discovery.StartDiscovery();
    }

    void OnHostMatch()
    {
        if (CODNetworkManager.SessionActive) return;
        if (!int.TryParse(hostMaxInput.text, out int max) || max < 1) max = 8;
        CODNetworkManager.EnsureExists()?.HostLanGame(hostRoomInput.text, max, true);
    }

    void OnDirectConnect()
    {
        string address = directIpInput.text.Trim();
        if (string.IsNullOrEmpty(address) || CODNetworkManager.SessionActive) return;
        CODNetworkManager.EnsureExists()?.JoinGame(address);
    }

    void OnClientError(string message)
    {
        if (browserStatus != null) browserStatus.text = message;
        HideLobby();
    }

    #endregion

    #region Lobby overlay

    void BuildLobbyOverlay()
    {
        var dim = UITheme.Image("LobbyOverlay", uiRoot, new Color(0f, 0f, 0f, 0.72f));
        UITheme.Stretch(dim.rectTransform);
        lobbyGroup = dim.gameObject.AddComponent<CanvasGroup>();

        var panel = UITheme.Image("LobbyPanel", dim.transform, UITheme.Panel);
        UITheme.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 560));

        lobbyTitle = UITheme.Text("Title", panel.transform, "LOBBY", 26, UITheme.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        UITheme.Place(lobbyTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(400, 34));
        lobbyTitle.characterSpacing = 4;

        lobbyList = UITheme.Rect("Players", panel.transform);
        UITheme.Stretch(lobbyList);
        lobbyList.offsetMin = new Vector2(30, 96);
        lobbyList.offsetMax = new Vector2(-30, -80);

        lobbyStartButton = UITheme.Button("Start", panel.transform, "START MATCH", 19, UITheme.Accent, UITheme.TextOnAccent,
            () => CODNetworkManager.Instance?.BeginGame());
        UITheme.Place((RectTransform)lobbyStartButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 88), new Vector2(300, 48));

        var leave = UITheme.Button("Leave", panel.transform, "LEAVE", 15, UITheme.PanelSoft, UITheme.TextMain, () =>
        {
            CODNetworkManager.Instance?.Leave();
            HideLobby();
        });
        UITheme.Place((RectTransform)leave.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(300, 42));

        dim.gameObject.SetActive(false);
    }

    void ShowLobby()
    {
        lobbyGroup.gameObject.SetActive(true);
        var manager = CODNetworkManager.Instance;
        lobbyTitle.text = manager != null && CODNetworkManager.ServerActive
            ? manager.serverName.ToUpperInvariant()
            : "LOBBY";
        RefreshLobby();
    }

    void HideLobby()
    {
        if (lobbyGroup != null) lobbyGroup.gameObject.SetActive(false);
    }

    void RefreshLobby()
    {
        if (lobbyGroup == null || !lobbyGroup.gameObject.activeSelf) return;

        foreach (Transform child in lobbyList) Destroy(child.gameObject);

        float y = 0;
        foreach (var player in CODLobbyPlayer.All)
        {
            if (player == null) continue;
            bool mine = player.IsOwner;

            var row = UITheme.Image("Player", lobbyList, mine ? new Color(1f, 0.54f, 0f, 0.14f) : UITheme.PanelSoft);
            UITheme.TL(row.rectTransform, 0, y, 460, 44);

            var name = UITheme.Text("Name", row.transform, player.playerName.Value, 17,
                mine ? UITheme.Accent : UITheme.TextMain, FontStyles.Bold, TextAlignmentOptions.Left);
            UITheme.Stretch(name.rectTransform);
            name.margin = new Vector4(16, 0, 0, 0);
            name.alignment = TextAlignmentOptions.Left;
            name.verticalAlignment = VerticalAlignmentOptions.Middle;

            y += 50;
        }

        lobbyStartButton.gameObject.SetActive(CODNetworkManager.ServerActive);
    }

    #endregion

    #region OPERATORS tab

    void BuildOperatorsTab()
    {
        var panel = CreateTabPanel("OPERATORS");
        var library = CharacterSkinLibrary.Instance;

        var header = UITheme.Text("Header", panel, "SELECT OPERATOR", 30, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 48, 36, 600, 40);
        header.characterSpacing = 3;

        var sub = UITheme.Text("Sub", panel, "Your operator is visible to every player in the match.", 15, UITheme.TextDim);
        UITheme.TL(sub.rectTransform, 48, 78, 700, 22);

        if (library == null || library.Count == 0) return;

        for (int i = 0; i < library.Count; i++)
        {
            int index = i;
            var skin = library.skins[i];

            var card = UITheme.Image($"Operator_{skin.id}", panel, UITheme.Panel);
            UITheme.TL(card.rectTransform, 48 + i * 300, 130, 280, 380);

            var frame = UITheme.Image("Frame", card.transform, Color.clear);
            UITheme.Stretch(frame.rectTransform);
            frame.raycastTarget = false;
            // hollow frame via 4 edges would be complex; use bottom bar highlight instead
            var highlight = UITheme.Image("Highlight", card.transform, Color.clear);
            UITheme.Place(highlight.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(280, 5));

            var swatch = UITheme.Image("Swatch", card.transform, skin.tint);
            UITheme.Place(swatch.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(210, 190));

            var name = UITheme.Text("Name", card.transform, skin.displayName.ToUpperInvariant(), 24, UITheme.TextMain,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UITheme.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(260, 32));
            name.characterSpacing = 3;

            var desc = UITheme.Text("Desc", card.transform, skin.description, 13, UITheme.TextDim,
                FontStyles.Normal, TextAlignmentOptions.Center);
            UITheme.Place(desc.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 52), new Vector2(240, 60));
            desc.textWrappingMode = TextWrappingModes.Normal;

            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            button.onClick.AddListener(() =>
            {
                PlayerAppearance.SavedSkinIndex = index;
                stage.operatorDisplay?.RefreshSkin();
                RefreshOperatorCards();
            });

            operatorCards.Add((index, highlight));
        }

        RefreshOperatorCards();
    }

    void RefreshOperatorCards()
    {
        int selected = PlayerAppearance.SavedSkinIndex;
        foreach (var (index, highlight) in operatorCards)
            highlight.color = index == selected ? UITheme.Accent : Color.clear;
    }

    #endregion

    #region LOADOUT tab

    void BuildLoadoutTab()
    {
        var panel = CreateTabPanel("LOADOUT");

        var header = UITheme.Text("Header", panel, "LOADOUT", 30, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 48, 36, 600, 40);
        header.characterSpacing = 3;

        var sub = UITheme.Text("Sub", panel, "Changes apply instantly to your operator and your next spawn.", 15, UITheme.TextDim);
        UITheme.TL(sub.rectTransform, 48, 78, 700, 22);

        var database = WeaponDatabase.Instance;
        GameObject playerPrefab = CODNetworkManager.PlayerPrefabAsset;
        WeaponController controller = playerPrefab != null
            ? playerPrefab.GetComponentInChildren<WeaponController>(true)
            : null;

        if (database == null || controller == null || controller.slots == null) return;

        // current selection from prefs
        string[] savedIds = PlayerLoadout.SavedLoadout.Split(',');
        loadoutOptions = new List<List<WeaponDatabase.Entry>>();
        loadoutSelection = new List<int>();

        float y = 130;
        for (int slotIndex = 0; slotIndex < controller.slots.Length; slotIndex++)
        {
            var slot = controller.slots[slotIndex];
            Weapon defaultWeapon = slot != null ? slot.GetComponentInChildren<Weapon>(true) : null;

            var options = defaultWeapon != null
                ? database.GetBySlotType(defaultWeapon.slotType)
                : new List<WeaponDatabase.Entry>(database.weapons);
            if (options.Count == 0) options = new List<WeaponDatabase.Entry>(database.weapons);
            loadoutOptions.Add(options);

            string savedId = slotIndex < savedIds.Length ? savedIds[slotIndex].Trim() : "";
            int selected = options.FindIndex(o => o.id == savedId);
            if (selected < 0 && defaultWeapon != null)
                selected = options.FindIndex(o => defaultWeapon.name.StartsWith(o.id));
            loadoutSelection.Add(Mathf.Max(0, selected));

            // slot label
            var label = UITheme.Text($"Slot{slotIndex}", panel, SlotLabel(slot, slotIndex), 17, UITheme.Accent, FontStyles.Bold);
            UITheme.TL(label.rectTransform, 48, y, 400, 24);
            label.characterSpacing = 3;
            y += 32;

            // weapon cards
            var cards = new List<(string, Image)>();
            float x = 48;
            foreach (var option in options)
            {
                int si = slotIndex;
                string id = option.id;

                var card = UITheme.Image($"W_{option.id}", panel, UITheme.Panel);
                UITheme.TL(card.rectTransform, x, y, 235, 120);

                var highlight = UITheme.Image("Highlight", card.transform, Color.clear);
                UITheme.Place(highlight.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(235, 4));

                Sprite icon = Resources.Load<Sprite>($"UI/{option.id}_UI");
                if (icon != null)
                {
                    var img = UITheme.Image("Icon", card.transform, Color.white);
                    img.sprite = icon;
                    img.preserveAspect = true;
                    UITheme.Place(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(190, 62));
                    img.raycastTarget = false;
                }

                var name = UITheme.Text("Name", card.transform, option.displayName.ToUpperInvariant(), 15,
                    UITheme.TextMain, FontStyles.Bold, TextAlignmentOptions.Center);
                UITheme.Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 24), new Vector2(220, 20));

                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = card;
                button.onClick.AddListener(() =>
                {
                    loadoutSelection[si] = loadoutOptions[si].FindIndex(o => o.id == id);
                    SaveLoadout();
                    RefreshLoadoutCards();
                    if (si == 0) stage.operatorDisplay?.RefreshWeapon();
                });

                cards.Add((option.id, highlight));
                x += 247;
            }
            loadoutCards.Add(cards);
            y += 138;
        }

        RefreshLoadoutCards();
    }

    static string SlotLabel(WeaponSlotRig slot, int index)
    {
        if (slot == null) return $"SLOT {index + 1}";
        return slot.name.Replace("SlotRig", "").Replace("Slot", " ").Replace("Rig", "").ToUpperInvariant().Trim()
               + (index == 0 ? "  ·  PRIMARY" : "");
    }

    void SaveLoadout()
    {
        var ids = new string[loadoutSelection.Count];
        for (int i = 0; i < loadoutSelection.Count; i++)
        {
            var options = loadoutOptions[i];
            ids[i] = options.Count > 0 ? options[Mathf.Clamp(loadoutSelection[i], 0, options.Count - 1)].id : "-";
        }
        PlayerLoadout.SavedLoadout = string.Join(",", ids);
    }

    void RefreshLoadoutCards()
    {
        for (int slotIndex = 0; slotIndex < loadoutCards.Count; slotIndex++)
        {
            var options = loadoutOptions[slotIndex];
            string selectedId = options.Count > 0
                ? options[Mathf.Clamp(loadoutSelection[slotIndex], 0, options.Count - 1)].id
                : "";
            foreach (var (id, highlight) in loadoutCards[slotIndex])
                highlight.color = id == selectedId ? UITheme.Accent : Color.clear;
        }
    }

    #endregion

    #region Placeholder tabs

    void BuildBarracksTab()
    {
        var panel = CreateTabPanel("BARRACKS");

        var header = UITheme.Text("Header", panel, "BARRACKS", 30, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 48, 36, 600, 40);
        header.characterSpacing = 3;

        var card = UITheme.Image("ProfileCard", panel, UITheme.Panel);
        UITheme.TL(card.rectTransform, 48, 100, 430, 180);

        var name = UITheme.Text("Name", card.transform, CODNetworkManager.PlayerName.ToUpperInvariant(), 26, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(name.rectTransform, 24, 22, 380, 34);

        var rank = UITheme.Text("Rank", card.transform, "RANK 1  ·  RECRUIT", 15, UITheme.Accent, FontStyles.Bold);
        UITheme.TL(rank.rectTransform, 24, 60, 380, 22);

        var stats = UITheme.Text("Stats", card.transform, "KILLS  --      DEATHS  --      K/D  --      MATCHES  --", 14, UITheme.TextDim);
        UITheme.TL(stats.rectTransform, 24, 100, 380, 22);

        var note = UITheme.Text("Note", card.transform, "Stat tracking arrives with the progression update.", 12, UITheme.TextDim, FontStyles.Italic);
        UITheme.TL(note.rectTransform, 24, 136, 380, 20);

        AddComingSoonModule(panel, 48, 320, "CHALLENGES");
        AddComingSoonModule(panel, 268, 320, "CAMOS");
        AddComingSoonModule(panel, 488, 320, "CALLING CARDS");
    }

    void BuildStoreTab()
    {
        var panel = CreateTabPanel("STORE");

        var header = UITheme.Text("Header", panel, "STORE", 30, UITheme.TextMain, FontStyles.Bold);
        UITheme.TL(header.rectTransform, 48, 36, 600, 40);
        header.characterSpacing = 3;

        AddComingSoonModule(panel, 48, 100, "OPERATOR BUNDLES");
        AddComingSoonModule(panel, 268, 100, "WEAPON BLUEPRINTS");
        AddComingSoonModule(panel, 488, 100, "BATTLE TOKENS");
    }

    void AddComingSoonModule(RectTransform parent, float x, float y, string title)
    {
        var card = UITheme.Image($"Soon_{title}", parent, new Color(0.06f, 0.07f, 0.09f, 0.85f));
        UITheme.TL(card.rectTransform, x, y, 200, 150);

        var name = UITheme.Text("Name", card.transform, title, 15, UITheme.Locked, FontStyles.Bold, TextAlignmentOptions.Center);
        UITheme.Place(name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(180, 40));
        name.textWrappingMode = TextWrappingModes.Normal;

        var soon = UITheme.Text("Soon", card.transform, "COMING SOON", 11, UITheme.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        UITheme.Place(soon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(180, 18));
        soon.characterSpacing = 3;
    }

    #endregion
}
