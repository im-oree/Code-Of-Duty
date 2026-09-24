using System;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Core network orchestrator of the game (FishNet).
///
/// One flow for everything, like a real game:
/// - Offline / solo   -> starts an internal host (server + client in one process)
/// - LAN host         -> same host, advertised on the local network via CODNetworkDiscovery
/// - LAN client       -> joins a discovered server (or by direct IP)
/// - Dedicated server -> headless build auto-starts the server
///
/// While in the StartMenu scene, connected players get a lightweight lobby player
/// (CODLobbyPlayer). When the host starts the game, the server swaps the global scene
/// to the arena and every lobby player is replaced with the real game player.
/// </summary>
public class CODNetworkManager : MonoBehaviour
{
    [Header("COD Setup")]
    [Tooltip("The real gameplay player prefab (PlayerNet Variant).")]
    public NetworkObject gamePlayerPrefab;

    [Tooltip("Lightweight lobby player prefab used while in the menu.")]
    public NetworkObject lobbyPlayerPrefab;

    [Tooltip("Scene name of the gameplay arena")]
    public string gameplayScene = "DMArena1";

    [Tooltip("Scene name of the main menu")]
    public string menuScene = "StartMenu";

    [Tooltip("LAN discovery component (on the same object)")]
    public CODNetworkDiscovery discovery;

    [Header("Session")]
    public string serverName = "LAN Game";
    public ushort port = 7777;
    public int maxConnections = 16;
    public bool advertiseOnLan = true;

    /// <summary>Raised on the client when the connection fails/stops (for menu feedback).</summary>
    public static event Action<string> ClientError;

    const string PlayerNamePrefKey = "PlayerName";
    const string ManagerResourcePath = "Network/NetworkManager";

    static CODNetworkManager instance;

    NetworkManager fishNet;
    bool matchStarted;

    /// <summary>Local player nickname, persisted between sessions.</summary>
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNamePrefKey, "Player");
        set => PlayerPrefs.SetString(PlayerNamePrefKey, value);
    }

    public static CODNetworkManager Instance => instance;

    public static bool ServerActive => InstanceFinder.IsServerStarted;
    public static bool ClientActive => InstanceFinder.IsClientStarted;
    public static bool SessionActive => ServerActive || ClientActive;

    /// <summary>
    /// Returns the active manager, instantiating it from Resources when needed,
    /// so any scene can be started directly (menu or gameplay).
    /// </summary>
    public static CODNetworkManager EnsureExists()
    {
        if (instance != null) return instance;

        GameObject prefab = Resources.Load<GameObject>(ManagerResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"CODNetworkManager prefab not found at Resources/{ManagerResourcePath}");
            return null;
        }

        GameObject managerObject = Instantiate(prefab);
        managerObject.name = "NetworkManager";
        return managerObject.GetComponent<CODNetworkManager>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        fishNet = GetComponent<NetworkManager>();
        if (fishNet == null) fishNet = InstanceFinder.NetworkManager;

        fishNet.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        fishNet.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
        fishNet.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        fishNet.ClientManager.OnClientConnectionState += OnClientConnectionState;

        // dedicated server build: start listening immediately
        if (Application.isBatchMode)
        {
            fishNet.ServerManager.StartConnection(port);
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (fishNet == null) return;

        fishNet.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
        fishNet.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
        fishNet.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        fishNet.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    bool InGameplayScene => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameplayScene;

    #region Session entry points

    /// <summary>Host a game on the LAN (create room).</summary>
    public void HostLanGame(string roomName, int maxPlayers, bool visibleOnLan)
    {
        if (SessionActive) return;

        serverName = string.IsNullOrWhiteSpace(roomName) ? PlayerName + "'s game" : roomName;
        maxConnections = Mathf.Max(1, maxPlayers);
        advertiseOnLan = visibleOnLan;
        matchStarted = false;
        StartHost();
    }

    /// <summary>Solo game from the menu: internal server, not advertised, straight into the arena.</summary>
    public void StartSoloGame()
    {
        if (SessionActive) return;

        maxConnections = 1;
        advertiseOnLan = false;
        StartHost();
        BeginGame();
    }

    /// <summary>
    /// Boots the internal server when a gameplay scene is opened directly
    /// (offline play / pressing Play in the editor on the arena scene).
    /// </summary>
    public void StartInternalHost()
    {
        if (SessionActive) return;

        advertiseOnLan = false;
        matchStarted = true;
        StartHost();
    }

    /// <summary>Join a server by direct address (IP or hostname).</summary>
    public void JoinGame(string address)
    {
        if (SessionActive) return;
        if (discovery != null) discovery.StopDiscovery();
        fishNet.ClientManager.StartConnection(address, port);
    }

    /// <summary>Host only: move everyone from the lobby into the gameplay scene.</summary>
    public void BeginGame()
    {
        if (!ServerActive) return;

        matchStarted = true;

        SceneLoadData sld = new SceneLoadData(gameplayScene)
        {
            ReplaceScenes = ReplaceOption.All,
            PreferredActiveScene = new PreferredScene(new SceneLookupData(gameplayScene))
        };
        fishNet.SceneManager.LoadGlobalScenes(sld);
    }

    /// <summary>Leave the current session (host or client).</summary>
    public void Leave()
    {
        if (discovery != null) discovery.StopDiscovery();
        if (ClientActive) fishNet.ClientManager.StopConnection();
        if (ServerActive) fishNet.ServerManager.StopConnection(true);
        matchStarted = false;
    }

    void StartHost()
    {
        fishNet.ServerManager.StartConnection(port);
        fishNet.ClientManager.StartConnection("localhost", port);

        if (advertiseOnLan && discovery != null)
            discovery.AdvertiseServer(port);
    }

    #endregion

    #region Player spawning (server)

    /// <summary>First contact: client finished loading its start scenes.</summary>
    void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) return;

        // joined while the match is running (or internal/solo host in the arena):
        // presence-change will handle it once the client enters the arena scene.
        if (matchStarted || InGameplayScene) return;

        // menu lobby
        SpawnFor(conn, lobbyPlayerPrefab, null, null);
    }

    /// <summary>A client entered/left a scene: swap lobby players for game players.</summary>
    void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
    {
        if (!args.Added) return;
        if (args.Scene.name != gameplayScene) return;

        NetworkConnection conn = args.Connection;

        // carry the nickname over from the lobby player, then replace it
        string carriedName = null;
        NetworkObject lobbyObject = null;
        foreach (NetworkObject owned in conn.Objects)
        {
            CODLobbyPlayer lobby = owned.GetComponent<CODLobbyPlayer>();
            if (lobby != null)
            {
                carriedName = lobby.playerName.Value;
                lobbyObject = owned;
                break;
            }
        }

        Transform spawnPoint = GetSpawnPoint();
        SpawnFor(conn, gamePlayerPrefab, spawnPoint, carriedName);

        if (lobbyObject != null)
            fishNet.ServerManager.Despawn(lobbyObject);
    }

    void SpawnFor(NetworkConnection conn, NetworkObject prefab, Transform spawnPoint, string playerName)
    {
        if (prefab == null) return;

        // don't double-spawn
        foreach (NetworkObject owned in conn.Objects)
        {
            if (owned.name.StartsWith(prefab.name)) return;
        }

        NetworkObject nob = spawnPoint != null
            ? Instantiate(prefab, spawnPoint.position, spawnPoint.rotation)
            : Instantiate(prefab);

        nob.name = $"{prefab.name} [conn={conn.ClientId}]";

        if (!string.IsNullOrEmpty(playerName) && nob.TryGetComponent(out NetCMDs netCmds))
            netCmds.playerName.Value = playerName;

        fishNet.ServerManager.Spawn(nob, conn);
    }

    Transform GetSpawnPoint()
    {
        return SpawnPointsController.instance != null
            ? SpawnPointsController.instance.GetRandomSpawnPointTransform()
            : null;
    }

    void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        // enforce max connections
        if (args.ConnectionState == RemoteConnectionState.Started &&
            fishNet.ServerManager.Clients.Count > maxConnections)
        {
            conn.Disconnect(true);
        }
    }

    #endregion

    #region Client callbacks

    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped && !ServerActive)
        {
            ClientError?.Invoke("Disconnected from server");
        }
    }

    #endregion
}
