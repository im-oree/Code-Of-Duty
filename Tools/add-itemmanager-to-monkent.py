#!/usr/bin/env python3
"""
Gives MonKent the same NATIVE Invector inventory stack as VBot:
 - equip handler transforms under both hands (defaultHandler + meleeHandler,
   Invector's native equip points)
 - vItemManager wired to the ShooterMelee item list and those handlers
 - embedded Inventory UI + HUD + grenade ThrowManager prefab instances
 - CODLoadout.itemList reference (also patched on VBot)
Idempotent.
"""
import re

ITEMLIST = "1493f9d9326e8014494b8cb04de38f25"     # vShooterMelee_ItemListData
G_ITEMMANAGER = "b286f937058a6044fa211351c07fbef0"
G_CODLOADOUT = "4bfcd34672e04386bc2a3edcecba764f"
PI_INVENTORY = ("8cff4600ff380fc45910465a0a4d4ab1", None)   # Inventory_ShooterMelee
PI_HUD       = ("65d09cff7cef67d47a9d1f9a022f2d84", None)   # HUD
PI_THROW     = ("a103a69a923bfe449bec391ee2d584ca", None)   # ThrowManager-Inventory_EquipArea

def root_tr_of(prefab_path, guid):
    s = open(prefab_path).read()
    docs = re.split(r"^--- ", s, flags=re.M)[1:]
    byid = {}
    for d in docs:
        m = re.match(r"!u!(\d+) &(-?\d+)( stripped)?", d)
        byid[m.group(2)] = (m.group(1), d)
    for fid, (t, d) in byid.items():
        if t in ("4", "224") and re.search(r"m_Father: {fileID: 0}\s", d):
            return fid
    # prefab VARIANT: root transform is the stripped one of the root PrefabInstance
    for d in docs:
        m = re.match(r"!u!4 &(-?\d+) stripped", d)
        if m:
            return m.group(1)
    raise SystemExit(f"no root transform in {prefab_path}")

import subprocess
def prefab_path_for(guid):
    out = subprocess.run(["grep", "-rl", f"guid: {guid}",
                          "Assets/Invector-3rdPersonController", "--include=*.prefab.meta"],
                         capture_output=True, text=True).stdout.strip().splitlines()
    return out[0].replace(".meta", "")

PIS = []
for guid, _ in (PI_INVENTORY, PI_HUD, PI_THROW):
    PIS.append((guid, root_tr_of(prefab_path_for(guid), guid)))

# ---------------- patch CODLoadout.itemList on both prefabs -----------------
for p in ("Assets/Prefabs/Characters/VBot.prefab", "Assets/Prefabs/Characters/MonKent.prefab"):
    s = open(p).read()
    def add_itemlist(m):
        if "itemList:" in m.group(0):
            return m.group(0)
        return m.group(0) + f"  itemList: {{fileID: 11400000, guid: {ITEMLIST}, type: 2}}\n"
    s2 = re.sub(rf"(m_Script: {{fileID: 11500000, guid: {G_CODLOADOUT}, type: 3}}\n"
                rf"  m_Name: \n  m_EditorClassIdentifier: \n)",
                add_itemlist, s)
    if s2 != s:
        open(p, "w").write(s2)
        print(f"{p}: CODLoadout.itemList wired")

# ---------------- MonKent native stack ---------------------------------------
p = "Assets/Prefabs/Characters/MonKent.prefab"
s = open(p).read()
if G_ITEMMANAGER in s:
    print("MonKent: already has vItemManager")
    raise SystemExit(0)

docs = re.split(r"^--- ", s, flags=re.M)[1:]
byid, order = {}, []
for d in docs:
    m = re.match(r"!u!(\d+) &(-?\d+)( stripped)?", d)
    byid[m.group(2)] = [m.group(1), d]
    order.append(m.group(2))

def tr_of_named(name):
    for fid, (t, d) in byid.items():
        if t == "1" and re.search(rf"m_Name: {name}\s*$", d, flags=re.M):
            for c in re.findall(r"component: {fileID: (-?\d+)}", d):
                if byid[c][0] == "4":
                    return c
    return None

ROOT_GO = "9100000000000000000"
ROOT_TR = "9100000000000000001"
right_hand_tr = tr_of_named("RightHand")
left_hand_tr = tr_of_named("LeftHand")
assert right_hand_tr and left_hand_tr, "hand bones not found"

base = 9600000000000000000
nid = lambda i: str(base + i)

# handler transforms: RightHandlers>(defaultHandler, meleeHandler), LeftHandlers>(defaultHandler)
new = ""
def empty_go(gofid, trfid, name, father, children=(), pos="{x: 0, y: 0, z: 0}"):
    global new
    new += f"""--- !u!1 &{gofid}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {trfid}}}
  m_Layer: 8
  m_Name: {name}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{trfid}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {gofid}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {pos}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children:
{"".join(f"  - {{fileID: {c}}}" + chr(10) for c in children)}  m_Father: {{fileID: {father}}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""

RH_GO, RH_TR = nid(1), nid(2)
RD_GO, RD_TR = nid(3), nid(4)       # right defaultHandler
RM_GO, RM_TR = nid(5), nid(6)       # right meleeHandler
LH_GO, LH_TR = nid(7), nid(8)
LD_GO, LD_TR = nid(9), nid(10)      # left defaultHandler
IM_MB = nid(11)                     # vItemManager component

empty_go(RD_GO, RD_TR, "defaultHandler", RH_TR)
empty_go(RM_GO, RM_TR, "meleeHandler", RH_TR)
empty_go(RH_GO, RH_TR, "RightHandlers", right_hand_tr, children=(RD_TR, RM_TR))
empty_go(LD_GO, LD_TR, "defaultHandler", LH_TR)
empty_go(LH_GO, LH_TR, "LeftHandlers", left_hand_tr, children=(LD_TR,))

new += f"""--- !u!114 &{IM_MB}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {G_ITEMMANAGER}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  inventory: {{fileID: 0}}
  itemListData: {{fileID: 11400000, guid: {ITEMLIST}, type: 2}}
  startItems: []
  equipPoints:
  - equipPointName: LeftArm
    area: {{fileID: 0}}
    handler:
      defaultHandler: {{fileID: {LD_TR}}}
      customHandlers: []
    onInstantiateEquiment:
      m_PersistentCalls:
        m_Calls: []
  - equipPointName: RightArm
    area: {{fileID: 0}}
    handler:
      defaultHandler: {{fileID: {RD_TR}}}
      customHandlers:
      - {{fileID: {RM_TR}}}
    onInstantiateEquiment:
      m_PersistentCalls:
        m_Calls: []
  applyAttributeEvents: []
"""

# nested UI / throw manager prefab instances under the root
pi_base = 12
for guid, src_tr in PIS:
    pid, ptr = nid(pi_base), nid(pi_base + 1)
    pi_base += 2
    new += f"""--- !u!1001 &{pid}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: {ROOT_TR}}}
    m_Modifications:
    - target: {{fileID: {src_tr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}
--- !u!4 &{ptr} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {src_tr}, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {pid}}}
  m_PrefabAsset: {{fileID: 0}}
"""
    s = re.sub(rf"(--- !u!4 &{ROOT_TR}\nTransform:[^&]*?m_Children:\n(?:  - {{fileID: -?\d+}}\n)*)",
               lambda m, ptr=ptr: m.group(1) + f"  - {{fileID: {ptr}}}\n", s, count=1)

# register itemmanager component + handler children
s = re.sub(rf"(--- !u!1 &{ROOT_GO}\nGameObject:[^&]*?m_Component:\n(?:  - component: {{fileID: -?\d+}}\n)*)",
           lambda m: m.group(1) + f"  - component: {{fileID: {IM_MB}}}\n", s, count=1)
s = re.sub(rf"(--- !u!4 &{right_hand_tr}\nTransform:[^&]*?m_Children:\n(?:  - {{fileID: -?\d+}}\n)*)",
           lambda m: m.group(1) + f"  - {{fileID: {RH_TR}}}\n", s, count=1)
s = re.sub(rf"(--- !u!4 &{left_hand_tr}\nTransform:[^&]*?m_Children:\n(?:  - {{fileID: -?\d+}}\n)*)",
           lambda m: m.group(1) + f"  - {{fileID: {LH_TR}}}\n", s, count=1)

open(p, "w").write(s.rstrip("\n") + "\n" + new)
print("MonKent: native item manager + handlers + inventory/HUD/throw UI added")
