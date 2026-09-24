using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class RoomListMenu : MonoBehaviourPunCallbacks
{
    [SerializeField] private MainMenuPanels mainMenuPanels;
    [SerializeField] private GameObject roomItemPrefab;
    [SerializeField] private GameObject roomsContent;
    private Dictionary<string, RoomInfo> roomListData;
    private Dictionary<string, GameObject> roomListGameobject;


    void Start()
    {
        roomListData = new Dictionary<string, RoomInfo>();
        roomListGameobject = new Dictionary<string, GameObject>();
    }

    public override void OnLeftLobby()
    {
        ClearRoomList();
        roomListData.Clear();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        ClearRoomList();

        foreach (RoomInfo roomInfo in roomList)
        {
            if (!roomInfo.IsOpen || !roomInfo.IsVisible || roomInfo.RemovedFromList)
            {
                if (roomListData.ContainsKey(roomInfo.Name))
                {
                    roomListData.Remove(roomInfo.Name);
                }
            }
            else
            {
                if (roomListData.ContainsKey(roomInfo.Name))
                {
                    roomListData[roomInfo.Name] = roomInfo;
                }
                else
                {
                    roomListData.Add(roomInfo.Name, roomInfo);
                }
            }
        }

        foreach (RoomInfo roomInfo in roomListData.Values)
        {
            GameObject roomItemObject = Instantiate(roomItemPrefab);
            roomItemObject.transform.SetParent(roomsContent.transform);
            roomItemObject.transform.GetChild(0).GetComponent<TMP_Text>().text = roomInfo.Name;
            roomItemObject.transform.GetChild(1).GetComponent<TMP_Text>().text = roomInfo.PlayerCount.ToString() + "/" + roomInfo.MaxPlayers.ToString();
            roomItemObject.transform.localScale = Vector3.one;
            roomItemObject.transform.GetChild(2).GetComponent<Button>().onClick.AddListener(() => JoinRoomFromlist(roomInfo.Name));

            roomListGameobject.Add(roomInfo.Name, roomItemObject);
        }
    }

    private void JoinRoomFromlist(string roomName)
    {
        if (PhotonNetwork.InLobby)
        {
            PhotonNetwork.LeaveLobby();
            PhotonNetwork.JoinRoom(roomName);
        }
    }

    public void ClearRoomList()
    {
        if (roomListGameobject.Count == 0) return;

        foreach (var item in roomListGameobject.Values)
        {
            Destroy(item);
        }
        roomListGameobject.Clear();
    }

    public void OnBackButtonClicked()
    {
        if (PhotonNetwork.InLobby) PhotonNetwork.LeaveLobby();
        mainMenuPanels.SetActivePanel(mainMenuPanels.selectRoomPanel);
    }
}
