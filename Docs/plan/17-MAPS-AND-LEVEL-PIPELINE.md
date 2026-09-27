# 17 — Maps & Level Pipeline

## 1. Design principles

**Three-lane as the backbone.** Most good arena maps are three routes between the team spawns,
with cross-connections. It works because it creates predictable engagement zones while still
rewarding rotation.

Rules we hold ourselves to:
1. **Every lane has a counter-route.** No corridor without a flank.
2. **Sightlines are capped.** Longest uninterrupted line ≈ 55 m, and only on one lane.
3. **Cover cadence.** A player crossing open ground reaches cover within 1.5 s at run speed.
4. **Verticality that matters, sparingly.** At most two elevated positions per lane, each with at
   least two access routes and a clear counter.
5. **Readable landmarks.** Each area has a distinct colour/shape/material identity so callouts are
   learnable and the minimap is legible.
6. **No spawn that can be seen from the enemy's spawn.** Ever.
7. **Symmetry for competitive modes, asymmetry only with deliberate balance work.**

## 2. `LevelDefinition` (the map asset)

```
identity      id, displayName, description, previewImage, sizeClass(S|M|L)
geometry      sceneRef | prefabRefs[], collisionBakeRef
lighting      sunDirection, sunColour, intensity, ambientMode + colours,
              fog{mode,colour,density,start,end}, skyRef, reflectionProbes[]
spawns        SpawnGroup[] { id, team, objectiveAssoc, points[{pos,rot}] }
objectives    ObjectivePlacement[] { type, id, transform, radius, order }
callouts      CalloutZone[] { name, bounds }
navigation    navMeshRef, jumpLinks[], mantleHints[]
minimap       bounds, orientation, generatedTextureRef, heightSlices[]
gameplay      blockedStreaks[], modeSupport[], maxPlayers,
              outOfBoundsVolumes[], killVolumes[]
audio         ambienceRef, reverbZones[], surfaceOverrides[]
props         PropPlacement[] { catalogId, transform, variantSeed }
```

One asset per map, editable in the Editor, consumed by `LevelLoader`, and **readable by the Unity
Web Clone** so maps can be previewed headlessly.

## 3. Build pipeline

```
 1. PAPER      top-down layout sketch; lanes, cover, spawns, sightline budget
 2. GREYBOX    ProBuilder blockout in-engine at exact final scale
 3. METRICS    validate: sightlines, cover cadence, traversal times, jump/mantle reachability
 4. PLAYTEST   bots + humans; heatmaps of deaths and paths
 5. PROP PASS  swap greybox volumes for catalog props (doc 23 generates them)
 6. ART PASS   materials, lighting, sky, fog, decals, detail
 7. BAKE       collision, NavMesh, occlusion, minimap texture, reflection probes
 8. SIGN-OFF   automated metrics re-run + screenshot set per area
```

**Greybox must be playable and shippable-if-ugly.** Art never fixes a bad layout, so step 4 gates
step 5.

## 4. Spawn groups

Each map declares spawn groups rather than loose points:

| Group kind | Used by |
|---|---|
| `TeamHome_A` / `TeamHome_B` | S&D, round starts, initial spawns |
| `Contested_<lane>` | TDM/Dom/Hardpoint dynamic spawning |
| `Objective_<id>` | spawns weighted toward an owned objective |
| `FFA_Ring` | FFA — spread across the whole map |

The selector in doc 16 §4 scores points *within* the currently valid groups. Group validity flips
with map control, which is how spawn flipping is implemented cleanly.

## 5. Collision & navigation

- **Collision** is authored as simplified convex geometry, not the render mesh. A bake step emits a
  collision-only scene the server loads — the **dedicated server never loads render meshes**,
  which keeps headless builds small and fast (and is enforced by the asmdef split, doc 05 §3).
- **NavMesh** for bots, with jump links and mantle hints authored alongside so bots can use the
  same traversal a player can (doc 19).
- **Vision obstruction volumes** for cheap AI line-of-sight and recon-streak occlusion.

## 6. Callouts

Named zones drive: killfeed context, ping messages, bot chatter, spectator UI, and the minimap.
Authored as boxes; a point-in-zone lookup returns the name. Every square metre of playable space
must be inside exactly one callout zone — an editor validator reports gaps and overlaps.

## 7. Minimap generation

Generated from the level asset, not hand-drawn:
1. Orthographic top-down render of collision geometry at several height slices.
2. Posterise to the UI palette; walls as solid, floors as tint, stairs hatched.
3. Overlay callout labels and objective markers.
4. Export a texture + a bounds transform so world→minimap is a simple affine map.

This runs in the Unity Web Clone too, so minimaps can be regenerated headlessly.

## 8. v1.0 maps (original designs)

| Map | Size | Layout | Identity |
|---|---|---|---|
| **Foundry** *(exists, needs a pass)* | M | Three lanes around a central smelter platform with four ramps; container lanes left/right; catwalks north/south | Industrial, orange/steel, hot light |
| **Substation** | S | Tight two-lane cross with a raised control room; deliberately chaotic | Concrete + electrical, cold blue, enclosed |
| **Terrace** | L | Hillside town: stepped streets, a long market lane, rooftop route, tunnel underpass | Warm stone, sun-bleached, long sightlines on one lane only |

Foundry already exists as a 64×64 industrial arena and is the P6 starting point: convert it to a
`LevelDefinition`, add spawn groups/callouts/objectives, validate metrics, then art pass.

## 9. Streaming & performance

- Maps are single scenes at this size (no streaming needed under ~200×200 m).
- Props are pooled and instanced; identical props share material instances.
- Occlusion baked; the quality subsystem (doc 26) handles LOD and shadow distance.
- Budget per map: see doc 26 §3.

## 10. Validation tooling

`COD / Maps / Validate` runs and reports:
- sightline lengths over budget
- cover-cadence violations (open ground > 1.5 s)
- spawn points visible from enemy spawns or out of bounds
- callout gaps/overlaps
- NavMesh islands, unreachable objectives
- missing surface materials (footsteps would be silent)
- props intersecting playable volume incorrectly

A map cannot ship with any error-level finding.

## 11. Acceptance (P6)

- [ ] Three maps exist as `LevelDefinition` assets and load through `LevelLoader`.
- [ ] All five modes are supported and validated on every map that declares support.
- [ ] `COD / Maps / Validate` clean on all three.
- [ ] Minimaps generated, not hand-authored.
- [ ] Dedicated server loads collision-only and never touches render meshes.
- [ ] Each map has a screenshot set (one per callout zone) in `Artifacts/p6/`.
