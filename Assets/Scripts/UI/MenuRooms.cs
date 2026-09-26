using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>LAN server browser: lists rooms discovered via CODNetworkDiscovery.</summary>
public class MenuRooms : MonoBehaviour
{
    [SerializeField] private GameObject roomUIItemPrefab;
    [SerializeField] private GameObject roomsContent;
    private readonly Dictionary<long, CODServerResponse> discoveredServers = new Dictionary<long, CODServerResponse>();
    private readonly List<GameObject> roomListGameobject = new List<GameObject>();
    private CODNetworkDiscovery discovery;

    void OnEnable()
    {
        CODNetworkManager manager = CODNetworkManager.EnsureExists();
        discovery = manager != null ? manager.discovery : null;

        discoveredServers.Clear();
        ClearRoomList();

        if (discovery != null && !CODNetworkManager.SessionActive)
        {
            discovery.OnServerFound.AddListener(OnServerDiscovered);
            discovery.StartDiscovery();
        }
    }

    void OnDisable()
    {
        if (discovery != null)
        {
            discovery.OnServerFound.RemoveListener(OnServerDiscovered);

            // don't kill the broadcast when we're hosting/connected
            if (!CODNetworkManager.SessionActive)
            {
                discovery.StopDiscovery();
            }
        }
    }

    void OnServerDiscovered(CODServerResponse info)
    {
        discoveredServers[info.serverId] = info;
        RebuildRoomList();
    }

    void RebuildRoomList()
    {
        ClearRoomList();

        foreach (var info in discoveredServers.Values)
        {
            GameObject roomItemObject = Instantiate(roomUIItemPrefab);
            roomItemObject.transform.SetParent(roomsContent.transform);
            roomItemObject.transform.localScale = Vector3.one;

            roomItemObject.transform.GetChild(0).GetComponent<TMP_Text>().text = info.serverName;
            roomItemObject.transform.GetChild(1).GetComponent<TMP_Text>().text = info.players.ToString() + "/" + info.maxPlayers.ToString();

            string joinAddress = info.address;
            roomItemObject.transform.GetChild(2).GetComponent<Button>().onClick.AddListener(() => JoinRoomFromlist(joinAddress));

            roomListGameobject.Add(roomItemObject);
        }
    }

    private void JoinRoomFromlist(string address)
    {
        CODNetworkManager.Instance.JoinGame(address);
    }

    public void ClearRoomList()
    {
        if (roomListGameobject.Count == 0) return;

        foreach (var item in roomListGameobject)
        {
            Destroy(item);
        }
        roomListGameobject.Clear();
    }

    public void OnCloseButtonClick()
    {
        MenuPanelsManager.instance.CloseRightPanel();
    }
}
