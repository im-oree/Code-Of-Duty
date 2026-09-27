#!/usr/bin/env python3
"""
StartMenu.unity loadout migration to Invector's native arsenal:
 1. deletes the leftover ONLINE-TPS-KIT gun objects baked into the authored
    OperatorModel hand/holsters (the 'well aligned old gun' in the menu)
 2. replaces the PrimaryWeapons/SecondaryWeapons card containers with one
    'WeaponCards' grid — a card per WeaponDatabase entry (all Invector guns,
    melee weapons and grenades) showing the real Invector item icon
 3. extends the ActiveLoadout panel with MELEE and GRENADE value rows
"""
import re

scene = "Assets/Scenes/StartMenu.unity"

# (id, display, type label, icon guid, column, row)
CARDS = [
    ("AssaultRifle", "ASSAULT RIFLE", "PRIMARY",  "c9bcb3ac64db75147b5fde5864fdfcb2"),
    ("vRifle",       "V-RIFLE",       "PRIMARY",  "f6cfffab8b48ab1498161d83bea40058"),
    ("Sniper",       "SNIPER",        "PRIMARY",  "71509c5d804e82d4bab6ecd153b20f9f"),
    ("RPG",          "RPG",           "PRIMARY",  "89ba1f6f55195c944aad86c1767dfe52"),
    ("Shotgun",      "SHOTGUN",       "PRIMARY",  "0cf8e5588722acf40bfed44d2b39a429"),
    ("Handgun",      "HANDGUN",       "SECONDARY","09867a516d66b7d42bbe651bb2204313"),
    ("ShortKatana",  "SHORT KATANA",  "MELEE",    "56e79d7e38be01c4c9171802884dfb05"),
    ("ShortSword",   "SHORT SWORD",   "MELEE",    "359a7ebb90d1a2c4eba5c493a73ca543"),
    ("Axe",          "AXE",           "MELEE",    "b2158c09d5bc7284989f53a47f7638e3"),
    ("GreatSword",   "GREAT SWORD",   "MELEE",    "68199bff0f78a2d4ca77e20ccc91ef76"),
    ("GreatKatana",  "GREAT KATANA",  "MELEE",    "75c369d61ea60a54e877b01a25ae0916"),
    ("DualSwords",   "DUAL SWORDS",   "MELEE",    "a568bb7d845ecf140a30a74582d14f45"),
    ("FragGrenade",  "FRAG GRENADE",  "GRENADE",  "072fabb08b4333748a030d028e0f9170"),
    ("Molotov",      "MOLOTOV",       "GRENADE",  "dc69ea5879559e44ca874c4a0e1a2945"),
    ("SmokeGrenade", "SMOKE",         "GRENADE",  "b16842e1e839833449a249a61ddf53c6"),
    ("StunGrenade",  "STUN",          "GRENADE",  "1f3b8e1bda8349946a81d2a5adaaec75"),
]
COLS = 4
CARD_W, CARD_H = 225, 148
PITCH_X, PITCH_Y = 237, 160
START_X, START_Y = 48, -205

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
        if t == "1" and (f'm_Name: "{name}"' in d or re.search(rf"m_Name: {name}\s*$", d, flags=re.M)):
            return fid
def subtree(go, acc):
    acc.append(go)
    for c in comps(go):
        acc.append(c)
    t = tr_of(go)
    if t:
        for ch in kids(t):
            if ch in byid:
                g = re.search(r"m_GameObject: {fileID: (-?\d+)}", byid[ch][1])
                if g: subtree(g.group(1), acc)
    return acc

kill = set()

# ---- 1. kit guns inside the authored OperatorModel --------------------------
opmodel = find_go("OperatorModel")
if opmodel:
    names_to_kill = []
    def hunt(go):
        d = byid[go][1]
        n = re.search(r'm_Name: "?([^"\n]*)"?', d).group(1)
        if ("Slot" in n) or n in ("Glok_Pistol", "N4_Rifle", "Saga_Rifle", "P6_SMG"):
            subtree(go, names_to_kill := [] if False else list(kill))  # collect below
            for x in subtree(go, []):
                kill.add(x)
            return
        t = tr_of(go)
        if t:
            for ch in kids(t):
                if ch in byid:
                    g = re.search(r"m_GameObject: {fileID: (-?\d+)}", byid[ch][1])
                    if g: hunt(g.group(1))
    hunt(opmodel)

# ---- 2. loadout containers ---------------------------------------------------
prim = find_go("PrimaryWeapons")
sec = find_go("SecondaryWeapons")
template_card = find_go("Weapon_N4_Rifle")
screen_tr = tr_of(find_go("Screen_LOADOUT"))
tmpl_ids = subtree(template_card, [])

prim_tr, sec_tr = tr_of(prim), tr_of(sec)
prim_parent = re.search(r"m_Father: {fileID: (-?\d+)}", byid[prim_tr][1]).group(1)
prim_rect = byid[prim_tr][1]

for go in (prim, sec):
    for x in subtree(go, []):
        kill.add(x)

# new container 'WeaponCards' reusing PrimaryWeapons' anchoring
base = 9700000000000000000
n = [0]
def nid():
    n[0] += 1
    return str(base + n[0])

new_docs = []
card_trs = []

for idx, (cid, disp, typ, icon) in enumerate(CARDS):
    remap = {}
    for old in tmpl_ids:
        remap[old] = nid()
    col, row = idx % COLS, idx // COLS
    x = START_X + col * PITCH_X
    y = START_Y - row * PITCH_Y + 155  # cards live inside the container; offset applied there
    for old in tmpl_ids:
        t, d = byid[old]
        body = d
        for o, nn in remap.items():
            body = re.sub(rf"&{o}\b", f"&{nn}", body)
            body = body.replace(f"{{fileID: {o}}}", f"{{fileID: {nn}}}")
        owner_old = re.search(r"m_GameObject: {fileID: (-?\d+)}", d)
        oname = re.search(r'm_Name: "?([^"\n]*)"?', byid[owner_old.group(1)][1]).group(1) if owner_old else None
        if t == "1":
            body = re.sub(r'm_Name: "?Weapon_N4_Rifle"?', f'm_Name: "Weapon_{cid}"', body)
            if oname == "Metric":
                # repurpose the metric bar object as the icon holder
                body = body.replace('m_Name: "Metric"', 'm_Name: "Portrait"')
        if t == "224" and old == tr_of(template_card):
            body = re.sub(r"m_AnchoredPosition: {[^}]*}",
                          lambda m: f"m_AnchoredPosition: {{x: {x}, y: {y}}}", body)
            body = re.sub(r"m_Father: {fileID: -?\d+}",
                          lambda m: "m_Father: {fileID: WCROOT}", body)
        if t == "114" and "m_text:" in body and oname:
            if oname == "Name":
                body = re.sub(r"m_text: .*", lambda m: f'm_text: "{disp}"', body, count=1)
            elif oname == "Type":
                body = re.sub(r"m_text: .*", lambda m: f'm_text: "{typ}"', body, count=1)
            elif oname == "Desc":
                body = re.sub(r"m_text: .*", lambda m: 'm_text: ""', body, count=1)
        if t == "114" and oname == "Portrait" is False:
            pass
        if oname == "Metric" and t == "114" and "m_Sprite" in body:
            body = re.sub(r"m_Sprite: {[^}]*}",
                          lambda m: f"m_Sprite: {{fileID: 21300000, guid: {icon}, type: 3}}", body, count=1)
            body = re.sub(r"m_Color: {[^}]*}",
                          lambda m: "m_Color: {r: 1, g: 1, b: 1, a: 1}", body, count=1)
            body = re.sub(r"m_Type: \d+", "m_Type: 0", body, count=1)
            body = re.sub(r"m_PreserveAspect: \d", "m_PreserveAspect: 1", body, count=1)
        new_docs.append(body if body.endswith("\n") else body + "\n")
    card_trs.append(remap[tr_of(template_card)])

# container GO
wc_go, wc_tr = nid(), nid()
container_rect = re.sub(r"--- !u!224 &-?\d+\n", "", "")  # unused
prim_rect_doc = byid[prim_tr][1]
anch = {
    "pos": re.search(r"m_AnchoredPosition: {[^}]*}", prim_rect_doc).group(0),
    "size": re.search(r"m_SizeDelta: {[^}]*}", prim_rect_doc).group(0),
    "amin": re.search(r"m_AnchorMin: {[^}]*}", prim_rect_doc).group(0),
    "amax": re.search(r"m_AnchorMax: {[^}]*}", prim_rect_doc).group(0),
    "pivot": re.search(r"m_Pivot: {[^}]*}", prim_rect_doc).group(0),
}
new_docs.append(f"""!u!1 &{wc_go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {wc_tr}}}
  m_Layer: 5
  m_Name: "WeaponCards"
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
""")
new_docs.append(f"""!u!224 &{wc_tr}
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {wc_go}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_Children:
{"".join(f"  - {{fileID: {t}}}" + chr(10) for t in card_trs)}  m_Father: {{fileID: {prim_parent}}}
  m_RootOrder: 1
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  {anch["amin"]}
  {anch["amax"]}
  {anch["pos"]}
  {anch["size"]}
  {anch["pivot"]}
""")
new_docs = [d.replace("{fileID: WCROOT}", f"{{fileID: {wc_tr}}}") for d in new_docs]

# ---- 3. ActiveLoadout: clone Secondary row into Melee + Grenade rows ---------
al = find_go("ActiveLoadout")
al_tr = tr_of(al)
sec_label = None
sec_value = None
for ch in kids(al_tr):
    g = re.search(r"m_GameObject: {fileID: (-?\d+)}", byid[ch][1]).group(1)
    nm = re.search(r'm_Name: "?([^"\n]*)"?', byid[g][1]).group(1)
    if nm == "SecondaryValue": sec_value = g
    if nm == "SecondaryLabel": sec_label = g

extra_rows = []
for tag, label in (("Melee", "MELEE"), ("Grenade", "GRENADE")):
    for src, kind in ((sec_label, "Label"), (sec_value, "Value")):
        if src is None: continue
        ids = subtree(src, [])
        remap = {o: nid() for o in ids}
        for old in ids:
            t, d = byid[old]
            body = d
            for o, nn in remap.items():
                body = re.sub(rf"&{o}\b", f"&{nn}", body)
                body = body.replace(f"{{fileID: {o}}}", f"{{fileID: {nn}}}")
            if t == "1":
                body = re.sub(r'm_Name: "?Secondary(Label|Value)"?',
                              lambda m: f'm_Name: "{tag}{m.group(1)}"', body)
            if t == "224" and old == tr_of(src):
                off = 46 if tag == "Melee" else 92
                mm = re.search(r"m_AnchoredPosition: {x: ([-\d.]+), y: ([-\d.]+)}", body)
                if mm:
                    body = body.replace(mm.group(0),
                        f"m_AnchoredPosition: {{x: {mm.group(1)}, y: {float(mm.group(2)) - off}}}")
            if t == "114" and "m_text:" in body and kind == "Label":
                body = re.sub(r"m_text: .*", lambda m: f'm_text: "{label}"', body, count=1)
            new_docs.append(body if body.endswith("\n") else body + "\n")
        extra_rows.append(remap[tr_of(src)])

# ---- write -------------------------------------------------------------------
out_parts = []
for fid in order:
    if fid in kill:
        continue
    t, d = byid[fid]
    if fid == prim_parent:  # screen/container transform holding PrimaryWeapons
        d = d.replace(f"  - {{fileID: {prim_tr}}}\n", f"  - {{fileID: {wc_tr}}}\n")
        d = d.replace(f"  - {{fileID: {sec_tr}}}\n", "")
    if fid == al_tr:
        d = re.sub(r"(m_Children:\n(?:  - {fileID: -?\d+}\n)*)",
                   lambda m: m.group(1) + "".join(f"  - {{fileID: {t2}}}\n" for t2 in extra_rows),
                   d, count=1)
    out_parts.append(d)

with open(scene, "w") as f:
    f.write(header)
    for d in out_parts:
        f.write("--- " + (d if d.endswith("\n") else d + "\n"))
    for d in new_docs:
        f.write("--- " + d)

print(f"loadout rebuilt: {len(CARDS)} weapon cards, kit guns removed ({len(kill)} docs), melee+grenade rows added")
