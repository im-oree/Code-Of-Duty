using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainMenuPanels : MonoBehaviour
{
    public GameObject playerStateInfoPanel;
    public GameObject loginPanel;
    public GameObject selectRoomPanel;
    public GameObject createRoomPanel;
    public GameObject roomListPanel;
    public GameObject insideRoomPanel;
    public GameObject connectingPanel;

    GameObject[] allPanels = new GameObject[6];

    private void Awake()
    {
        allPanels[0] = loginPanel;
        allPanels[1] = selectRoomPanel;
        allPanels[2] = createRoomPanel;
        allPanels[3] = connectingPanel;
        allPanels[4] = roomListPanel;
        allPanels[5] = insideRoomPanel;
    }

    public void SetActivePanel(GameObject panel)
    {
        foreach (var pan in allPanels)
        {
            if (pan == panel) pan.SetActive(true);
            else pan.SetActive(false);
        }
    }
}
