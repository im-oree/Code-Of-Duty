#!/usr/bin/env python3
"""
StartMenu.unity: replaces the two hardcoded operator cards (Crimson/Cobalt)
with one authored card per CharacterDatabase entry (Operator_<id>), cloned
from the existing card design. CODMainMenu binds them by name at runtime.
"""
import re

scene = "Assets/Scenes/StartMenu.unity"

CARDS = [  # (id, display, role, desc, x)
    ("VBot",         "V-BOT",         "OPERATOR PROFILE", "Invector combat android.\\nBaseline for every setup.", 48),
    ("MonKent",      "MON KENT",      "OPERATOR PROFILE", "Veteran operator. Fast, quiet,\\nfirst through the door.", 348),
    ("VBot_Crimson", "V-BOT CRIMSON", "VARIATION \\u2022 V-BOT", "Crimson unit variation\\nof the V-Bot chassis.", 648),
    ("VBot_Cobalt",  "V-BOT COBALT",  "VARIATION \\u2022 V-BOT", "Cobalt unit variation\\nof the V-Bot chassis.", 948),
    ("MaleBase",     "RECRUIT",       "IN TRAINING", "Unrigged base mesh.\\nAwaiting rigging for deployment.", 1248),
]

s = open(scene).read()
header = s[:s.index("--- ")]
docs = re.split(r"^--- ", s, flags=re.M)[1:]
byid, order = {}, []
for d in docs:
    m = re.match(r"!u!(\d+) &(-?\d+)", d)
    byid[m.group(2)] = [m.group(1), d]
    order.append(m.group(2))

def comps(go): return re.findall(r"component: {fileID: (-?\d+)}", byid[go][1])
def tr_of(go):
    for c in comps(go):
        if c in byid and byid[c][0] in ("4", "224"):
            return c
def kids(tr):
    m = re.search(r"m_Children:\n((?:  - {fileID: -?\d+}\n)*)", byid[tr][1])
    return re.findall(r"{fileID: (-?\d+)}", m.group(1)) if m else []

def find_go(name):
    for fid in order:
        t, d = byid[fid]
        if t == "1" and f'm_Name: "{name}"' in d:
            return fid

crimson = find_go("Operator_Crimson")
cobalt = find_go("Operator_Cobalt")
cards_go = find_go("OperatorCards")
cards_tr = tr_of(cards_go)

def subtree(go, acc):
    acc.append(go)
    for c in comps(go):
        acc.append(c)
    for ch in kids(tr_of(go)):
        cgo = re.search(r"m_GameObject: {fileID: (-?\d+)}", byid[ch][1]).group(1)
        subtree(cgo, acc)
    return acc

tmpl_ids = subtree(crimson, [])
kill = set(subtree(crimson, []) + subtree(cobalt, []))

next_id = [9400000000000000000]
new_docs = []
new_card_trs = []
for cid, disp, role, desc, x in CARDS:
    remap = {}
    for old in tmpl_ids:
        next_id[0] += 1
        remap[old] = str(next_id[0])
    for old in tmpl_ids:
        t, d = byid[old]
        body = d
        for o, n in remap.items():
            body = re.sub(rf"&{o}\b", f"&{n}", body)
            body = body.replace(f"{{fileID: {o}}}", f"{{fileID: {n}}}")
        go_owner = re.search(r"m_GameObject: {fileID: (-?\d+)}", body)
        if t == "1":
            body = body.replace('m_Name: "Operator_Crimson"', f'm_Name: "Operator_{cid}"')
            body = body.replace('m_Name: "PortraitField"', 'm_Name: "Portrait"')
        if t == "224" and old == tr_of(crimson):
            body = re.sub(r"m_AnchoredPosition: {[^}]*}",
                          f"m_AnchoredPosition: {{x: {x}, y: -184}}", body)
        if t == "114" and "m_text:" in body:
            # which child does this text belong to?
            owner_old = re.search(r"m_GameObject: {fileID: (-?\d+)}", d).group(1)
            oname = re.search(r'm_Name: "?([^"\n]*)"?', byid[owner_old][1]).group(1)
            if oname == "Name":
                body = re.sub(r"m_text: .*", lambda _m: f'm_text: "{disp}"', body, count=1)
            elif oname == "Role":
                body = re.sub(r"m_text: .*", lambda _m: f'm_text: "{role}"', body, count=1)
            elif oname == "Desc":
                body = re.sub(r"m_text: .*", lambda _m: f'm_text: "{desc}"', body, count=1)
        new_docs.append(body if body.endswith("\n") else body + "\n")
    new_card_trs.append(remap[tr_of(crimson)])

# rebuild OperatorCards children: drop old card transforms, add new ones
old_trs = {tr_of(crimson), tr_of(cobalt)}
b = byid[cards_tr][1]
for ot in old_trs:
    b = b.replace(f"  - {{fileID: {ot}}}\n", "")
b = re.sub(r"(  m_Children:\n(?:  - {fileID: -?\d+}\n)*)",
           lambda m: m.group(1) + "".join(f"  - {{fileID: {t}}}\n" for t in new_card_trs),
           b, count=1)
byid[cards_tr][1] = b

with open(scene, "w") as f:
    f.write(header)
    for fid in order:
        if fid in kill:
            continue
        d = byid[fid][1]
        f.write("--- " + (d if d.endswith("\n") else d + "\n"))
    for d in new_docs:
        f.write("--- " + d)
print(f"replaced 2 cards with {len(CARDS)}; killed {len(kill)} docs, added {len(new_docs)}")
