# 07 — Verification & the Visual Loop

> *"Everything must be like a real developer did it."* A real developer looks at the screen.
> This document is how we look at the screen.

## 1. The environment, measured

| Capability | Status | Consequence |
|---|---|---|
| Unity Editor | ❌ absent | No play-mode, no Editor screenshots from the agent |
| `dotnet` / `mono` / Roslyn | ❌ absent | `Tools/verify-csharp.sh` cannot run in the sandbox |
| Node.js 22 | ✅ | Harness + tooling language |
| **Headless Chromium 131 + WebGL 2.0** | ✅ **verified** | **The visual loop is possible** |
| Python 3.11 | ✅ | Image diffing, mesh generation |
| GPU | ❌ (SwiftShader software raster) | ~1.1 s per 640×360 frame. Fine for stills, not for realtime. |

### How Chromium was obtained (reproducible)

Google and Azure CDNs are blocked; the npm registry and GitHub are reachable. So:

```bash
npm i @sparticuz/chromium@131.0.1        # ships the binary inside the tarball
# extract bin/chromium.br (brotli), swiftshader.tar.br, al2023.tar.br
# copy swiftshader *.so NEXT TO the chromium binary (it dlopen()s them by path)
# LD_LIBRARY_PATH=<al2023 libs>  (provides libnss3 et al)
```

Launch flags that matter:
```
--no-sandbox --disable-dev-shm-usage
--use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader
--in-process-gpu --disable-gpu-sandbox
```
Content must be served over **http://** — `file://` fails ES-module CORS.

This is scripted in `Tools/setup-headless-browser.sh` so it survives a fresh sandbox.

## 2. The four verification gates

Every change passes the gates that apply to it. Nothing merges on assertion alone.

### Gate A — Structural (always)
- `Tools/lint-csharp.mjs` — a Node-based C# structural linter we own. Checks: balanced
  braces/parens, `namespace`/`class` symmetry, unresolved `using`s against a known symbol index,
  banned tokens (`DestroyImmediate` outside Editor code, `Input.` outside `PlayerInputSource`,
  `FindObjectOfType` in `Update`), file/class name agreement, and asmdef reference legality.
- Runs in <2 s across the whole project. Not a compiler — a **cheap net for the errors that
  actually happen** when writing C# blind.
- **The real compile gate is the user's Editor.** Every handoff states clearly what still needs an
  Editor compile.

### Gate B — Data (always, where data changed)
- ScriptableObject/asset YAML validated by `Tools/validate-assets.mjs`: GUID references resolve,
  required fields present, numeric ranges sane, no duplicate IDs in databases.

### Gate C — Visual (any change with a visual result)
- `UnityWeb` harness renders the affected scene/prefab and writes PNGs to `Artifacts/<phase>/`.
- The agent **reads the PNG** and judges it against the spec in the relevant design doc.
- For pose/animation work: a **contact sheet** (see §4) covering the full blend range, in both
  perspectives.

### Gate D — Behavioural (sim logic)
- Pure-logic C# (state machines, stat aggregation, spawn scoring, ballistics math) is mirrored by
  a small TS port under `UnityWeb/tests/sim/` **only where the math is subtle**, and unit-tested
  there. This is not duplication for its own sake — it is how we test math we cannot compile.
- Unity `EditMode`/`PlayMode` tests are written alongside the C# for the user's Editor to run
  (Test Framework 1.8 is already installed).

## 3. The loop, concretely

```
 1. READ the spec doc for the feature (numbers, intent, acceptance shots)
 2. WRITE the C#
 3. GATE A  — lint
 4. MIRROR  — if visual, update/author the TS mirror in UnityWeb/src/scripting/mirrors/
 5. GATE C  — npm run harness -- <sequence>   → Artifacts/*.png
 6. LOOK    — read the PNGs; compare against the spec's acceptance criteria
 7. FIX     — iterate 2–6 until it reads right
 8. RECORD  — save the approved shots, update the doc's "verified" line
 9. HANDOFF — state exactly what needs an Editor compile / play-mode confirmation
```

Steps 5–7 are the point. A pose change is not "probably better", it is *seen to be better*.

## 4. Harness sequence format

A sequence is a small declarative file. It sets up a scene, drives state over time, and captures
frames from named cameras.

```jsonc
// UnityWeb/harness/tacsprint.json
{
  "name": "tacsprint",
  "scene": "Assets/Scenes/DevRange.unity",
  "subject": "Player",
  "cameras": [
    { "id": "fp", "mode": "component", "path": "Player/Head/FPCamera" },
    { "id": "tp", "mode": "orbit", "target": "Player/Hips", "yaw": 35, "pitch": 12, "dist": 3.4 },
    { "id": "tp_front", "mode": "orbit", "target": "Player/Hips", "yaw": 195, "pitch": 6, "dist": 2.8 }
  ],
  "timeline": [
    { "t": 0.00, "set": { "locomotion": "Idle" } },
    { "t": 0.20, "set": { "locomotion": "Sprint" } },
    { "t": 0.60, "set": { "locomotion": "TacSprint" } },
    { "t": 1.40, "set": { "locomotion": "Sprint" } },
    { "t": 1.80, "set": { "locomotion": "Idle" } }
  ],
  "capture": { "everySeconds": 0.2, "size": [960, 540], "contactSheet": true }
}
```

Output: `Artifacts/p1/tacsprint/{fp,tp,tp_front}-contact.png` — a labelled grid of every sampled
frame. **One image tells you whether a blend reads correctly across its whole range**, which is
exactly what a single screenshot cannot.

## 5. Screenshot comparison

`Tools/imgdiff.mjs` (pure Node, no native deps):
- Perceptual diff between two PNGs → a heatmap + a `% changed` number.
- Used for **regression**: an approved shot becomes a baseline in `Artifacts/baseline/`. Re-running
  a sequence after an unrelated change must not move the baseline by more than a threshold.
- Used for **before/after**: every pose fix records the pair.

## 6. Performance measurement

The harness is a software rasteriser, so it cannot measure real frame time. Performance is
verified differently:
- **Static budgets** (tri counts, draw calls, texture memory, material count) computed from the
  parsed scene by `Tools/scene-budget.mjs` — these *are* meaningful and catch the common problems.
- **Runtime FPS** is measured by the user's Editor/build against the budgets in doc 26, reported
  back, and recorded.

## 7. Network verification

LAN multiplayer cannot be validated by a renderer. Instead:
- **Headless simulation:** a Node harness that speaks our protocol shape and runs N simulated
  clients against a server build, asserting convergence, snapshot size, and reconciliation error.
- **In-Editor:** ParrelSync clones (already vendored) for real 2–4 client tests by the user.
- **Assertions in code:** the server logs desync magnitude per client; a threshold breach is a test
  failure, not a warning.

## 8. What the agent must always disclose

At every handoff, explicitly separate:

| Claim type | Wording |
|---|---|
| Verified by screenshot | "Rendered and checked: `<path to PNG>`" |
| Verified by lint/test | "Lint clean / test `<name>` passes" |
| **Not** verified | "**Needs Editor compile**" / "**Needs play-mode confirmation**" |

Never blur these. The value of the loop is destroyed the moment an unverified claim is presented
as a verified one.

## 9. Baseline artifacts

```
Artifacts/
├── baseline/          approved reference shots (committed? NO — gitignored, regenerated)
├── p0/ p1/ p2/ …      per-phase output
└── diffs/             comparison heatmaps
```

`Artifacts/` is gitignored — screenshots are build output, not source. Approved shots that matter
for the record are referenced by name in the phase's doc, and the sequence that produces them is
committed, so any of them can be regenerated on demand.
