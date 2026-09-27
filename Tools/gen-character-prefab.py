#!/usr/bin/env python3
"""
Generates Assets/Prefabs/Characters/VBot.prefab from Invector's INVENTORY
shooter/melee template (vShooterMelee_Inventory.prefab) — the fully native
path: vItemManager (weapon slots + icons + equip handlers), embedded
Inventory UI + HUD + grenade ThrowManagers, holsters, ragdoll, hitboxes.

Changes against the stock template (nothing else is touched):
  - vThirdPersonCamera subtree removed   (camera lives in GameplayRig)
  - embedded ShooterUI instance removed  (crosshair UI lives in GameplayRig)
  - vThirdPersonController -> CODThirdPersonController (network damage)
  - vShooterMeleeInput     -> CODShooterInput, disabled (owner-only enable)
  - COD/FishNet components appended (player sync, health, loadout bridge)
External fileIDs (root GO/transform, NetworkObject) are kept IDENTICAL to the
previous VBot so every scene/database/manager reference stays valid.
"""
import re, os

SRC = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Player/vShooterMelee_Inventory.prefab"
DST = "Assets/Prefabs/Characters/VBot.prefab"

ROOT_GO_SRC, ROOT_TR_SRC = "146720", "404034"
ROOT_GO_OUT, ROOT_TR_OUT = "114070", "468602"     # ids the rest of the project references
CAMERA_GO = "479055091"
SHOOTERUI_PI = "2949936891192245523"

G_V_CONTROLLER = "73fbf3aa05f6be24780438449f505aa3"
G_V_INPUT      = "46ebb0c68fc51da4b94e466ea07cd110"
G_COD_CONTROLLER = "1f9b384e23764a35940de5d4758540af"
G_COD_INPUT      = "c783d862d55a4338beee21a5ae4621d6"
NEW_COMPONENTS = [  # (fileID, script guid) — appended to the root
    ("9000000000000000001", "9cfd22f5a2e74b46937f8bc13006901d"),  # CODFirstPersonBody
    ("9000000000000000002", "33fb0f150f264c2d8f1c8a8d8a736806"),  # CODInvectorPlayer
    ("9000000000000000003", "299b9e2f871a4268aade2d2a5691623f"),  # CODNetworkHealth
    ("9000000000000000004", "4bfcd34672e04386bc2a3edcecba764f"),  # CODLoadout
    ("9000000000000000005", "26b716c41e9b56b4baafaf13a523ba2e"),  # NetworkObject
    ("9000000000000000006", "a2836e36774ca1c4bbbee976e17b649c"),  # NetworkTransform
    ("9000000000000000007", "e8cac635f24954048aad3a6ff9110beb"),  # NetworkAnimator
]

text = open(SRC).read()
header = text[:text.index("--- ")]
raw_docs = re.split(r"^--- ", text, flags=re.M)[1:]

docs, order = {}, []
for d in raw_docs:
    m = re.match(r"!u!(\d+) &(-?\d+)( stripped)?\n", d)
    docs[m.group(2)] = {"type": m.group(1), "body": d, "stripped": bool(m.group(3))}
    order.append(m.group(2))

def go_of(fid):
    m = re.search(r"m_GameObject: {fileID: (-?\d+)}", docs[fid]["body"])
    return m.group(1) if m else None

def components_of(go):
    return re.findall(r"component: {fileID: (-?\d+)}", docs[go]["body"])

def tr_of(go):
    for c in components_of(go):
        if c in docs and docs[c]["type"] in ("4", "224"):
            return c

def kids(tr):
    m = re.search(r"m_Children:\n((?:  - {fileID: -?\d+}\n)*)", docs[tr]["body"])
    return re.findall(r"{fileID: (-?\d+)}", m.group(1)) if m else []

# ---- kill camera subtree + ShooterUI PI ------------------------------------
kill = set()
def collect(go):
    kill.add(go)
    for c in components_of(go):
        kill.add(c)
    t = tr_of(go)
    if t:
        for ch in kids(t):
            if ch in docs:
                if docs[ch]["stripped"]:
                    kill.add(ch)
                    pi = re.search(r"m_PrefabInstance: {fileID: (-?\d+)}", docs[ch]["body"])
                    if pi: kill.add(pi.group(1))
                else:
                    g = go_of(ch)
                    if g: collect(g)
collect(CAMERA_GO)
kill.add(SHOOTERUI_PI)

# stripped docs of killed PIs + PIs parented under killed transforms
changed = True
while changed:
    changed = False
    for fid, d in docs.items():
        if fid in kill: continue
        if d["stripped"]:
            pi = re.search(r"m_PrefabInstance: {fileID: (-?\d+)}", d["body"])
            if pi and pi.group(1) in kill:
                kill.add(fid); changed = True
        elif d["type"] == "1001":
            pa = re.search(r"m_TransformParent: {fileID: (-?\d+)}", d["body"])
            if pa and pa.group(1) in kill:
                kill.add(fid); changed = True

kill_transforms = {c for g in list(kill) if g in docs and docs[g]["type"] == "1"
                   for c in components_of(g) if c in docs and docs[c]["type"] in ("4", "224")}
kill_transforms |= {f for f in kill if f in docs and (docs[f]["type"] in ("4", "224"))}

# ---- root surgery ------------------------------------------------------------
root = docs[ROOT_GO_SRC]
for c in components_of(ROOT_GO_SRC):
    if c not in docs or docs[c]["type"] != "114":
        continue
    b = docs[c]["body"]
    g = re.search(r"m_Script: {fileID: \d+, guid: (\w+)", b)
    if not g: continue
    if g.group(1) == G_V_CONTROLLER:
        docs[c]["body"] = b.replace(G_V_CONTROLLER, G_COD_CONTROLLER)
    elif g.group(1) == G_V_INPUT:
        b = b.replace(G_V_INPUT, G_COD_INPUT)
        docs[c]["body"] = re.sub(r"m_Enabled: 1", "m_Enabled: 0", b, count=1)

add_lines = "".join(f"  - component: {{fileID: {fid}}}\n" for fid, _ in NEW_COMPONENTS)
root["body"] = re.sub(r"(  m_Component:\n(?:  - component: {fileID: -?\d+}\n)*)",
                      lambda m: m.group(1) + add_lines, root["body"], count=1)
root["body"] = root["body"].replace("m_Name: vShooterMelee_Inventory", "m_Name: VBot")

new_docs = []
for fid, guid in NEW_COMPONENTS:
    new_docs.append(f"""!u!114 &{fid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {ROOT_GO_SRC}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
""")

# ---- prune dead refs ----------------------------------------------------------
for fid, d in docs.items():
    if fid in kill: continue
    b = d["body"]
    if d["type"] in ("4", "224"):
        for kt in kill_transforms:
            b = b.replace(f"  - {{fileID: {kt}}}\n", "")
    if d["type"] == "1":
        for kc in kill:
            b = b.replace(f"  - component: {{fileID: {kc}}}\n", "")
    d["body"] = b

# ---- write with stable external ids -------------------------------------------
os.makedirs(os.path.dirname(DST), exist_ok=True)
out = header
for fid in order:
    if fid in kill: continue
    b = docs[fid]["body"]
    out += "--- " + (b if b.endswith("\n") else b + "\n")
for b in new_docs:
    out += "--- " + b

out = re.sub(rf"&{ROOT_GO_SRC}\b", f"&{ROOT_GO_OUT}", out)
out = out.replace(f"{{fileID: {ROOT_GO_SRC}}}", f"{{fileID: {ROOT_GO_OUT}}}")
out = re.sub(rf"&{ROOT_TR_SRC}\b", f"&{ROOT_TR_OUT}", out)
out = out.replace(f"{{fileID: {ROOT_TR_SRC}}}", f"{{fileID: {ROOT_TR_OUT}}}")

open(DST, "w").write(out)
kept = len([f for f in order if f not in kill]) + len(new_docs)
print(f"wrote {DST}: {kept} docs (removed {len(kill)}) — native inventory stack")
