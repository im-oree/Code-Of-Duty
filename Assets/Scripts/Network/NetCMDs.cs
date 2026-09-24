using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Photon.Realtime;

public class NetCMDs : MonoBehaviour, IPunObservable
{
    [SerializeField] private PhotonView photonView;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private BodySlope_Handler bodySlope_Handler;
    [SerializeField] private BodyTurnHandler bodyTurnHandler;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerLifeController playerLifeController;
    [SerializeField] private EventsCenter eventsCenter;
    bool weapSyncInStart = false;

    private void OnEnable()
    {
        weaponController.OnShoot += ShootRPCSender;
        eventsCenter.OnWeaponChange += WeaponChangeRPCSender;
    }
    private void OnDisable()
    {
        weaponController.OnShoot -= ShootRPCSender;
        eventsCenter.OnWeaponChange -= WeaponChangeRPCSender;
    }

    private void ShootRPCSender()
    {
        photonView.RPC("ShootRPC", RpcTarget.Others);
    }

    [PunRPC]
    void ShootRPC()
    {
        if (photonView.IsMine) return;
        weaponController.StartShoot();
    }


    private void WeaponChangeRPCSender(bool change)
    {
        if (!change) return;

        photonView.RPC("ToChangeRPC", RpcTarget.Others, weaponController.nextID);
    }


    [PunRPC]
    void ToChangeRPC(int nextGunSlotID)
    {
        if (photonView.IsMine) return;
        weaponController.ToChange(nextGunSlotID);
    }

    [PunRPC]
    public void DamageRPC(float damage, int photonViewID, bool hitOnTheHead, string weaponName)
    {
        Debug.Log("damage ebat");
        float health = playerHealth.SetDamage(damage);

        if (health <= 0)
        {
            var killerPV = PhotonView.Find(photonViewID);

            bool isMine = killerPV.IsMine | photonView.IsMine;

            UIManger.instance.killPanel.CreateKillItemUI(killerPV.Owner.NickName, photonView.Owner.NickName, weaponName, hitOnTheHead, isMine);
        }
    }

    [PunRPC]
    public void RespawnRPC()
    {
        playerLifeController.Respawn();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(bodySlope_Handler.targetAngle);
            stream.SendNext(bodyTurnHandler.momentaryTurn);
            stream.SendNext(weaponController.activeID);
        }
        else
        {
            bodySlope_Handler.targetAngle = (float)stream.ReceiveNext();
            bodyTurnHandler.momentaryTurn = (bool)stream.ReceiveNext();
            weaponController.activeID = (int)stream.ReceiveNext();

            if (!weapSyncInStart)
            {
                SyncActiveWeaponInStart(weaponController.activeID);
            }
        }
    }

    void SyncActiveWeaponInStart(int activeWeap)
    {
        weapSyncInStart = true;
        weaponController.animator.Play("GunPickUp", 1);
    }
}
