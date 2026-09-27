# 26 — Performance, Quality & Cross-Platform

## 1. Targets

| Tier | Hardware | Resolution | Target | Floor |
|---|---|---|---|---|
| High | Discrete GPU (mid-range, ~2020+) | 1920×1080 | 144 fps | 120 fps |
| Medium | Entry discrete / strong integrated | 1920×1080 | 90 fps | 60 fps |
| Low | Integrated graphics | 1600×900 | 60 fps | 45 fps |
| Dedicated server | Headless, 2 cores | — | 60 tick, < 8 ms/tick @ 12 players | — |

**Frame time is the target, not average fps.** We measure the 99th percentile; a stutter is worse
than a lower average.

## 2. Frame budget (16.6 ms @ 60, 8.3 ms @ 120)

| System | Budget @ 120 fps |
|---|---|
| Rendering (GPU-bound work submitted) | 4.5 ms |
| Animation (all characters) | 0.9 ms |
| Physics | 0.6 ms |
| Gameplay + character controllers | 0.8 ms |
| Networking (serialise/deserialise/interp) | 0.4 ms |
| UI / HUD | 0.5 ms |
| Audio | 0.3 ms |
| Slack | 0.3 ms |

## 3. Scene budgets

| Metric | Budget |
|---|---|
| Draw calls (after batching) | ≤ 900 |
| Triangles rendered | ≤ 1.6 M |
| Unique materials per map | ≤ 40 |
| Realtime shadow casters | ≤ 60 |
| Texture memory | ≤ 1.2 GB (High), 600 MB (Low) |
| Dynamic lights per view | ≤ 8 |
| Particle systems active | ≤ 24 |
| Skinned meshes | ≤ 14 (12 players + streaks) |
| Audio voices | ≤ 64 |

Enforced by `Tools/scene-budget.mjs`, which parses the level asset and reports statically —
runnable without an Editor (doc 07 §6).

## 4. Quality subsystem

A real subsystem, not a settings menu (this is what the reference gets right, doc 02 §1).

| Module | Responsibility |
|---|---|
| `QualityPresets` | Low / Medium / High / Ultra / Custom; every knob in one asset |
| `AdaptiveResolution` | Dynamic render scale targeting a frame-time budget; hysteresis so it doesn't oscillate; never below 0.6 |
| `ShadowDirector` | Cascade count/distance/resolution by preset and by scene context |
| `OcclusionCuller` | Baked occlusion + runtime portal checks |
| `StaticBatcher` | Batches static geometry at load |
| `LODManager` | 3 LODs per prop, 2 per character, screen-height driven |
| `FrustumCullingManager` | Wide-net culling for gameplay-irrelevant objects (VFX, decals) |
| `EffectBudget` | Caps concurrent decals/particles/casings, recycling oldest first |

## 5. Preset matrix

| Setting | Low | Medium | High | Ultra |
|---|---|---|---|---|
| Render scale | 0.75 | 0.9 | 1.0 | 1.0 |
| Shadow distance | 35 m | 60 m | 90 m | 140 m |
| Shadow cascades | 1 | 2 | 4 | 4 |
| Shadow resolution | 1024 | 2048 | 2048 | 4096 |
| Texture quality | Half | Half | Full | Full |
| Anisotropic | Off | 4× | 8× | 16× |
| MSAA | Off | 2× | 4× | 4× |
| Post (bloom/AO/motion blur) | Off | Bloom | Bloom+AO | All |
| Particle quality | Low | Medium | High | High |
| Decal count | 24 | 64 | 128 | 256 |
| Ragdolls | 2 | 4 | 8 | 12 |
| LOD bias | 0.6 | 0.8 | 1.0 | 1.2 |

## 6. Optimisation principles

1. **Measure first.** No optimisation without a profile capture showing the cost.
2. **Zero allocation in per-frame paths.** Object pools for bullets, casings, decals, VFX, audio
   sources, UI elements, and network buffers. A GC spike is a lost gunfight.
3. **Avoid `Update` where possible.** Managers tick lists, rather than 200 `MonoBehaviour.Update`
   calls (this is a real, measurable Unity cost).
4. **Cache component lookups.** No `GetComponent` in hot paths.
5. **Physics layers configured tightly** — the collision matrix should be mostly empty.
6. **Animation:** culling mode `Cull Update Transforms` for off-screen characters; IK disabled
   beyond a distance; remote players at reduced animation LOD.
7. **UI:** split canvases so a per-frame element (ammo) never dirties a static canvas (minimap
   frame). This is the single biggest uGUI performance mistake and we design around it.
8. **Shaders:** one URP Lit variant family; aggressive variant stripping at build.

## 7. Server performance

- The dedicated build strips `Presentation` + `UI` assemblies entirely (doc 05 §3).
- Collision-only level loading (doc 17 §5) — no render meshes, no textures.
- Bot perception raycasts budgeted and round-robined (doc 19 §9).
- Target < 8 ms/tick at 12 players + 12 bots on 2 cores.

## 8. Cross-platform readiness

Covered in doc 08 §9 for input. Additionally:

| Concern | Rule |
|---|---|
| Rendering | URP only; no platform-specific shader paths |
| Memory | Asset budgets sized for an 8 GB machine |
| Storage | One `SaveService`; no absolute paths |
| Frame pacing | No `Application.targetFrameRate` assumptions; v-sync respected |
| Resolution | Everything resolution-independent; 16:9 through 21:9 tested |
| Safe area | Respected from day one |
| Text | Localisation table, no literals |
| Threading | No platform-specific threading assumptions |

## 9. Profiling workflow

1. **Static:** `scene-budget` on every map, every commit that touches level assets.
2. **In-Editor:** Unity Profiler captures from the user on the reference machine, per phase.
3. **Automated:** a perf test scene (12 bots, worst-case VFX, full HUD) run as a PlayMode test
   asserting frame-time budgets; fails the build if exceeded.
4. **Telemetry:** in dev builds, a rolling frame-time histogram written to disk, so stutters can be
   correlated with events.

## 10. Acceptance (P9)

- [ ] All three hardware tiers hit their targets on the reference machines.
- [ ] Scene budgets met on all maps (static check clean).
- [ ] Zero GC allocation during a 60-second match sample.
- [ ] Automated perf test passes with 12 bots + full VFX.
- [ ] Dedicated server < 8 ms/tick at 24 participants.
- [ ] All quality presets produce a stable, non-oscillating frame rate.
