using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class MenuPanelsManager : MonoBehaviourPunCallbacks
{
    public GameObject failedPanel;
    public TMP_InputField failedInputField;
    public static MenuPanelsManager instance;
    public GameObject lowPanel;

    [Header("Left Panel")]
    public GameObject leftPanel;
    public GameObject loginPanel;
    public GameObject connectingPanel;
    public GameObject selectRoomPanel;

    private GameObject[] leftPanelObjects = new GameObject[3];

    [Header("Right Panel")]
    public GameObject rightPanel;
    public GameObject createRoomPanel;
    public GameObject quickGamePanel;
    public GameObject roomsPanel;
    public GameObject insideRoomPanel;

    private GameObject[] rightPanelObjects = new GameObject[4];

    private void Start()
    {
        if (instance != null)
        {
            Debug.LogError("2 menuPanels objects: " + instance.gameObject.name + " | " + this);
            Destroy(instance);
        }
        instance = this;

        leftPanelObjects[0] = loginPanel;
        leftPanelObjects[1] = connectingPanel;
        leftPanelObjects[2] = selectRoomPanel;

        rightPanelObjects[0] = createRoomPanel;
        rightPanelObjects[1] = quickGamePanel;
        rightPanelObjects[2] = roomsPanel;
        rightPanelObjects[3] = insideRoomPanel;
    }

    public static void SetActiveForSingleObject(GameObject panel, bool active)
    {
        panel.SetActive(active);
    }

    public void CloseLeftPanel()
    {
        foreach (var pan in instance.leftPanelObjects)
        {
            if (pan == null) continue;
            pan.SetActive(false);
        }

        instance.leftPanel.SetActive(false);
    }

    public void CloseRightPanel()
    {
        foreach (var pan in instance.rightPanelObjects)
        {
            if (pan == null) continue;
            pan.SetActive(false);
        }

        instance.rightPanel.SetActive(false);
    }

    public static void SetActiveInLeftPanel(GameObject panel)
    {
        instance.leftPanel.SetActive(true);

        foreach (var pan in instance.leftPanelObjects)
        {
            if (pan == null) continue;
            if (pan == panel) pan.SetActive(true);
            else pan.SetActive(false);
        }
    }

    public static void SetActiveInRightPanel(GameObject panel)
    {
        instance.rightPanel.SetActive(true);

        foreach (var pan in instance.rightPanelObjects)
        {
            if (pan == null) continue;
            if (pan == panel) pan.SetActive(true);
            else pan.SetActive(false);
        }
    }
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.Log("sa");
        failedPanel.SetActive(true);
        failedInputField.text += Environment.NewLine + message;
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        failedPanel.SetActive(true);
        failedInputField.text += Environment.NewLine + message;
    }

    public void onCloseFailedPanelClick()
    {
        failedPanel.SetActive(false);
    }
}
