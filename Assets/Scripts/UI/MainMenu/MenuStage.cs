using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the 3D backdrop of the main menu entirely from code:
/// dark concrete ground, far wall, moody fog, orange rim light,
/// drifting smoke and floating dust — with the operator standing
/// front and center holding the selected primary weapon.
/// </summary>
public class MenuStage : MonoBehaviour
{
    public OperatorDisplay operatorDisplay;

    Camera cam;

    public static MenuStage Create()
    {
        var stage = new GameObject("MenuStage").AddComponent<MenuStage>();
        stage.Build();
        return stage;
    }

    void Build()
    {
        SetupCamera();
        SetupEnvironmentLighting();
        BuildSet();
        BuildSmoke();
        BuildDust();

        // the star of the show
        operatorDisplay = OperatorDisplay.Create(transform, new Vector3(0.55f, 0f, 0f), Quaternion.Euler(0f, 162f, 0f));
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null) return;

        cam.transform.SetPositionAndRotation(new Vector3(-0.35f, 1.35f, 3.1f), Quaternion.Euler(4.5f, 178f, 0f));
        cam.fieldOfView = 42f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1f);

        // hide any legacy menu lights so we fully own the mood
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.transform.root != transform.root) light.gameObject.SetActive(false);
        }
    }

    void SetupEnvironmentLighting()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.045f;
        RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.045f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.22f);

        // key light (cool, from camera side)
        Light key = new GameObject("KeyLight").AddComponent<Light>();
        key.transform.SetParent(transform, false);
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(38f, 205f, 0f);
        key.color = new Color(0.85f, 0.9f, 1f);
        key.intensity = 1.15f;
        key.shadows = LightShadows.Soft;

        // orange rim/practical from behind-left of the operator
        Light rim = new GameObject("RimLight").AddComponent<Light>();
        rim.transform.SetParent(transform, false);
        rim.type = LightType.Point;
        rim.transform.position = new Vector3(2.6f, 1.9f, -2.4f);
        rim.color = new Color(1f, 0.55f, 0.1f);
        rim.intensity = 14f;
        rim.range = 9f;

        // soft cool fill from the right
        Light fill = new GameObject("FillLight").AddComponent<Light>();
        fill.transform.SetParent(transform, false);
        fill.type = LightType.Point;
        fill.transform.position = new Vector3(-2.4f, 1.4f, 2.2f);
        fill.color = new Color(0.4f, 0.55f, 0.85f);
        fill.intensity = 5f;
        fill.range = 8f;
    }

    #region Set dressing

    void BuildSet()
    {
        // ground
        CreateBlock("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f),
            new Color(0.055f, 0.06f, 0.068f), 0.05f, 0.35f);

        // far back wall
        CreateBlock("BackWall", new Vector3(0f, 4f, -9f), new Vector3(34f, 10f, 0.6f),
            new Color(0.07f, 0.075f, 0.085f), 0.0f, 0.15f);

        // side wall hint (left of frame)
        CreateBlock("SideWall", new Vector3(9.5f, 3f, -2f), new Vector3(0.6f, 8f, 16f),
            new Color(0.06f, 0.065f, 0.075f), 0.0f, 0.15f);

        // scattered crates behind the operator for depth
        CreateBlock("Crate_A", new Vector3(2.6f, 0.5f, -3.4f), new Vector3(1.1f, 1.1f, 1.1f),
            new Color(0.12f, 0.10f, 0.08f), 0.0f, 0.3f);
        CreateBlock("Crate_B", new Vector3(3.4f, 0.35f, -2.6f), new Vector3(0.75f, 0.75f, 0.75f),
            new Color(0.10f, 0.09f, 0.075f), 0.0f, 0.3f);
        CreateBlock("Crate_C", new Vector3(-3.2f, 0.65f, -4.2f), new Vector3(1.4f, 1.4f, 1.4f),
            new Color(0.09f, 0.095f, 0.11f), 0.3f, 0.45f);

        // low platform the operator stands on
        CreateBlock("Platform", new Vector3(0.55f, -0.035f, 0f), new Vector3(2.4f, 0.08f, 2.4f),
            new Color(0.10f, 0.105f, 0.12f), 0.45f, 0.6f);

        // thin accent edge on the platform
        CreateBlock("PlatformEdge", new Vector3(0.55f, 0.006f, 1.21f), new Vector3(2.42f, 0.025f, 0.05f),
            new Color(1f, 0.54f, 0f), 0.2f, 0.7f, emissive: new Color(1f, 0.45f, 0.05f) * 1.6f);
    }

    GameObject CreateBlock(string name, Vector3 pos, Vector3 scale, Color color,
        float metallic, float smoothness, Color? emissive = null)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(transform, false);
        block.transform.position = pos;
        block.transform.localScale = scale;
        Object.Destroy(block.GetComponent<Collider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (emissive.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emissive.Value);
        }
        block.GetComponent<Renderer>().material = mat;
        return block;
    }

    #endregion

    #region Atmosphere

    void BuildSmoke()
    {
        var ps = CreateParticleObject("Smoke", new Vector3(2.5f, 0.2f, -4.5f));
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.5f, 0.52f, 0.58f, 0.055f);
        main.maxParticles = 60;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;

        var emission = ps.emission;
        emission.rateOverTime = 3.5f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 0.5f, 4f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-0.35f, -0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        ps.Play();
    }

    void BuildDust()
    {
        var ps = CreateParticleObject("Dust", new Vector3(0.5f, 1.6f, 0.5f));
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startSpeed = 0.02f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.02f);
        main.startColor = new Color(1f, 0.85f, 0.6f, 0.5f);
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;

        var emission = ps.emission;
        emission.rateOverTime = 14f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(5f, 3f, 5f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.06f;
        noise.frequency = 0.3f;

        ps.Play();
    }

    ParticleSystem CreateParticleObject(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = CreateSoftParticleMaterial();
        renderer.sortingOrder = 1;
        return ps;
    }

    static Material softParticleMaterial;

    static Material CreateSoftParticleMaterial()
    {
        if (softParticleMaterial != null) return softParticleMaterial;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", CreateSoftCircleTexture());
        mat.SetColor("_BaseColor", Color.white);

        // transparent alpha-blend setup for URP particles
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        softParticleMaterial = mat;
        return mat;
    }

    static Texture2D CreateSoftCircleTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        float maxDist = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float a = Mathf.Clamp01(1f - d);
                a = a * a; // soft falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return tex;
    }

    #endregion
}
