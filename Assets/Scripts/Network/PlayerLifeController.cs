using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
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

    // Start is called before the first frame update
    public void Die()
    {
        SpawnRagdollCopy();

        hitBoxColidersList.HitboxesAsTriggers(true);
        characterController.enabled = false;

        if (transform.root.GetComponent<PhotonView>().IsMine)
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

        transform.root.GetComponent<PhotonView>().RPC("RespawnRPC", RpcTarget.All);
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

        if (transform.root.GetComponent<PhotonView>().IsMine)
        {
            input_Handler.enabled = true;
            characterMove.enabled = true;
            characterMove.enabled = true;

            UIManger.instance.respawnPanel.respawnPanelObject.SetActive(false);
            UIManger.instance.healthPanel.SetHealthValue(100f);
        }
    }

    private void SpawnRagdollCopy()
    {
        var playerGO = Instantiate(playerRagdollObject, playerRagdollObject.transform.position, playerRagdollObject.transform.rotation);
        Destroy(playerGO.GetComponent<PhotonAnimatorView>());
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
