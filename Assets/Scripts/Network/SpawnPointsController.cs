using UnityEngine;

public class SpawnPointsController : MonoBehaviour
{
    public static SpawnPointsController instance;

    public Transform[] spawnPoints;

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public Vector3 GetRandomSpawnPoints()
    {
        Transform point = GetRandomSpawnPointTransform();
        return point != null ? point.position : Vector3.zero;
    }

    public Transform GetRandomSpawnPointTransform()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;

        int pointId = Random.Range(0, spawnPoints.Length);
        return spawnPoints[pointId];
    }
}
