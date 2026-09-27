using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Hides the local player's body from its own first person camera by flipping
/// the configured renderers to shadows-only (the body still casts shadows, so
/// the world stays grounded). Part of the standard COD character setup — the
/// character creator adds it automatically.
/// </summary>
public class CODFirstPersonBody : MonoBehaviour
{
    [Tooltip("Renderers hidden in first person. Leave empty to auto-collect all skinned meshes.")]
    public List<Renderer> bodyRenderers = new List<Renderer>();
    [Tooltip("Renderers that stay visible in first person (arms/weapon), if any")]
    public List<Renderer> keepVisible = new List<Renderer>();

    readonly Dictionary<Renderer, ShadowCastingMode> originalModes = new Dictionary<Renderer, ShadowCastingMode>();
    bool isFirstPerson;

    void Awake()
    {
        if (bodyRenderers.Count == 0)
        {
            bodyRenderers.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }
    }

    /// <summary>Apply or remove the shadows-only mode. Only ever called for the locally controlled player.</summary>
    public void SetFirstPerson(bool value)
    {
        if (isFirstPerson == value)
        {
            return;
        }
        isFirstPerson = value;

        foreach (Renderer r in bodyRenderers)
        {
            if (r == null || keepVisible.Contains(r)) continue;

            if (value)
            {
                if (!originalModes.ContainsKey(r)) originalModes[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            else if (originalModes.TryGetValue(r, out ShadowCastingMode mode))
            {
                r.shadowCastingMode = mode;
            }
        }
    }

    /// <summary>Re-collect renderers (after a model or weapon swap).</summary>
    public void Refresh()
    {
        bool wasFp = isFirstPerson;
        SetFirstPerson(false);
        bodyRenderers.Clear();
        bodyRenderers.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        originalModes.Clear();
        SetFirstPerson(wasFp);
    }
}
