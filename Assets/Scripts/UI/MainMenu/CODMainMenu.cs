using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu of CODE OF DUTY, built 100% from code on top of the 3D
/// MenuStage (live operator + weapon preview). Tabbed layout in the style of
/// modern AAA shooters: top tab bar, big mode cards, operator gallery with
/// realtime switching, loadout screen, placeholder tabs for future features.
/// No scene surgery: it injects itself whenever the StartMenu scene loads.
/// </summary>
[ExecuteAlways]
public class CODMainMenu : MonoBehaviour
{
    const string MenuSceneName = "StartMenu";

    /// <summary>Root all UI is built under (child object, so editor previews can be swapped cleanly).</summary>
    Transform uiRoot;

    static bool IsEditMode => !Application.isPlaying;

    #region Editor bake — the menu is REAL SAVED SCENE OBJECTS, visible at all times.
    // In the editor the whole menu (canvas, tabs, 3D stage) is baked into the
    // scene as plain persistent GameObjects — no HideFlags, no DontSave — so
    // everything the player sees on play already exists in the saved scene and
    // can be inspected/tweaked there. After every scene open / script recompile
    // the bake is refreshed from the CURRENT code (old copy purged, new copy
    // built in place) so code changes always reflect; save the scene to persist.
    // At runtime Start() replaces the baked copy with a fully wired live build.

#if UNITY_EDITOR
    void OnEnable()
    {
        if (!IsEditMode) return;

        // never build during OnEnable itself (scene may still be loading)
        UnityEditor.EditorApplication.delayCall += DeferredBakeMenu;
    }

    void OnDisable()
    {
        UnityEditor.EditorApplication.delayCall -= DeferredBakeMenu;
        // baked objects are persistent scene content — nothing to destroy here
    }

    void DeferredBakeMenu()
    {
        if (this == null || !IsEditMode) return;
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!gameObject.scene.isLoaded) return;

        try
        {
            DestroyPreview(); // purge the previous bake (and any legacy ghosts)
            BuildCanvas();
            BuildTopBar();
            BuildTabs();
            SelectTab("PLAY");
            stage = MenuStage.Create();

            // persistent scene objects: mark the scene dirty so saving keeps them
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[CODMainMenu] editor menu bake complete (build 5) — save the scene to persist it");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }
#endif

    static readonly string[] PreviewRootNames =
        { "MenuUI", "MenuStage", "TopBar", "BottomBar", "LobbyOverlay", "Panel_" };

    /// <summary>Removes every menu object built by ANY previous version of this
    /// code — baked copies, editor previews and ghosts that leaked to the scene
    /// root (some may be saved inside the user's local scene file). Prefix
    /// matching also catches duplicates like "TopBar (1)".</summary>
    void DestroyPreview()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (MatchesMenuName(child.name)) DestroyImmediate(child.gameObject);
        }

        var scene = gameObject.scene;
        if (scene.isLoaded)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null || root == gameObject) continue;
                if (MatchesMenuName(root.name)) DestroyImmediate(root);
            }
        }

        tabButtons.Clear();
        tabPanels.Clear();
        operatorCards.Clear();
        loadoutCards.Clear();
    }

    static bool MatchesMenuName(string name)
    {
        foreach (var prefix in PreviewRootNames)
            if (name.StartsWith(prefix)) return true;
        return false;
    }

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn(scene);

    static void TrySpawn(Scene scene)
    {
        if (scene.name != MenuSceneName) return;
        if (FindFirstObjectByType<CODMainMenu>() != null) return;
        new GameObject("CODMainMenu").AddComponent<CODMainMenu>();
    }

    MenuStage stage;
    Canvas canvas;
    CODNetworkDiscovery discovery;

    // tabs
    readonly List<(string id, Button button, TextMeshProUGUI label, Image underline)> tabButtons = new();
    readonly Dictionary<string, RectTransform> tabPanels = new();
    string activeTab;

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

    // operators / loadout live state
    readonly List<(int index, Image frame)> operatorCards = new();
    readonly List<List<(string id, Image frame)>> loadoutCards = new();
    List<List<WeaponDatabase.Entry>> loadoutOptions;
    List<int> loadoutSelection;

    void Awake()
    {
        // earliest possible cleanup: a saved scene may contain baked menu
        // copies (or ghosts from older versions) — wipe them before anything
        // else runs so play mode ALWAYS starts from a clean slate.
        if (!IsEditMode) Phase("awake purge", DestroyPreview);
    }

    void Start()
    {
        if (IsEditMode) return;

        // Each phase is isolated: one broken subsystem (stage, network, vfx)
        // must NEVER take the whole menu UI down with it.
        Phase("purge baked copy", DestroyPreview); // replaced by the live wired build below
        Phase("hide legacy menu", HideLegacyMenu);
        Phase("3D stage", () => stage = MenuStage.Create());
        Phase("network manager", () =>
        {
            var manager = CODNetworkManager.EnsureExists();
            discovery = manager != null ? manager.discovery : null;
        });
        Phase("menu UI", () =>
        {
            BuildCanvas();
            BuildTopBar();
            BuildTabs();
            BuildLobbyOverlay();
            SelectTab("PLAY");
        });
        Phase("lobby events", () =>
        {
            CODLobbyPlayer.LobbyChanged += RefreshLobby;
            CODLobbyPlayer.LocalPlayerJoined += ShowLobby;
            CODNetworkManager.ClientError += OnClientError;
        });
        Phase("server discovery", StartBrowserDiscovery);

        Debug.Log("[CODMainMenu] runtime menu build finished (build 5)");
    }

    static void Phase(string label, System.Action action)
    {
        try { action(); }
        catch (System.Exception e)
        {
            Debug.LogError($"[CODMainMenu] phase '{label}' failed — menu continues without it. {e}");
        }
    }

    void OnDestroy()
    {
        CODLobbyPlayer.LobbyChanged -= RefreshLobby;
        CODLobbyPlayer.LocalPlayerJoined -= ShowLobby;
        CODNetworkManager.ClientError -= OnClientError;
        if (discovery != null) discovery.OnServerFound.RemoveListener(OnServerFound);
    }

    static void HideLegacyMenu()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas.GetComponentInParent<CODMainMenu>() == null)
                canvas.gameObject.SetActive(false);
        }
    }

    #region Canvas & top bar

    void BuildCanvas()
    {
        var root = new GameObject("MenuUI");
        root.transform.SetParent(transform, false);
        uiRoot = root.transform;

        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
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
        // one broken tab must never take the other five down with it
        Phase("PLAY tab", BuildPlayTab);
        Phase("OPERATORS tab", BuildOperatorsTab);
        Phase("LOADOUT tab", BuildLoadoutTab);
        Phase("BARRACKS tab", BuildBarracksTab);
        Phase("STORE tab", BuildStoreTab);
        Phase("SETTINGS tab", BuildSettingsTab);
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
