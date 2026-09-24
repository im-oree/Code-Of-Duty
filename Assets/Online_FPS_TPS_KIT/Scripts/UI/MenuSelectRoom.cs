using Photon.Pun;

public class MenuSelectRoom : MonoBehaviourPunCallbacks
{
    public void OnCreateRoomClick()
    {
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.createRoomPanel);
    }

    public void OnQuickGameClick()
    {
        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.quickGamePanel);
    }

    public void OnRoomsClick()
    {
        if (!PhotonNetwork.InLobby)
        {
            PhotonNetwork.JoinLobby();
        }
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.roomsPanel);
    }
}
