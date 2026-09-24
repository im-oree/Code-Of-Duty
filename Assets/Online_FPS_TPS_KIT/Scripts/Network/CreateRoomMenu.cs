using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Photon.Realtime;
using UnityEngine.UI;
using Photon.Pun;

public class CreateRoomMenu : MonoBehaviourPunCallbacks
{   
    [SerializeField] private MainMenuPanels mainMenuPanels;
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private TMP_InputField maxPlayersInputField;
    [SerializeField] private Toggle showInRoomListToggle;

    public void OnCreateClick()
    {
        string roomName = roomNameInputField.text;
        if (string.IsNullOrEmpty(roomName))
        {
            return;
        }
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = (byte)int.Parse(maxPlayersInputField.text);
        roomOptions.IsVisible = true;//showInRoomListToggle.isOn;
        roomOptions.IsOpen = true;
        PhotonNetwork.CreateRoom(roomName, roomOptions, null);
    }

    public void OnCancelClick()
    {
        mainMenuPanels.SetActivePanel(mainMenuPanels.selectRoomPanel);
    }

    public override void OnCreatedRoom()
    {
        Debug.Log("Room create " + PhotonNetwork.CurrentRoom.Name + " visible: " + PhotonNetwork.CurrentRoom.IsVisible);

        Debug.Log("vvv " + PhotonNetwork.CountOfRooms);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("vvv2 " + PhotonNetwork.CountOfRooms);

        Debug.Log(PhotonNetwork.LocalPlayer.NickName + " Join to room " + PhotonNetwork.CurrentRoom.Name);
    }



}
