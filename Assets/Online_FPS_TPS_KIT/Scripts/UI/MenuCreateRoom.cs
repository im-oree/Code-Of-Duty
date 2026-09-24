using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuCreateRoom : MonoBehaviourPunCallbacks
{
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private TMP_InputField maxPlayersInputField;
    [SerializeField] private Toggle showInRoomListToggle;

    public override void OnEnable()
    {
        base.OnEnable();

        if (string.IsNullOrEmpty(roomNameInputField.text))
        {
            roomNameInputField.text = "Room" + ((int)Random.Range(1, 999)).ToString();
        }

        if (string.IsNullOrEmpty(maxPlayersInputField.text))
        {
            maxPlayersInputField.text = 4.ToString();
        }
        
    }

    public void OnCreateRoomClick()
    {
        string roomName = roomNameInputField.text;
        if (string.IsNullOrEmpty(roomName))
        {
            return;
        }
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = (byte)int.Parse(maxPlayersInputField.text);
        roomOptions.IsVisible = showInRoomListToggle.isOn;
        roomOptions.IsOpen = true;
        PhotonNetwork.CreateRoom(roomName, roomOptions, null);
    }
    public override void OnCreatedRoom()
    {
        Debug.Log("Room create " + PhotonNetwork.CurrentRoom.Name + " visible: " + PhotonNetwork.CurrentRoom.IsVisible);
    }
}

