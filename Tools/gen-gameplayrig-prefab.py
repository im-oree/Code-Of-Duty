#!/usr/bin/env python3
"""
Generates Assets/Prefabs/GameplayRig.prefab — the separate camera + UI prefab
(requirement: characters carry NO camera/UI; the rig is editable on its own
and authored into every gameplay scene).

GameplayRig
├─ vThirdPersonCamera        (Invector camera, CameraStateList = COD@CameraState
│   └─ Camera                 incl. the native FirstPerson state)
├─ ShooterUI (prefab instance: Invector crosshair/aim canvas/ammo & HUD)
└─ CODHUD (authored canvas: kill feed rows + death overlay, driven by CODGameHUD)
"""
import os, uuid

DST = "Assets/Prefabs/GameplayRig.prefab"

G_VCAM   = "1a1bfe72fbc87d04e885296b53e91c66"   # vThirdPersonCamera
G_STATE  = "b906973ec2ff4d8a980c72d8049147af"   # COD@CameraState.asset
G_SHOOTERUI = "041726e4330290b43ac95e1cf84d3636"
SHOOTERUI_ROOT_TR = "1789445608270938811"
G_HUD    = "50ec0f84d5cc42fa80d8a211c6a7c71f"   # CODGameHUD
G_CANVASSCALER = "0cd44c1031e13a943bb63640046fad76"
G_RAYCASTER    = "dc42784cf147c0c48a680349fa168899"
G_IMAGE  = "fe87c0e1cc204ed48ad3b37840f39efc"
G_TMP    = "f4688fdb7df04437aeb418b961361dc5"
G_FONT   = "8f586378b4e144a9851e7b34d9b748ee"

ids = iter(range(7000000000000000000, 7000000000000001000))
def nid(): return str(next(ids))

docs = []

def go(fid, name, comps, layer=0, tag="Untagged", active=1):
    docs.append(f"""!u!1 &{fid}
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
  m_IsActive: {active}
""")

def tr(fid, gofid, father, children, pos="{x: 0, y: 0, z: 0}", rot="{x: 0, y: 0, z: 0, w: 1}"):
    docs.append(f"""!u!4 &{fid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_LocalRotation: {rot}
  m_LocalPosition: {pos}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children:
{"".join(f"  - {{fileID: {c}}}" + chr(10) for c in children)}  m_Father: {{fileID: {father}}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
""")

def rt(fid, gofid, father, children, anch_min="{x: 0, y: 0}", anch_max="{x: 1, y: 1}",
       pos="{x: 0, y: 0}", size="{x: 0, y: 0}", pivot="{x: 0.5, y: 0.5}"):
    docs.append(f"""!u!224 &{fid}
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children:
{"".join(f"  - {{fileID: {c}}}" + chr(10) for c in children)}  m_Father: {{fileID: {father}}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {anch_min}
  m_AnchorMax: {anch_max}
  m_AnchoredPosition: {pos}
  m_SizeDelta: {size}
  m_Pivot: {pivot}
""")

def mb(fid, gofid, guid, body="", enabled=1):
    docs.append(f"""!u!114 &{fid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_Enabled: {enabled}
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
{body}""")

def canvas_renderer(fid, gofid):
    docs.append(f"""!u!222 &{fid}
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_CullTransparentMesh: 0
""")

def image(fid, gofid, color):
    mb(fid, gofid, G_IMAGE, f"""  m_Material: {{fileID: 0}}
  m_Color: {color}
  m_RaycastTarget: 0
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_Sprite: {{fileID: 0}}
  m_Type: 0
  m_PreserveAspect: 0
  m_FillCenter: 1
  m_FillMethod: 4
  m_FillAmount: 1
  m_FillClockwise: 1
  m_FillOrigin: 0
  m_UseSpriteMesh: 0
  m_PixelsPerUnitMultiplier: 1
""")

def tmp(fid, gofid, text, size, color="{r: 1, g: 1, b: 1, a: 1}", align=257, style=1):
    mb(fid, gofid, G_TMP, f"""  m_Material: {{fileID: 0}}
  m_Color: {{r: 1, g: 1, b: 1, a: 1}}
  m_RaycastTarget: 0
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_text: {text}
  m_isRightToLeft: 0
  m_fontAsset: {{fileID: 11400000, guid: {G_FONT}, type: 2}}
  m_sharedMaterial: {{fileID: 2180264, guid: {G_FONT}, type: 2}}
  m_fontSharedMaterials: []
  m_fontMaterial: {{fileID: 0}}
  m_fontMaterials: []
  m_fontColor32:
    serializedVersion: 2
    rgba: 4294967295
  m_fontColor: {color}
  m_enableVertexGradient: 0
  m_colorMode: 3
  m_fontColorGradient:
    topLeft: {{r: 1, g: 1, b: 1, a: 1}}
    topRight: {{r: 1, g: 1, b: 1, a: 1}}
    bottomLeft: {{r: 1, g: 1, b: 1, a: 1}}
    bottomRight: {{r: 1, g: 1, b: 1, a: 1}}
  m_fontColorGradientPreset: {{fileID: 0}}
  m_spriteAsset: {{fileID: 0}}
  m_tintAllSprites: 0
  m_StyleSheet: {{fileID: 0}}
  m_TextStyleHashCode: -1183493901
  m_overrideHtmlColors: 0
  m_faceColor:
    serializedVersion: 2
    rgba: 4294967295
  m_fontSize: {size}
  m_fontSizeBase: {size}
  m_fontWeight: 400
  m_enableAutoSizing: 0
  m_fontSizeMin: 18
  m_fontSizeMax: 72
  m_fontStyle: {style}
  m_HorizontalAlignment: {align & 0xFF}
  m_VerticalAlignment: {align & 0xFF00}
  m_textAlignment: 65535
  m_characterSpacing: 1
  m_wordSpacing: 0
  m_lineSpacing: 0
  m_lineSpacingMax: 0
  m_paragraphSpacing: 0
  m_charWidthMaxAdj: 0
  m_enableWordWrapping: 0
  m_wordWrappingRatios: 0.4
  m_overflowMode: 0
  m_linkedTextComponent: {{fileID: 0}}
  parentLinkedComponent: {{fileID: 0}}
  m_enableKerning: 1
  m_enableExtraPadding: 0
  checkPaddingRequired: 0
  m_isRichText: 1
  m_parseCtrlCharacters: 1
  m_isOrthographic: 1
  m_isCullingEnabled: 0
  m_horizontalMapping: 0
  m_verticalMapping: 0
  m_uvLineOffset: 0
  m_geometrySortingOrder: 0
  m_IsTextObjectScaleStatic: 0
  m_VertexBufferAutoSizeReduction: 0
  m_useMaxVisibleDescender: 1
  m_pageToDisplay: 1
  m_margin: {{x: 0, y: 0, z: 0, w: 0}}
  m_isUsingLegacyAnimationComponent: 0
  m_isVolumetricText: 0
""")

# ---------------- root ----------------
root_go, root_tr = nid(), nid()
cam_go, cam_tr, cam_mb = nid(), nid(), nid()
uc_go, uc_tr, uc_cam, uc_al = nid(), nid(), nid(), nid()
hud_go, hud_rt, hud_canvas, hud_scaler, hud_ray, hud_mb = nid(), nid(), nid(), nid(), nid(), nid()
feed_go, feed_rt = nid(), nid()
rows = [(nid(), nid(), nid(), nid()) for _ in range(4)]  # go, rt, cr, tmp
death_go, death_rt, death_cr, death_img = nid(), nid(), nid(), nid()
dl_go, dl_rt, dl_cr, dl_tmp = nid(), nid(), nid(), nid()
pi_ui = nid()
pi_ui_tr = nid()

go(root_go, "GameplayRig", [root_tr])
tr(root_tr, root_go, "0", [cam_tr, pi_ui_tr, hud_rt])

# camera rig
go(cam_go, "vThirdPersonCamera", [cam_tr, cam_mb], layer=8)
tr(cam_tr, cam_go, root_tr, [uc_tr], pos="{x: 0, y: 1.7, z: -2.2}")
mb(cam_mb, cam_go, G_VCAM, f"""  mainTarget: {{fileID: 0}}
  _smoothBetweenState: 6
  _smoothCameraRotation: 12
  _smoothSwitchSide: 2
  _scrollSpeed: 10
  _joystickSensitivity: 1
  cullingLayer:
    serializedVersion: 2
    m_Bits: 1
  clipPlaneMargin: 0
  checkHeightRadius: 0.4
  showGizmos: 0
  startUsingTargetRotation: 1
  startSmooth: 0
  startSmoothFactor: 1
  autoBehindTarget: 0
  behindTargetDelay: 2
  behindTargetSmoothRotation: 1
  lockCamera: 0
  offsetMouse: {{x: 0, y: 0}}
  CameraStateList: {{fileID: 11400000, guid: {G_STATE}, type: 2}}
""")
go(uc_go, "Camera", [uc_tr, uc_cam, uc_al], layer=8, tag="MainCamera")
tr(uc_tr, uc_go, cam_tr, [])
docs.append(f"""!u!20 &{uc_cam}
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {uc_go}}}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 1
  m_BackGroundColor: {{r: 0.19215687, g: 0.3019608, b: 0.4745098, a: 0}}
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
  near clip plane: 0.15
  far clip plane: 1000
  field of view: 60
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
docs.append(f"""!u!81 &{uc_al}
AudioListener:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {uc_go}}}
  m_Enabled: 1
""")

# ShooterUI nested prefab instance
docs.append(f"""!u!1001 &{pi_ui}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: {root_tr}}}
    m_Modifications:
    - target: {{fileID: {SHOOTERUI_ROOT_TR}, guid: {G_SHOOTERUI}, type: 3}}
      propertyPath: m_RootOrder
      value: 1
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {G_SHOOTERUI}, type: 3}}
""")
docs.append(f"""!u!4 &{pi_ui_tr} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {SHOOTERUI_ROOT_TR}, guid: {G_SHOOTERUI}, type: 3}}
  m_PrefabInstance: {{fileID: {pi_ui}}}
  m_PrefabAsset: {{fileID: 0}}
""")

# COD HUD canvas
go(hud_go, "CODHUD", [hud_rt, hud_canvas, hud_scaler, hud_ray, hud_mb], layer=5)
rt(hud_rt, hud_go, root_tr, [feed_rt, death_rt], size="{x: 0, y: 0}")
docs.append(f"""!u!223 &{hud_canvas}
Canvas:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {hud_go}}}
  m_Enabled: 1
  serializedVersion: 3
  m_RenderMode: 0
  m_Camera: {{fileID: 0}}
  m_PlaneDistance: 100
  m_PixelPerfect: 0
  m_ReceivesEvents: 1
  m_OverrideSorting: 0
  m_OverridePixelPerfect: 0
  m_SortingBucketNormalizedSize: 0
  m_VertexColorAlwaysGammaSpace: 0
  m_AdditionalShaderChannelsFlag: 25
  m_UpdateRectTransformForStandalone: 0
  m_SortingLayerID: 0
  m_SortingOrder: 60
  m_TargetDisplay: 0
""")
mb(hud_scaler, hud_go, G_CANVASSCALER, """  m_UiScaleMode: 1
  m_ReferencePixelsPerUnit: 100
  m_ScaleFactor: 1
  m_ReferenceResolution: {x: 1920, y: 1080}
  m_ScreenMatchMode: 0
  m_MatchWidthOrHeight: 0.5
  m_PhysicalUnit: 3
  m_FallbackScreenDPI: 96
  m_DefaultSpriteDPI: 96
  m_DynamicPixelsPerUnit: 1
  m_PresetInfoIsWorld: 0
""")
mb(hud_ray, hud_go, G_RAYCASTER, """  m_IgnoreReversedGraphics: 1
  m_BlockingObjects: 0
  m_BlockingMask:
    serializedVersion: 2
    m_Bits: 4294967295
""")

# kill feed (top-right column)
go(feed_go, "KillFeed", [feed_rt], layer=5)
rt(feed_rt, feed_go, hud_rt, [r[1] for r in rows],
   anch_min="{x: 1, y: 1}", anch_max="{x: 1, y: 1}",
   pos="{x: -240, y: -60}", size="{x: 420, y: 140}", pivot="{x: 0.5, y: 1}")
for i, (rgo, rrt, rcr, rtm) in enumerate(rows):
    go(rgo, f"Row{i}", [rrt, rcr, rtm], layer=5, active=0)
    rt(rrt, rgo, feed_rt, [], anch_min="{x: 0, y: 1}", anch_max="{x: 1, y: 1}",
       pos=f"{{x: 0, y: {-16 - i * 30}}}", size="{x: 0, y: 28}", pivot="{x: 0.5, y: 0.5}")
    tmp(rtm, rgo, "KILLER  \u27a4  VICTIM", 20, align=260)  # right-ish

# death overlay
go(death_go, "DeathOverlay", [death_rt, death_cr, death_img], layer=5, active=0)
rt(death_rt, death_go, hud_rt, [dl_rt])
canvas_renderer(death_cr, death_go)
image(death_img, death_go, "{r: 0.25, g: 0, b: 0, a: 0.45}")
go(dl_go, "Label", [dl_rt, dl_cr, dl_tmp], layer=5)
rt(dl_rt, dl_go, death_rt, [], anch_min="{x: 0.5, y: 0.5}", anch_max="{x: 0.5, y: 0.5}",
   size="{x: 900, y: 80}")
canvas_renderer(dl_cr, dl_go)
tmp(dl_tmp, dl_go, "YOU ARE DOWN \u2014 RESPAWNING...", 42, align=2)

# CanvasRenderers for feed rows (already in comps) -> emit
for rgo, rrt, rcr, rtm in rows:
    canvas_renderer(rcr, rgo)

# CODGameHUD binder
mb(hud_mb, hud_go, G_HUD, f"""  ammoPlate: {{fileID: 0}}
  magazineText: {{fileID: 0}}
  reserveText: {{fileID: 0}}
  weaponNameText: {{fileID: 0}}
  healthFill: {{fileID: 0}}
  healthText: {{fileID: 0}}
  killFeedRows:
{"".join(f"  - {{fileID: {r[3]}}}" + chr(10) for r in rows)}  killFeedRowLifetime: 6
  deathOverlay: {{fileID: {death_go}}}
""")

os.makedirs(os.path.dirname(DST), exist_ok=True)
with open(DST, "w") as f:
    f.write("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n")
    for d in docs:
        f.write("--- " + d)

meta_guid = "3c1f2a9b8d7e4f60a1b2c3d4e5f60718"
open(DST + ".meta", "w").write(f"""fileFormatVersion: 2
guid: {meta_guid}
PrefabImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""")
print(f"wrote {DST} ({len(docs)} docs), guid {meta_guid}")
