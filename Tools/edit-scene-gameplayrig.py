#!/usr/bin/env python3
"""
Scene surgery: removes kit HUD/camera root objects from a gameplay scene and
instantiates the GameplayRig prefab (camera + UI) in their place.
Usage: edit-scene-gameplayrig.py <scene> [rootNameToRemove ...]
"""
import re, sys

scene = sys.argv[1]
remove_names = sys.argv[2:]

RIG_GUID = "3c1f2a9b8d7e4f60a1b2c3d4e5f60718"
RIG_ROOT_TR = "7000000000000000001"   # GameplayRig root transform fileID inside the prefab

s = open(scene).read()
header = s[:s.index("--- ")]
docs = re.split(r"^--- ", s, flags=re.M)[1:]
byid, order = {}, []
for d in docs:
    m = re.match(r"!u!(\d+) &(\d+)( stripped)?", d)
    byid[m.group(2)] = [m.group(1), d]
    order.append(m.group(2))

def comps(go):
    return re.findall(r"component: {fileID: (\d+)}", byid[go][1])

def tr_of(go):
    for c in comps(go):
        if c in byid and byid[c][0] in ("4", "224"):
            return c

kill = set()
def collect(go):
    kill.add(go)
    for c in comps(go):
        kill.add(c)
    tr = tr_of(go)
    m = re.search(r"m_Children:\n((?:  - {fileID: \d+}\n)*)", byid[tr][1]) if tr else None
    for ch in (re.findall(r"{fileID: (\d+)}", m.group(1)) if m else []):
        if ch in byid:
            cgo = re.search(r"m_GameObject: {fileID: (\d+)}", byid[ch][1])
            if cgo:
                collect(cgo.group(1))

removed_root_trs = set()
for fid in order:
    t, d = byid[fid]
    if t == "1":
        name = re.search(r"m_Name: (.*)", d)
        if name and name.group(1) in remove_names:
            tr = tr_of(fid)
            if tr and "m_Father: {fileID: 0}" in byid[tr][1]:
                removed_root_trs.add(tr)
                collect(fid)

# GameplayRig prefab instance
PI = "9200000000000000001"
PI_TR = "9200000000000000002"
rig_pi = f"""!u!1001 &{PI}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: {RIG_ROOT_TR}, guid: {RIG_GUID}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {RIG_ROOT_TR}, guid: {RIG_GUID}, type: 3}}
      propertyPath: m_LocalPosition.y
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {RIG_ROOT_TR}, guid: {RIG_GUID}, type: 3}}
      propertyPath: m_LocalPosition.z
      value: 0
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {RIG_GUID}, type: 3}}
"""
rig_tr = f"""!u!4 &{PI_TR} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {RIG_ROOT_TR}, guid: {RIG_GUID}, type: 3}}
  m_PrefabInstance: {{fileID: {PI}}}
  m_PrefabAsset: {{fileID: 0}}
"""

out = []
for fid in order:
    if fid in kill:
        continue
    t, d = byid[fid]
    if t == "1660057539":  # SceneRoots
        for rt in removed_root_trs:
            d = d.replace(f"  - {{fileID: {rt}}}\n", "")
        d = re.sub(r"(  m_Roots:\n(?:  - {fileID: \d+}\n)*)",
                   lambda m: m.group(1) + f"  - {{fileID: {PI_TR}}}\n", d, count=1)
    out.append(d)

with open(scene, "w") as f:
    f.write(header)
    for d in out:
        f.write("--- " + d if d.endswith("\n") else "--- " + d + "\n")
    f.write("--- " + rig_pi)
    f.write("--- " + rig_tr)

print(f"{scene}: removed {len(kill)} docs for roots {remove_names}, added GameplayRig instance")
