using EZCameraShake;
using UnityEngine;

/// <summary>
/// Bridges EZ Camera Shake with the kit's Cinemachine-driven camera.
///
/// The CinemachineBrain rewrites the Main Camera transform every LateUpdate,
/// so a CameraShaker sitting on the camera itself would be overwritten.
/// Instead the shaker writes its offsets to a hidden proxy transform (Update),
/// and this rig composes those offsets onto the real camera AFTER the brain
/// has positioned it (LateUpdate at execution order 32000). Because the brain
/// resets the transform every frame the offsets never accumulate.
///
/// Purely local + cosmetic: never touches networking or simulation state.
/// </summary>
[DefaultExecutionOrder(32000)]
public class CameraShakeRig : MonoBehaviour
{
    static CameraShakeRig instance;

    CameraShaker shaker;
    Transform proxy;

    // ---------------- public static API ----------------

    /// <summary>Small kick when the local player fires (layered on top of aim recoil).</summary>
    public static void FireKick()
    {
        CameraShakeRig rig = Ensure();
        if (rig != null) rig.shaker.ShakeOnce(0.45f, 10f, 0.01f, 0.16f);
    }

    /// <summary>Directionless flinch when the local player takes damage; scales with damage dealt.</summary>
    public static void HitReaction(float damage)
    {
        CameraShakeRig rig = Ensure();
        if (rig == null) return;

        float magnitude = Mathf.Lerp(1.1f, 2.6f, Mathf.InverseLerp(5f, 60f, damage));
        rig.shaker.ShakeOnce(magnitude, 5f, 0f, 0.45f);
    }

    /// <summary>Heavy rumble for deaths / explosions near the local player.</summary>
    public static void Explosion()
    {
        CameraShakeRig rig = Ensure();
        if (rig != null) rig.shaker.Shake(CameraShakePresets.Explosion);
    }

    // ---------------- internals ----------------

    static CameraShakeRig Ensure()
    {
        if (instance != null) return instance;

        Camera cam = Camera.main;
        if (cam == null) return null;

        instance = cam.GetComponent<CameraShakeRig>();
        if (instance == null) instance = cam.gameObject.AddComponent<CameraShakeRig>();
        return instance;
    }

    void Awake()
    {
        // Hidden proxy the CameraShaker animates. Unique name keeps
        // CameraShaker's internal name->instance registry collision-free.
        var proxyGO = new GameObject($"CameraShakeProxy_{GetInstanceID()}");
        proxyGO.hideFlags = HideFlags.HideInHierarchy;
        proxyGO.transform.SetParent(transform, false);
        proxy = proxyGO.transform;

        shaker = proxyGO.AddComponent<CameraShaker>();
        shaker.DefaultPosInfluence = new Vector3(0.12f, 0.12f, 0.06f);
        shaker.DefaultRotInfluence = new Vector3(1f, 1f, 0.6f);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void LateUpdate()
    {
        if (proxy == null) return;

        // Compose the shaker's local offsets on top of wherever
        // Cinemachine just placed the camera this frame.
        transform.position += transform.rotation * proxy.localPosition;
        transform.rotation *= proxy.localRotation;
    }
}
