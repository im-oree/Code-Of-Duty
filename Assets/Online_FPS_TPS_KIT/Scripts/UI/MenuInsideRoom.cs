using UnityEngine;
using TMPro;
using Photon.Realtime;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections.Generic;

public class MenuInsideRoom : MonoBehaviourPunCallbacks
{
    public Color myItemColor;
    private Color defaultColor;
    [SerializeField] private GameObject PlayerUIItemPrefab;
    [SerializeField] private GameObject PlayerListContent;
    [SerializeField] private GameObject StartGameButton;
    private Dictionary<int, GameObject> playerListGameobjects;

    // Start is called before the first frame update
    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        defaultColor = PlayerUIItemPrefab.GetComponent<Image>().color;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public override void OnJoinedRoom()
    {
        Debug.Log(PhotonNetwork.LocalPlayer.NickName + " room joined");

        MenuPanelsManager.instance.CloseLeftPanel();
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.insideRoomPanel);

        StartGameButton.SetActive(PhotonNetwork.IsMasterClient);


        if (playerListGameobjects == null)
        {
            playerListGameobjects = new Dictionary<int, GameObject>();
        }

        foreach (var p in PhotonNetwork.PlayerList)
        {
            CreatePlayerItemObject(p);
        }

    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        CreatePlayerItemObject(newPlayer);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Destroy(playerListGameobjects[otherPlayer.ActorNumber]);
        playerListGameobjects.Remove(otherPlayer.ActorNumber);

        StartGameButton.SetActive(PhotonNetwork.IsMasterClient);
    }
    private void CreatePlayerItemObject(Player player)
    {
        GameObject playerItemObject = Instantiate(PlayerUIItemPrefab);
        playerItemObject.transform.SetParent(PlayerListContent.transform);
        playerItemObject.transform.localScale = Vector3.one;
        playerItemObject.transform.GetChild(0).GetComponent<TMP_Text>().text = player.NickName;

        if (player.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            playerItemObject.GetComponent<Image>().color = myItemColor;
        }
        else
        {
            playerItemObject.GetComponent<Image>().color = defaultColor;
        }

        playerListGameobjects.Add(player.ActorNumber, playerItemObject);
    }

    public override void OnLeftRoom()
    {
        MenuPanelsManager.SetActiveInLeftPanel(MenuPanelsManager.instance.selectRoomPanel);

        foreach (GameObject obj in playerListGameobjects.Values)
        {
            Destroy(obj);
        }
        playerListGameobjects.Clear();
    }

    public void OnDisconnectClicked()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }
        MenuPanelsManager.SetActiveInLeftPanel(MenuPanelsManager.instance.selectRoomPanel);
        MenuPanelsManager.instance.CloseRightPanel();
    }

    public void OnStartGameClicked()
    {
        if (PhotonNetwork.IsMasterClient)
        {
           PhotonNetwork.LoadLevel("DMArena1");
        }
    }
}
