using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Lightweight player object used while players sit in the room lobby (StartMenu scene).
/// Carries the nickname and drives the lobby player list UI.
/// Replaced by the real game player when the host starts the match.
/// </summary>
public class CODLobbyPlayer : NetworkBehaviour
{
    /// <summary>All lobby players known to this client (including our own).</summary>
    public static readonly List<CODLobbyPlayer> All = new List<CODLobbyPlayer>();

    /// <summary>Raised whenever the lobby roster changes (join/leave/rename).</summary>
    public static event Action LobbyChanged;

    /// <summary>Raised on the local client when its own lobby player spawns (we're in the room).</summary>
    public static event Action LocalPlayerJoined;

    public readonly SyncVar<string> playerName = new SyncVar<string>("Player");

    void Awake()
    {
        playerName.OnChange += OnNameChanged;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // lobby players survive the scene change until they are replaced
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        DontDestroyOnLoad(gameObject);

        if (!All.Contains(this)) All.Add(this);
        LobbyChanged?.Invoke();

        if (IsOwner)
        {
            ServerSetPlayerName(CODNetworkManager.PlayerName);
            LocalPlayerJoined?.Invoke();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (All.Remove(this)) LobbyChanged?.Invoke();
    }

    void OnDestroy()
    {
        playerName.OnChange -= OnNameChanged;
        if (All.Remove(this)) LobbyChanged?.Invoke();
    }

    [ServerRpc]
    void ServerSetPlayerName(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            playerName.Value = newName.Trim();
    }

    void OnNameChanged(string _, string __, bool asServer) => LobbyChanged?.Invoke();
}
