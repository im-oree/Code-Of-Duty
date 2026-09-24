using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Disables local-only components (camera, input, movement, etc.)
/// on player objects that don't belong to this client.
/// </summary>
public class NetComponentEnabler : NetworkBehaviour
{
    [SerializeField] private List<MonoBehaviour> disableComponents;
    [SerializeField] private List<GameObject> inactiveGameObjects;
    [SerializeField] private Camera playerCamera;

    public override void OnStartClient()
    {
        if (!isOwned)
        {
            ComponentsDisaber();
        }
    }

    public override void OnStartServer()
    {
        // dedicated server: nothing is local, disable everything local-only
        if (!NetworkClient.active)
        {
            ComponentsDisaber();
        }
    }

    void ComponentsDisaber()
    {
        playerCamera.enabled = false;
        foreach (var item in disableComponents)
        {
            item.enabled = false;
        }
        foreach (var item in inactiveGameObjects)
        {
            item.SetActive(false);
        }
    }
}
