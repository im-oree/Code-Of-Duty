#!/usr/bin/env python3
"""
OfflineTest.unity: replaces the kit Player prefab instance and the bare
SceneCamera with a VBot character instance + the GameplayRig (Invector camera
+ UI). Result: pressing Play in this scene runs the full Invector character
offline (CODInvectorPlayer's offline fallback enables local control).
"""
import re

scene = "Assets/Scenes/OfflineTest.unity"
VBOT_GUID = "9259d6ae1f0a4f5882d0ef3efe588c9d"
VBOT_ROOT_TR = None  # resolved from prefab (root transform of GO 114070)
RIG_GUID = "3c1f2a9b8d7e4f60a1b2c3d4e5f60718"
RIG_ROOT_TR = "7000000000000000001"

# resolve VBot root transform id
vb = open("Assets/Prefabs/Characters/VBot.prefab").read()
m = re.search(r"--- !u!4 &(\d+)\nTransform:[^&]*?m_GameObject: {fileID: 114070}", vb)
VBOT_ROOT_TR = m.group(1)

s = open(scene).read()
header = s[:s.index("--- ")]
docs = re.split(r"^--- ", s, flags=re.M)[1:]
byid, order = {}, []
for d in docs:
    mm = re.match(r"!u!(\d+) &(\d+)( stripped)?", d)
    byid[mm.group(2)] = [mm.group(1), d, bool(mm.group(3))]
    order.append(mm.group(2))

kill = set()
# kit player prefab instance + its stripped docs
for fid, (t, d, st) in byid.items():
    if t == "1001" and "1711848c311a7e84a8d4e466405d6c0c" in d:
        kill.add(fid)
for fid, (t, d, st) in byid.items():
    if st:
        pi = re.search(r"m_PrefabInstance: {fileID: (\d+)}", d)
        if pi and pi.group(1) in kill:
            kill.add(fid)
# SceneCamera GO + components
def comps(go): return re.findall(r"component: {fileID: (\d+)}", byid[go][1])
cam_tr = None
for fid, (t, d, st) in byid.items():
    if t == "1" and "m_Name: SceneCamera" in d:
        kill.add(fid)
        for c in comps(fid):
            kill.add(c)
            if c in byid and byid[c][0] == "4":
                cam_tr = c

def pinst(pid, ptr, guid, roottr, x, y, z, name):
    pi = f"""!u!1001 &{pid}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: {x}
      objectReference: {{fileID: 0}}
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.y
      value: {y}
      objectReference: {{fileID: 0}}
    - target: {{fileID: {roottr}, guid: {guid}, type: 3}}
      propertyPath: m_LocalPosition.z
      value: {z}
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {guid}, type: 3}}
"""
    tr = f"""!u!4 &{ptr} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {roottr}, guid: {guid}, type: 3}}
  m_PrefabInstance: {{fileID: {pid}}}
  m_PrefabAsset: {{fileID: 0}}
"""
    return pi, tr

vb_pi, vb_tr = pinst("9300000000000000001", "9300000000000000002",
                     VBOT_GUID, VBOT_ROOT_TR, 0, 0.1, 0, "VBot")
rig_pi, rig_tr = pinst("9300000000000000003", "9300000000000000004",
                       RIG_GUID, RIG_ROOT_TR, 0, 0, 0, "GameplayRig")

out = []
for fid in order:
    if fid in kill:
        continue
    t, d, st = byid[fid]
    if t == "1660057539":
        if cam_tr:
            d = d.replace(f"  - {{fileID: {cam_tr}}}\n", "")
        # killed prefab instance root entry: stripped transforms of that PI
        for k in kill:
            d = d.replace(f"  - {{fileID: {k}}}\n", "")
        d = re.sub(r"(  m_Roots:\n(?:  - {fileID: -?\d+}\n)*)",
                   lambda m: m.group(1) +
                   "  - {fileID: 9300000000000000002}\n  - {fileID: 9300000000000000004}\n",
                   d, count=1)
    out.append(d)

with open(scene, "w") as f:
    f.write(header)
    for d in out:
        f.write("--- " + (d if d.endswith("\n") else d + "\n"))
    for d in (vb_pi, vb_tr, rig_pi, rig_tr):
        f.write("--- " + d)
print(f"OfflineTest: removed {len(kill)} docs, added VBot + GameplayRig (VBot root tr {VBOT_ROOT_TR})")
