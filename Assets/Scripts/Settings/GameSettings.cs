using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Persistent player settings — audio, video (incl. granular URP control),
/// mouse — saved to PlayerPrefs and applied live. Systems that need to react
/// (cameras for FOV, etc.) subscribe to <see cref="Changed"/>.
/// Applied automatically on boot.
/// </summary>
public static class GameSettings
{
    public static event System.Action Changed;

    // ------------------------------------------------------------- audio

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat("set_masterVolume", 1f);
        set { PlayerPrefs.SetFloat("set_masterVolume", Mathf.Clamp01(value)); Apply(); }
    }

    // ------------------------------------------------------------- mouse

    public static float MouseSensitivity
    {
        get => PlayerPrefs.GetFloat("set_mouseSens", 1f);
        set { PlayerPrefs.SetFloat("set_mouseSens", Mathf.Clamp(value, 0.05f, 4f)); Apply(); }
    }

    // ------------------------------------------------------------- video

    public static float FieldOfView
    {
        get => PlayerPrefs.GetFloat("set_fov", 70f);
        set { PlayerPrefs.SetFloat("set_fov", Mathf.Clamp(value, 55f, 110f)); Apply(); }
    }

    public static float RenderScale
    {
        get => PlayerPrefs.GetFloat("set_renderScale", 1f);
        set { PlayerPrefs.SetFloat("set_renderScale", Mathf.Clamp(value, 0.5f, 2f)); Apply(); }
    }

    /// <summary>MSAA samples: 1 (off), 2, 4, 8.</summary>
    public static int Antialiasing
    {
        get => PlayerPrefs.GetInt("set_msaa", 4);
        set { PlayerPrefs.SetInt("set_msaa", Mathf.Clamp(value, 1, 8)); Apply(); }
    }

    public static float ShadowDistance
    {
        get => PlayerPrefs.GetFloat("set_shadowDist", 50f);
        set { PlayerPrefs.SetFloat("set_shadowDist", Mathf.Clamp(value, 0f, 150f)); Apply(); }
    }

    public static int QualityLevel
    {
        get => PlayerPrefs.GetInt("set_quality", QualitySettings.GetQualityLevel());
        set { PlayerPrefs.SetInt("set_quality", Mathf.Clamp(value, 0, QualitySettings.names.Length - 1)); Apply(); }
    }

    public static bool VSync
    {
        get => PlayerPrefs.GetInt("set_vsync", 1) == 1;
        set { PlayerPrefs.SetInt("set_vsync", value ? 1 : 0); Apply(); }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt("set_fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        set { PlayerPrefs.SetInt("set_fullscreen", value ? 1 : 0); Apply(); }
    }

    // ------------------------------------------------------------- apply

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyOnBoot() => Apply();

    public static void Apply()
    {
        AudioListener.volume = MasterVolume;

        // quality level first (it can swap the URP asset), then granular URP
        if (QualitySettings.GetQualityLevel() != QualityLevel)
            QualitySettings.SetQualityLevel(QualityLevel, true);

        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Screen.fullScreen = Fullscreen;

        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.renderScale = RenderScale;
            urp.msaaSampleCount = Antialiasing;
            urp.shadowDistance = ShadowDistance;
        }

        Changed?.Invoke();
        PlayerPrefs.Save();
    }
}
