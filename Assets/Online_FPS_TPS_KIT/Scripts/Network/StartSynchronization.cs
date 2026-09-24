using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Photon.Realtime;

public class StartSynchronization : MonoBehaviourPunCallbacks
{
    private void Start() {
      
    }

    [SerializeField] WeaponController weaponController;
    [SerializeField] Animator animator;

    public override void OnJoinedRoom()
    {
        Debug.Log("HUY0");
        var newPlayer = PhotonNetwork.LocalPlayer;
        photonView.RPC("ToChangeRPC", RpcTarget.Others, PhotonNetwork.LocalPlayer.ActorNumber);
        transform.GetComponent<PhotonView>().RPC("ToChangeRPC", RpcTarget.Others, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    void GetNewPlayerRPC(int ID)
    {
        Player player = PhotonNetwork.CurrentRoom.GetPlayer(ID, true);

        photonView.RPC("SenSyncRPC", player, weaponController.activeID);
        Debug.Log("HUY1");

    }

    void SenSyncRPC(int weaponID)
    {
        weaponController.activeID = weaponID;
        animator.Play("GunPickUp", 1);
        Debug.Log("HUY2");
    }
}
