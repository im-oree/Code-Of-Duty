using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

public class Login : MonoBehaviourPunCallbacks
{
    [SerializeField] private MainMenuPanels mainMenuPanels;
    [SerializeField] private TMP_InputField playerNameField;
    const string playerNamePrefKey = "PlayerName";

    #region Unity methods

    private void Awake()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    void Start()
    {
        GetSavedPlayerName();
    }

    #endregion

    #region UI methods

    private void GetSavedPlayerName()
    {
        string defaultName = string.Empty;
        if (playerNameField != null)
        {
            if (PlayerPrefs.HasKey(playerNamePrefKey))
            {
                defaultName = PlayerPrefs.GetString(playerNamePrefKey);
                playerNameField.text = defaultName;
            }
        }
    }

    public void OnLoginClick()
    {
        if (string.IsNullOrEmpty(playerNameField.text))
        {
            Debug.LogError("Player Name is null or empty");
            return;
        }

        PlayerPrefs.SetString(playerNamePrefKey, playerNameField.text);

        mainMenuPanels.SetActivePanel(mainMenuPanels.connectingPanel);

        PhotonNetwork.LocalPlayer.NickName = playerNameField.text;
        PhotonNetwork.ConnectUsingSettings();
    }

    #endregion

    #region Photon callbacks

    public override void OnConnected()
    {
        Debug.Log("Connected");
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log(PhotonNetwork.LocalPlayer.NickName + " connected!");
        PhotonNetwork.GameVersion = Application.version;
        mainMenuPanels.SetActivePanel(mainMenuPanels.selectRoomPanel);
    }

    #endregion
}
