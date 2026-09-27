#!/usr/bin/env python3
"""
Generates the COD character prefabs from the Invector shooter template.

VBot.prefab: derived from vShooterMelee_NoInventory.prefab —
  - camera + UI subtrees removed (characters must NOT carry camera/UI)
  - inventory/pickup-only components removed
  - vThirdPersonController -> CODThirdPersonController
  - vShooterMeleeInput     -> CODShooterInput (starts disabled; the network
    layer enables it for the owner)
  - adds CODFirstPersonBody, CODInvectorPlayer, CODNetworkHealth,
    CODLoadoutEquipper, FishNet NetworkObject/NetworkTransform/NetworkAnimator
"""
import re, sys, os

SRC = "Assets/Invector-3rdPersonController/Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab"
DST = "Assets/Prefabs/Characters/VBot.prefab"

# script guids
G_V_CONTROLLER = "73fbf3aa05f6be24780438449f505aa3"   # vThirdPersonController
G_V_INPUT      = "46ebb0c68fc51da4b94e466ea07cd110"   # vShooterMeleeInput
G_COD_CONTROLLER = "1f9b384e23764a35940de5d4758540af"
G_COD_INPUT      = "c783d862d55a4338beee21a5ae4621d6"
G_COD_FPBODY     = "9cfd22f5a2e74b46937f8bc13006901d"
G_COD_PLAYER     = "33fb0f150f264c2d8f1c8a8d8a736806"
G_COD_HEALTH     = "299b9e2f871a4268aade2d2a5691623f"
G_COD_LOADOUT    = "4bfcd34672e04386bc2a3edcecba764f"
G_FN_NOB   = "26b716c41e9b56b4baafaf13a523ba2e"
G_FN_NT    = "a2836e36774ca1c4bbbee976e17b649c"
G_FN_NA    = "e8cac635f24954048aad3a6ff9110beb"

# root components dropped entirely (pickup/lock-on/ladder/generic action)
DROP_GUIDS = {
    "8eae4461da03f6a4380b797d66f1589a",  # vCollectShooterMeleeControl
    "86d3d021f27159e4e93e4a7a7f096c49",  # vLockOnShooter
    "7d2c95dd758dfa4469068df6a1432f05",  # vLadderAction
    "f1660eeab87ecf543be4a3ca42e6e836",  # vGenericAction
    "ad63bcafabe53104c838faae1bdabd3a",  # vEntityPreview (item manager)
    "9b5f5c85a39b12f45990b2e749fa43cc",  # vSnapToBody (item manager)
    "80594385e4f69be409516ae62219c6f9",  # vBodySnappingControl
    "2af67e890060a8242ad5e94cffbb8eca",  # vRemoveParent
}
# UI scripts (package scripts have no .cs in Assets): TMP / uGUI on template HUD bits
UI_GUIDS = {
    "fe87c0e1cc204ed48ad3b37840f39efc",  # TextMeshProUGUI
    "dc42784cf147c0c48a680349fa168899",  # (uGUI)
    "0cd44c1031e13a943bb63640046fad76",  # (uGUI)
}

ROOT_GO = "114070"
CAMERA_GO = "4873610712850481247"

text = open(SRC).read()
header = text[:text.index("--- ")]
raw_docs = re.split(r"^--- ", text, flags=re.M)[1:]

docs = {}   # fileID -> dict(type, id, body)
order = []
for d in raw_docs:
    m = re.match(r"!u!(\d+) &(\d+)( stripped)?\n", d)
    t, fid = m.group(1), m.group(2)
    docs[fid] = {"type": t, "body": d}
    order.append(fid)

def go_of(fid):
    m = re.search(r"m_GameObject: {fileID: (\d+)}", docs[fid]["body"])
    return m.group(1) if m else None

def components_of(go_fid):
    return re.findall(r"component: {fileID: (\d+)}", docs[go_fid]["body"])

def transform_of(go_fid):
    for c in components_of(go_fid):
        if c in docs and docs[c]["type"] in ("4", "224"):
            return c
    return None

def children_of(tr_fid):
    body = docs[tr_fid]["body"]
    sec = re.search(r"m_Children:\n((?:  - {fileID: \d+}\n)*)", body)
    return re.findall(r"{fileID: (\d+)}", sec.group(1)) if sec else []

def script_guid(fid):
    m = re.search(r"m_Script: {fileID: \d+, guid: (\w+)", docs[fid]["body"])
    return m.group(1) if m else None

# ---- collect subtrees to delete: camera GO + any GO carrying UI scripts ----
kill_gos = set()

def collect_subtree(go_fid, acc):
    acc.add(go_fid)
    tr = transform_of(go_fid)
    if tr is None:
        return
    for child_tr in children_of(tr):
        if child_tr in docs:
            cgo = go_of(child_tr)
            if cgo:
                collect_subtree(cgo, acc)

collect_subtree(CAMERA_GO, kill_gos)

for fid in list(docs):
    if docs[fid]["type"] == "114":
        g = script_guid(fid)
        if g in UI_GUIDS:
            go = go_of(fid)
            if go and go not in kill_gos:
                collect_subtree(go, kill_gos)

# canvases (type 223 RectTransform canvas roots): remove GOs owning Canvas (223)
for fid in list(docs):
    if docs[fid]["type"] == "223":
        go = go_of(fid)
        if go:
            collect_subtree(go, kill_gos)

kill_fids = set()
for go in kill_gos:
    if go in docs:
        kill_fids.add(go)
        for c in components_of(go):
            kill_fids.add(c)

# ---- root component surgery -------------------------------------------------
root_body = docs[ROOT_GO]["body"]
root_comps = components_of(ROOT_GO)
removed_root = []
for c in root_comps:
    if c in docs and docs[c]["type"] == "114" and script_guid(c) in DROP_GUIDS:
        kill_fids.add(c)
        removed_root.append(c)

# swap controller/input scripts
for fid in root_comps:
    if fid in kill_fids or fid not in docs or docs[fid]["type"] != "114":
        continue
    g = script_guid(fid)
    if g == G_V_CONTROLLER:
        docs[fid]["body"] = docs[fid]["body"].replace(G_V_CONTROLLER, G_COD_CONTROLLER)
    elif g == G_V_INPUT:
        b = docs[fid]["body"].replace(G_V_INPUT, G_COD_INPUT)
        # start disabled: CODInvectorPlayer enables it for the owner only
        b = re.sub(r"m_Enabled: 1", "m_Enabled: 0", b, count=1)
        docs[fid]["body"] = b

# drop vGenericAction etc. from other GOs too
for fid in list(docs):
    if fid in kill_fids or docs[fid]["type"] != "114":
        continue
    if script_guid(fid) in DROP_GUIDS:
        kill_fids.add(fid)

# ---- new components ---------------------------------------------------------
NEW = [
    ("9000000000000000001", G_COD_FPBODY,  ""),
    ("9000000000000000002", G_COD_PLAYER,  ""),
    ("9000000000000000003", G_COD_HEALTH,  ""),
    ("9000000000000000004", G_COD_LOADOUT, ""),
    ("9000000000000000005", G_FN_NOB,      ""),
    ("9000000000000000006", G_FN_NT,       ""),
    ("9000000000000000007", G_FN_NA,       ""),
]
new_docs = []
for fid, guid, extra in NEW:
    body = (f"!u!114 &{fid}\nMonoBehaviour:\n"
            "  m_ObjectHideFlags: 0\n"
            "  m_CorrespondingSourceObject: {fileID: 0}\n"
            "  m_PrefabInstance: {fileID: 0}\n"
            "  m_PrefabAsset: {fileID: 0}\n"
            f"  m_GameObject: {{fileID: {ROOT_GO}}}\n"
            "  m_Enabled: 1\n"
            "  m_EditorHideFlags: 0\n"
            f"  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n"
            "  m_Name: \n"
            "  m_EditorClassIdentifier: \n")
    if extra:
        body += extra
    new_docs.append((fid, body))

# rebuild root component list
def rebuild_components(body, remove, add):
    lines = body.split("\n")
    out = []
    for ln in lines:
        m = re.match(r"  - component: {fileID: (\d+)}", ln)
        if m and m.group(1) in remove:
            continue
        out.append(ln)
    body = "\n".join(out)
    add_lines = "".join(f"  - component: {{fileID: {fid}}}\n" for fid in add)
    body = re.sub(r"(  m_Component:\n(?:  - component: {fileID: \d+}\n)*)",
                  lambda mm: mm.group(1) + add_lines, body, count=1)
    return body

docs[ROOT_GO]["body"] = rebuild_components(
    docs[ROOT_GO]["body"], kill_fids, [fid for fid, _ in new_docs])

# kill nested PrefabInstances whose parent transform was removed (and their stripped docs)
kill_transforms = {c for go in kill_gos if go in docs for c in components_of(go)
                   if c in docs and docs[c]["type"] in ("4", "224")}
changed = True
while changed:
    changed = False
    for fid in list(docs):
        if fid in kill_fids:
            continue
        if docs[fid]["type"] == "1001":
            m = re.search(r"m_TransformParent: {fileID: (\d+)}", docs[fid]["body"])
            if m and (m.group(1) in kill_fids or m.group(1) in kill_transforms):
                kill_fids.add(fid)
                changed = True
        elif " stripped" in docs[fid]["body"].split("\n", 1)[0]:
            m = re.search(r"m_PrefabInstance: {fileID: (\d+)}", docs[fid]["body"])
            if m and m.group(1) in kill_fids:
                kill_fids.add(fid)
                changed = True

# remove killed children/component references from every remaining doc
for fid in docs:
    if fid in kill_fids:
        continue
    b = docs[fid]["body"]
    if docs[fid]["type"] in ("4", "224"):
        for kt in kill_transforms:
            b = b.replace(f"  - {{fileID: {kt}}}\n", "")
    if docs[fid]["type"] == "1":
        for kc in kill_fids:
            b = b.replace(f"  - component: {{fileID: {kc}}}\n", "")
    docs[fid]["body"] = b

# rename root
docs[ROOT_GO]["body"] = docs[ROOT_GO]["body"].replace(
    "m_Name: vShooterMelee_NoInventory", "m_Name: VBot")

# ---- write ------------------------------------------------------------------
os.makedirs(os.path.dirname(DST), exist_ok=True)
with open(DST, "w") as f:
    f.write(header)
    for fid in order:
        if fid in kill_fids:
            continue
        f.write("--- " + docs[fid]["body"])
        if not docs[fid]["body"].endswith("\n"):
            f.write("\n")
    for fid, body in new_docs:
        f.write("--- " + body)

kept = len([f_ for f_ in order if f_ not in kill_fids]) + len(new_docs)
print(f"wrote {DST}: {kept} documents (removed {len(kill_fids)})")
