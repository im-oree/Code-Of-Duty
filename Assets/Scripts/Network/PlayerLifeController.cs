using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class PlayerLifeController : MonoBehaviour
{
    [SerializeField] private GameObject playerRagdollObject;
    [SerializeField] private HitBoxColidersList hitBoxColidersList;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Input_Handler input_Handler;
    [SerializeField] private CharacterMove characterMove;
    public List<SkinnedMeshRenderer> disableRenderComponentsOnDeath = new List<SkinnedMeshRenderer>();
    public List<MonoBehaviour> disableMonoBehComponentsOnDeath = new List<MonoBehaviour>();
    public List<GameObject> disableGameObjectsOnDeath = new List<GameObject>();

    NetworkIdentity rootIdentity;
    NetCMDs netCmds;

    // no identity (offline test scenes) counts as the local player
    bool IsLocalPlayer => rootIdentity == null || rootIdentity.isOwned;

    void Awake()
    {
        rootIdentity = transform.root.GetComponent<NetworkIdentity>();
        netCmds = transform.root.GetComponent<NetCMDs>();
    }

    public void Die()
    {
        SpawnRagdollCopy();

        hitBoxColidersList.HitboxesAsTriggers(true);
        characterController.enabled = false;

        if (IsLocalPlayer)
        {
            input_Handler.enabled = false;
            characterMove.enabled = false;

            StartCoroutine(SayRespawn());
        }

        foreach (var comp in disableRenderComponentsOnDeath)
        {
            comp.enabled = false;
        }

        foreach (var go in disableGameObjectsOnDeath)
        {
            go.SetActive(false);
        }

        foreach (var monobeh in disableMonoBehComponentsOnDeath)
        {
            monobeh.enabled = false;
        }
    }

    IEnumerator SayRespawn()
    {
        UIManger.instance.respawnPanel.respawnPanelObject.SetActive(true);
        float t = 0;
        while (t < 5)
        {
            float value = (float)System.Math.Round(5 - t, 2);
            UIManger.instance.respawnPanel.SetRespawnTimeText(value);
            t += Time.deltaTime;
            yield return null;
        }

        if (netCmds != null) netCmds.RequestRespawn();
        else Respawn(); // offline fallback
        yield break;
    }

    public void Respawn()
    {
        transform.root.position = SpawnPointsController.instance.GetRandomSpawnPoints();

        characterController.enabled = true;

        foreach (var comp in disableRenderComponentsOnDeath)
        {
            comp.enabled = true;
        }

        foreach (var monobeh in disableMonoBehComponentsOnDeath)
        {
            monobeh.enabled = true;
        }

        foreach (var go in disableGameObjectsOnDeath)
        {
            go.SetActive(true);
        }

        hitBoxColidersList.HitboxesAsTriggers(false);

        if (IsLocalPlayer)
        {
            input_Handler.enabled = true;
            characterMove.enabled = true;

            UIManger.instance.respawnPanel.respawnPanelObject.SetActive(false);
            UIManger.instance.healthPanel.SetHealthValue(100f);
        }
    }

    private void SpawnRagdollCopy()
    {
        var playerGO = Instantiate(playerRagdollObject, playerRagdollObject.transform.position, playerRagdollObject.transform.rotation);

        // strip every networking component from the local-only ragdoll copy
        foreach (var networkBehaviour in playerGO.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            Destroy(networkBehaviour);
        }
        foreach (var identity in playerGO.GetComponentsInChildren<NetworkIdentity>(true))
        {
            Destroy(identity);
        }

        Destroy(playerGO.GetComponent<Animator>());
        playerGO.GetComponent<RigExecutor>().rigActive = false;

        var ragHitboxes = playerGO.GetComponent<HitBoxColidersList>();
        ragHitboxes.HitboxesAsTriggers(false);
        playerGO.GetComponent<HitBoxColidersList>().Activate();

        var slots = playerGO.GetComponentsInChildren<WeaponSlotRig>();

        foreach (var slot in slots)
        {
            Transform weapon = slot.transform.GetChild(0);
            weapon.parent = null;
            weapon.GetComponent<Collider>().enabled = true;
            weapon.GetComponent<Rigidbody>().isKinematic = false;
        }
    }
}
