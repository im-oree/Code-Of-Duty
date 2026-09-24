using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuQuickGame : MonoBehaviourPunCallbacks
{
    [SerializeField] private TMP_InputField roomNameInputField;

    public void OnConnectClick()
    {
        if (string.IsNullOrEmpty(roomNameInputField.text)) return;

        if (PhotonNetwork.InLobby)
        {
            PhotonNetwork.LeaveLobby();
            PhotonNetwork.JoinRoom(roomNameInputField.text);
        }
    }

    public void OnConnectToRandomClick()
    {
        if (PhotonNetwork.InLobby)
        {
            PhotonNetwork.LeaveLobby();
            PhotonNetwork.JoinRandomRoom();
        }
    }
    
    public void OnCloseButtonClick()
    {
        if (PhotonNetwork.InLobby) PhotonNetwork.LeaveLobby();
        MenuPanelsManager.instance.CloseRightPanel();
    }
}
