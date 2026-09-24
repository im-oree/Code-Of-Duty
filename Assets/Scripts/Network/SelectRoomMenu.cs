using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class SelectRoomMenu : MonoBehaviourPunCallbacks
{
    public MainMenuPanels mainMenuPanels;

    public void OnCreateRoomClick()
    {
        mainMenuPanels.SetActivePanel(mainMenuPanels.createRoomPanel);
    }

    public void OnRoomListClick()
    {
        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }
        mainMenuPanels.SetActivePanel(mainMenuPanels.roomListPanel);
    }

}
