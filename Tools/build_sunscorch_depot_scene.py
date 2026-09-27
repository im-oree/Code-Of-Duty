#!/usr/bin/env python3
"""Materialise the Sunscorch Depot arena into DMArena1.unity.

This generator deliberately emits standard Unity YAML primitives rather than
keeping level geometry hidden in a runtime builder. The resulting hierarchy is
fully visible, editable and collision-ready in the Unity editor. Run it after
changing the layout declarations below; it removes and recreates only the
marked Sunscorch Depot section.
"""
from __future__ import annotations

import hashlib
import math
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

ROOT = Path(__file__).resolve().parents[1]
SCENE = ROOT / "Assets/Scenes/DMArena1.unity"
BEGIN = "# SUNSCORCH_DEPOT_BEGIN"
END = "# SUNSCORCH_DEPOT_END"
CUBE = 10202
SPHERE = 10207
CYLINDER = 10206


def guid(path: str) -> str:
    return re.search(r"^guid: ([0-9a-f]+)", (ROOT / path).read_text(), re.M).group(1)


M = {
    "ground": guid("Assets/Art/SunscorchDepot/Materials/SD_DustyAsphalt.mat.meta"),
    "plaster": guid("Assets/Art/SunscorchDepot/Materials/SD_DesertPlaster.mat.meta"),
    "corrugated": guid("Assets/Art/SunscorchDepot/Materials/SD_RustedCorrugated.mat.meta"),
    "wood": guid("Assets/Art/SunscorchDepot/Materials/SD_WeatheredWood.mat.meta"),
    "concrete": guid("Assets/Materials/Arena/Concrete.mat.meta"),
    "concrete_dark": guid("Assets/Materials/Arena/ConcreteDark.mat.meta"),
    "steel": guid("Assets/Materials/Arena/Steel.mat.meta"),
    "blue": guid("Assets/Materials/Arena/MetalBlue.mat.meta"),
    "red": guid("Assets/Materials/Arena/MetalRed.mat.meta"),
    "orange": guid("Assets/Materials/Arena/AccentOrange.mat.meta"),
}
SCRIPT_GUID = guid("Assets/Scripts/Maps/SunscorchDepotRuntime.cs.meta")


class Ids:
    def __init__(self) -> None:
        self.value = 930000000

    def take(self) -> int:
        self.value += 1
        return self.value


ids = Ids()


def v3(v: tuple[float, float, float]) -> str:
    return "{x: %.5g, y: %.5g, z: %.5g}" % v


def q_euler(e: tuple[float, float, float]) -> tuple[float, float, float, float]:
    """Unity-compatible enough for authored yaw / single-axis ramp rotations."""
    x, y, z = [math.radians(a) / 2 for a in e]
    # Quaternion corresponding to Unity's documented Z-X-Y euler order.
    sx, cx, sy, cy, sz, cz = math.sin(x), math.cos(x), math.sin(y), math.cos(y), math.sin(z), math.cos(z)
    return (sx * cy * cz + cx * sy * sz,
            cx * sy * cz - sx * cy * sz,
            cx * cy * sz - sx * sy * cz,
            cx * cy * cz + sx * sy * sz)


def quat(e: tuple[float, float, float]) -> str:
    x, y, z, w = q_euler(e)
    return "{x: %.7g, y: %.7g, z: %.7g, w: %.7g}" % (x, y, z, w)


@dataclass
class Node:
    name: str
    pos: tuple[float, float, float] = (0, 0, 0)
    scale: tuple[float, float, float] = (1, 1, 1)
    rotation: tuple[float, float, float] = (0, 0, 0)
    material: Optional[str] = None
    primitive: int = CUBE
    collider: bool = False
    rigidbody: bool = False
    mass: float = 10.0
    static: bool = True
    script: Optional[str] = None
    children: list["Node"] = field(default_factory=list)
    go: int = field(default_factory=ids.take)
    tr: int = field(default_factory=ids.take)
    col: Optional[int] = None
    mf: Optional[int] = None
    mr: Optional[int] = None
    rb: Optional[int] = None
    mono: Optional[int] = None

    def __post_init__(self) -> None:
        if self.material:
            if self.collider:
                self.col = ids.take()
            self.mf, self.mr = ids.take(), ids.take()
            if self.rigidbody:
                self.rb = ids.take()
        if self.script:
            self.mono = ids.take()

    def add(self, node: "Node") -> "Node":
        self.children.append(node)
        return node


def group(name: str, pos=(0, 0, 0), rotation=(0, 0, 0), script: Optional[str] = None) -> Node:
    return Node(name, pos=pos, rotation=rotation, script=script, static=False)


def box(name: str, pos, scale, material, *, parent: Node, rotation=(0, 0, 0), physics=False, mass=10.0) -> Node:
    return parent.add(Node(name, pos, scale, rotation, material, CUBE, True, physics, mass, not physics))


def cylinder(name: str, pos, scale, material, *, parent: Node, rotation=(0, 0, 0), physics=False, mass=10.0) -> Node:
    return parent.add(Node(name, pos, scale, rotation, material, CYLINDER, True, physics, mass, not physics))


def sphere(name: str, pos, scale, material, *, parent: Node, rotation=(0, 0, 0)) -> Node:
    return parent.add(Node(name, pos, scale, rotation, material, SPHERE, True, False, 10.0, True))


def marker(name: str, pos, *, parent: Node) -> Node:
    return parent.add(Node(name, pos=pos, static=False))


def emit_node(node: Node) -> str:
    comps = [node.tr]
    if node.col: comps.append(node.col)
    if node.mf: comps.append(node.mf)
    if node.mr: comps.append(node.mr)
    if node.rb: comps.append(node.rb)
    if node.mono: comps.append(node.mono)
    comp_yaml = "\n".join(f"  - component: {{fileID: {c}}}" for c in comps)
    static_flags = 4294967295 if node.static else 0
    out = [f"--- !u!1 &{node.go}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n{comp_yaml}\n  m_Layer: 0\n  m_Name: {node.name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: {static_flags}\n  m_IsActive: 1"]
    children = "\n".join(f"  - {{fileID: {child.tr}}}" for child in node.children)
    children_field = "\n" + children if children else " []"
    father = 0  # filled by the caller when traversing
    parent = getattr(node, "parent", None)
    if parent is not None:
        father = parent.tr
    out.append(f"--- !u!4 &{node.tr}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  serializedVersion: 2\n  m_LocalRotation: {quat(node.rotation)}\n  m_LocalPosition: {v3(node.pos)}\n  m_LocalScale: {v3(node.scale)}\n  m_ConstrainProportionsScale: 0\n  m_Children:{children_field}\n  m_Father: {{fileID: {father}}}\n  m_LocalEulerAnglesHint: {v3(node.rotation)}")
    if node.col:
        out.append(f"--- !u!65 &{node.col}\nBoxCollider:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  m_Material: {{fileID: 0}}\n  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_LayerOverridePriority: 0\n  m_IsTrigger: 0\n  m_ProvidesContacts: 0\n  m_Enabled: 1\n  serializedVersion: 3\n  m_Size: {{x: 1, y: 1, z: 1}}\n  m_Center: {{x: 0, y: 0, z: 0}}")
    if node.mf:
        out.append(f"--- !u!33 &{node.mf}\nMeshFilter:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  m_Mesh: {{fileID: {node.primitive}, guid: 0000000000000000e000000000000000, type: 0}}")
    if node.mr:
        out.append(f"--- !u!23 &{node.mr}\nMeshRenderer:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  m_Enabled: 1\n  m_CastShadows: 1\n  m_ReceiveShadows: 1\n  m_DynamicOccludee: 1\n  m_StaticShadowCaster: 1\n  m_MotionVectors: 1\n  m_LightProbeUsage: 1\n  m_ReflectionProbeUsage: 1\n  m_RayTracingMode: 2\n  m_RayTraceProcedural: 0\n  m_RayTracingAccelStructBuildFlagsOverride: 0\n  m_RayTracingAccelStructBuildFlags: 1\n  m_SmallMeshCulling: 1\n  m_ForceMeshLod: -1\n  m_MeshLodSelectionBias: 0\n  m_RenderingLayerMask: 1\n  m_RendererPriority: 0\n  m_Materials:\n  - {{fileID: 2100000, guid: {M[node.material]}, type: 2}}\n  m_StaticBatchInfo:\n    firstSubMesh: 0\n    subMeshCount: 0\n  m_StaticBatchRoot: {{fileID: 0}}\n  m_ProbeAnchor: {{fileID: 0}}\n  m_LightProbeVolumeOverride: {{fileID: 0}}\n  m_ScaleInLightmap: 1\n  m_ReceiveGI: 1\n  m_PreserveUVs: 0\n  m_IgnoreNormalsForChartDetection: 0\n  m_ImportantGI: 0\n  m_StitchLightmapSeams: 1\n  m_SelectedEditorRenderState: 3\n  m_MinimumChartSize: 4\n  m_AutoUVMaxDistance: 0.5\n  m_AutoUVMaxAngle: 89\n  m_LightmapParameters: {{fileID: 0}}\n  m_GlobalIlluminationMeshLod: 0\n  m_SortingLayerID: 0\n  m_SortingLayer: 0\n  m_SortingOrder: 0\n  m_MaskInteraction: 0\n  m_AdditionalVertexStreams: {{fileID: 0}}")
    if node.rb:
        out.append(f"--- !u!54 &{node.rb}\nRigidbody:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  serializedVersion: 2\n  m_Mass: {node.mass}\n  m_Drag: 0.12\n  m_AngularDrag: 0.15\n  m_UseGravity: 1\n  m_IsKinematic: 0\n  m_Interpolate: 1\n  m_Constraints: 0\n  m_CollisionDetection: 0")
    if node.mono:
        out.append(f"--- !u!114 &{node.mono}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {node.go}}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {node.script}, type: 3}}\n  m_Name:\n  m_EditorClassIdentifier:")
    return "\n".join(out)


def emit_tree(node: Node, parent: Optional[Node] = None) -> str:
    node.parent = parent
    text = emit_node(node)
    for child in node.children:
        text += "\n" + emit_tree(child, node)
    return text


def compose_map() -> Node:
    root = group("SunscorchDepot", script=SCRIPT_GUID)
    terrain = root.add(group("01_Terrain_And_Lanes"))
    buildings = root.add(group("02_Buildings"))
    cover = root.add(group("03_Combat_Cover"))
    setdress = root.add(group("04_Set_Dressing"))
    effects = root.add(group("05_Atmosphere_Markers"))

    # Broad play space: a 72 m depot, three readable lanes and a central plaza.
    box("DesertTerrainBase", (0, -0.65, 0), (78, 1.2, 78), "plaster", parent=terrain)
    box("EastWest_ServiceRoad", (0, -0.02, 0), (72, 0.12, 10.5), "ground", parent=terrain)
    box("NorthSouth_ServiceRoad", (0, -0.015, 0), (10.5, 0.13, 72), "ground", parent=terrain)
    box("Central_Plaza_Paving", (0, 0.06, 0), (19.5, 0.16, 19.5), "concrete_dark", parent=terrain)
    # Curbs and lane-edge strips provide strong traversal read at player height.
    for x, z, sx, sz in [(-35, 6, .32, 58), (35, -6, .32, 58), (-6, 35, 58, .32), (6, -35, 58, .32)]:
        box("Raised_Curb", (x, .18, z), (sx, .34, sz), "concrete", parent=terrain)
    for x in (-4.15, 4.15): box("Road_Edge_NorthSouth", (x, .09, 0), (.16, .035, 67), "orange", parent=terrain)
    for z in (-4.15, 4.15): box("Road_Edge_EastWest", (0, .09, z), (67, .035, .16), "orange", parent=terrain)
    # Low irregular berms are sphere geometry, breaking the rectangular outer silhouette.
    for i, (x, z, sx, sy, sz) in enumerate([(-34, -30, 6, 1.8, 3), (-31, 26, 5, 1.4, 4), (31, -27, 6, 1.7, 3), (33, 24, 5, 1.5, 4), (-20, -35, 7, 1.4, 2), (18, 35, 8, 1.6, 2), (-36, 4, 2, 1.2, 6), (36, -5, 2, 1.3, 6)]):
        sphere(f"Sculpted_Berm_{i:02}", (x, .12, z), (sx, sy, sz), "plaster", parent=terrain)
    # Broken perimeter walls leave deliberate flanking entrances rather than a boxed arena.
    for name, pos, scale in [
        ("North_Perimeter_West", (-22, 2.2, 37), (23, 4.4, .7)), ("North_Perimeter_East", (22, 2.2, 37), (23, 4.4, .7)),
        ("South_Perimeter_West", (-24, 2.0, -37), (19, 4.0, .7)), ("South_Perimeter_East", (24, 2.0, -37), (19, 4.0, .7)),
        ("West_Perimeter_North", (-37, 2.1, 23), (.7, 4.2, 19)), ("West_Perimeter_South", (-37, 2.1, -23), (.7, 4.2, 19)),
        ("East_Perimeter_North", (37, 2.1, 23), (.7, 4.2, 19)), ("East_Perimeter_South", (37, 2.1, -23), (.7, 4.2, 19)),
    ]:
        box(name, pos, scale, "plaster", parent=terrain)

    # North warehouse — long-range north lane anchor with a usable open loading bay.
    north = buildings.add(group("North_Warehouse", (0, 0, 25)))
    box("Warehouse_BackWall", (0, 3.5, 5), (24, 7, .55), "corrugated", parent=north)
    box("Warehouse_WestWall", (-12, 3.5, 0), (.55, 7, 10), "plaster", parent=north)
    box("Warehouse_EastWall", (12, 3.5, 0), (.55, 7, 10), "plaster", parent=north)
    box("Warehouse_Roof", (0, 7.15, 0), (24.8, .42, 10.7), "corrugated", parent=north)
    for x in (-10.5, -6.5, -2.1, 2.1, 6.5, 10.5):
        cylinder("Warehouse_IBeam", (x, 3.4, -4.5), (.22, 3.5, .22), "steel", parent=north)
        box("Warehouse_RoofTruss", (x, 6.7, 0), (.18, .25, 9.7), "steel", parent=north)
    for x in (-7.5, 0, 7.5):
        box("Warehouse_LoadingBay", (x, 1.15, -4.72), (5.3, 2.25, .28), "concrete_dark", parent=north)
        box("Warehouse_BayHeader", (x, 3.45, -4.72), (5.7, .38, .45), "steel", parent=north)
    box("Warehouse_InteriorCatwalk", (0, 4.75, 1.65), (20.5, .24, 1.35), "steel", parent=north)
    for x in (-8.5, 8.5):
        box("Warehouse_CatwalkRail", (x, 5.35, .85), (.12, 1.15, 2.6), "orange", parent=north)

    # South pump house: compact opposite anchor, with roof service pipes and a rear stair route.
    south = buildings.add(group("South_Pump_House", (1, 0, -25)))
    box("PumpHouse_BackWall", (0, 2.9, -5.5), (18, 5.8, .6), "plaster", parent=south)
    box("PumpHouse_WestWall", (-9, 2.9, 0), (.6, 5.8, 11), "plaster", parent=south)
    box("PumpHouse_EastWall", (9, 2.9, 0), (.6, 5.8, 11), "plaster", parent=south)
    box("PumpHouse_Roof", (0, 5.95, 0), (18.7, .42, 11.7), "corrugated", parent=south)
    box("PumpHouse_FrontCap_Left", (-6.5, 2.9, 5.25), (5, 5.8, .55), "plaster", parent=south)
    box("PumpHouse_FrontCap_Right", (6.5, 2.9, 5.25), (5, 5.8, .55), "plaster", parent=south)
    for x in (-5.5, 5.5):
        cylinder("PumpHouse_RoofVent", (x, 6.6, 0), (.5, .7, .5), "steel", parent=south)
        cylinder("PumpHouse_VentCap", (x, 7.25, 0), (.72, .11, .72), "steel", parent=south)
    # exterior metal stairs to an alternate roof route
    for step in range(7):
        box("PumpHouse_RoofStep", (-11.1, .22 + step * .38, -1.9 + step * .54), (3.2, .38, .62), "steel", parent=south)

    # East garage uses a canopy, work bay, rolling door and a visible utility vehicle.
    east = buildings.add(group("East_Garage", (25, 0, -3)))
    box("Garage_BackWall", (5.5, 3, 0), (.55, 6, 17), "corrugated", parent=east)
    box("Garage_NorthWall", (0, 3, 8.5), (11, 6, .55), "plaster", parent=east)
    box("Garage_SouthWall", (0, 3, -8.5), (11, 6, .55), "plaster", parent=east)
    box("Garage_Roof", (0, 6.2, 0), (11.5, .4, 17.5), "corrugated", parent=east)
    for z in (-5.5, 0, 5.5): cylinder("Garage_FrontPost", (-5.1, 3, z), (.26, 3.05, .26), "steel", parent=east)
    box("Garage_OverheadDoor", (-5.18, 2.5, 0), (.36, 4.8, 7.4), "blue", parent=east)
    box("Garage_OverheadDoorStripe", (-5.4, 2.45, 0), (.08, .32, 7.6), "orange", parent=east)
    for z in (-5.3, 0, 5.3): box("Garage_CanopyBeam", (-6.65, 5.75, z), (3.4, .32, .18), "steel", parent=east)

    # West market is deliberately low cover, with open awnings rather than another full cube building.
    west = buildings.add(group("West_Salvage_Market", (-25, 0, 4)))
    box("Market_StoreBack", (-5, 2.5, 0), (.55, 5, 18), "corrugated", parent=west)
    for z in (-6, 0, 6):
        box("Market_StallCounter", (0, 1.05, z), (7.4, 1.6, 2.2), "wood", parent=west)
        box("Market_Awning", (0, 4.2, z), (8.4, .25, 3.3), "red", parent=west)
        for x in (-3.6, 3.6): cylinder("Market_AwningPost", (x, 2.05, z), (.13, 2.1, .13), "steel", parent=west)
    box("Market_UpperWalkway", (-2.0, 5.1, 0), (5.2, .25, 16), "steel", parent=west)

    # Centre tower gives vertical control but exposes the user via railings and two stair approaches.
    tower = buildings.add(group("Central_Control_Tower", (0, 0, 0)))
    box("Tower_Base", (0, 1.4, 0), (8.2, 2.8, 8.2), "plaster", parent=tower)
    box("Tower_UpperRoom", (0, 5.1, 0), (6.4, 4.4, 6.4), "corrugated", parent=tower)
    box("Tower_Roof", (0, 7.45, 0), (7.2, .35, 7.2), "steel", parent=tower)
    for x in (-4.5, 4.5):
        for z in (-4.5, 4.5): cylinder("Tower_CornerColumn", (x, 3.55, z), (.25, 3.65, .25), "steel", parent=tower)
    # balcony perimeter / rail fragments
    for x, z, sx, sz in [(0, 4.55, 9.4, .12), (0, -4.55, 9.4, .12), (4.55, 0, .12, 9.4), (-4.55, 0, .12, 9.4)]:
        box("Tower_BalconyRail", (x, 4.95, z), (sx, .82, sz), "orange", parent=tower)
    for side in (-1, 1):
        for step in range(8):
            box("Tower_Stair", (side * 5.9, .23 + step * .38, -2.8 + step * .65), (3.0, .38, .72), "concrete_dark", parent=tower)
    cylinder("Tower_AntennaMast", (0, 10.4, 0), (.14, 3.0, .14), "steel", parent=tower)
    for h, r in [(8.0, 1.45), (9.2, 1.1), (10.3, .72)]: cylinder("Tower_AntennaDish", (0, h, 0), (r, .08, r), "orange", parent=tower)

    # Container lanes / articulated cover. Door ribs, locks and roof beams provide more than bare boxes.
    containers = cover.add(group("Containers_And_Barriers"))
    def container(name, pos, yaw, material):
        c = containers.add(group(name, pos, (0, yaw, 0)))
        box("ContainerBody", (0, 1.35, 0), (2.55, 2.7, 7.0), material, parent=c)
        for z in (-3.1, -2.05, -1.0, 0, 1.0, 2.05, 3.1):
            box("CorrugatedRib", (1.31, 1.35, z), (.10, 2.5, .13), "steel", parent=c)
        for x in (-.65, .65):
            box("ContainerDoor", (x, 1.35, -3.56), (1.15, 2.45, .12), material, parent=c)
            box("DoorBrace", (x, 1.35, -3.72), (.08, 2.1, .08), "steel", parent=c, rotation=(0, 0, -24 if x < 0 else 24))
        box("ContainerTopRail", (0, 2.78, 0), (2.72, .12, 7.15), "steel", parent=c)
    container("Container_Rust_A", (-16, 0, 1), 90, "red")
    container("Container_Rust_B", (-20, 0, -14), 0, "corrugated")
    container("Container_Blue_A", (16, 0, -1), 90, "blue")
    container("Container_Blue_B", (21, 0, 14), 0, "corrugated")
    container("Container_Stacked", (-19.4, 2.78, -14), 0, "blue")

    # Jersey barriers, pallet stacks, rock cover, pipe trench and a detailed utility truck.
    for i, (x, z, yaw) in enumerate([(-11, 9, 0), (11, -9, 0), (-9, -11, 90), (9, 11, 90), (-27, 12, 90), (27, -13, 90)]):
        box(f"JerseyBarrier_{i}", (x, .75, z), (3.6, 1.5, .72), "concrete", parent=containers, rotation=(0, yaw, 0))
        box(f"JerseyBarrierStripe_{i}", (x, 1.1, z + (.38 if yaw == 0 else 0)), (3.05 if yaw == 0 else .12, .16, .10 if yaw == 0 else 3.05), "orange", parent=containers, rotation=(0, yaw, 0))
    for i, (x, z) in enumerate([(-7, 17), (9, 17), (-13, -20), (14, -19), (29, 6), (-29, -7)]):
        p = containers.add(group(f"PalletStack_{i}", (x, 0, z), (0, i * 17, 0)))
        for level in range(2 + (i % 2)):
            box("PalletBase", (0, .16 + level * 1.12, 0), (2.2, .15, 1.8), "wood", parent=p)
            box("PalletCrate", (0, .68 + level * 1.12, 0), (1.85, .9, 1.45), "wood", parent=p)
            for sign in (-1, 1): box("CrateBrace", (sign * .88, .68 + level * 1.12, 0), (.08, .78, .08), "steel", parent=p, rotation=(0, 0, sign * 35))
    for i, (x, z, sx, sy, sz) in enumerate([(-28, 21, 1.8, 1.2, 1.3), (-25, 23, 1.2, .8, 1.7), (26, -23, 2, 1.1, 1.2), (29, -21, 1.3, .7, 1.9), (-5, 30, 2.2, 1, 1.1), (7, -30, 2.1, 1.2, 1.2)]):
        sphere(f"Sandstone_Cover_{i}", (x, sy * .55, z), (sx, sy, sz), "plaster", parent=containers, rotation=(0, i * 31, 0))
    pipe = containers.add(group("West_Pipe_Trench", (-12, 0, -7), (0, 90, 0)))
    for z in (-5, -2.5, 0, 2.5, 5):
        cylinder("PipeSegment", (0, .72, z), (.72, 1.35, .72), "steel", parent=pipe, rotation=(90, 0, 0))
        cylinder("PipeJoint", (0, .72, z + 1.2), (.83, .20, .83), "orange", parent=pipe, rotation=(90, 0, 0))
    truck = containers.add(group("Derelict_Utility_Truck", (13, 0, 23), (0, -90, 0)))
    box("TruckChassis", (0, .65, 0), (3.0, .5, 7.2), "steel", parent=truck)
    box("TruckCab", (0, 1.75, -2.0), (2.8, 2.0, 2.4), "blue", parent=truck)
    box("TruckBed", (0, 1.55, 1.7), (2.9, 1.6, 3.5), "corrugated", parent=truck)
    box("TruckWindshield", (0, 2.1, -3.22), (2.25, .85, .08), "concrete_dark", parent=truck, rotation=(18, 0, 0))
    for x in (-1.6, 1.6):
        for z in (-2.2, 2.15): cylinder("TruckTire", (x, .62, z), (.62, .35, .62), "concrete_dark", parent=truck, rotation=(0, 0, 90))

    # Set dressing: radio gantry, barrel clusters, tire piles, lighting details and dynamic clutter.
    props = setdress.add(group("Industrial_Props"))
    gantry = props.add(group("Radio_Gantry", (-30, 0, 28)))
    for x in (-2.5, 2.5): cylinder("GantryLeg", (x, 4.2, 0), (.16, 4.2, .16), "steel", parent=gantry)
    cylinder("GantryMast", (0, 8.0, 0), (.12, 4.2, .12), "steel", parent=gantry)
    box("GantryCrossbeam", (0, 7.0, 0), (5.8, .18, .18), "steel", parent=gantry)
    for h in (6.2, 7.6, 9):
        for x in (-1, 1): box("GantryBrace", (x * 1.4, h, 0), (3.2, .12, .12), "orange", parent=gantry, rotation=(0, 0, x * 38))
    def barrel_stack(name, pos, fire=False):
        b = props.add(group(name, pos))
        for dx, dz in [(-.38, 0), (.38, 0), (0, .55)]:
            cylinder("BarrelBody", (dx, .55, dz), (.35, .55, .35), "red", parent=b)
            cylinder("BarrelTopRim", (dx, 1.08, dz), (.39, .08, .39), "steel", parent=b)
            cylinder("BarrelBottomRim", (dx, .08, dz), (.39, .08, .39), "steel", parent=b)
        if fire: marker("FX_FireBarrel_" + name, (0, 1.16, .18), parent=b)
    barrel_stack("BurnBarrels_North", (-10, 0, 24), True)
    barrel_stack("BurnBarrels_South", (19, 0, -22), True)
    barrel_stack("SupplyBarrels_West", (-28, 0, 1))
    for i, (x, z, count) in enumerate([(-23, 18, 3), (24, 20, 4), (-22, -22, 3), (25, -3, 5)]):
        pile = props.add(group(f"TirePile_{i}", (x, 0, z), (0, i * 22, 0)))
        for n in range(count): cylinder("RubberTire", ((n % 2) * .72, .48 + (n // 2) * .84, (n % 3) * .22), (.52, .2, .52), "concrete_dark", parent=pile, rotation=(0, 0, 90))
    for i, (x, z) in enumerate([(-2, 28), (31, 2), (3, -29), (-32, -3)]):
        # Metal floodlights: non-shadowing visual fixtures, sun remains primary gameplay light.
        pole = props.add(group(f"Floodlight_{i}", (x, 0, z)))
        cylinder("FloodlightPole", (0, 3.8, 0), (.12, 3.8, .12), "steel", parent=pole)
        box("FloodlightHead", (0, 7.25, 0), (1.0, .55, .55), "orange", parent=pole)
        box("FloodlightVisor", (0, 7.45, -.32), (1.1, .12, .12), "steel", parent=pole)
    # A few real loose props have Rigidbody + collider physics. They are deliberately off the critical routes.
    for i, (x, z) in enumerate([(-16, -5), (16, 6), (-5, -17), (8, 19)]):
        cylinder(f"PhysicsLooseBarrel_{i}", (x, 1.0, z), (.42, .78, .42), "red", parent=props, rotation=(0, 0, 90 if i % 2 else 0), physics=True, mass=18)
    for i, (x, z) in enumerate([(-18, 8), (18, -8), (-7, 12), (7, -12)]):
        box(f"PhysicsSupplyCrate_{i}", (x, .75, z), (1.35, 1.35, 1.35), "wood", parent=props, rotation=(0, i * 19, 0), physics=True, mass=24)

    marker("FX_DustVolume", (0, .3, 0), parent=effects)
    return root


def replace_transform_position(data: str, transform_id: int, pos: tuple[float, float, float]) -> str:
    pattern = rf"(--- !u!4 &{transform_id}\nTransform:.*?\n  m_LocalPosition: )\{{x: [^}}]+\}}"
    updated, count = re.subn(pattern, rf"\g<1>{v3(pos)}", data, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"could not update spawn transform {transform_id}")
    return updated


def main() -> None:
    data = SCENE.read_text()
    # Idempotence: remove a prior generated section then edit from the clean native scene.
    data = re.sub(r"\n?# SUNSCORCH_DEPOT_BEGIN.*?# SUNSCORCH_DEPOT_END\n?", "\n", data, flags=re.S)
    # Deactivate the former prototype arena root. We retain it in the scene for comparison/rollback.
    data, changed = re.subn(r"(m_Name: Props\n(?:.*\n){0,8}?  m_IsActive: )[01]", r"\g<1>0", data, count=1)
    if changed != 1:
        raise RuntimeError("could not deactivate the old Props root")
    # Existing gameplay systems retain their references; update the five existing spawn transforms.
    for tid, pos in zip([352044292, 2075195951, 1501580236, 1088383870, 1748963868],
                        [(-28, .75, -27), (28, .75, 27), (-28, .75, 24), (28, .75, -24), (16, .75, -31)]):
        data = replace_transform_position(data, tid, pos)
    # More legible desert daylight/fog, overridden with panoramic sky atmosphere at runtime.
    data = re.sub(r"  m_Fog: \d+", "  m_Fog: 1", data, count=1)
    data = re.sub(r"  m_FogColor: \{r: [^}]+\}", "  m_FogColor: {r: 0.61, g: 0.45, b: 0.30, a: 1}", data, count=1)
    data = re.sub(r"  m_FogMode: \d+", "  m_FogMode: 1", data, count=1)
    data = re.sub(r"  m_FogDensity: [^\n]+", "  m_FogDensity: 0.006", data, count=1)

    root = compose_map()
    generated = BEGIN + "\n" + emit_tree(root) + "\n" + END + "\n"
    roots_marker = "--- !u!1660057539 &9223372036854775807\nSceneRoots:\n"
    index = data.find(roots_marker)
    if index < 0:
        raise RuntimeError("SceneRoots document not found")
    # Root insertion is a normal scene root and appears in the hierarchy beside the existing runtime objects.
    data = data[:index] + generated + data[index:]
    scene_roots_start = data.find(roots_marker)
    roots_end = data.find("\n--- !u!", scene_roots_start + len(roots_marker))
    if roots_end < 0: roots_end = len(data)
    roots_doc = data[scene_roots_start:roots_end]
    roots_doc = roots_doc.replace("  m_Roots:\n", f"  m_Roots:\n  - {{fileID: {root.tr}}}\n", 1)
    data = data[:scene_roots_start] + roots_doc + data[roots_end:]
    SCENE.write_text(data)
    print(f"Wrote {SCENE.relative_to(ROOT)} with Sunscorch Depot: {ids.value - 930000000} serialized objects/components; root transform {root.tr}.")


if __name__ == "__main__":
    main()
