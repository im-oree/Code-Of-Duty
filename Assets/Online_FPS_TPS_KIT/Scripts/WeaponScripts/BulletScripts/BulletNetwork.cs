using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class BulletNetwork : BulletBehaviour
{
    private PhotonView photonView;
    private string weaponName;
    private float PlayerDamage;
    public float lifeTime;
    private Vector3 _startPoint;
    public float startSpeed;
    public float force = 1;
    public GameObject decalPrefab;
    public GameObject bloodPrefab;
    public LayerMask mask; // Raycast Ignored Layers;


    private void Start()
    {
        _startPoint = transform.position;
        Destroy(gameObject, lifeTime);
        GetComponent<Rigidbody>().AddForce(transform.forward * startSpeed, ForceMode.Impulse);
    }

    public override void BulletStart(Transform bulletCreator)
    {
        var weap = bulletCreator.GetComponent<Weapon>();

        photonView = bulletCreator.root.GetComponent<PhotonView>();
        PlayerDamage = weap.playerDamage;
        force = weap.bulletForce;
        startSpeed = weap.bulletStartSpeed;
        weaponName = bulletCreator.name;
    }

    void Update()
    {
        if (Physics.Linecast(_startPoint, transform.position, out RaycastHit hit, mask))
        {
            // spawn decals
            if (decalPrefab && !hit.transform.CompareTag("HitBox"))
            {
                var decal = Instantiate(
                            decalPrefab,
                            hit.point + (hit.normal * 0.001f), Quaternion.FromToRotation(Vector3.up, hit.normal));
                decal.transform.SetParent(hit.transform);
                Destroy(decal, 15);
            }

            if (bloodPrefab && hit.transform.CompareTag("HitBox"))
            {
                var blood = Instantiate(
                            bloodPrefab,
                            hit.point + (hit.normal * 0.001f), Quaternion.FromToRotation(Vector3.up, hit.normal));
                blood.transform.SetParent(hit.transform);
                Destroy(blood, 3);
            }

            if (photonView.IsMine)
            {

                // add force for rigid body hit
                if (hit.collider.CompareTag("HitBox") && hit.transform.root.CompareTag("Player"))
                {
                    hit.transform.root.GetComponent<PhotonView>().RPC("DamageRPC", RpcTarget.All, PlayerDamage *= hit.collider.name == "Head" ? 3 : 1, photonView.ViewID, hit.collider.name == "Head", weaponName);
                }
            }

            if (hit.rigidbody)
            {
                hit.rigidbody.AddForceAtPosition(force * transform.forward, hit.point);
            }

            Destroy(gameObject);

        }

        _startPoint = transform.position;
    }
}
