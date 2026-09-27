#!/usr/bin/env python3
"""
Grafts the Parachute sub-state machine from the add-on's Invector@Parachute
animator controller into Invector@ShooterMelee.controller, exactly as the
add-on documentation prescribes (Base Layer > Actions > Parachute, plus the
exit transition back to Exit). Idempotent: skips if already grafted.
"""
import re

SRC = "Assets/Invector-3rdPersonController/Add-ons/Parachute/Anims/Invector@Parachute.controller"
DST = "Assets/Invector-3rdPersonController/Shooter/Animator/Invector@ShooterMelee.controller"

PARA_SSM = "1107692270324197912"
EXIT_TRANS = "1109560104326942490"
DST_ACTIONS = "1107191697213795204"   # ShooterMelee Base Layer > Actions

dst = open(DST).read()
if "m_Name: Parachute" in dst:
    print("already grafted — nothing to do")
    raise SystemExit(0)

src = open(SRC).read()
sdocs = re.split(r"^--- ", src, flags=re.M)[1:]
sbyid = {}
for d in sdocs:
    m = re.match(r"!u!(\d+) &(-?\d+)", d)
    sbyid[m.group(2)] = (m.group(1), d)

# collect the parachute subtree
need = []
def collect(fid):
    if fid in need or fid not in sbyid:
        return
    need.append(fid)
    for r in re.findall(r"{fileID: (-?\d+)}", sbyid[fid][1]):
        if r != "0" and r in sbyid and sbyid[r][0] in ("1101", "1102", "1107", "1109", "206", "114"):
            collect(r)
collect(PARA_SSM)
collect(EXIT_TRANS)

# remap ids into a reserved range (no collisions with either controller)
remap = {}
base = 8800000000000000000
for i, fid in enumerate(sorted(need)):
    remap[fid] = str(base + i + 1)

copied = []
for fid in need:
    t, d = sbyid[fid]
    for o, n in remap.items():
        d = re.sub(rf"&{o}\b", f"&{n}", d)
        d = d.replace(f"{{fileID: {o}}}", f"{{fileID: {n}}}")
    copied.append(d if d.endswith("\n") else d + "\n")

new_ssm = remap[PARA_SSM]
new_exit = remap[EXIT_TRANS]

# edit the destination Actions SSM
ddocs = re.split(r"^--- ", dst, flags=re.M)
header = ddocs[0]
ddocs = ddocs[1:]
out = []
for d in ddocs:
    m = re.match(r"!u!(\d+) &(-?\d+)", d)
    if m and m.group(2) == DST_ACTIONS:
        d = re.sub(r"(m_ChildStateMachines:\n(?:  - serializedVersion: 1\n    m_StateMachine: {fileID: -?\d+}\n    m_Position: {[^}]*}\n)*)",
                   lambda mm: mm.group(1) +
                   f"  - serializedVersion: 1\n    m_StateMachine: {{fileID: {new_ssm}}}\n    m_Position: {{x: 576, y: 300, z: 0}}\n",
                   d, count=1)
        d = re.sub(r"(m_StateMachineTransitions:\n(?:  - first: {fileID: -?\d+}\n    second:(?:\n    - {fileID: -?\d+})*\n|  - first: {fileID: 0}\n    second: \[\]\n)*)",
                   lambda mm: mm.group(1) +
                   f"  - first: {{fileID: {new_ssm}}}\n    second:\n    - {{fileID: {new_exit}}}\n",
                   d, count=1)
    out.append(d)

with open(DST, "w") as f:
    f.write(header)
    for d in out:
        f.write("--- " + d)
    for d in copied:
        f.write("--- " + d)

print(f"grafted {len(copied)} docs; Parachute SSM={new_ssm}, exit transition={new_exit}")
