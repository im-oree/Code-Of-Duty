using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Server-authoritative frag grenade.
///
/// Life cycle: GrenadeThrower (owner) -> ServerRpc -> server spawns this,
/// physics simulates ON THE SERVER (clients are kinematic, NetworkTransform
/// replicates motion), fuse expires -> server computes radius-falloff damage
/// against every player and broadcasts the explosion. Every client then plays
/// the VFX and a DISTANCE-BASED camera shake (the explosion effector: point
/// blank rattles hard, far away is a dull thump).
/// </summary>
public class Grenade : NetworkBehaviour
{
    [Tooltip("Material used by the code-built explosion particles (FireParticle).")]
    [SerializeField] Material explosionMaterial;

    /// <summary>Set on the server before Spawn — used for kill credit.</summary>
    public NetworkObject Thrower { get; set; }

    float fuseEndTime;
    bool exploded;
    Rigidbody body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        fuseEndTime = Time.time + GameConfig.Instance.grenadeFuse;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // physics is server-authoritative; clients just follow NetworkTransform
        if (!IsServerStarted && body != null) body.isKinematic = true;
    }

    void Update()
    {
        if (!IsServerStarted || exploded) return;
        if (Time.time >= fuseEndTime) Explode();
    }

    [Server]
    void Explode()
    {
        exploded = true;

        float radius = GameConfig.Instance.grenadeRadius;
        float maxDamage = GameConfig.Instance.grenadeDamage;

        // distinct players in range (a player has many hitbox colliders)
        var victims = new HashSet<NetCMDs>();
        foreach (var col in Physics.OverlapSphere(transform.position, radius))
        {
            var victim = col.GetComponentInParent<NetCMDs>();
            if (victim != null) victims.Add(victim);
        }

        foreach (var victim in victims)
        {
            float distance = Vector3.Distance(victim.transform.position + Vector3.up * 0.9f, transform.position);
            float damage = maxDamage * Mathf.Clamp01(1f - distance / radius);
            if (damage >= 1f)
                victim.ServerApplyAreaDamage(damage, Thrower, "Frag Grenade");
        }

        ObserversExplode(transform.position);
        StartCoroutine(DespawnAfterVfx());
    }

    IEnumerator DespawnAfterVfx()
    {
        yield return new WaitForSeconds(0.15f);
        Despawn();
    }

    [ObserversRpc(RunLocally = true)] // host player gets the VFX too
    void ObserversExplode(Vector3 position)
    {
        // hide the shell immediately; despawn follows from the server
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        if (body != null) body.isKinematic = true;

        ExplosionVFX.Spawn(position, explosionMaterial);

        // distance-based explosion effector for the LOCAL camera
        var cam = Camera.main;
        if (cam != null)
        {
            float feelRange = GameConfig.Instance.grenadeRadius * 3f; // felt well beyond lethal range
            float distance01 = Mathf.Clamp01(Vector3.Distance(cam.transform.position, position) / feelRange);
            if (distance01 < 1f) CameraShake.Explosion(distance01);
        }
    }
}
