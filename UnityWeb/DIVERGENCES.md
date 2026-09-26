# Divergences from Unity

Where the Unity Web Clone does not match the Unity Editor, and why.

**Standing rule: when the harness disagrees with the Editor, the harness is wrong
until proven otherwise.** Add the case here, then fix it. A viewer that quietly
renders something plausible but wrong is worse than no viewer, because it
produces confident wrong conclusions.

Target Unity version: **6000.6.3f1**, URP 17.6.0, uGUI 2.6.0, TextMeshPro
(bundled), Input System 1.20.0.

---

## 1. Resolved — was wrong, now matches

These were real defects found by rendering real scenes. Each has a regression
test or a verifiable measurement.

| # | Symptom | Cause | Fix | Guard |
|---|---------|-------|-----|-------|
| R1 | References to Unity's built-in meshes silently broke | The default-resources GUID `0000000000000000e000000000000000` parses as scientific notation → the number `0` | 32-hex-char scalars are always strings; exponents now need a decimal point or a short mantissa | `tests/parser.test.ts` "reads object references" |
| R2 | TMP labels rendered as literal `\u200B`, `8\u20Max` | YAML double-quoted scalars only handled `\"` and `\\` | Full single-pass YAML 1.1 unescape (`\n`, `\t`, `\xXX`, `\uXXXX`, `\UXXXXXXXX`, `\N`, `\_`, `\L`, `\P`) | `tests/parser.test.ts` "decodes double-quoted escapes" |
| R3 | Menu tabs overlapped ("OPERATORSLOADOUT") | `m_characterSpacing` treated as pixels | TMP measures it in em/100: `spacing = characterSpacing * fontSize / 100` | visual, `Artifacts/p0/menu-final.png` |
| R4 | Text too large relative to its boxes at non-reference resolutions | Rects were multiplied by the CanvasScaler factor, font sizes were not | `LayoutNode.canvasScale` carries the factor to paint time | `tests/layout.test.ts` CanvasScaler cases |
| R5 | Uppercase styling applied to the wrong labels | Used bit 8 for UpperCase | TMP `FontStyles`: Bold 1, Italic 2, Underline 4, **LowerCase 8, UpperCase 16**, SmallCaps 32 | visual |
| R6 | Orphaned UI panels laid out against the full screen | Root panels were given the screen rect instead of solving their own anchors | Orphan roots now solve against the screen; only a real Canvas is forced to screen size | visual — `Panel_PLAY` inset 132px |
| R7 | Weapons ~100x too large | Unity's ModelImporter scale was ignored | `importScale = meshes.globalScale × UnitScaleFactor / 100`, read from the `.meta` and the FBX header. Verified against Unity's own derived value (N4_Rifle: 0.165 × 0.01 = 0.00165) | measurement: rifle is 0.15 × 0.44 × 0.40 m |
| R8 | Characters rendered as scattered limbs | Imported vertex data was still left-handed while the scene graph around it had been mirrored | `render/Mirror.ts` — mirror positions, normals, tangents, winding, and bind matrices (`M·A·M`) | visual, `Artifacts/p0/stage3.png` |
| R9 | Character flattened into a pancake (`geometrySize` `[1.789, 0.304, 1.552]`) | `SkinnedMesh.bind()` was given an identity bind matrix, discarding the mesh node's own transform, where FBX exporters park the up-axis correction | Carry `bindMatrix` through, mirrored | visual + `window.uw.skinInfo()` |

---

## 2. Known gaps — not yet implemented

Listed in rough priority order. Each says what you would see, so a wrong
screenshot is never mistaken for a correct one.

| # | Gap | What you see instead | Priority |
|---|-----|----------------------|----------|
| D1 | **Animator state machines.** Clips, controllers, root-motion stripping and explicit playback now work (§5). Not simulated: transition *conditions*, blend trees, layers/avatar masks, IK | A state only changes when something asks it to. Nothing here sets gameplay parameters, so evaluating conditions would invent behaviour | **Medium** — deliberate, see §5 |
| D2 | **Prefab instances** (classID 1001) with `m_Modification` overrides | Nothing renders for the instance. Affects `OfflineTest.unity` only (1 instance); `StartMenu` and `DMArena1` have none | Medium |
| D3 | **Sprites with atlases / non-PNG formats.** Sprite rects, 9-slice borders, TGA/PSD | `Image` falls back to a flat tinted quad — usually right for this project, whose panels are untextured | Medium |
| D4 | **TextMeshPro SDF fidelity.** We render the source TTF with the browser's text engine, not TMP's baked atlas | Glyph advances differ by ~1–3%; no outline, underlay, bevel or gradient | Medium |
| D5 | **Layout components.** `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `GridLayoutGroup`, `ContentSizeFitter`, `AspectRatioFitter` | Children sit at their serialized rects. Correct whenever Unity has already baked the layout into the scene, wrong the moment a driven rect is stale | Medium |
| D6 | **World-space and Screen Space – Camera canvases** (`m_RenderMode` 1 and 2) | Skipped, with a warning | Low — this project uses Overlay |
| D7 | **Particle systems** (classID 198) | Not drawn; warned per system. 2 in `StartMenu` (`Smoke`, `Dust`) | Low |
| D8 | **Post-processing / URP Volumes.** Bloom, tonemapping curves, vignette, colour grading | Neutral tonemap only, so the image is flatter and less contrasty than the Editor | Low |
| D9 | **Shadows and lighting detail.** Only one shadow-casting light; no lightmaps, light probes, reflection probes, or URP's additional-light limits | Broadly right, quantitatively different | Low |
| D10 | **Terrain, LOD groups, occlusion culling, decals** | Not rendered | Low |
| D11 | **Scripts do not execute.** `MonoBehaviour` fields are read for display only | Anything a script builds or moves at runtime is absent. This is deliberate — see below | By design |
| D12 | **Mesh sub-asset identity.** Unity's `fileID → sub-mesh` hash is not reproducible, and this project's model `.meta` files have an empty `internalIDToNameTable` | Resolved by GameObject name instead. Currently **29 exact-name + 1 normalized-name, zero guesses** on `StartMenu`. Guessed matches are warned | Acceptable |

### On D11 — scripts do not run

The viewer renders what is *saved in the scene*. It deliberately does not
emulate `MonoBehaviour` lifecycles.

That is a feature for this project. The main-menu bug is precisely that content
is built by scripts at runtime instead of authored in the scene, and a viewer
that faithfully re-ran those scripts would hide the problem. The gap between
"what the file contains" and "what you see in play mode" is the thing being
measured.

---

## 3. Findings about the project, produced by this tool

Real defects in `Code-Of-Duty` that the renderer surfaced. These belong to
Phase 1, not to the harness.

### F1 — Every UI panel is orphaned from its Canvas — FIXED

`Assets/Scenes/StartMenu.unity` has **8 root RectTransforms with no Canvas
ancestor**:

```
Panel_LOADOUT   Panel_BARRACKS  BottomBar   Panel_OPERATORS
Panel_STORE     Panel_PLAY      TopBar      Panel_SETTINGS
```

The `MenuUI` Canvas exists and **has zero children**. uGUI draws nothing for a
RectTransform that no Canvas owns, and Unity reports no error — the screen is
simply blank. This is the root cause of "the UI is gone".

Fix: reparent all eight under `MenuUI`. Tracked as Phase 1 / D1 in
`Docs/plan/29-PHASE-1-WORKPLAN.md`.

### F2 — The operator is not holding the weapon

`OperatorDisplay/OperatorModel/MonKent` renders correctly (62 bones, skinned,
both `BodyMash` and `HeadMash`), but the rifle sits beside the character rather
than in its hands: no bone attachment, no grip pose. Matches the reported "no
gun, no animation".

### F3 — Five of eight panels are inactive

`Panel_OPERATORS`, `Panel_LOADOUT`, `Panel_BARRACKS`, `Panel_STORE` and
`Panel_SETTINGS` have `m_IsActive: 0`; `TopBar`, `BottomBar` and `Panel_PLAY` are
active. Consistent with the PLAY tab being selected, so this is expected — noted
so it is not mistaken for a rendering fault.

### F4 — The menu camera framing puts the operator at the frame edge

At 16:9 the operator is cropped against the right edge and very close to the
camera. Visible in `Artifacts/p0/menu-final.png`.

---

### F5 — The menu's idle animation is a single frame — FIXED

`Assets/Resources/Character/MenuIdleAnimator.controller` has exactly one state,
`MenuIdle`, and it is bound to the clip `Root|Aim_C_Idle`.

That clip is **0.0333 s long — one frame at 30 fps.** It is a crouched-aim
*pose*, not an animation. So the reported symptom "animation is gone" in the
main menu is true at the asset level, and no amount of code will fix it: the
controller is pointed at a still.

`MonKent.fbx` carries 46 usable takes. Looping candidates for a menu idle:

| Clip | Length | Note |
|---|---|---|
| `Root|Idle` | 0.70 s | standing idle, verified to play and loop |
| `Root|Aim_W_Idle` | 0.70 s | weapon-ready standing idle — the better fit for a menu operator holding a rifle |
| `Root|Aim_Idle.TL` / `.TR` | 0.67 s | slow lean left/right, useful as an additive |

Fixed: `MenuIdle` now points at `Root|Aim_W_Idle` (21 frames, `loopTime: 1`).
The renderer reports `state "MenuIdle" -> clip "Root|Aim_W_Idle"`, 0.70 s, with no
fallback guess.

### F6 — Baked root motion must be discarded, and Unity is already doing so

Every take in `MonKent.fbx` animates the top bone `Root` with the character's
travel baked in. Playing those tracks verbatim launched the operator 1.19 m into
the air.

This is not a rendering bug. The scene's Animator has `m_ApplyRootMotion: 0`,
and Unity's contract for that flag is that the clip's root translation is *not*
applied — the animation plays in place. The renderer now implements the same
rule (`AnimatorInstance.stripRootMotion`), after which the rig's root sits at
exactly the GameObject's world position, `(-0.600, 0.007, 0.000)`.

Worth knowing before the gameplay work: any locomotion built on these clips gets
its movement from code, never from the clips.

## 4. Calibration log

Side-by-side comparisons against the real Editor. **Empty until the Unity Editor
is available** — no entry here may be filled in from reasoning alone.

| Date | Scene | Unity screenshot | UnityWeb screenshot | Δ | Action |
|------|-------|------------------|---------------------|---|--------|
| _(none yet)_ | | | | | |

To add an entry: capture the same scene and camera in both, put the two PNGs in
`Artifacts/calibration/`, and record the difference and what was done about it.

---

## 5. What the Animator layer does and does not do

Scope was chosen so the tool stays *evidence*, not a second implementation that
can disagree with Unity.

**Implemented**

- Clips are read from the model file the Animator's meshes came from. This
  project has **zero `.anim` assets** — all 46 takes live inside `MonKent.fbx`
  as FBX AnimStacks, which is why clip discovery follows the mesh, not the
  `Assets/Animations` folder that does not exist.
- Track values cross the same handedness mirror as vertex data: position
  `(x, y, −z)`, quaternion `(−x, −y, z, w)`, scale unchanged. Skipping this
  makes a character animate as its own mirror image, which is subtle enough to
  pass a glance and fail a screenshot comparison.
- `.controller` files are parsed for their layers, states, default state and
  each state's motion, so the mapping from state name to clip is real rather
  than guessed.
- Root motion is stripped when the Animator disables it (F6).
- The default state plays on load, so a scene opens the way Unity would show it.

**Not implemented, on purpose**

- *Transition conditions.* They read parameters that gameplay scripts set, and
  no scripts run here (D11). Evaluating them would mean inventing values and
  then presenting the result as what Unity does.
- *Blend trees, layers, avatar masks, IK.* Each is a place where a plausible
  approximation would be worse than an obvious absence, because a
  nearly-right pose invites you to trust it.

Instead, playback is explicit and addressable:

```
--list-anims                 every animator, its controller, states and clips
--anim "Root|Aim_W_Idle"     play a clip or a controller state by name
--anim-time 0.4              freeze at a normalised position, for a stable shot
--advance 0.75               step forward N seconds, then capture
--eval "window.uw.bones(undefined,'hand')"   read bone transforms numerically
```

Freezing at a normalised time is what makes animation review possible at all: a
screenshot of a moving rig is not reproducible, so a pose has to be addressable
before two captures can be compared.
