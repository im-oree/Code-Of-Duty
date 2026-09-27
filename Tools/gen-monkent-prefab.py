#!/usr/bin/env python3
"""
Builds Assets/Prefabs/Characters/MonKent.prefab:
root GO with the full Invector+COD component stack (same layout as VBot.prefab)
and the authored MonKent skeleton/meshes lifted from the StartMenu scene
(kit-era weapon slot objects stripped, Animator moved to the prefab root).
"""
import re, os

SCENE = "Assets/Scenes/StartMenu.unity"
DST = "Assets/Prefabs/Characters/MonKent.prefab"
MODEL_GO = None  # resolved: GO named MonKent under OperatorModel

G = {
    "cod_controller": "1f9b384e23764a35940de5d4758540af",
    "cod_input":      "c783d862d55a4338beee21a5ae4621d6",
    "shooter_mgr":    "4c53b06e64363f74ca9377ee0b41842b",
    "ammo_mgr":       "d48792010207c394f9890a9c148beb3a",
    "headtrack":      "61a5d2516d5dbbc40b1cfb5cbf17758a",
    "melee_mgr":      "c11b181c688f7664aba8ba5b38942ac9",
    "fp_body":        "9cfd22f5a2e74b46937f8bc13006901d",
    "cod_player":     "33fb0f150f264c2d8f1c8a8d8a736806",
    "cod_health":     "299b9e2f871a4268aade2d2a5691623f",
    "cod_loadout":    "4bfcd34672e04386bc2a3edcecba764f",
    "fn_nob":         "26b716c41e9b56b4baafaf13a523ba2e",
    "fn_nt":          "a2836e36774ca1c4bbbee976e17b649c",
    "fn_na":          "e8cac635f24954048aad3a6ff9110beb",
}
MONKENT_FBX = "0ab042ede6df83a4cae288f199b749f2"
ANIM_CONTROLLER = "87885946b43e2d1449e1d5aa2042f8a8"
AMMO_LIST = "63dd85acb3dbe1746ab7200f610a2d5f"

s = open(SCENE).read()
docs = re.split(r"^--- ", s, flags=re.M)[1:]
byid = {}
for d in docs:
    m = re.match(r"!u!(\d+) &(\d+)", d)
    byid[m.group(2)] = [m.group(1), d]

def comps(go):
    return re.findall(r"component: {fileID: (\d+)}", byid[go][1])

def tr_of(go):
    for c in comps(go):
        if byid[c][0] in ("4", "224"):
            return c

def name_of(go):
    return re.search(r"m_Name: (.*)", byid[go][1]).group(1)

# locate MonKent GO (child of OperatorModel)
model_go = None
for fid, (t, d) in byid.items():
    if t == "1" and re.search(r"m_Name: MonKent\s*$", d, flags=re.M):
        model_go = fid
        break
assert model_go, "MonKent GO not found in StartMenu"

# collect subtree, skipping kit weapon slot objects
keep_gos = []
def walk(go):
    n = name_of(go)
    if "Slot" in n or n in ("Glok_Pistol", "N4_Rifle", "Saga_Rifle", "P6_SMG"):
        return
    keep_gos.append(go)
    m = re.search(r"m_Children:\n((?:  - {fileID: \d+}\n)*)", byid[tr_of(go)][1])
    for ch in (re.findall(r"{fileID: (\d+)}", m.group(1)) if m else []):
        if ch in byid:
            cgo = re.search(r"m_GameObject: {fileID: (\d+)}", byid[ch][1]).group(1)
            walk(cgo)
walk(model_go)
keep = set(keep_gos)

out_docs = []

ROOT_GO = "9100000000000000000"
ROOT_TR = "9100000000000000001"
ROOT_ANIM = "9100000000000000002"
ROOT_RB = "9100000000000000003"
ROOT_CAP = "9100000000000000004"
mb_ids = {k: str(9100000000000000010 + i) for i, k in enumerate(
    ["cod_controller", "cod_input", "shooter_mgr", "ammo_mgr", "headtrack",
     "melee_mgr", "fp_body", "cod_player", "cod_health", "cod_loadout",
     "fn_nob", "fn_nt", "fn_na"])}

comp_list = [ROOT_TR, ROOT_ANIM] + [mb_ids[k] for k in
    ["cod_controller", "cod_input", "shooter_mgr", "ammo_mgr", "headtrack"]] + \
    [ROOT_RB, ROOT_CAP] + [mb_ids[k] for k in
    ["melee_mgr", "fp_body", "cod_player", "cod_health", "cod_loadout",
     "fn_nob", "fn_nt", "fn_na"]]

out_docs.append(f"""!u!1 &{ROOT_GO}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
{"".join(f"  - component: {{fileID: {c}}}" + chr(10) for c in comp_list)}  m_Layer: 8
  m_Name: MonKent
  m_TagString: Player
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
""")

model_tr = tr_of(model_go)
out_docs.append(f"""!u!4 &{ROOT_TR}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children:
  - {{fileID: {model_tr}}}
  m_Father: {{fileID: 0}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
""")

out_docs.append(f"""!u!95 &{ROOT_ANIM}
Animator:
  serializedVersion: 5
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  m_Enabled: 1
  m_Avatar: {{fileID: 9000000, guid: {MONKENT_FBX}, type: 3}}
  m_Controller: {{fileID: 9100000, guid: {ANIM_CONTROLLER}, type: 2}}
  m_CullingMode: 1
  m_UpdateMode: 0
  m_ApplyRootMotion: 1
  m_LinearVelocityBlending: 0
  m_StabilizeFeet: 0
  m_WarningMessage: 
  m_HasTransformHierarchy: 1
  m_AllowConstantClipSamplingOptimization: 1
  m_KeepAnimatorStateOnDisable: 0
  m_WriteDefaultValuesOnDisable: 0
""")

out_docs.append(f"""!u!54 &{ROOT_RB}
Rigidbody:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  serializedVersion: 4
  m_Mass: 50
  m_Drag: 0
  m_AngularDrag: 0.05
  m_CenterOfMass: {{x: 0, y: 0, z: 0}}
  m_InertiaTensor: {{x: 1, y: 1, z: 1}}
  m_InertiaRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ImplicitCom: 1
  m_ImplicitTensor: 1
  m_UseGravity: 1
  m_IsKinematic: 0
  m_Interpolate: 0
  m_Constraints: 112
  m_CollisionDetection: 3
""")

out_docs.append(f"""!u!136 &{ROOT_CAP}
CapsuleCollider:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  m_Material: {{fileID: 0}}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_IsTrigger: 0
  m_ProvidesContacts: 0
  m_Enabled: 1
  m_Radius: 0.3
  m_Height: 1.75
  m_Direction: 1
  m_Center: {{x: 0, y: 0.875, z: 0}}
""")

for key, fid in mb_ids.items():
    enabled = 0 if key == "cod_input" else 1
    extra = ""
    if key == "ammo_mgr":
        extra = f"  ammoListData: {{fileID: 11400000, guid: {AMMO_LIST}, type: 2}}\n"
    out_docs.append(f"""!u!114 &{fid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  m_Enabled: {enabled}
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {G[key]}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
{extra}""")

# ---- copy the model subtree docs -------------------------------------------
kept_ids = set()
for go in keep:
    kept_ids.add(go)
    for c in comps(go):
        kept_ids.add(c)

# strip the model's own Animator (root owns it now)
model_anim = None
for c in comps(model_go):
    if byid[c][0] == "95":
        model_anim = c
        kept_ids.discard(c)

for fid in kept_ids:
    t, d = byid[fid]
    body = d
    if fid == model_go:
        body = body.replace(f"  - component: {{fileID: {model_anim}}}\n", "")
    if fid == model_tr:
        # reparent under the new root, zero pose
        body = re.sub(r"m_Father: {fileID: \d+}", f"m_Father: {{fileID: {ROOT_TR}}}", body)
        body = re.sub(r"m_LocalPosition: {[^}]*}", "m_LocalPosition: {x: 0, y: 0, z: 0}", body)
        body = re.sub(r"m_LocalRotation: {[^}]*}", "m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}", body)
    # drop references to removed (Slot) children
    if byid[fid][0] in ("4", "224"):
        for m in re.finditer(r"  - {fileID: (\d+)}\n", body):
            cid = m.group(1)
            if cid in byid and cid not in kept_ids and byid[cid][0] in ("4", "224"):
                body = body.replace(f"  - {{fileID: {cid}}}\n", "")
    out_docs.append(body if body.endswith("\n") else body + "\n")

os.makedirs(os.path.dirname(DST), exist_ok=True)
with open(DST, "w") as f:
    f.write("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n")
    for d in out_docs:
        f.write("--- " + d)
print(f"wrote {DST}: {len(out_docs)} docs (model subtree {len(keep)} GOs)")
