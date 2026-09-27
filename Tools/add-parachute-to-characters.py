#!/usr/bin/env python3
"""
Adds the Invector Parachute add-on to the COD character prefabs, following the
add-on documentation:
  step 2 — a 'Body Snap Control' child with vBodySnappingControl (humanoid:
           bones auto-resolve at play)
  step 3 — the 'Parachute' prefab nested inside the character
(step 1, the animator graft, is done by graft-parachute-animator.py)
Idempotent per prefab.
"""
import re, sys

G_BODYSNAP = "80594385e4f69be409516ae62219c6f9"   # vBodySnappingControl
PARA_GUID = "bf37ca666e59483468981f3ca3f117c2"    # Parachute.prefab
PARA_ROOT_TR = "3827063471218194386"

TARGETS = [
    # (prefab, root GO id, root transform id, id base for new docs)
    ("Assets/Prefabs/Characters/VBot.prefab", "114070", None, 9500000000000000000),
    ("Assets/Prefabs/Characters/MonKent.prefab", "9100000000000000000", "9100000000000000001", 9500000000000001000),
]

for path, root_go, root_tr, base in TARGETS:
    s = open(path).read()
    if PARA_GUID in s:
        print(f"{path}: already set up")
        continue

    if root_tr is None:
        root_tr = re.search(
            rf"--- !u!4 &(\d+)\nTransform:[^&]*?m_GameObject: {{fileID: {root_go}}}", s).group(1)

    snap_go = str(base + 1)
    snap_tr = str(base + 2)
    snap_mb = str(base + 3)
    pi_id = str(base + 4)
    pi_tr = str(base + 5)

    new = f"""--- !u!1 &{snap_go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {snap_tr}}}
  - component: {{fileID: {snap_mb}}}
  m_Layer: 8
  m_Name: Body Snap Control
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &{snap_tr}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {snap_go}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children: []
  m_Father: {{fileID: {root_tr}}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!114 &{snap_mb}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {snap_go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {G_BODYSNAP}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
--- !u!1001 &{pi_id}
PrefabInstance:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Modification:
    serializedVersion: 3
    m_TransformParent: {{fileID: {root_tr}}}
    m_Modifications:
    - target: {{fileID: {PARA_ROOT_TR}, guid: {PARA_GUID}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {PARA_ROOT_TR}, guid: {PARA_GUID}, type: 3}}
      propertyPath: m_LocalPosition.y
      value: 0
      objectReference: {{fileID: 0}}
    - target: {{fileID: {PARA_ROOT_TR}, guid: {PARA_GUID}, type: 3}}
      propertyPath: m_LocalPosition.z
      value: 0
      objectReference: {{fileID: 0}}
    m_RemovedComponents: []
    m_RemovedGameObjects: []
    m_AddedGameObjects: []
    m_AddedComponents: []
  m_SourcePrefab: {{fileID: 100100000, guid: {PARA_GUID}, type: 3}}
--- !u!4 &{pi_tr} stripped
Transform:
  m_CorrespondingSourceObject: {{fileID: {PARA_ROOT_TR}, guid: {PARA_GUID}, type: 3}}
  m_PrefabInstance: {{fileID: {pi_id}}}
  m_PrefabAsset: {{fileID: 0}}
"""

    # parent the two new children under the root transform
    def add_children(match):
        return match.group(1) + f"  - {{fileID: {snap_tr}}}\n  - {{fileID: {pi_tr}}}\n"
    s2 = re.sub(rf"(--- !u!4 &{root_tr}\nTransform:[^&]*?m_Children:\n(?:  - {{fileID: -?\d+}}\n)*)",
                add_children, s, count=1)
    assert s2 != s, f"could not add children in {path}"
    s2 = s2.rstrip("\n") + "\n" + new
    open(path, "w").write(s2)
    print(f"{path}: added Body Snap Control + Parachute prefab instance")
