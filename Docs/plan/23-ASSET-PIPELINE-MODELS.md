# 23 — Asset Pipeline: Procedural Models & Rigging

> User requirement: *"for modelling you model actual stuff … make a reusable script that can build
> GLBs or FBX or anything with proper mats and we can use it when tested. I will now upload the
> actual model I want so you can fit to scale, orientation and all … make sure everything is
> replaceable by you: weapons can be added, models changed, attachments added."*

## 1. Two jobs

1. **Generate** original placeholder-to-production geometry procedurally, with correct materials,
   scale, orientation and pivots — so the game is never blocked on art.
2. **Ingest** user-supplied models, normalising them to our conventions so they drop in and work.

Both are served by one toolkit, `Tools/meshkit/`, written in TypeScript on Node (no Blender in the
environment, and none required).

## 2. `meshkit` — the generator

```
Tools/meshkit/
├── core/
│   ├── MeshBuilder.ts      positions, normals, uvs, tangents, indices, submeshes
│   ├── Primitives.ts       box, cylinder, capsule, tube, lathe, prism, extrude, loft
│   ├── Boolean.ts          cheap CSG for cut-outs (vents, trigger guards, rails)
│   ├── Bevel.ts            edge bevelling — what makes hard-surface reads good
│   ├── Modifiers.ts        mirror, array, twist, taper, bend, subdivide, weld
│   ├── UVTools.ts          box/cylindrical/planar projection, atlas packing
│   └── Skeleton.ts         joints, bind poses, skin weights, IK chain metadata
├── materials/
│   └── MaterialLibrary.ts  named PBR materials matching the game's palette
├── export/
│   ├── GLTFExport.ts       .glb — the primary format (Unity reads it via glTFast)
│   └── FBXExport.ts        ASCII FBX fallback for Unity's native importer
├── generators/
│   ├── weapons/            rifle, smg, lmg, marksman, sniper, shotgun, pistol, launcher
│   ├── attachments/        optics, muzzles, barrels, stocks, grips, mags, lasers
│   ├── characters/         operator base mesh, heads, gear variants
│   ├── props/              crates, barrels, containers, railings, pipes, barriers…
│   └── effects/            impact decal meshes, casing shells
└── cli.ts                  `npm run gen -- weapon:rifle --seed 7 --out ...`
```

### Why procedural
- Geometry is **parametric** — change a number, regenerate the whole weapon family.
- Everything is **ours** by construction (doc 01 §5).
- It runs in this environment (Node only) and outputs standard GLB.
- Generated meshes come with correct sockets, pivots, scale and materials **by construction**,
  which is where hand-modelled assets usually cost the most time.

### Quality approach
Low-to-mid poly with **bevelled edges and good silhouettes** (doc 01 §1). A generated rifle is
~3–6k tris with 3–5 material slots. Detail comes from bevels catching light and from material
contrast, not from triangle count.

## 3. Conventions every asset must satisfy

| Convention | Rule |
|---|---|
| Units | 1 unit = 1 metre |
| Up axis | +Y |
| Forward | +Z (Unity forward) |
| Handedness | Left-handed (Unity); the GLB exporter converts from our right-handed authoring space |
| Character pivot | Between the feet, at floor level |
| Weapon pivot | At the **grip**, not the mesh centre — this is what makes hand IK trivial |
| Scale | Real-world: rifle ≈ 0.90 m, operator ≈ 1.80 m |
| Normals | Smoothing groups with hard edges preserved via split vertices |
| UVs | UV0 unique, non-overlapping, 0–1; UV1 reserved for lightmaps |
| Materials | Named from `MaterialLibrary`, never `Material.001` |
| Naming | `WPN_<Class>_<Name>`, `ATT_<Slot>_<Name>`, `CHR_<Name>`, `PRP_<Category>_<Name>` |

An importer validator (`COD / Assets / Validate Selected`) checks every one of these and reports
failures rather than letting a bad asset in quietly.

## 4. Required sockets

Generated and validated automatically.

**Weapons:** `Socket_Muzzle`, `Socket_Barrel`, `Socket_Optic`, `Socket_Stock`,
`Socket_Underbarrel`, `Socket_Magazine`, `Socket_RearGrip`, `Socket_Laser`,
`Muzzle_Point`, `Eject_Point`, `Aim_Point`, `Grip_R`, `Grip_L`.

**Characters:** standard Humanoid bone names plus `Attach_Back`, `Attach_HipL`, `Attach_HipR`,
`Attach_Chest`, `Attach_HandR`, `Attach_HandL`.

## 5. Character rigging

The repo's `Male_Body_BaseMesh.fbx` is an **unrigged static mesh** — a known blocker (doc 03).
Three routes, in order:

1. **Generate a rigged operator** with `meshkit/characters/` — skeleton, bind pose and skin weights
   authored by code. Weights via a heat-diffusion approximation over the mesh, with per-joint
   falloff curves; good enough for a stylised mid-poly body, and fully ours.
2. **Auto-rig a supplied mesh** — fit our standard skeleton to the mesh by landmark detection
   (bounding-box proportions + symmetry plane + limb axis fitting), then transfer weights from the
   generated base mesh by closest-point-on-surface with smoothing.
3. **Accept a rigged upload** — validate bone names against the Humanoid map and the conventions
   above; remap if the naming differs.

Route 3 is what happens when the user uploads their model; routes 1–2 mean we're never blocked
waiting for it.

**Humanoid rig** is mandatory so clips retarget (doc 11 §8).

## 6. Animation generation

`meshkit` also emits animation tracks into the GLB: keyframed curves for `Loco_Idle`, `Loco_Walk_*`,
`Loco_Run_*`, `Loco_Sprint`, `Loco_TacSprint`, `Loco_Crouch*`.

Authored as **procedural curve generators** (a walk cycle is a small set of sine/spline curves per
joint with phase offsets), not captured data. They are correct, in-scale, loopable, and replaceable
by better animation later because they use the contract names in doc 11 §7.

## 7. Ingesting a user-supplied model

```
 1. INSPECT   report: units, axes, bounds, tri count, materials, UV sets, rig, clips
 2. NORMALISE scale to metres, rotate to +Y/+Z, recentre the pivot per §3
 3. RIG       route 2 or 3 above
 4. VALIDATE  conventions + sockets + naming
 5. MATERIALS map incoming materials onto MaterialLibrary equivalents, keeping textures
 6. EMIT      a normalised .glb + a Unity prefab via the existing Weapon/Character wizard
 7. PREVIEW   render turntable + in-hand shots in the harness for approval
```
Step 7 is the point: the user sees the model **in the game's hands, at the right scale**, before
anything else is built on it.

## 8. Replaceability (an explicit requirement)

Everything references assets **by id through a database**, never by direct link from gameplay code:
- Swap a weapon model: change `WeaponProfile.modelRef`. Sockets are re-validated; IK follows
  `Grip_R`/`Grip_L`; attachments follow their sockets. Nothing else changes.
- Swap the operator: change `OperatorDefinition.modelRef`. Clips retarget through Humanoid; poses
  are rig-normalised (doc 11 §4), so tac sprint and every other pose still read correctly.
- Add an attachment: create the asset (doc 13 §10).
- Change movement/UI values: they're in tuning assets, not code.

## 9. Textures

Generated procedurally where needed (`Tools/meshkit/textures/`): noise-based wear, painted-metal
masks, camo patterns, decals. Output as compact PNG atlases. All original.

## 10. Budgets

| Asset | Tris | Materials | Textures |
|---|---|---|---|
| Operator | 12–20k | 3–4 | 2× 2048 |
| Weapon | 3–6k | 3–5 | 1–2× 1024 |
| Attachment | 300–1.5k | 1–2 | shared atlas |
| Small prop | 100–800 | 1 | shared atlas |
| Large prop | 800–3k | 1–2 | shared atlas |

## 11. CLI

```bash
cd Tools/meshkit && npm install
npm run gen -- weapon:rifle --name SAGA7 --seed 12 --out ../../Assets/Models/Weapons/
npm run gen -- character:operator --build lean --out ../../Assets/Models/Characters/
npm run gen -- props:industrial --count 24 --atlas --out ../../Assets/Models/Props/
npm run ingest -- ~/upload/my_model.fbx --type character --report
npm run preview -- Assets/Models/Weapons/SAGA7.glb --turntable 12
```

## 12. Acceptance (P2)

- [ ] `meshkit` generates a rifle, an SMG, a pistol and an operator meeting §3 and §10.
- [ ] Generated operator is rigged (Humanoid), skinned, and plays generated locomotion clips.
- [ ] A weapon generated by the tool imports into Unity via the existing wizard with working
      sockets and correct hand IK, verified by FP and TP screenshots.
- [ ] `ingest` normalises a deliberately wrong-scale, wrong-axis test model correctly.
- [ ] Swapping `WeaponProfile.modelRef` to a new model requires no code change.
- [ ] Turntable previews rendered in the harness for every generated asset.
