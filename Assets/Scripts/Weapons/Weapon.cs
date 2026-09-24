using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Photon.Pun;

public class Weapon : MonoBehaviour
{
    public enum SlotType
    {
        rifle = 1,
        smg = 2,
        pistol = 3
    }
    public SlotType slotType;
    public int playerDamage = 10;
    //public int slotType; // (1: two slots in the back) (2: chest slot) (3: pistol slot)
    public float shotTemp; // 0 - fast 1 - slow
    private bool _canShoot = true;
    public bool singleShoot; // only single shoot?

    [Header("shotgun parameters")]
    public bool shotgun;
    public int bulletAmount;
    public float accuracy = 1;

    [Header("Components")]
    public Transform aimPoint;
    public GameObject muzzleFlash;
    public GameObject casingPrefab;
    public Transform casingSpawnPoint;
    public GameObject bulletPrefab;
    public Transform bulletSpawnPoint;
    public float bulletForce;
    public float bulletStartSpeed;

    [Header("position and points")]
    public Vector3 inHandsPositionOffset; // offset in hands
    public WeaponPoint[] weaponPoints;
    public List<WeaponSight> weaponSights;

    [Header("View resistance")]
    public float resistanceForce; // view offset rotation
    public float resistanceSmoothing; // view offset rotation speed
    public float collisionDetectionLength;
    public float maxZPositionOffsetCollision;

    [Header("Recoil Parameters")]
    public RecoilParametersModel recoilParametersModel = new RecoilParametersModel();

    [Header("Sound")]
    public AudioClip fireSound;
    private AudioSource _audioSource;
    private BoltAnimation boltAnimation;



    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        boltAnimation = GetComponent<BoltAnimation>();
    }


    public bool Shoot()
    {
        if (!_canShoot) return false;
        _canShoot = false;

        if (shotgun)
        {
            for (int i = 0; i < bulletAmount; i++)
            {
                Quaternion bulletSpawnDirection = Quaternion.Euler(bulletSpawnPoint.rotation.eulerAngles + new Vector3(Random.Range(-accuracy, accuracy), Random.Range(-accuracy, accuracy), 0));
                float bulletSpeed = Random.Range(bulletStartSpeed * 0.8f, bulletStartSpeed);
                BulletSpawn(bulletStartSpeed, bulletSpawnDirection);
            }
        }
        else
        {
            BulletSpawn(bulletStartSpeed, bulletSpawnPoint.rotation);
        }

        CasingSpaw();

        MuzzleFlashSpawn();

        if (fireSound) _audioSource.PlayOneShot(fireSound);

        if (boltAnimation) boltAnimation.StartAnim(0.05f);
        StartCoroutine(ShootPause());

        return true;
    }

    private IEnumerator ShootPause()
    {
        yield return new WaitForSeconds(shotTemp);
        _canShoot = true;
    }

    private void BulletSpawn(float startSpeed, Quaternion bulletDirection)
    {
        GameObject bulletGO = Instantiate(bulletPrefab, bulletSpawnPoint.position, bulletDirection);
        var bulletComponent = bulletGO.GetComponent<BulletBehaviour>();
        bulletComponent.BulletStart(transform);
    }

    private void MuzzleFlashSpawn()
    {
        var muzzleSpawn = Instantiate(muzzleFlash, bulletSpawnPoint.position, bulletSpawnPoint.rotation);
        Destroy(muzzleSpawn, 0.5f);
    }

    private void CasingSpaw()
    {
        if (casingPrefab)
        {
            //Spawn casing
            var cas = Instantiate(casingPrefab, casingSpawnPoint.transform.position, Random.rotation);

            cas.GetComponent<Rigidbody>().AddForce(casingSpawnPoint.transform.forward * 55 + new Vector3(
                Random.Range(-20, 40),
                Random.Range(-20, 40),
                Random.Range(-20, 40)));
            Destroy(cas, 5f);
        }
    }
}
