# Unity Web Clone

A TypeScript + three.js runtime that opens **real Unity scene and prefab files** and renders them
in a browser — headless for automated screenshots, or interactive for inspection.

It lives outside `Assets/` on purpose, so Unity never imports or compiles it.

Full design: [`Docs/plan/06-UNITY-WEB-CLONE.md`](../Docs/plan/06-UNITY-WEB-CLONE.md)
Verification loop: [`Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md`](../Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md)

## Why it exists

The agent working on this project has no Unity Editor. Without a renderer, every claim about how
something *looks* is a guess. This makes animation, pose, lighting and UI work verifiable.

## Quick start

```bash
# one-time, if the sandbox has no chromium yet
../Tools/setup-headless-browser.sh

npm install

# interactive viewer (binds 0.0.0.0 for the Arena live preview)
npm run dev            # -> http://localhost:5180

# headless screenshot of a real Unity scene
npm run shot -- --scene Assets/Scenes/DMArena1.unity --out ../Artifacts/arena.png
npm run shot -- --scene Assets/Scenes/StartMenu.unity --camera orbit \
                --orbit 0.7,0.45,22,0,2,0 --size 1600x900
```

### `shot` options

| Flag | Meaning |
|---|---|
| `--scene <path>` | Project-relative `.unity` path (required) |
| `--out <path>` | Output PNG (default `../Artifacts/<scene>.png`) |
| `--camera <name>` | A camera in the scene, or `orbit` |
| `--orbit y,p,d[,tx,ty,tz]` | Orbit yaw/pitch/distance and target |
| `--size WxH` | Viewport size (default 1280x720) |
| `--chrome` | Keep the editor panels visible in the capture |
| `--overlay` | Show the scene/warning overlay |
| `--keep-server` | Leave the vite server running for the next call |

## What works today

- Unity YAML parser (`.unity`, `.prefab`, `.mat`, `.asset`) — the real format, with its quirks
- GUID → asset path index built from `Assets/**/*.meta`
- Full GameObject/Transform/RectTransform hierarchy
- Unity → three.js coordinate conversion (left→right handed, `ZXY` euler order)
- Built-in primitive meshes, URP Lit/Unlit materials, textures
- Directional / point / spot lights with shadows
- `RenderSettings`: fog (Linear/Exp/Exp2), ambient (Flat/Trilight/Skybox), default sky
- Scene cameras with Unity's FOV and clip planes
- Hierarchy tree + inspector + orbit navigation
- Headless capture that drives the *same page* the user sees

Measured on `Assets/Scenes/StartMenu.unity`: 1,644 documents → 484 GameObjects parsed and built.

## What does not work yet

Tracked honestly in [`DIVERGENCES.md`](DIVERGENCES.md). Headlines: FBX/GLB meshes, skinned
meshes, prefab instances, uGUI widget layout, particles, post-processing.

**Standing rule: the harness is wrong until proven otherwise.** When it disagrees with the Unity
Editor, log the case in `DIVERGENCES.md` and fix the harness.
