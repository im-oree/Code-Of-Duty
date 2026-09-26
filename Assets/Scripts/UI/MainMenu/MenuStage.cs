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

    /// <summary>Where the operator stands, and the yaw that turns him to face the camera.</summary>
    static readonly Vector3 OperatorPosition = new Vector3(-0.6f, 0f, 0f);
    static readonly Quaternion OperatorRotation = Quaternion.Euler(0f, -14f, 0f);

    /// <summary>
    /// Returns the stage that is in the scene, building one only if there is none.
    ///
    /// The set — ground, walls, crates, lights, the operator — is saved scene
    /// content. It is found, not rebuilt. The previous version destroyed every
    /// MenuStage it could see and made a new one on every play, which is half
    /// of why two operators appeared: the destroy was immediate for some
    /// objects and deferred for others, so the rebuild ran while a copy was
    /// still alive and counted it as absent.
    ///
    /// Only the settings that live outside the scene graph are re-applied:
    /// camera framing and RenderSettings fog/ambient are global state that the
    /// scene does not own per-object, so they are idempotent to set and cheap.
    /// </summary>
    public static MenuStage EnsureInScene()
    {
        var existing = FindAnyObjectByType<MenuStage>(FindObjectsInactive.Include);
        if (existing != null)
        {
            existing.Adopt();
            return existing;
        }

        Debug.LogWarning("[MenuStage] no MenuStage in the scene — building one from code. " +
                         "Save the scene to keep it, or run COD / Main Menu / Generate Frontend Scene.");
        var stage = new GameObject("MenuStage").AddComponent<MenuStage>();
        stage.Build();
        return stage;
    }

    /// <summary>Attaches to the set already in the scene without changing it.</summary>
    void Adopt()
    {
        SetupCamera();
        SetupEnvironmentLighting();

        operatorDisplay = GetComponentInChildren<OperatorDisplay>(true);
        if (operatorDisplay == null)
        {
            Debug.LogWarning("[MenuStage] the stage has no OperatorDisplay — creating one.");
            operatorDisplay = OperatorDisplay.EnsureUnder(transform, OperatorPosition, OperatorRotation);
        }
        else
        {
            operatorDisplay.Adopt();
        }
    }

    /// <summary>First-run construction. Only reached when the scene has no stage.</summary>
    void Build()
    {
        SetupCamera();
        SetupEnvironmentLighting();
        BuildSet();
        BuildSmoke();
        BuildDust();

        operatorDisplay = OperatorDisplay.EnsureUnder(transform, OperatorPosition, OperatorRotation);
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null) return;

        // close-up COD-style framing: roughly knees-up on the operator
        cam.transform.SetPositionAndRotation(new Vector3(-0.25f, 1.22f, 2.45f), Quaternion.Euler(2.5f, 178f, 0f));
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1f);

        // Lights outside the stage are deliberately left alone. Switching off
        // every Light in the scene to "own the mood" also switches off whatever
        // the next scene or an editor tool put there, and it is not recorded
        // anywhere — the light just stops working and nothing says why.
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
        Light key = EnsureLight("KeyLight");
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(38f, 205f, 0f);
        key.color = new Color(0.85f, 0.9f, 1f);
        key.intensity = 1.15f;
        key.shadows = LightShadows.Soft;

        // orange rim/practical from behind-left of the operator
        Light rim = EnsureLight("RimLight");
        rim.type = LightType.Point;
        rim.transform.position = new Vector3(2.6f, 1.9f, -2.4f);
        rim.color = new Color(1f, 0.55f, 0.1f);
        rim.intensity = 14f;
        rim.range = 9f;

        // soft cool fill from the right
        Light fill = EnsureLight("FillLight");
        fill.type = LightType.Point;
        fill.transform.position = new Vector3(-2.4f, 1.4f, 2.2f);
        fill.color = new Color(0.4f, 0.55f, 0.85f);
        fill.intensity = 5f;
        fill.range = 8f;
    }

    /// <summary>
    /// The stage light with this name, creating it only if the scene has none.
    ///
    /// This method runs on every play, including the adopt path, so creating
    /// unconditionally would add three more lights to the scene each time you
    /// pressed Play — and the menu would get visibly brighter the longer you
    /// worked on it.
    /// </summary>
    Light EnsureLight(string name)
    {
        Transform existing = transform.Find(name);
        if (existing != null)
            return existing.GetComponent<Light>() ?? existing.gameObject.AddComponent<Light>();

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<Light>();
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
        CreateBlock("Platform", new Vector3(-0.6f, -0.035f, 0f), new Vector3(2.4f, 0.08f, 2.4f),
            new Color(0.10f, 0.105f, 0.12f), 0.45f, 0.6f);

        // thin accent edge on the platform
        CreateBlock("PlatformEdge", new Vector3(-0.6f, 0.006f, 1.21f), new Vector3(2.42f, 0.025f, 0.05f),
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
        var blockCollider = block.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(blockCollider);
        else Object.DestroyImmediate(blockCollider);

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
        vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); // all axes MUST share a mode

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
        var mat = CreateSoftParticleMaterial();
        if (mat != null) renderer.material = mat;
        renderer.sortingOrder = 1;
        return ps;
    }

    static Material softParticleMaterial;

    static Material CreateSoftParticleMaterial()
    {
        if (softParticleMaterial != null) return softParticleMaterial;

        // shader fallback chain so particles can NEVER render magenta:
        // URP particles -> URP unlit -> Sprites/Default (always included)
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null; // caller keeps whatever default exists

        var mat = new Material(shader);
        mat.mainTexture = CreateSoftCircleTexture();
        mat.color = Color.white;

        // transparent alpha-blend setup for URP shaders (guarded per-property)
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
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
