# Sunscorch Depot — Arena redesign

`Assets/Scenes/DMArena1.unity` is now an original desert-industrial 6v6 arena
called **Sunscorch Depot**. It is intentionally an original space rather than a
literal copy of a Call of Duty level: it has the close-quarters, readable
three-lane combat rhythm requested, without shipping a third-party map, logo,
or screenshot.

## Layout and gameplay scale

The map is a 78 m × 78 m depot designed around the included roughly 1.8 m human
player prefab.

- **North lane:** an open-front loading warehouse, catwalk, roof trusses, and
  container stack create a long but interrupted sightline.
- **South lane:** the pump house has a central doorway, external roof stair,
  roof vents, and dense mixed-height cover.
- **East lane:** garage canopy, operational bay, derelict utility truck, and
  containers form vehicle-height cover.
- **West lane:** low salvage-market counters and awnings provide mobile,
  flankable cover instead of another sealed building.
- **Middle:** a two-storey control tower, exposed balcony rails, external stairs,
  plaza, and crossed service roads make a high-risk power position.
- **Perimeter:** broken walls and irregular sculpted berm geometry preserve
  flanking routes while retaining a clean playable boundary.

The five existing gameplay spawn references are retained (so no network/startup
references were broken) and have been moved to valid depot lanes.

## Authored scene content

The hierarchy starts at `SunscorchDepot` and is split into named, editable
production groups:

1. `01_Terrain_And_Lanes` — textured desert base, asphalt lanes, curbs, road
   strips, broken perimeter, and non-rectangular berms.
2. `02_Buildings` — warehouse, pump house, garage, salvage market, and control
   tower, each composed from openings, trusses, rails, doors, stairs, vents, and
   roof elements rather than a single block.
3. `03_Combat_Cover` — detailed containers (door leaves, ribs, top rails),
   barriers, pallet stacks, pipes, rock cover, and a utility truck with wheels.
4. `04_Set_Dressing` — radio gantry, tire piles, barrel clusters, floodlights,
   and eight intentionally loose dynamic props.
5. `05_Atmosphere_Markers` — designer-movable fire and dust positions.

There are **360 renderable static geometry pieces with matching BoxColliders**
and **8 Rigidbody loose props**. Every structural surface is collision-ready;
the dynamic props provide physical interaction without blocking mandatory lanes.
The prior prototype `Props` root remains in the scene but is disabled for easy
comparison and rollback.

## Textures, UV treatment, sky, and atmosphere

Four original repeatable 2K albedo textures are under
`Assets/Art/SunscorchDepot/Textures/`; corresponding URP/Lit materials specify
real tiling values for usable UV density on roads, plaster, corrugated steel,
and wood. The material reference used for the corrugated art direction and its
CC0 source attribution are recorded in `Assets/Art/SunscorchDepot/README.md`.

`SD_DesertSky_Panorama.png` is an equirectangular sky map and is deliberately
placed in `Assets/Resources/SunscorchDepot/` for runtime loading. The
`SunscorchDepotRuntime` component applies it through Unity's
`Skybox/Panoramic` shader, sets desert trilight ambient light, and creates
looping, lightweight barrel flames, embers, fire practical lights, and ambient
dust at the authored marker transforms.

## Editing and regeneration

The scene data is materialized—not hidden in a runtime procedural level
builder—so the Unity hierarchy remains editable. The checked-in generator is
only a reproducible authoring tool:

```bash
python3 Tools/build_sunscorch_depot_scene.py
```

It replaces only the section delimited by `SUNSCORCH_DEPOT_BEGIN/END`, keeps the
scene's startup/UI/network objects and their file references, reapplies the
spawn locations, and validates cleanly via the structural checks below.

## Verification run

The Unity Editor is not installed in this agent environment, so native play-mode
verification must still be done in the editor. Static verification completed
here:

- generator Python compilation and idempotent regeneration;
- all generated serialized references resolve;
- 1,891 generated Unity YAML documents, 421 renderers total in the scene,
  421 colliders total, and 8 Rigidbody components;
- zero duplicate Unity file IDs;
- `UnityWeb`: `npm run check` (28 passing tests) and `npm run build`;
- UnityWeb headless visual inspection rendered the scene successfully at an
  overview and player-height lane views: 491 objects, 421 meshes, one sun light,
  one camera, and no scene-builder warnings.

To inspect it in the browser clone locally:

```bash
cd UnityWeb
npm install
npm run dev
# open the viewer and choose Assets/Scenes/DMArena1.unity
```
