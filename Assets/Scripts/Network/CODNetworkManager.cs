using System;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Core network manager of the game (Mirror).
///
/// One flow for everything, like a real game:
/// - Offline / solo   -> starts an internal host (server + client in one process)
/// - LAN host         -> same host, advertised on the local network via CODNetworkDiscovery
/// - LAN client       -> joins a discovered server (or by direct IP)
/// - Dedicated server -> headless build auto-starts the server (headlessStartMode)
///
/// While in the StartMenu scene, connected players get a lightweight lobby player
/// (CODLobbyPlayer). When the host starts the game, the server changes scene to the
/// gameplay scene and every lobby player is replaced with the real game player.
/// </summary>
public class CODNetworkManager : NetworkManager
{
    [Header("COD Setup")]
    [Tooltip("The real gameplay player prefab (PlayerNet Variant). NetworkManager.playerPrefab is the lobby player.")]
    public GameObject gamePlayerPrefab;

    [Tooltip("Scene name of the gameplay arena")]
    public string gameplayScene = "DMArena1";

    [Tooltip("LAN discovery component (on the same object)")]
    public CODNetworkDiscovery discovery;

    [Header("Session")]
    public string serverName = "LAN Game";
    public bool advertiseOnLan = true;

    /// <summary>Raised on the client when the transport reports an error (for menu feedback).</summary>
    public static event Action<string> ClientError;

    const string PlayerNamePrefKey = "PlayerName";
    const string ManagerResourcePath = "Network/NetworkManager";

    /// <summary>Local player nickname, persisted between sessions.</summary>
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNamePrefKey, "Player");
        set => PlayerPrefs.SetString(PlayerNamePrefKey, value);
    }

    public static CODNetworkManager Instance => singleton as CODNetworkManager;

    /// <summary>
    /// Returns the active manager, instantiating it from Resources when needed,
    /// so any scene can be started directly (menu or gameplay).
    /// </summary>
    public static CODNetworkManager EnsureExists()
    {
        if (singleton is CODNetworkManager existing) return existing;

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

    bool InGameplayScene => SceneManager.GetActiveScene().name == gameplayScene;

    #region Session entry points

    /// <summary>Host a game on the LAN (create room).</summary>
    public void HostLanGame(string roomName, int maxPlayers, bool visibleOnLan)
    {
        if (NetworkServer.active || NetworkClient.active) return;

        serverName = string.IsNullOrWhiteSpace(roomName) ? PlayerName + "'s game" : roomName;
        maxConnections = Mathf.Max(1, maxPlayers);
        advertiseOnLan = visibleOnLan;
        StartHost();
    }

    /// <summary>Solo game from the menu: internal server, not advertised, straight into the arena.</summary>
    public void StartSoloGame()
    {
        if (NetworkServer.active || NetworkClient.active) return;

        maxConnections = 1;
        advertiseOnLan = false;
        StartHost();
        ServerChangeScene(gameplayScene);
    }

    /// <summary>
    /// Boots the internal server when a gameplay scene is opened directly
    /// (offline play / pressing Play in the editor on the arena scene).
    /// </summary>
    public void StartInternalHost()
    {
        if (NetworkServer.active || NetworkClient.active) return;

        advertiseOnLan = false;
        StartHost();
    }

    /// <summary>Join a discovered LAN server.</summary>
    public void JoinGame(Uri uri)
    {
        if (NetworkServer.active || NetworkClient.active) return;
        if (discovery != null) discovery.StopDiscovery();
        StartClient(uri);
    }

    /// <summary>Join a server by direct address (IP or hostname).</summary>
    public void JoinGame(string address)
    {
        if (NetworkServer.active || NetworkClient.active) return;
        if (discovery != null) discovery.StopDiscovery();
        networkAddress = address;
        StartClient();
    }

    /// <summary>Host only: move everyone from the lobby into the gameplay scene.</summary>
    public void BeginGame()
    {
        if (NetworkServer.active) ServerChangeScene(gameplayScene);
    }

    /// <summary>Leave the current session (host or client).</summary>
    public void Leave()
    {
        if (NetworkServer.active && NetworkClient.isConnected) StopHost();
        else if (NetworkClient.active) StopClient();
        else if (NetworkServer.active) StopServer();
    }

    #endregion

    #region Server callbacks

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (advertiseOnLan && discovery != null)
            discovery.AdvertiseServer();
    }

    public override void OnStopServer()
    {
        if (discovery != null) discovery.StopDiscovery();
        base.OnStopServer();
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (InGameplayScene)
        {
            // joined directly into a running match (or internal/solo host): spawn the game player
            SpawnGamePlayer(conn, null);
        }
        else
        {
            // menu scene: spawn the lightweight lobby player (playerPrefab)
            base.OnServerAddPlayer(conn);
        }
    }

    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);

        // after ServerChangeScene each client re-readies:
        // swap its lobby player for the real game player
        if (InGameplayScene &&
            conn.identity != null &&
            conn.identity.TryGetComponent(out CODLobbyPlayer lobbyPlayer))
        {
            SpawnGamePlayer(conn, lobbyPlayer.playerName);
        }
    }

    void SpawnGamePlayer(NetworkConnectionToClient conn, string playerName)
    {
        Transform spawnPoint = GetSpawnPoint();
        GameObject player = spawnPoint != null
            ? Instantiate(gamePlayerPrefab, spawnPoint.position, spawnPoint.rotation)
            : Instantiate(gamePlayerPrefab);

        player.name = $"{gamePlayerPrefab.name} [connId={conn.connectionId}]";

        if (!string.IsNullOrEmpty(playerName) && player.TryGetComponent(out NetCMDs netCmds))
            netCmds.playerName = playerName;

        if (conn.identity != null)
            NetworkServer.ReplacePlayerForConnection(conn, player, ReplacePlayerOptions.Destroy);
        else
            NetworkServer.AddPlayerForConnection(conn, player);
    }

    Transform GetSpawnPoint()
    {
        if (SpawnPointsController.instance != null)
            return SpawnPointsController.instance.GetRandomSpawnPointTransform();

        // fallback: Mirror NetworkStartPosition objects
        return GetStartPosition();
    }

    #endregion

    #region Client callbacks

    public override void OnClientError(TransportError error, string reason)
    {
        ClientError?.Invoke($"{error}: {reason}");
    }

    #endregion
}
