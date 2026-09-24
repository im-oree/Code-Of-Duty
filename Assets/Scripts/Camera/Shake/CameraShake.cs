using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CODE OF DUTY native camera-feel system (written for this project — no
/// third-party package). Perlin-noise driven camera shakes with per-state
/// presets: weapon kicks, damage flinches, landings, jumps, melee swings,
/// sustained movement rumble (walk / sprint / tac-sprint) and distance-scaled
/// explosions.
///
/// Cinemachine-safe: the brain owns the camera transform and rewrites it every
/// LateUpdate, so this component (execution order 32000) composes its offsets
/// AFTER the brain each frame. Offsets are absolute per frame — they can never
/// accumulate or drift.
///
/// Everything is local-player cosmetic feedback. Never touches simulation.
/// </summary>
[DefaultExecutionOrder(32000)]
public class CameraShake : MonoBehaviour
{
    // ---------------------------------------------------------------- shakes

    class Shake
    {
        public float magnitude;      // degrees of rotational shake at full strength
        public float roughness;      // noise frequency
        public float fadeIn;         // seconds
        public float fadeOut;        // seconds
        public bool sustained;      // stays alive until strength is driven to 0
        public Vector3 rotInfluence = Vector3.one;
        public Vector3 posInfluence = new Vector3(0.010f, 0.010f, 0.005f);

        float noiseTime;
        float fade;                  // 0..1 envelope
        bool fadingOut;
        readonly float seed;

        public Shake() { seed = Random.value * 100f; }

        public void BeginFadeOut() => fadingOut = true;

        /// <returns>false when the shake is finished and can be removed.</returns>
        public bool Evaluate(float dt, out Vector3 rot, out Vector3 pos)
        {
            noiseTime += dt * roughness;

            if (!fadingOut) fade = fadeIn <= 0f ? 1f : Mathf.Min(1f, fade + dt / fadeIn);
            if (!sustained && fade >= 1f) fadingOut = true;
            if (fadingOut) fade = fadeOut <= 0f ? 0f : fade - dt / fadeOut;

            float amp = magnitude * Mathf.Clamp01(fade);
            rot = new Vector3(
                (Mathf.PerlinNoise(seed, noiseTime) - 0.5f) * 2f * amp * rotInfluence.x,
                (Mathf.PerlinNoise(seed + 11f, noiseTime) - 0.5f) * 2f * amp * rotInfluence.y,
                (Mathf.PerlinNoise(seed + 23f, noiseTime) - 0.5f) * 2f * amp * rotInfluence.z);
            pos = new Vector3(
                (Mathf.PerlinNoise(seed + 37f, noiseTime) - 0.5f) * 2f * amp * posInfluence.x,
                (Mathf.PerlinNoise(seed + 53f, noiseTime) - 0.5f) * 2f * amp * posInfluence.y,
                (Mathf.PerlinNoise(seed + 71f, noiseTime) - 0.5f) * 2f * amp * posInfluence.z);

            return sustained || fade > 0f;
        }
    }

    // ------------------------------------------------------------- singleton

    static CameraShake instance;

    static CameraShake Ensure()
    {
        if (instance != null) return instance;
        Camera cam = Camera.main;
        if (cam == null) return null;
        instance = cam.GetComponent<CameraShake>();
        if (instance == null) instance = cam.gameObject.AddComponent<CameraShake>();
        return instance;
    }

    readonly List<Shake> shakes = new List<Shake>();
    Shake movementRumble;            // single sustained instance, strength-driven
    float rumbleStrength;            // current target 0..1
    Vector3 frameRot, framePos;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        frameRot = Vector3.zero;
        framePos = Vector3.zero;
        float dt = Time.deltaTime;

        for (int i = shakes.Count - 1; i >= 0; i--)
        {
            bool alive = shakes[i].Evaluate(dt, out Vector3 rot, out Vector3 pos);
            frameRot += rot;
            framePos += pos;
            if (!alive) shakes.RemoveAt(i);
        }

        // sustained movement rumble follows its driven strength
        if (movementRumble != null)
        {
            movementRumble.magnitude = Mathf.MoveTowards(movementRumble.magnitude, rumbleStrength, dt * 6f);
            if (rumbleStrength <= 0.001f && movementRumble.magnitude <= 0.001f)
            {
                movementRumble.BeginFadeOut();
                shakes.Remove(movementRumble);
                movementRumble = null;
            }
        }
    }

    void LateUpdate()
    {
        // compose on top of wherever Cinemachine just placed the camera
        transform.position += transform.rotation * framePos;
        transform.rotation *= Quaternion.Euler(frameRot);
    }

    Shake Add(float magnitude, float roughness, float fadeIn, float fadeOut, bool sustained = false)
    {
        var shake = new Shake
        {
            magnitude = magnitude,
            roughness = roughness,
            fadeIn = fadeIn,
            fadeOut = fadeOut,
            sustained = sustained,
        };
        shakes.Add(shake);
        return shake;
    }

    // ---------------------------------------------------------- state presets

    /// <summary>Per-shot kick. strength ~0.3 (SMG) .. 1.0 (heavy single-shot).</summary>
    public static void FireKick(float strength = 0.5f)
    {
        var s = Ensure();
        if (s == null) return;
        var shake = s.Add(0.9f * strength, 14f, 0.015f, 0.14f);
        shake.rotInfluence = new Vector3(1f, 0.7f, 0.4f);
    }

    /// <summary>Directionless flinch when the local player takes damage.</summary>
    public static void HitFlinch(float damage)
    {
        var s = Ensure();
        if (s == null) return;
        float m = Mathf.Lerp(1.2f, 2.8f, Mathf.InverseLerp(5f, 60f, damage));
        s.Add(m, 5f, 0f, 0.45f);
    }

    /// <summary>Heavy rumble when the local player dies.</summary>
    public static void Death()
    {
        var s = Ensure();
        if (s == null) return;
        s.Add(4.5f, 6f, 0f, 1.4f);
    }

    /// <summary>Explosion feedback. distance01: 0 = point blank, 1 = edge of radius.</summary>
    public static void Explosion(float distance01)
    {
        var s = Ensure();
        if (s == null) return;
        float m = Mathf.Lerp(5f, 0.6f, Mathf.Clamp01(distance01));
        s.Add(m, 9f, 0f, Mathf.Lerp(1.1f, 0.35f, Mathf.Clamp01(distance01)));
    }

    /// <summary>Landing thud scaled by fall speed (m/s).</summary>
    public static void Land(float fallSpeed)
    {
        var s = Ensure();
        if (s == null) return;
        float k = Mathf.InverseLerp(3f, 14f, Mathf.Abs(fallSpeed));
        if (k <= 0f) return;
        var shake = s.Add(Mathf.Lerp(0.5f, 2.6f, k), 11f, 0f, Mathf.Lerp(0.12f, 0.32f, k));
        shake.rotInfluence = new Vector3(1f, 0.25f, 0.35f);           // mostly a pitch dip
        shake.posInfluence = new Vector3(0.004f, 0.02f, 0.004f);      // vertical thump
    }

    /// <summary>Tiny pop when jumping off the ground.</summary>
    public static void JumpKick()
    {
        var s = Ensure();
        if (s == null) return;
        var shake = s.Add(0.45f, 10f, 0.02f, 0.12f);
        shake.rotInfluence = new Vector3(1f, 0.3f, 0.3f);
    }

    /// <summary>Whoosh on a melee swing.</summary>
    public static void MeleeSwing()
    {
        var s = Ensure();
        if (s == null) return;
        var shake = s.Add(1.1f, 8f, 0.02f, 0.22f);
        shake.rotInfluence = new Vector3(0.7f, 1f, 0.8f);
    }

    /// <summary>
    /// Sustained handheld rumble while moving. Drive every frame:
    /// 0 = still, ~0.15 walk, ~0.35 sprint, ~0.55 tac-sprint.
    /// </summary>
    public static void SetMovementRumble(float strength)
    {
        var s = Ensure();
        if (s == null) return;

        s.rumbleStrength = Mathf.Clamp01(strength);
        if (s.rumbleStrength > 0.001f && s.movementRumble == null)
        {
            s.movementRumble = s.Add(0f, 6.5f, 0.15f, 0.25f, sustained: true);
            s.movementRumble.rotInfluence = new Vector3(0.8f, 0.6f, 0.45f);
            s.movementRumble.posInfluence = new Vector3(0.006f, 0.009f, 0.003f);
        }
    }
}
