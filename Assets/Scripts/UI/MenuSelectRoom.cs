using UnityEngine;

public class MenuSelectRoom : MonoBehaviour
{
    public void OnCreateRoomClick()
    {
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.createRoomPanel);
    }

    public void OnQuickGameClick()
    {
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.quickGamePanel);
    }

    public void OnRoomsClick()
    {
        MenuPanelsManager.SetActiveInRightPanel(MenuPanelsManager.instance.roomsPanel);
    }
}
