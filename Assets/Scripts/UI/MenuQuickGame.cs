using TMPro;
using UnityEngine;

/// <summary>
/// Quick game: connect to a direct address (IP/hostname), or auto-join
/// the first LAN server found by discovery.
/// </summary>
public class MenuQuickGame : MonoBehaviour
{
    [Tooltip("Direct connect field: server IP or hostname")]
    [SerializeField] private TMP_InputField roomNameInputField;

    private CODNetworkDiscovery discovery;
    private bool searching;

    public void OnConnectClick()
    {
        if (string.IsNullOrEmpty(roomNameInputField.text)) return;

        CODNetworkManager manager = CODNetworkManager.EnsureExists();
        manager.JoinGame(roomNameInputField.text.Trim());
    }

    public void OnConnectToRandomClick()
    {
        CODNetworkManager manager = CODNetworkManager.EnsureExists();
        discovery = manager != null ? manager.discovery : null;

        if (discovery == null || searching) return;
        if (CODNetworkManager.SessionActive) return;

        searching = true;
        discovery.OnServerFound.AddListener(OnServerDiscovered);
        discovery.StartDiscovery();
    }

    void OnServerDiscovered(CODServerResponse info)
    {
        if (!searching) return;

        StopSearch();
        CODNetworkManager.Instance.JoinGame(info.address);
    }

    void StopSearch()
    {
        searching = false;
        if (discovery != null)
        {
            discovery.OnServerFound.RemoveListener(OnServerDiscovered);
        }
    }

    void OnDisable()
    {
        StopSearch();
    }

    public void OnCloseButtonClick()
    {
        StopSearch();

        if (discovery != null && !CODNetworkManager.SessionActive)
        {
            discovery.StopDiscovery();
        }

        MenuPanelsManager.instance.CloseRightPanel();
    }
}
