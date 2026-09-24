using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class WeaponSightSync : MonoBehaviour, IPunObservable
{
    [SerializeField] private PhotonView pv;
    [SerializeField] private TransformToTargetRig transformToTargetRig;
    [SerializeField] private WeaponSightPositionGetter weaponSightPositionGetter;


    private void Start() {
        if (!pv.IsMine) weaponSightPositionGetter.execute = false;    
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transformToTargetRig.targetPosition);
            stream.SendNext(transformToTargetRig.targetRotation);
        }
        else
        {
            transformToTargetRig.SetPositionTarget((Vector3)stream.ReceiveNext());
            transformToTargetRig.SetRotationTarget((Quaternion)stream.ReceiveNext());
        }
    }

}
