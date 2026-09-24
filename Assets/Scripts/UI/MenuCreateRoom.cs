using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MenuCreateRoom : MonoBehaviour
{
    [SerializeField] private TMP_InputField roomNameInputField;
    [SerializeField] private TMP_InputField maxPlayersInputField;
    [SerializeField] private Toggle showInRoomListToggle;

    public void OnEnable()
    {
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

        if (NetworkServer.active || NetworkClient.active) return;

        if (!int.TryParse(maxPlayersInputField.text, out int maxPlayers) || maxPlayers < 1)
        {
            maxPlayers = 4;
        }

        bool visibleOnLan = showInRoomListToggle == null || showInRoomListToggle.isOn;

        CODNetworkManager manager = CODNetworkManager.EnsureExists();
        manager.HostLanGame(roomName, maxPlayers, visibleOnLan);

        Debug.Log("Room created: " + roomName + " (max " + maxPlayers + " players, visible: " + visibleOnLan + ")");
        // panel switch happens when our lobby player spawns (MenuInsideRoom)
    }
}
