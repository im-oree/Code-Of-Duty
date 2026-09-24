using System;
using System.Collections.Generic;
using Mirror;
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

    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    public override void OnStartServer()
    {
        // lobby players survive the scene change until they are replaced
        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartClient()
    {
        DontDestroyOnLoad(gameObject);

        if (!All.Contains(this)) All.Add(this);
        LobbyChanged?.Invoke();
    }

    public override void OnStopClient()
    {
        if (All.Remove(this)) LobbyChanged?.Invoke();
    }

    public override void OnStartLocalPlayer()
    {
        CmdSetPlayerName(CODNetworkManager.PlayerName);
        LocalPlayerJoined?.Invoke();
    }

    void OnDestroy()
    {
        if (All.Remove(this)) LobbyChanged?.Invoke();
    }

    [Command]
    void CmdSetPlayerName(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            playerName = newName.Trim();
    }

    void OnNameChanged(string _, string __) => LobbyChanged?.Invoke();
}
