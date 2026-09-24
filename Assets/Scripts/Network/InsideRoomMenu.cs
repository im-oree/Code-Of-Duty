using UnityEngine;
using TMPro;
using Photon.Realtime;
using UnityEngine.UI;
using Photon.Pun;
using System.Collections.Generic;

public class InsideRoomMenu : MonoBehaviourPunCallbacks
{
    [SerializeField] private MainMenuPanels mainMenuPanels;
    [SerializeField] private GameObject PlayerListItemPrefab;
    [SerializeField] private GameObject PlayerListContent;
    [SerializeField] private GameObject PlayButton;
    private Dictionary<int, GameObject> playerListGameobjects;

    // Start is called before the first frame update
    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public override void OnJoinedRoom()
    {
        Debug.Log(PhotonNetwork.LocalPlayer.NickName + " room joined");

        mainMenuPanels.SetActivePanel(mainMenuPanels.insideRoomPanel);

        PlayButton.SetActive(PhotonNetwork.IsMasterClient);


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

        PlayButton.SetActive(PhotonNetwork.IsMasterClient);
    }
    private void CreatePlayerItemObject(Player player)
    {
        GameObject playerItemObject = Instantiate(PlayerListItemPrefab);
        playerItemObject.transform.SetParent(PlayerListContent.transform);
        playerItemObject.transform.localScale = Vector3.one;
        playerItemObject.transform.GetChild(0).GetComponent<TMP_Text>().text = player.NickName;

        if (player.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            playerItemObject.transform.GetChild(1).gameObject.SetActive(true);
        }
        else
        {
            playerItemObject.transform.GetChild(1).gameObject.SetActive(false);
        }

        playerListGameobjects.Add(player.ActorNumber, playerItemObject);
    }

    public override void OnLeftRoom()
    {
        mainMenuPanels.SetActivePanel(mainMenuPanels.selectRoomPanel);

        foreach (GameObject obj in playerListGameobjects.Values)
        {
            Destroy(obj);
        }
        playerListGameobjects.Clear();
    }

    public void OnLeaveRoomClicked()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }
        mainMenuPanels.SetActivePanel(mainMenuPanels.selectRoomPanel);
    }

    public void OnPlayClicked()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LoadLevel("Game");
        }
    }
}
