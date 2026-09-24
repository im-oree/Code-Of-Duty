using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuInsideRoom : MonoBehaviour
{
    public Color myItemColor;
    private Color defaultColor;
    [SerializeField] private GameObject PlayerUIItemPrefab;
    [SerializeField] private GameObject PlayerListContent;
    [SerializeField] private GameObject StartGameButton;
    private readonly List<GameObject> playerListGameobjects = new List<GameObject>();

    void Awake()
    {
        defaultColor = PlayerUIItemPrefab.GetComponent<Image>().color;
    }

    void OnEnable()
    {
        CODLobbyPlayer.LobbyChanged += RebuildPlayerList;
        CODLobbyPlayer.LocalPlayerJoined += OnLocalPlayerJoined;
    }

    void OnDisable()
    {
        CODLobbyPlayer.LobbyChanged -= RebuildPlayerList;
        CODLobbyPlayer.LocalPlayerJoined -= OnLocalPlayerJoined;
    }

    /// <summary>Our own lobby player spawned: we're inside the room.</summary>
    void OnLocalPlayerJoined()
    {
        Debug.Log(CODNetworkManager.PlayerName + " room joined");

        MenuPanelsManager.instance.CloseLeftPanel();
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.insideRoomPanel);

        RebuildPlayerList();
    }

    void RebuildPlayerList()
    {
        foreach (var go in playerListGameobjects)
        {
            Destroy(go);
        }
        playerListGameobjects.Clear();

        foreach (var lobbyPlayer in CODLobbyPlayer.All)
        {
            if (lobbyPlayer == null) continue;

            GameObject playerItemObject = Instantiate(PlayerUIItemPrefab);
            playerItemObject.transform.SetParent(PlayerListContent.transform);
            playerItemObject.transform.localScale = Vector3.one;
            playerItemObject.transform.GetChild(0).GetComponent<TMP_Text>().text = lobbyPlayer.playerName.Value;
            playerItemObject.GetComponent<Image>().color = lobbyPlayer.IsOwner ? myItemColor : defaultColor;

            playerListGameobjects.Add(playerItemObject);
        }

        // only the host can start the match
        StartGameButton.SetActive(CODNetworkManager.ServerActive);
    }

    public void OnDisconnectClicked()
    {
        if (CODNetworkManager.Instance != null)
        {
            CODNetworkManager.Instance.Leave();
        }

        MenuPanelsManager.SetActiveInLeftPanel(MenuPanelsManager.instance.selectRoomPanel);
        MenuPanelsManager.instance.CloseRightPanel();
    }

    public void OnStartGameClicked()
    {
        if (CODNetworkManager.ServerActive)
        {
            CODNetworkManager.Instance.BeginGame();
        }
    }
}
