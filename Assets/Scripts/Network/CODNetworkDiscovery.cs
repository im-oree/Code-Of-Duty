using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Info about a LAN game found by discovery (server browser entry).</summary>
[Serializable]
public struct CODServerResponse
{
    public string address;     // where to connect
    public ushort port;
    public long serverId;

    // room info shown in the rooms list
    public string serverName;
    public int players;
    public int maxPlayers;
}

/// <summary>
/// Transport-agnostic LAN discovery over raw UDP broadcast.
/// Hosts advertise their room; clients build the rooms list from replies.
/// (Replaces Mirror's NetworkDiscovery; works with any FishNet transport.)
/// </summary>
[DisallowMultipleComponent]
public class CODNetworkDiscovery : MonoBehaviour
{
    [Serializable] public class ServerFoundEvent : UnityEvent<CODServerResponse> { }

    [Tooltip("UDP port used for discovery broadcasts (NOT the game port).")]
    public int discoveryPort = 47777;

    [Tooltip("How often clients broadcast a search probe, in seconds.")]
    public float activeDiscoveryInterval = 3f;

    /// <summary>Raised on the client for every server reply (may repeat per server).</summary>
    public ServerFoundEvent OnServerFound = new ServerFoundEvent();

    const string Handshake = "COD_LAN_V1";

    long serverId;
    UdpClient serverSocket;   // host: answers probes
    UdpClient clientSocket;   // client: sends probes, receives replies
    Thread serverThread;
    Thread clientThread;
    float nextProbeTime;
    bool searching;
    ushort advertisedGamePort;

    readonly Queue<CODServerResponse> pendingResponses = new Queue<CODServerResponse>();
    readonly object queueLock = new object();

    void Awake()
    {
        serverId = (long)UnityEngine.Random.Range(int.MinValue, int.MaxValue) << 32 |
                   (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
    }

    void Update()
    {
        // deliver replies on the main thread
        lock (queueLock)
        {
            while (pendingResponses.Count > 0)
                OnServerFound.Invoke(pendingResponses.Dequeue());
        }

        // periodic probe while searching
        if (searching && clientSocket != null && Time.time >= nextProbeTime)
        {
            nextProbeTime = Time.time + Mathf.Max(0.5f, activeDiscoveryInterval);
            SendProbe();
        }
    }

    void OnDestroy() => StopDiscovery();
    void OnApplicationQuit() => StopDiscovery();

    #region Server (host)

    /// <summary>Start answering discovery probes with this room's info.</summary>
    public void AdvertiseServer(ushort gamePort)
    {
        StopDiscovery();
        advertisedGamePort = gamePort;

        try
        {
            serverSocket = new UdpClient(discoveryPort) { EnableBroadcast = true };
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"LAN discovery port {discoveryPort} busy: {e.Message}");
            return;
        }

        serverThread = new Thread(ServerListenLoop) { IsBackground = true };
        serverThread.Start();
    }

    void ServerListenLoop()
    {
        try
        {
            while (true)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = serverSocket.Receive(ref remote);
                if (Encoding.UTF8.GetString(data) != Handshake) continue;

                CODNetworkManager manager = CODNetworkManager.Instance;
                string reply = string.Join("|",
                    Handshake,
                    serverId.ToString(),
                    advertisedGamePort.ToString(),
                    manager != null ? manager.serverName.Replace("|", "/") : "LAN Game",
                    manager != null && FishNet.InstanceFinder.ServerManager != null
                        ? FishNet.InstanceFinder.ServerManager.Clients.Count.ToString() : "0",
                    manager != null ? manager.maxConnections.ToString() : "0");

                byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                serverSocket.Send(replyBytes, replyBytes.Length, remote);
            }
        }
        catch (Exception) { /* socket closed */ }
    }

    #endregion

    #region Client (search)

    /// <summary>Start broadcasting probes and listening for replies.</summary>
    public void StartDiscovery()
    {
        if (searching) return;

        try
        {
            clientSocket = new UdpClient(0) { EnableBroadcast = true };
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"LAN discovery failed to open socket: {e.Message}");
            return;
        }

        searching = true;
        nextProbeTime = 0f;

        clientThread = new Thread(ClientListenLoop) { IsBackground = true };
        clientThread.Start();
    }

    void SendProbe()
    {
        try
        {
            byte[] probe = Encoding.UTF8.GetBytes(Handshake);
            clientSocket.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
        }
        catch (Exception) { /* ignore transient send errors */ }
    }

    void ClientListenLoop()
    {
        try
        {
            while (true)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = clientSocket.Receive(ref remote);
                string[] parts = Encoding.UTF8.GetString(data).Split('|');
                if (parts.Length < 6 || parts[0] != Handshake) continue;

                CODServerResponse response = new CODServerResponse
                {
                    address = remote.Address.ToString(),
                    serverId = long.TryParse(parts[1], out long id) ? id : 0,
                    port = ushort.TryParse(parts[2], out ushort p) ? p : (ushort)7777,
                    serverName = parts[3],
                    players = int.TryParse(parts[4], out int pl) ? pl : 0,
                    maxPlayers = int.TryParse(parts[5], out int mp) ? mp : 0
                };

                lock (queueLock) pendingResponses.Enqueue(response);
            }
        }
        catch (Exception) { /* socket closed */ }
    }

    #endregion

    /// <summary>Stop advertising and searching.</summary>
    public void StopDiscovery()
    {
        searching = false;

        serverSocket?.Close();
        serverSocket = null;
        clientSocket?.Close();
        clientSocket = null;

        serverThread = null;
        clientThread = null;

        lock (queueLock) pendingResponses.Clear();
    }
}
