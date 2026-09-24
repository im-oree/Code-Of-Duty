using System;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

/// <summary>Broadcast request sent by clients searching for LAN games.</summary>
public struct CODServerRequest : NetworkMessage { }

/// <summary>Reply sent by a host: where to connect + room info for the server browser.</summary>
public struct CODServerResponse : NetworkMessage
{
    // filled in by the client after receiving the reply (not serialized)
    public IPEndPoint EndPoint { get; set; }

    public Uri uri;
    public long serverId;

    // room info shown in the rooms list
    public string serverName;
    public int players;
    public int maxPlayers;
}

/// <summary>
/// LAN discovery: hosts advertise their room, clients build the rooms list from replies.
/// Replaces the Photon lobby/room listing.
/// </summary>
[DisallowMultipleComponent]
public class CODNetworkDiscovery : NetworkDiscoveryBase<CODServerRequest, CODServerResponse>
{
    #region Server

    protected override CODServerResponse ProcessRequest(CODServerRequest request, IPEndPoint endpoint)
    {
        try
        {
            CODNetworkManager manager = NetworkManager.singleton as CODNetworkManager;

            return new CODServerResponse
            {
                serverId = ServerId,
                uri = transport.ServerUri(),
                serverName = manager != null ? manager.serverName : "LAN Game",
                players = NetworkServer.connections.Count,
                maxPlayers = NetworkManager.singleton != null ? NetworkManager.singleton.maxConnections : 0
            };
        }
        catch (NotImplementedException)
        {
            Debug.LogError($"Transport {transport} does not support network discovery");
            throw;
        }
    }

    #endregion

    #region Client

    protected override CODServerRequest GetRequest() => default;

    protected override void ProcessResponse(CODServerResponse response, IPEndPoint endpoint)
    {
        response.EndPoint = endpoint;

        // the advertised uri may contain an unresolvable hostname:
        // rebuild it with the real address the reply came from
        UriBuilder realUri = new UriBuilder(response.uri)
        {
            Host = response.EndPoint.Address.ToString()
        };
        response.uri = realUri.Uri;

        OnServerFound.Invoke(response);
    }

    #endregion
}
