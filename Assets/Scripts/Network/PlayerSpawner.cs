using UnityEngine;

/// <summary>
/// Gameplay scene bootstrapper. Player spawning itself is handled by CODNetworkManager;
/// this makes sure the network is running when the arena scene is opened directly
/// (offline play / pressing Play on the scene in the editor) by booting the internal server.
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab; // legacy field, spawning lives in CODNetworkManager

    public Transform[] spawnPoints;

    void Start()
    {
        CODNetworkManager manager = CODNetworkManager.EnsureExists();

        // opened directly without a session: start the internal server, like a real game
        if (manager != null && !CODNetworkManager.SessionActive)
        {
            manager.StartInternalHost();
        }
    }

    public Transform GetRandomSpawnPoint()
    {
        int spawnID = Random.Range(0, spawnPoints.Length);

        return spawnPoints[spawnID];
    }
}
