using UnityEngine;

/// <summary>
/// Code-built one-shot explosion: fireball burst + lingering smoke + a bright
/// point light that fades out. No prefab needed — everything is configured
/// procedurally and destroys itself.
/// </summary>
public static class ExplosionVFX
{
    public static void Spawn(Vector3 position, Material particleMaterial)
    {
        var root = new GameObject("Explosion");
        root.transform.position = position;

        // ---------------- fireball ----------------
        BuildBurst(root.transform, particleMaterial,
            count: 38, speed: 9f, sizeMin: 0.45f, sizeMax: 1.3f,
            lifeMin: 0.18f, lifeMax: 0.45f,
            from: new Color(1f, 0.85f, 0.45f), to: new Color(1f, 0.35f, 0.05f));

        // ---------------- smoke ----------------
        BuildBurst(root.transform, particleMaterial,
            count: 22, speed: 3.2f, sizeMin: 1.1f, sizeMax: 2.4f,
            lifeMin: 0.7f, lifeMax: 1.4f,
            from: new Color(0.28f, 0.26f, 0.24f, 0.85f), to: new Color(0.12f, 0.12f, 0.12f, 0f));

        // ---------------- light flash ----------------
        var lightGO = new GameObject("Flash");
        lightGO.transform.SetParent(root.transform, false);
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.25f);
        light.intensity = 9f;
        light.range = 16f;
        light.shadows = LightShadows.None;
        root.AddComponent<ExplosionFade>().flash = light;

        Object.Destroy(root, 2.2f);
    }

    static void BuildBurst(Transform parent, Material material, int count, float speed,
        float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color from, Color to)
    {
        var go = new GameObject("Burst");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(from, to);
        main.gravityModifier = -0.05f; // slight rise
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        if (material == null || material.shader == null) material = FallbackParticleMaterial();
        if (material != null) renderer.material = material;

        ps.Play();
    }

    static Material fallbackParticleMaterial;

    /// <summary>Never-pink safety net: URP particles -> URP unlit -> Sprites/Default.</summary>
    static Material FallbackParticleMaterial()
    {
        if (fallbackParticleMaterial != null) return fallbackParticleMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        var mat = new Material(shader) { color = new Color(1f, 0.6f, 0.25f, 0.9f) };
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        fallbackParticleMaterial = mat;
        return mat;
    }

    /// <summary>Fades the flash light out over the first quarter second.</summary>
    class ExplosionFade : MonoBehaviour
    {
        public Light flash;
        float t;

        void Update()
        {
            if (flash == null) return;
            t += Time.deltaTime * 4f;
            flash.intensity = Mathf.Lerp(9f, 0f, t);
            if (t >= 1f) flash.enabled = false;
        }
    }
}
