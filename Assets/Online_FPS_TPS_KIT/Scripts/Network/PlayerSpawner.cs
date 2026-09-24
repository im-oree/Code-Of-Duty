using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    public Transform[] spawnPoints;
    // Start is called before the first frame update
    void Start()
    {
        if (PhotonNetwork.IsConnectedAndReady)
        {
            Transform sPoint = GetRandomSpawnPoint();
            PhotonNetwork.Instantiate(playerPrefab.name, sPoint.position, Quaternion.identity);
        }

    }

    public Transform GetRandomSpawnPoint()
    {
        int spawnID = Random.Range(0, spawnPoints.Length);

        return spawnPoints[spawnID];
    }
}
