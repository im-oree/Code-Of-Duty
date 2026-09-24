using TMPro;
using UnityEngine;

public class MenuLogin : MonoBehaviour
{
    [SerializeField] private TMP_InputField playerNameField;
    const string playerNamePrefKey = "PlayerName";

    #region Unity methods

    void Start()
    {
        GetSavedPlayerName();
    }

    #endregion

    #region UI methods

    private void GetSavedPlayerName()
    {
        if (playerNameField != null && PlayerPrefs.HasKey(playerNamePrefKey))
        {
            playerNameField.text = PlayerPrefs.GetString(playerNamePrefKey);
        }
    }

    public void OnLoginClick()
    {
        if (string.IsNullOrEmpty(playerNameField.text))
        {
            Debug.LogError("Player Name is null or empty");
            return;
        }

        CODNetworkManager.PlayerName = playerNameField.text;

        // make sure the network core exists; no master server needed with LAN play
        CODNetworkManager.EnsureExists();

        Debug.Log(CODNetworkManager.PlayerName + " logged in");
        MenuPanelsManager.SetActiveInLeftPanel(MenuPanelsManager.instance.selectRoomPanel);
    }

    #endregion
}
