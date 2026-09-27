using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation controller for the authored MenuStage in StartMenu.unity.
/// The stage geometry, lights, particle systems, operator and camera are saved
/// scene objects. This class only applies runtime-safe lighting/camera settings
/// and the small looping practical-light motion; it never manufactures a stage.
/// </summary>
public sealed class MenuStage : MonoBehaviour
{
    [Tooltip("The authored live operator preview below this MenuStage.")]
    public OperatorDisplay operatorDisplay;

    Camera menuCamera;
    Light rimLight;
    Light fillLight;
    float rimBaseIntensity;
    float fillBaseIntensity;

    public static MenuStage EnsureInScene()
    {
        MenuStage existing = FindAnyObjectByType<MenuStage>(FindObjectsInactive.Include);
        if (existing == null)
        {
            Debug.LogError("[MenuStage] StartMenu has no authored MenuStage. No runtime replacement will be created.");
            return null;
        }

        existing.Adopt();
        return existing;
    }

    void OnEnable() => Adopt();

    /// <summary>Resolve only the serialized scene objects required for presentation.</summary>
    public void Adopt()
    {
        menuCamera = Camera.main;
        if (menuCamera == null)
        {
            Debug.LogError("[MenuStage] StartMenu is missing its tagged Main Camera.");
            return;
        }

        // Authored transform values establish the tableau. These properties are
        // runtime display policy, so they are set idempotently rather than
        // attempting to construct any visual object.
        menuCamera.fieldOfView = 40f;
        menuCamera.clearFlags = CameraClearFlags.SolidColor;
        menuCamera.backgroundColor = new Color(.02f, .025f, .03f, 1f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = .045f;
        RenderSettings.fogColor = new Color(.03f, .035f, .045f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.16f, .18f, .22f);

        rimLight = FindAuthoredLight("RimLight");
        fillLight = FindAuthoredLight("FillLight");
        if (rimLight != null) rimBaseIntensity = rimLight.intensity;
        if (fillLight != null) fillBaseIntensity = fillLight.intensity;

        if (operatorDisplay == null)
            operatorDisplay = GetComponentInChildren<OperatorDisplay>(true);
        if (operatorDisplay == null)
        {
            Debug.LogError("[MenuStage] MenuStage is missing the authored OperatorDisplay.");
            return;
        }
        operatorDisplay.Adopt();
    }

    Light FindAuthoredLight(string lightName)
    {
        Transform lightTransform = transform.Find(lightName);
        Light light = lightTransform != null ? lightTransform.GetComponent<Light>() : null;
        if (light == null)
            Debug.LogError("[MenuStage] Missing authored stage light: " + lightName);
        return light;
    }

    void Update()
    {
        // Subtle live practical variation gives the serialized scene a sense of
        // motion without allocations, spawned objects, or expensive effects.
        float time = Time.unscaledTime;
        if (rimLight != null) rimLight.intensity = rimBaseIntensity * (1f + Mathf.Sin(time * .73f) * .055f);
        if (fillLight != null) fillLight.intensity = fillBaseIntensity * (1f + Mathf.Sin(time * .51f + 1.7f) * .035f);
    }
}
