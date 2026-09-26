using FishNet.Object;
using UnityEngine;

/// <summary>
/// The grenade LAYER — independent of the 2-weapon loadout. Owner presses the
/// bound grenade key (default G, rebindable like everything else) and the
/// server spawns an authoritative Grenade. Count refills on respawn
/// (PlayerLifeController re-enables components → OnEnable). Infinite ammo in
/// GameConfig also grants infinite grenades.
/// </summary>
public class GrenadeThrower : NetworkBehaviour
{
    /// <summary>The local player's thrower (for the HUD).</summary>
    public static GrenadeThrower Local { get; private set; }

    public int Remaining { get; private set; }

    float nextThrowTime;
    static GameObject grenadePrefab;

    void OnEnable()
    {
        Remaining = GameConfig.Instance.grenadesPerLife; // spawn + respawn refill
    }

    void Update()
    {
        if (!IsOwner) return; // live check — ownership arrives after Start
        Local = this;

        if (PauseMenu.IsOpen) return;
        if (InputBindings.Down("grenade")) TryThrow();
    }

    void TryThrow()
    {
        if (Time.time < nextThrowTime) return;

        bool infinite = GameConfig.InfiniteAmmo;
        if (!infinite && Remaining <= 0) return;

        var cam = Camera.main;
        if (cam == null) return;

        nextThrowTime = Time.time + 0.9f;
        if (!infinite) Remaining--;

        Vector3 origin = cam.transform.position + cam.transform.forward * 0.35f;
        Vector3 velocity = cam.transform.forward * GameConfig.Instance.grenadeThrowForce + Vector3.up * 2.5f;

        CameraShake.MeleeSwing(); // small throw kick
        ServerThrow(origin, velocity);
    }

    [ServerRpc]
    void ServerThrow(Vector3 origin, Vector3 velocity)
    {
        // server-side sanity: origin must be near the player, speed capped
        if ((origin - transform.position).sqrMagnitude > 9f)
            origin = transform.position + Vector3.up * 1.6f;
        velocity = Vector3.ClampMagnitude(velocity, GameConfig.Instance.grenadeThrowForce * 1.5f);

        if (grenadePrefab == null)
            grenadePrefab = Resources.Load<GameObject>("Network/GrenadeNetwork");
        if (grenadePrefab == null)
        {
            Debug.LogError("[GrenadeThrower] Resources/Network/GrenadeNetwork prefab missing.");
            return;
        }

        GameObject go = Instantiate(grenadePrefab, origin, Random.rotation);
        go.GetComponent<Grenade>().Thrower = NetworkObject;

        var body = go.GetComponent<Rigidbody>();
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 8f;

        ServerManager.Spawn(go, null);
    }
}
