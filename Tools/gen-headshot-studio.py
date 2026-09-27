#!/usr/bin/env python3
"""
Generates Assets/Scenes/HeadshotStudio.unity — the authored portrait studio:
per-character stations (character instance + backdrop + key/fill/rim lights)
and one fixed, nicely-angled headshot camera per station named
"Headshot_<characterId>". The character selector's headshot sprites are
rendered from THESE cameras (UnityWeb headless in the sandbox, or the
COD > Headshot Studio editor tool inside Unity), so selector art always
matches the authored studio.
"""
import re, os, uuid

DST = "Assets/Scenes/HeadshotStudio.unity"

VBOT_GUID = "9259d6ae1f0a4f5882d0ef3efe588c9d"
MONKENT_GUID = "412a51087dad4cc79df988cf565a6a04"

vb = open("Assets/Prefabs/Characters/VBot.prefab").read()
VBOT_ROOT_TR = re.search(r"--- !u!4 &(\d+)\nTransform:[^&]*?m_GameObject: {fileID: 114070}", vb).group(1)
MONKENT_ROOT_TR = "9100000000000000001"

STATIONS = [  # (id, prefab guid | None, root tr fileID, x)
    ("VBot", VBOT_GUID, VBOT_ROOT_TR, 0),
    ("MonKent", MONKENT_GUID, MONKENT_ROOT_TR, 6),
    ("MaleBase", None, None, 12),  # unrigged: injected at shot time / editor tool
]
HEAD_Y = 1.62          # camera aim height
CAM_DIST = 1.05        # distance in front of the face
CAM_Y = 1.66           # slightly above eye line, looking gently down

ids = iter(range(100000, 200000))
def nid(): return str(next(ids))

docs = []
roots = []

def doc(body): docs.append(body)

def go(fid, name, comps, layer=0, tag="Untagged"):
    doc(f"""!u!1 &{fid}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
{"".join(f"  - component: {{fileID: {c}}}" + chr(10) for c in comps)}  m_Layer: {layer}
  m_Name: {name}
  m_TagString: {tag}
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
""")

def tr(fid, gofid, pos, euler=(0, 0, 0), scale=(1, 1, 1), father="0", children=()):
    import math
    def q(e):
        cx, cy, cz = [math.cos(math.radians(v) / 2) for v in e]
        sx, sy, sz = [math.sin(math.radians(v) / 2) for v in e]
        return (sx * cy * cz + cx * sy * sz * -1 + 0, 0, 0, 0)  # placeholder
    # proper euler(ZXY, unity) -> quaternion
    import math
    ex, ey, ez = [math.radians(v) for v in euler]
    cx, sx = math.cos(ex / 2), math.sin(ex / 2)
    cy, sy = math.cos(ey / 2), math.sin(ey / 2)
    cz, sz = math.cos(ez / 2), math.sin(ez / 2)
    qx = sx * cy * cz + cx * sy * sz
    qy = cx * sy * cz - sx * cy * sz
    qz = cx * cy * sz - sx * sy * cz
    qw = cx * cy * cz + sx * sy * sz
    doc(f"""!u!4 &{fid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_LocalRotation: {{x: {qx:.6f}, y: {qy:.6f}, z: {qz:.6f}, w: {qw:.6f}}}
  m_LocalPosition: {{x: {pos[0]}, y: {pos[1]}, z: {pos[2]}}}
  m_LocalScale: {{x: {scale[0]}, y: {scale[1]}, z: {scale[2]}}}
  m_Children:
{"".join(f"  - {{fileID: {c}}}" + chr(10) for c in children)}  m_Father: {{fileID: {father}}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: {euler[0]}, y: {euler[1]}, z: {euler[2]}}}
""")

def light(fid, gofid, ltype, intensity, color=(1, 1, 1), range_=10, spot=60):
    doc(f"""!u!108 &{fid}
Light:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_Enabled: 1
  serializedVersion: 11
  m_Type: {ltype}
  m_Color: {{r: {color[0]}, g: {color[1]}, b: {color[2]}, a: 1}}
  m_Intensity: {intensity}
  m_Range: {range_}
  m_SpotAngle: {spot}
  m_InnerSpotAngle: {spot * 0.7:.2f}
  m_CookieSize: 10
  m_Shadows:
    m_Type: 0
    m_Resolution: -1
    m_CustomResolution: -1
    m_Strength: 1
    m_Bias: 0.05
    m_NormalBias: 0.4
    m_NearPlane: 0.2
    m_CullingMatrixOverride:
      e00: 1
      e01: 0
      e02: 0
      e03: 0
      e10: 0
      e11: 1
      e12: 0
      e13: 0
      e20: 0
      e21: 0
      e22: 1
      e23: 0
      e30: 0
      e31: 0
      e32: 0
      e33: 1
    m_UseCullingMatrixOverride: 0
  m_Cookie: {{fileID: 0}}
  m_DrawHalo: 0
  m_Flare: {{fileID: 0}}
  m_RenderMode: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingLayerMask: 1
  m_Lightmapping: 4
  m_LightShadowCasterMode: 0
  m_AreaSize: {{x: 1, y: 1}}
  m_BounceIntensity: 1
  m_ColorTemperature: 6570
  m_UseColorTemperature: 0
  m_BoundingSphereOverride: {{x: 0, y: 0, z: 0, w: 0}}
  m_UseBoundingSphereOverride: 0
  m_UseViewFrustumForShadowCasterCull: 1
  m_ShadowRadius: 0
  m_ShadowAngle: 0
""")

def camera(fid, gofid, fov=32):
    doc(f"""!u!20 &{fid}
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 2
  m_BackGroundColor: {{r: 0.09, g: 0.1, b: 0.12, a: 1}}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_Iso: 200
  m_ShutterSpeed: 0.005
  m_Aperture: 16
  m_FocusDistance: 10
  m_FocalLength: 50
  m_BladeCount: 5
  m_Curvature: {{x: 2, y: 11}}
  m_BarrelClipping: 0.25
  m_Anamorphism: 0
  m_SensorSize: {{x: 36, y: 24}}
  m_LensShift: {{x: 0, y: 0}}
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  near clip plane: 0.05
  far clip plane: 50
  field of view: {fov}
  orthographic: 0
  orthographic size: 5
  m_Depth: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingPath: -1
  m_TargetTexture: {{fileID: 0}}
  m_TargetDisplay: 0
  m_TargetEye: 3
  m_HDR: 1
  m_AllowMSAA: 1
  m_AllowDynamicResolution: 0
  m_ForceIntoRT: 0
  m_OcclusionCulling: 1
  m_StereoConvergence: 10
  m_StereoSeparation: 0.022
""")

def mesh_filter(fid, gofid, mesh_fileid):
    doc(f"""!u!33 &{fid}
MeshFilter:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_Mesh: {{fileID: {mesh_fileid}, guid: 0000000000000000e000000000000000, type: 0}}
""")

def mesh_renderer(fid, gofid, mat_guid="31321ba15b8f8eb4c954353edc038b1d"):
    doc(f"""!u!23 &{fid}
MeshRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RayTraceProcedural: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {mat_guid}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 0
""")

# ---- fixed scene settings docs ----------------------------------------------
doc("""!u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
""")
doc("""!u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 10
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.28, g: 0.29, b: 0.32, a: 1}
  m_AmbientEquatorColor: {r: 0.2, g: 0.2, b: 0.22, a: 1}
  m_AmbientGroundColor: {r: 0.11, g: 0.11, b: 0.12, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 3
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_UseRadianceAmbientProbe: 0
""")
doc("""!u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 13
  m_BakeOnSceneLoad: 0
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 0
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 1
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 5
    m_PVRFilteringGaussRadiusAO: 2
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 0}
  m_LightingSettings: {fileID: 0}
""")
doc("""!u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
""")

# ---- stations ---------------------------------------------------------------
pi_docs = []
for cid, guid, roottr, x in STATIONS:
    st_go, st_tr = nid(), nid()
    kids = []

    # floor + backdrop (builtin quad 10210, plane 10209, default material)
    fl_go, fl_tr, fl_mf, fl_mr = nid(), nid(), nid(), nid()
    go(fl_go, f"Floor_{cid}", [fl_tr, fl_mf, fl_mr])
    tr(fl_tr, fl_go, (0, 0, 0), scale=(0.6, 1, 0.6), father=st_tr)
    mesh_filter(fl_mf, fl_go, 10209)
    mesh_renderer(fl_mr, fl_go)
    kids.append(fl_tr)

    bd_go, bd_tr, bd_mf, bd_mr = nid(), nid(), nid(), nid()
    go(bd_go, f"Backdrop_{cid}", [bd_tr, bd_mf, bd_mr])
    tr(bd_tr, bd_go, (0, 2, 1.4), euler=(0, 180, 0), scale=(6, 4, 1), father=st_tr)
    mesh_filter(bd_mf, bd_go, 10210)
    mesh_renderer(bd_mr, bd_go)
    kids.append(bd_tr)

    # key / fill / rim lights (spot=0? type: 0=Spot,1=Directional,2=Point)
    for lname, lpos, leuler, inten, col, ltype in [
        ("Key",  (0.7, 2.1, -1.4), (25, 155, 0), 3.2, (1.0, 0.96, 0.9), 0),
        ("Fill", (-1.0, 1.5, -1.2), (10, 40, 0), 1.4, (0.75, 0.8, 1.0), 0),
        ("Rim",  (0, 2.2, 1.1), (35, -15, 0), 2.4, (0.9, 0.95, 1.0), 0),
    ]:
        lg, lt, ll = nid(), nid(), nid()
        go(lg, f"{lname}_{cid}", [lt, ll])
        tr(lt, lg, lpos, euler=leuler, father=st_tr)
        light(ll, lg, ltype, inten, col, range_=8, spot=70)
        kids.append(lt)

    # headshot camera: in front of the face, aimed at the head
    cg, ct, cc = nid(), nid(), nid()
    go(cg, f"Headshot_{cid}", [ct, cc])
    tr(ct, cg, (0.12, CAM_Y, CAM_DIST), euler=(2.5, 180 + 7, 0), father=st_tr)
    camera(cc, cg)
    kids.append(ct)

    # character prefab instance
    if guid is not None:
        pid, ptr = nid(), nid()
        pi_docs.append(f"""!u!1001 &{pid}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: {st_tr}}}
    m_Modifications:
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.y
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.z
      value: 0
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}
""")
        pi_docs.append(f"""!u!4 &{ptr} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {roottr}, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {pid}}}
  m_PrefabAsset: {{fileID: 0}}
""")
        kids.append(ptr)

    go(st_go, f"Station_{cid}", [st_tr])
    tr(st_tr, st_go, (x, 0, 0), father="0", children=kids)
    roots.append(st_tr)

doc(f"""!u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
{"".join(f"  - {{fileID: {r}}}" + chr(10) for r in roots)}""")

with open(DST, "w") as f:
    f.write("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n")
    for d in docs:
        f.write("--- " + d)
    for d in pi_docs:
        f.write("--- " + d)

meta = DST + ".meta"
if not os.path.exists(meta):
    open(meta, "w").write(f"""fileFormatVersion: 2
guid: {uuid.uuid4().hex}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""")
print(f"wrote {DST} ({len(docs) + len(pi_docs)} docs)")
