using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Behaviour for the authored StartMenu frontend.
///
/// This component deliberately does not create, delete, lay out, or style UI
/// GameObjects. The complete menu hierarchy lives in StartMenu.unity under
/// CODMainMenu/TacticalCanvas and can be edited with Unity's scene tools.
/// This class only binds scene-authored controls to existing game systems and
/// drives lightweight screen transitions.
/// </summary>
public sealed class CODMainMenu : MonoBehaviour
{
    const string CanvasPath = "TacticalCanvas";

    readonly Dictionary<string, GameObject> screens = new Dictionary<string, GameObject>();
    readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>();
    readonly Dictionary<string, Image> tabBars = new Dictionary<string, Image>();
    readonly Dictionary<long, CODServerResponse> servers = new Dictionary<long, CODServerResponse>();
    readonly List<TextMeshProUGUI> lobbyRows = new List<TextMeshProUGUI>();
    readonly Dictionary<string, TextMeshProUGUI> keyLabels = new Dictionary<string, TextMeshProUGUI>();

    Transform ui;
    MenuStage stage;
    CODNetworkDiscovery discovery;
    TextMeshProUGUI notification;
    TextMeshProUGUI browserStatus;
    TextMeshProUGUI profileNameLabel;
    GameObject hostDialog;
    GameObject connectDialog;
    GameObject lobbyOverlay;
    TMP_InputField hostRoomInput;
    TMP_InputField hostMaxInput;
    TMP_InputField directAddressInput;
    TMP_InputField profileNameInput;
    string activeScreen;
    string awaitingBind;
    Coroutine transition;

    static readonly Color Accent = new Color(1f, 0.48f, 0.06f, 1f);
    static readonly Color Muted = new Color(0.52f, 0.57f, 0.64f, 1f);

    void Start()
    {
        ui = transform.Find(CanvasPath);
        if (ui == null)
        {
            Debug.LogError("[CODMainMenu] StartMenu is missing the authored TacticalCanvas. The menu will not construct a replacement at runtime.");
            enabled = false;
            return;
        }

        // The old MenuUI remains in this scene only as a disabled historical
        // layer. Keeping it disabled means a previous runtime builder can never
        // overlap or recreate the authored frontend.
        Transform legacy = transform.Find("MenuUI");
        if (legacy != null && legacy.gameObject.activeSelf) legacy.gameObject.SetActive(false);

        stage = MenuStage.EnsureInScene();
        if (stage == null)
            Debug.LogError("[CODMainMenu] StartMenu is missing its authored MenuStage.");

        CacheSceneReferences();
        BindSceneControls();

        var manager = CODNetworkManager.EnsureExists();
        discovery = manager != null ? manager.discovery : null;
        if (discovery != null) discovery.OnServerFound.AddListener(OnServerFound);

        CODLobbyPlayer.LobbyChanged += RefreshLobby;
        CODLobbyPlayer.LocalPlayerJoined += ShowLobby;
        CODNetworkManager.ClientError += OnClientError;

        SwitchScreen("PLAY", true);
        RefreshProfile();
        RefreshSettings();
        RefreshLoadout();
        RefreshOperatorCards();
        RefreshKeyLabels();
        StartBrowserDiscovery();
    }

    void OnDestroy()
    {
        CODLobbyPlayer.LobbyChanged -= RefreshLobby;
        CODLobbyPlayer.LocalPlayerJoined -= ShowLobby;
        CODNetworkManager.ClientError -= OnClientError;
        if (discovery != null) discovery.OnServerFound.RemoveListener(OnServerFound);
    }

    #region Scene binding

    void CacheSceneReferences()
    {
        foreach (string id in new[] { "PLAY", "LOADOUT", "OPERATORS", "CAREER", "SETTINGS" })
        {
            screens[id] = FindObject("Screens/Screen_" + id);
            tabs[id] = Find<Button>("TopNav/Tab_" + id);
            tabBars[id] = Find<Image>("TopNav/Tab_" + id + "/ActiveBar");
        }

        notification = Find<TextMeshProUGUI>("Toast/Message");
        browserStatus = Find<TextMeshProUGUI>("Screens/Screen_PLAY/BrowserPanel/Status");
        profileNameLabel = Find<TextMeshProUGUI>("TopNav/ProfileButton/Name");
        hostDialog = FindObject("Overlays/HostDialog");
        connectDialog = FindObject("Overlays/ConnectDialog");
        lobbyOverlay = FindObject("Overlays/LobbyOverlay");

        hostRoomInput = Find<TMP_InputField>("Overlays/HostDialog/Panel/Input_HostRoom");
        hostMaxInput = Find<TMP_InputField>("Overlays/HostDialog/Panel/Input_HostCapacity");
        directAddressInput = Find<TMP_InputField>("Overlays/ConnectDialog/Panel/Input_Address");
        profileNameInput = Find<TMP_InputField>("Screens/Screen_CAREER/ProfileCard/Input_ProfileName");

        for (int i = 0; i < 8; i++)
            lobbyRows.Add(Find<TextMeshProUGUI>($"Overlays/LobbyOverlay/Roster/PlayerRow_{i}/Name"));

        foreach (var binding in InputBindings.Actions)
            keyLabels[binding.id] = Find<TextMeshProUGUI>("Screens/Screen_SETTINGS/Keybinds/Bind_" + binding.id + "/Value");
    }

    void BindSceneControls()
    {
        foreach (var pair in tabs)
        {
            string screenId = pair.Key;
            Bind(pair.Value, () => SwitchScreen(screenId));
        }

        Bind("TopNav/ProfileButton", () => SwitchScreen("CAREER"));
        Bind("TopNav/SettingsButton", () => SwitchScreen("SETTINGS"));
        Bind("TopNav/QuitButton", QuitGame);

        // PLAY: each operation routes to an existing network entry point.
        Bind("Screens/Screen_PLAY/Operations/Action_QuickPlay", QuickPlay);
        Bind("Screens/Screen_PLAY/Operations/Action_Host", () => OpenOverlay(hostDialog));
        Bind("Screens/Screen_PLAY/Operations/Action_Browser", OpenBrowser);
        Bind("Screens/Screen_PLAY/Operations/Action_DirectConnect", () => OpenOverlay(connectDialog));
        Bind("Screens/Screen_PLAY/Operations/Action_Solo", StartSolo);
        Bind("Overlays/HostDialog/Panel/Button_Host", HostMatch);
        Bind("Overlays/HostDialog/Panel/Button_Cancel", CloseOverlays);
        Bind("Overlays/ConnectDialog/Panel/Button_Connect", DirectConnect);
        Bind("Overlays/ConnectDialog/Panel/Button_Cancel", CloseOverlays);
        for (int i = 0; i < 5; i++)
        {
            int row = i;
            Bind("Screens/Screen_PLAY/BrowserPanel/ServerRow_" + i, () => JoinServerRow(row));
        }

        // LOADOUT: cards are authored in the scene and map directly to the
        // actual WeaponDatabase ids used by PlayerLoadout.
        Bind("Screens/Screen_LOADOUT/PrimaryWeapons/Weapon_N4_Rifle", () => SetPrimary("N4_Rifle"));
        Bind("Screens/Screen_LOADOUT/PrimaryWeapons/Weapon_Saga_Rifle", () => SetPrimary("Saga_Rifle"));
        Bind("Screens/Screen_LOADOUT/PrimaryWeapons/Weapon_P6_SMG", () => SetPrimary("P6_SMG"));
        Bind("Screens/Screen_LOADOUT/SecondaryWeapons/Weapon_Glok_Pistol", () => SetSecondary("Glok_Pistol"));

        // OPERATORS: the two visible scene cards reflect the two entries in
        // CharacterSkinLibrary, rather than inventing unavailable operators.
        Bind("Screens/Screen_OPERATORS/OperatorCards/Operator_Crimson", () => SetOperator(0));
        Bind("Screens/Screen_OPERATORS/OperatorCards/Operator_Cobalt", () => SetOperator(1));

        Bind("Screens/Screen_CAREER/ProfileCard/Button_SaveProfile", SaveProfile);

        // SETTINGS: all visual controls are scene-authored; these bindings only
        // apply the values through the existing GameSettings/InputBindings APIs.
        Bind("Screens/Screen_SETTINGS/Audio/MasterRow/Button_MasterMinus", () => { GameSettings.MasterVolume -= .05f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Audio/MasterRow/Button_MasterPlus", () => { GameSettings.MasterVolume += .05f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/FovRow/Button_FovMinus", () => { GameSettings.FieldOfView -= 5f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/FovRow/Button_FovPlus", () => { GameSettings.FieldOfView += 5f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/RenderRow/Button_RenderMinus", () => { GameSettings.RenderScale -= .1f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/RenderRow/Button_RenderPlus", () => { GameSettings.RenderScale += .1f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/ShadowRow/Button_ShadowMinus", () => { GameSettings.ShadowDistance -= 10f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/ShadowRow/Button_ShadowPlus", () => { GameSettings.ShadowDistance += 10f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/QualityRow/Button_Quality", CycleQuality);
        Bind("Screens/Screen_SETTINGS/Video/MSAARow/Button_MSAA", CycleMsaa);
        Bind("Screens/Screen_SETTINGS/Video/VSyncRow/Button_VSync", () => { GameSettings.VSync = !GameSettings.VSync; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Video/FullscreenRow/Button_Fullscreen", () => { GameSettings.Fullscreen = !GameSettings.Fullscreen; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Controls/SensitivityRow/Button_SensitivityMinus", () => { GameSettings.MouseSensitivity -= .1f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Controls/SensitivityRow/Button_SensitivityPlus", () => { GameSettings.MouseSensitivity += .1f; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Controls/CrouchRow/Button_Crouch", () => { InputBindings.CrouchIsToggle = !InputBindings.CrouchIsToggle; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Controls/TacSprintRow/Button_TacSprint", () => { InputBindings.TacSprintMode = 1 - InputBindings.TacSprintMode; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Controls/FireSprintRow/Button_FireSprint", () => { InputBindings.FireWhileSprinting = !InputBindings.FireWhileSprinting; RefreshSettings(); });
        Bind("Screens/Screen_SETTINGS/Keybinds/Button_ResetBinds", () => { InputBindings.ResetToDefaults(); RefreshKeyLabels(); ShowToast("KEYBINDS RESET"); });

        foreach (var binding in InputBindings.Actions)
        {
            string id = binding.id;
            Bind("Screens/Screen_SETTINGS/Keybinds/Bind_" + id, () => BeginRebind(id));
        }

        Bind("Overlays/LobbyOverlay/Button_Start", BeginLobbyGame);
        Bind("Overlays/LobbyOverlay/Button_Leave", LeaveLobby);
    }

    GameObject FindObject(string path)
    {
        Transform result = ui != null ? ui.Find(path) : null;
        if (result == null) Debug.LogError("[CODMainMenu] authored control missing: " + CanvasPath + "/" + path);
        return result != null ? result.gameObject : null;
    }

    T Find<T>(string path) where T : Component
    {
        GameObject gameObject = FindObject(path);
        if (gameObject == null) return null;
        T component = gameObject.GetComponent<T>();
        if (component == null) Debug.LogError("[CODMainMenu] authored control has no " + typeof(T).Name + ": " + path);
        return component;
    }

    void Bind(string path, UnityEngine.Events.UnityAction action) => Bind(Find<Button>(path), action);

    static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        // This is the concrete NullReferenceException path reported for the
        // old menu: a Button authored while inactive can deserialize with a
        // null persistent-click event. Calling RemoveAllListeners on that
        // object aborts the rest of the menu binding. Restore the event object
        // before wiring this already-authored Button; no UI object is created.
        if (button.onClick == null)
        {
            Debug.LogWarning("[CODMainMenu] restored a missing Button.onClick event on '" + button.name + "'.");
            button.onClick = new Button.ButtonClickedEvent();
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    #endregion

    #region Navigation and motion

    void SwitchScreen(string id, bool immediate = false)
    {
        if (!screens.ContainsKey(id) || screens[id] == null) return;
        if (activeScreen == id && !immediate) return;

        foreach (var pair in screens)
        {
            bool selected = pair.Key == id;
            if (pair.Value != null) pair.Value.SetActive(selected);
            if (tabBars.TryGetValue(pair.Key, out Image bar) && bar != null) bar.enabled = selected;
            if (tabs.TryGetValue(pair.Key, out Button tab) && tab != null)
            {
                var text = tab.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text != null) text.color = selected ? Accent : Muted;
            }
        }

        activeScreen = id;
        if (transition != null) StopCoroutine(transition);
        if (!immediate) transition = StartCoroutine(AnimateScreen(screens[id].transform as RectTransform));
    }

    IEnumerator AnimateScreen(RectTransform target)
    {
        if (target == null) yield break;
        target.localScale = new Vector3(.985f, .985f, 1f);
        Vector2 finalPosition = target.anchoredPosition;
        target.anchoredPosition = finalPosition + new Vector2(22f, 0f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime * 7f)
        {
            float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            target.localScale = Vector3.Lerp(new Vector3(.985f, .985f, 1f), Vector3.one, eased);
            target.anchoredPosition = Vector2.Lerp(finalPosition + new Vector2(22f, 0f), finalPosition, eased);
            yield return null;
        }
        target.localScale = Vector3.one;
        target.anchoredPosition = finalPosition;
    }

    void OpenOverlay(GameObject overlay)
    {
        CloseOverlays();
        if (overlay == null) return;
        overlay.SetActive(true);
        StartCoroutine(AnimateOverlay(overlay.transform as RectTransform));
    }

    IEnumerator AnimateOverlay(RectTransform target)
    {
        if (target == null) yield break;
        target.localScale = new Vector3(.92f, .92f, 1f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime * 9f)
        {
            target.localScale = Vector3.Lerp(new Vector3(.92f, .92f, 1f), Vector3.one, 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f));
            yield return null;
        }
        target.localScale = Vector3.one;
    }

    void CloseOverlays()
    {
        if (hostDialog != null) hostDialog.SetActive(false);
        if (connectDialog != null) connectDialog.SetActive(false);
    }

    #endregion

    #region Network flow

    void StartBrowserDiscovery()
    {
        if (discovery == null) return;
        discovery.StartDiscovery();
        if (browserStatus != null) browserStatus.text = "SCANNING LOCAL NETWORK…";
    }

    void QuickPlay()
    {
        SwitchScreen("PLAY");
        StartBrowserDiscovery();
        foreach (var response in servers.Values)
        {
            Join(response);
            return;
        }
        ShowToast("SCANNING FOR A LAN OPERATION");
    }

    void OpenBrowser()
    {
        StartBrowserDiscovery();
        ShowToast("SERVER BROWSER REFRESHED");
    }

    void OnServerFound(CODServerResponse response)
    {
        servers[response.serverId] = response;
        RefreshServerRows();
    }

    void RefreshServerRows()
    {
        var all = new List<CODServerResponse>(servers.Values);
        all.Sort((a, b) => string.Compare(a.serverName, b.serverName, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < 5; i++)
        {
            Transform row = ui.Find("Screens/Screen_PLAY/BrowserPanel/ServerRow_" + i);
            if (row == null) continue;
            TextMeshProUGUI title = row.Find("Title")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI meta = row.Find("Meta")?.GetComponent<TextMeshProUGUI>();
            bool hasServer = i < all.Count;
            row.gameObject.SetActive(true);
            if (title != null) title.text = hasServer ? all[i].serverName.ToUpperInvariant() : "NO SIGNAL";
            if (meta != null) meta.text = hasServer ? $"{all[i].players}/{all[i].maxPlayers} PLAYERS  ·  {all[i].address}" : "WAITING FOR LAN BROADCAST";
            var image = row.GetComponent<Image>();
            if (image != null) image.color = hasServer ? new Color(.09f, .115f, .145f, .96f) : new Color(.045f, .055f, .07f, .78f);
        }
        if (browserStatus != null)
            browserStatus.text = all.Count == 0 ? "SCANNING LOCAL NETWORK…" : all.Count + " OPERATION" + (all.Count == 1 ? "" : "S") + " AVAILABLE";
    }

    void JoinServerRow(int row)
    {
        var all = new List<CODServerResponse>(servers.Values);
        all.Sort((a, b) => string.Compare(a.serverName, b.serverName, StringComparison.OrdinalIgnoreCase));
        if (row < 0 || row >= all.Count)
        {
            ShowToast("NO SERVER IN THIS CHANNEL");
            return;
        }
        Join(all[row]);
    }

    void Join(CODServerResponse response)
    {
        if (response.port != 0 && CODNetworkManager.Instance != null)
            CODNetworkManager.Instance.port = response.port;
        CODNetworkManager.EnsureExists()?.JoinGame(response.address);
        ShowToast("CONNECTING TO " + response.serverName.ToUpperInvariant());
    }

    void HostMatch()
    {
        string room = hostRoomInput != null ? hostRoomInput.text.Trim() : "";
        int capacity = 8;
        if (hostMaxInput != null) int.TryParse(hostMaxInput.text, out capacity);
        capacity = Mathf.Clamp(capacity, 1, 16);
        CloseOverlays();
        CODNetworkManager.EnsureExists()?.HostLanGame(room, capacity, true);
        ShowToast("HOSTING LAN OPERATION");
    }

    void DirectConnect()
    {
        string address = directAddressInput != null ? directAddressInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(address))
        {
            ShowToast("ENTER A SERVER ADDRESS");
            return;
        }
        CloseOverlays();
        CODNetworkManager.EnsureExists()?.JoinGame(address);
        ShowToast("CONNECTING TO " + address.ToUpperInvariant());
    }

    void StartSolo()
    {
        CODNetworkManager.EnsureExists()?.StartSoloGame();
        ShowToast("STARTING SOLO TRAINING");
    }

    void OnClientError(string message) => ShowToast(string.IsNullOrWhiteSpace(message) ? "CONNECTION FAILED" : message.ToUpperInvariant());

    #endregion

    #region Lobby

    void ShowLobby()
    {
        if (lobbyOverlay == null) return;
        CloseOverlays();
        lobbyOverlay.SetActive(true);
        RefreshLobby();
        StartCoroutine(AnimateOverlay(lobbyOverlay.transform as RectTransform));
    }

    void RefreshLobby()
    {
        if (lobbyOverlay == null || !lobbyOverlay.activeSelf) return;
        int row = 0;
        foreach (CODLobbyPlayer player in CODLobbyPlayer.All)
        {
            if (player == null || row >= lobbyRows.Count) continue;
            TextMeshProUGUI label = lobbyRows[row];
            if (label != null)
            {
                label.transform.parent.gameObject.SetActive(true);
                label.text = (player.IsOwner ? "[ YOU ]  " : "[ READY ]  ") + player.playerName.Value.ToUpperInvariant();
                label.color = player.IsOwner ? Accent : Color.white;
            }
            row++;
        }
        for (; row < lobbyRows.Count; row++)
        {
            if (lobbyRows[row] != null) lobbyRows[row].transform.parent.gameObject.SetActive(false);
        }

        Button start = Find<Button>("Overlays/LobbyOverlay/Button_Start");
        if (start != null) start.gameObject.SetActive(CODNetworkManager.ServerActive);
    }

    void BeginLobbyGame()
    {
        if (!CODNetworkManager.ServerActive)
        {
            ShowToast("ONLY THE HOST CAN DEPLOY");
            return;
        }
        CODNetworkManager.Instance?.BeginGame();
    }

    void LeaveLobby()
    {
        if (lobbyOverlay != null) lobbyOverlay.SetActive(false);
        CODNetworkManager.Instance?.Leave();
    }

    #endregion

    #region Profile, operator and loadout

    void RefreshProfile()
    {
        string player = CODNetworkManager.PlayerName;
        if (profileNameInput != null) profileNameInput.SetTextWithoutNotify(player);
        if (profileNameLabel != null) profileNameLabel.text = player.ToUpperInvariant();
    }

    void SaveProfile()
    {
        string requested = profileNameInput != null ? profileNameInput.text.Trim() : "";
        if (string.IsNullOrWhiteSpace(requested))
        {
            ShowToast("CALLSIGN CANNOT BE EMPTY");
            return;
        }
        CODNetworkManager.PlayerName = requested.Substring(0, Mathf.Min(18, requested.Length));
        RefreshProfile();
        ShowToast("CALLSIGN UPDATED");
    }

    void SetOperator(int index)
    {
        CharacterSkinLibrary library = CharacterSkinLibrary.Instance;
        if (library == null || index < 0 || index >= library.Count)
        {
            ShowToast("OPERATOR DATA UNAVAILABLE");
            return;
        }
        PlayerAppearance.SavedSkinIndex = index;
        stage?.operatorDisplay?.RefreshSkin();
        RefreshOperatorCards();
        ShowToast(library.skins[index].displayName.ToUpperInvariant() + " SELECTED");
    }

    void RefreshOperatorCards()
    {
        int selected = PlayerAppearance.SavedSkinIndex;
        foreach (var card in new[] { ("Operator_Crimson", 0), ("Operator_Cobalt", 1) })
        {
            Image bar = Find<Image>("Screens/Screen_OPERATORS/OperatorCards/" + card.Item1 + "/SelectedBar");
            if (bar != null) bar.enabled = card.Item2 == selected;
        }
    }

    void SetPrimary(string weaponId)
    {
        string[] saved = PlayerLoadout.SavedLoadout.Split(',');
        string secondary = saved.Length > 1 && !string.IsNullOrWhiteSpace(saved[1]) ? saved[1].Trim() : "Glok_Pistol";
        PlayerLoadout.SavedLoadout = weaponId + "," + secondary;
        stage?.operatorDisplay?.RefreshWeapon();
        RefreshLoadout();
        ShowToast("PRIMARY EQUIPPED");
    }

    void SetSecondary(string weaponId)
    {
        string[] saved = PlayerLoadout.SavedLoadout.Split(',');
        string primary = saved.Length > 0 && !string.IsNullOrWhiteSpace(saved[0]) ? saved[0].Trim() : "N4_Rifle";
        PlayerLoadout.SavedLoadout = primary + "," + weaponId;
        RefreshLoadout();
        ShowToast("SECONDARY EQUIPPED");
    }

    void RefreshLoadout()
    {
        string[] saved = PlayerLoadout.SavedLoadout.Split(',');
        string primary = saved.Length > 0 && !string.IsNullOrWhiteSpace(saved[0]) ? saved[0].Trim() : "N4_Rifle";
        string secondary = saved.Length > 1 && !string.IsNullOrWhiteSpace(saved[1]) ? saved[1].Trim() : "Glok_Pistol";
        foreach (string id in new[] { "N4_Rifle", "Saga_Rifle", "P6_SMG" })
        {
            Image bar = Find<Image>("Screens/Screen_LOADOUT/PrimaryWeapons/Weapon_" + id + "/SelectedBar");
            if (bar != null) bar.enabled = id == primary;
        }
        Image secondaryBar = Find<Image>("Screens/Screen_LOADOUT/SecondaryWeapons/Weapon_Glok_Pistol/SelectedBar");
        if (secondaryBar != null) secondaryBar.enabled = secondary == "Glok_Pistol";
        TextMeshProUGUI active = Find<TextMeshProUGUI>("Screens/Screen_LOADOUT/ActiveLoadout/PrimaryValue");
        if (active != null) active.text = WeaponName(primary).ToUpperInvariant();
        active = Find<TextMeshProUGUI>("Screens/Screen_LOADOUT/ActiveLoadout/SecondaryValue");
        if (active != null) active.text = WeaponName(secondary).ToUpperInvariant();
    }

    static string WeaponName(string id) => WeaponDatabase.Instance?.Get(id)?.displayName ?? id.Replace("_", " ");

    #endregion

    #region Settings and key rebinding

    void RefreshSettings()
    {
        SetText("Screens/Screen_SETTINGS/Audio/MasterRow/MasterValue", Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%");
        SetText("Screens/Screen_SETTINGS/Video/FovRow/FovValue", Mathf.RoundToInt(GameSettings.FieldOfView) + "°");
        SetText("Screens/Screen_SETTINGS/Video/RenderRow/RenderValue", Mathf.RoundToInt(GameSettings.RenderScale * 100f) + "%");
        SetText("Screens/Screen_SETTINGS/Video/ShadowRow/ShadowValue", Mathf.RoundToInt(GameSettings.ShadowDistance) + " M");
        SetText("Screens/Screen_SETTINGS/Video/QualityRow/QualityValue", QualitySettings.names.Length == 0 ? "DEFAULT" : QualitySettings.names[GameSettings.QualityLevel].ToUpperInvariant());
        SetText("Screens/Screen_SETTINGS/Video/MSAARow/MSAAValue", GameSettings.Antialiasing <= 1 ? "OFF" : GameSettings.Antialiasing + "X");
        SetText("Screens/Screen_SETTINGS/Video/VSyncRow/VSyncValue", GameSettings.VSync ? "ON" : "OFF");
        SetText("Screens/Screen_SETTINGS/Video/FullscreenRow/FullscreenValue", GameSettings.Fullscreen ? "ON" : "OFF");
        SetText("Screens/Screen_SETTINGS/Controls/SensitivityRow/SensitivityValue", GameSettings.MouseSensitivity.ToString("F1"));
        SetText("Screens/Screen_SETTINGS/Controls/CrouchRow/CrouchValue", InputBindings.CrouchIsToggle ? "TOGGLE" : "HOLD");
        SetText("Screens/Screen_SETTINGS/Controls/TacSprintRow/TacSprintValue", InputBindings.TacSprintMode == 0 ? "DOUBLE TAP" : "AUTO HOLD");
        SetText("Screens/Screen_SETTINGS/Controls/FireSprintRow/FireSprintValue", InputBindings.FireWhileSprinting ? "ON" : "OFF");
    }

    void CycleQuality()
    {
        int count = Mathf.Max(1, QualitySettings.names.Length);
        GameSettings.QualityLevel = (GameSettings.QualityLevel + 1) % count;
        RefreshSettings();
    }

    void CycleMsaa()
    {
        int[] values = { 1, 2, 4, 8 };
        int index = Array.IndexOf(values, GameSettings.Antialiasing);
        GameSettings.Antialiasing = values[(Mathf.Max(0, index) + 1) % values.Length];
        RefreshSettings();
    }

    void BeginRebind(string actionId)
    {
        awaitingBind = actionId;
        if (keyLabels.TryGetValue(actionId, out TextMeshProUGUI label) && label != null)
        {
            label.text = "PRESS KEY";
            label.color = Accent;
        }
    }

    void Update()
    {
        if (string.IsNullOrEmpty(awaitingBind)) return;
        if (!Input.anyKeyDown) return;
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (!Input.GetKeyDown(key)) continue;
            if (key == KeyCode.Escape)
            {
                awaitingBind = null;
                RefreshKeyLabels();
                return;
            }
            InputBindings.Set(awaitingBind, key);
            ShowToast("BINDING UPDATED");
            awaitingBind = null;
            RefreshKeyLabels();
            return;
        }
    }

    void RefreshKeyLabels()
    {
        foreach (var pair in keyLabels)
        {
            if (pair.Value == null) continue;
            pair.Value.text = InputBindings.Get(pair.Key).ToString().ToUpperInvariant();
            pair.Value.color = Color.white;
        }
    }

    #endregion

    void SetText(string path, string value)
    {
        TextMeshProUGUI label = Find<TextMeshProUGUI>(path);
        if (label != null) label.text = value;
    }

    void ShowToast(string message)
    {
        if (notification == null) return;
        notification.transform.parent.gameObject.SetActive(true);
        notification.text = message;
        StopCoroutine(nameof(HideToast));
        StartCoroutine(nameof(HideToast));
    }

    IEnumerator HideToast()
    {
        yield return new WaitForSecondsRealtime(3.25f);
        if (notification != null) notification.transform.parent.gameObject.SetActive(false);
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
