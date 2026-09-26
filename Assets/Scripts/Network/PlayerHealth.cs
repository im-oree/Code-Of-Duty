using FishNet.Object;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float health = 100f;
    [SerializeField] private PlayerLifeController playerLifeController;
    [SerializeField] private HitBoxColidersList hitBoxColidersList;

    NetworkObject rootNetworkObject;

    // no network object (offline test scenes) counts as the local player
    bool IsLocalPlayer => rootNetworkObject == null || rootNetworkObject.IsOwner;

    void Awake()
    {
        rootNetworkObject = transform.root.GetComponent<NetworkObject>();
    }

    void Start()
    {
        hitBoxColidersList.Init();
    }

    public float SetDamage(float damage)
    {
        health -= damage;

        float retValue = health;

        if (IsLocalPlayer && UIManger.instance != null)
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
