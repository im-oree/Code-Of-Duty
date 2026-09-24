using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class NetComponentEnabler : MonoBehaviour
{
    [SerializeField] private PhotonView photonView;
    [SerializeField] private List<MonoBehaviour> disableComponents;
    [SerializeField] private List<GameObject> inactiveGameObjects;
    [SerializeField] private Camera playerCamera;

    private void Awake()
    {
    }
    // Start is called before the first frame update
    void Start()
    {
        if (!photonView.IsMine)
        {
            ComponentsDisaber();
        }
    }

    // Update is called once per frame
    void Update()
    {

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
