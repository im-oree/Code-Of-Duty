using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float health = 100f;
    [SerializeField] private PlayerLifeController playerLifeController;
    [SerializeField] private HitBoxColidersList hitBoxColidersList;

    private void Start()
    {
        hitBoxColidersList.Init();
    }

    public float SetDamage(float damage)
    {
        health -= damage;

        float retValue = health;

        if (transform.root.GetComponent<PhotonView>().IsMine)
        {
            UIManger.instance.healthPanel.SetHealthValue(health);
        }

        if (health <= 0)
        {
            playerLifeController.Die();

            health = 100f;
        }

        return retValue;
    }
}
