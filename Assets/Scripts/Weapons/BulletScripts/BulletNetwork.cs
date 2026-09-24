using FishNet.Object;
using UnityEngine;

public class BulletNetwork : BulletBehaviour
{
    private NetworkObject shooterNetworkObject;
    private NetCMDs shooterCmds;
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

        shooterNetworkObject = bulletCreator.root.GetComponent<NetworkObject>();
        shooterCmds = bulletCreator.root.GetComponent<NetCMDs>();
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

            // only the shooting client reports the hit; damage is applied by the server
            if (shooterNetworkObject != null && shooterNetworkObject.IsOwner && shooterCmds != null)
            {
                if (hit.collider.CompareTag("HitBox") && hit.transform.root.CompareTag("Player"))
                {
                    var victimNetworkObject = hit.transform.root.GetComponent<NetworkObject>();

                    if (victimNetworkObject != null)
                    {
                        bool hitOnTheHead = hit.collider.name == "Head";
                        float damage = PlayerDamage * (hitOnTheHead ? 3f : 1f);

                        shooterCmds.ServerDealDamage(victimNetworkObject, damage, hitOnTheHead, weaponName);
                    }
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
