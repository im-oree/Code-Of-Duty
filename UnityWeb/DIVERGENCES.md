# Known divergences from the Unity Editor

The harness is **wrong until proven otherwise**. Every gap between what this renders and what
Unity renders is recorded here so a screenshot is never mistaken for ground truth where it isn't.

See `Docs/plan/06-UNITY-WEB-CLONE.md` §10.

| # | Area | Divergence | Impact | Status |
|---|---|---|---|---|
| 1 | Lightmaps / baked GI | Not read; everything is realtime | Baked scenes look flatter | accepted |
| 2 | Post-processing | No URP Volume support yet (bloom, tonemap curve, vignette, AO) | Contrast/glow differ | planned P2 |
| 3 | Skinned meshes | `SkinnedMeshRenderer` parsed but not yet rendered | Characters missing | planned P1 |
| 4 | Particles | `ParticleSystem` parsed but not rendered | VFX missing | planned P2 |
| 5 | uGUI | Canvas detected; widgets not yet laid out | Menu UI missing | planned P1 |
| 6 | Mesh assets | Only Unity's built-in primitives; FBX/GLB not yet loaded | Imported models missing | planned P1 |
| 7 | Prefab instances | `PrefabInstance` (1001) modifications not yet applied | Prefab content missing | planned P1 |
| 8 | Light units | Unity's physical light units approximated; point/spot intensity is not exact | Brightness differs | accepted |
| 9 | Shadows | Single shadow map, no cascades | Shadow quality differs at distance | accepted |
| 10 | Shader variants | Only URP Lit/Unlit mapped; custom shaders fall back to a neutral material | Custom-shaded objects look plain | accepted |

## Calibration log

When the user supplies a real Editor screenshot of a scene, diff it here and record the outcome.

| Date | Scene | Camera | Result | Action |
|---|---|---|---|---|
| — | — | — | — | — |
