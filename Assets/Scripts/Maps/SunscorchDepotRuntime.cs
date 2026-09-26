using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runtime atmosphere for the Sunscorch Depot multiplayer arena.
/// The authored scene owns geometry, collisions and rigidbody props; this
/// component only supplies lightweight looping fire, embers, dust and the
/// panoramic sky material that cannot be serialized reliably without opening
/// the Unity editor. Markers are ordinary transforms in the scene hierarchy,
/// so designers can move every source visually.
/// </summary>
public sealed class SunscorchDepotRuntime : MonoBehaviour
{
    static Material particleMaterial;

    void Awake()
    {
        ConfigureSkybox();

        foreach (Transform marker in GetComponentsInChildren<Transform>(true))
        {
            if (marker == transform || marker.name.StartsWith("Runtime", System.StringComparison.Ordinal)) continue;

            if (marker.name.StartsWith("FX_FireBarrel", System.StringComparison.Ordinal))
                CreateBarrelFire(marker);
            else if (marker.name == "FX_DustVolume")
                CreateDust(marker);
        }
    }

    static void ConfigureSkybox()
    {
        var shader = Shader.Find("Skybox/Panoramic");
        var panorama = Resources.Load<Texture2D>("SunscorchDepot/SD_DesertSky_Panorama");
        if (shader == null || panorama == null) return;

        var sky = new Material(shader) { name = "Sunscorch Depot Panoramic Sky (Runtime)" };
        sky.SetTexture("_MainTex", panorama);
        sky.SetFloat("_Exposure", 1.05f);
        sky.SetFloat("_Rotation", 18f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.64f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.58f, 0.43f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.12f, 0.08f);
        DynamicGI.UpdateEnvironment();
    }

    static void CreateBarrelFire(Transform marker)
    {
        if (marker.Find("RuntimeFire") != null) return;

        var fireRoot = new GameObject("RuntimeFire");
        fireRoot.transform.SetParent(marker, false);

        var fire = fireRoot.AddComponent<ParticleSystem>();
        var main = fire.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.36f, 0.72f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.14f, 0.015f, 0.9f), new Color(1f, 0.72f, 0.06f, 0.95f));
        main.gravityModifier = -0.06f;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = fire.emission;
        emission.rateOverTime = 34f;
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.18f;
        shape.angle = 14f;
        var noise = fire.noise;
        noise.enabled = true;
        noise.strength = 0.32f;
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.25f;
        var color = fire.colorOverLifetime;
        color.enabled = true;
        var fireGradient = new Gradient();
        fireGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.92f, 0.38f), 0f),
                new GradientColorKey(new Color(1f, 0.2f, 0.015f), 0.55f),
                new GradientColorKey(new Color(0.18f, 0.02f, 0.005f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.14f),
                new GradientAlphaKey(0.6f, 0.72f), new GradientAlphaKey(0f, 1f)
            });
        color.color = fireGradient;
        var size = fire.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.2f));

        var renderer = fire.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();
        renderer.sortingOrder = 2;

        var embers = new GameObject("RuntimeEmbers");
        embers.transform.SetParent(marker, false);
        var ember = embers.AddComponent<ParticleSystem>();
        var emberMain = ember.main;
        emberMain.loop = true;
        emberMain.playOnAwake = false;
        emberMain.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        emberMain.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.7f);
        emberMain.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        emberMain.startColor = new Color(1f, 0.35f, 0.04f, 0.9f);
        emberMain.maxParticles = 45;
        var emberEmission = ember.emission;
        emberEmission.rateOverTime = 8f;
        var emberShape = ember.shape;
        emberShape.shapeType = ParticleSystemShapeType.Cone;
        emberShape.radius = 0.22f;
        emberShape.angle = 10f;
        var emberRenderer = ember.GetComponent<ParticleSystemRenderer>();
        emberRenderer.material = GetParticleMaterial();
        emberRenderer.sortingOrder = 3;

        var lightObject = new GameObject("RuntimeFireLight");
        lightObject.transform.SetParent(marker, false);
        lightObject.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        var practical = lightObject.AddComponent<Light>();
        practical.type = LightType.Point;
        practical.color = new Color(1f, 0.26f, 0.035f);
        practical.intensity = 3.2f;
        practical.range = 5.5f;
        practical.shadows = LightShadows.None;

        fire.Play();
        ember.Play();
    }

    static void CreateDust(Transform marker)
    {
        if (marker.Find("RuntimeDust") != null) return;

        var dustRoot = new GameObject("RuntimeDust");
        dustRoot.transform.SetParent(marker, false);
        var dust = dustRoot.AddComponent<ParticleSystem>();
        var main = dust.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 13f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.13f);
        main.startColor = new Color(0.85f, 0.63f, 0.34f, 0.26f);
        main.maxParticles = 180;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;

        var emission = dust.emission;
        emission.rateOverTime = 15f;
        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(58f, 2.5f, 58f);
        var velocity = dust.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.18f);
        var noise = dust.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.16f;
        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetParticleMaterial();
        renderer.sortingOrder = 1;
        dust.Play();
    }

    static Material GetParticleMaterial()
    {
        if (particleMaterial != null) return particleMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        particleMaterial = new Material(shader) { name = "Sunscorch Depot Particle Material" };
        if (particleMaterial.HasProperty("_Surface"))
        {
            particleMaterial.SetFloat("_Surface", 1f);
            particleMaterial.SetOverrideTag("RenderType", "Transparent");
            particleMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            particleMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            particleMaterial.SetInt("_ZWrite", 0);
            particleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        particleMaterial.renderQueue = (int)RenderQueue.Transparent;
        return particleMaterial;
    }
}
