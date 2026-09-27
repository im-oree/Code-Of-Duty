# 28 — Risk Register

Scored `Likelihood × Impact` (1–5 each). Reviewed at every phase boundary.

---

## R1 — The agent cannot compile C#  ·  L5 × I5 = **25 (critical)**

No Unity, no `dotnet`, no `mono` in the sandbox. Every C# change is written blind.

**Mitigation**
- `Tools/lint-csharp.mjs` catches structural and reference errors cheaply (doc 27 §3).
- Small, incremental changes — never a 1,000-line rewrite in one go.
- Conservative language use: no exotic generics, no obscure API surface, prefer patterns already
  proven elsewhere in the repo.
- **Every handoff names exactly what needs an Editor compile** (doc 27 §9).
- The user's Editor is the compile gate; errors come back and are fixed immediately.

**Residual risk:** moderate. Accepted and managed by discipline, not eliminated.

---

## R2 — The Unity Web Clone diverges from real Unity  ·  L4 × I4 = **16 (high)**

A screenshot that doesn't match the Editor is worse than no screenshot, because it produces
confident wrong decisions.

**Mitigation**
- `UnityWeb/DIVERGENCES.md` — every known gap documented (doc 06 §10).
- Coordinate conversion unit-tested against known transforms (doc 06 §3).
- Periodic calibration: the user sends a real Editor screenshot of a scene; we diff and fix.
- The harness is **never** used to judge things it can't represent (post-processing, baked GI,
  exact URP shading) — those are explicitly out of scope and flagged.
- "The harness is wrong until proven otherwise" is the standing assumption.

---

## R3 — Scope is enormous  ·  L5 × I4 = **20 (critical)**

A full modern shooter is years of work. The realistic failure mode is many half-built systems.

**Mitigation**
- Strict phase gating with objective exit criteria (doc 04). A phase does not start until the
  previous one's criteria are demonstrated.
- Explicit v1.0 scope guardrails; vehicles, battle-royale and theatre are **deferred, documented,
  and designed-for but not built** (doc 01 §6).
- Every phase leaves the game runnable. No long-lived broken states.
- Depth before breadth: five excellent modes beats twelve mediocre ones.

---

## R4 — The existing codebase fights the new architecture  ·  L4 × I3 = **12 (high)**

12.6k LOC written against different assumptions (no asmdefs, raw input reads, presentation writing
state, a 1,033-line menu class).

**Mitigation**
- Migrate by assembly in a defined order (doc 05 §3), one commit each.
- Keep the genuinely good parts (networking, camera shake, `CharacterState`, data layer) and
  rewrite only what's broken.
- `WeaponMovementPose` is deleted rather than patched (doc 10 §2) — patching a wrong ownership
  model compounds the problem.

---

## R5 — Animation quality ceiling  ·  L4 × I4 = **16 (high)**

No animator, no motion capture, no Blender in the environment. Procedural generation gets us
correct, not beautiful.

**Mitigation**
- Procedural where procedural is genuinely better (sway, bob, IK, recoil, lean) — that's most of
  the *feel* (doc 11 §7).
- Generated locomotion clips are correct and in-scale; the clip-name contract makes them a
  drop-in replacement target.
- The harness contact sheets make animation quality *visible* and therefore improvable.
- Explicitly flagged to the user as a place where human-authored animation will raise the ceiling.

---

## R6 — Netcode feel (prediction/reconciliation) is hard  ·  L3 × I5 = **15 (high)**

Getting this subtly wrong produces rubber-banding, bad hit reg, and "I shot first" complaints.

**Mitigation**
- LAN-first means the hardest case (high latency) isn't the first case.
- Budgets and tests defined up front (doc 18 §13), not discovered late.
- Lag compensation implemented with a rewind buffer from the start, not retrofitted.
- Visual corrections smoothed over 0.2 s; camera never corrected.

---

## R7 — UI consistency decays  ·  L4 × I3 = **12 (high)**

Design systems rot the moment one screen is built "just quickly".

**Mitigation**
- `COD / UI / Audit` fails on hardcoded values, missing focus neighbours, and non-library
  components (doc 20 §11).
- Header/footer are shared components, so consistency is structural, not a matter of care.
- Controller reachability is an automated test — the most common place a screen cheats.

---

## R8 — Bots feel fake  ·  L3 × I3 = **9 (medium)**

The bar the user set ("no difference from real players") is high.

**Mitigation**
- Bots drive the real input contract — they can't have superhuman capabilities by construction.
- Human-shaped aiming model (slew, reaction, error, jitter — doc 19 §5), never snap-aim.
- Perception lint forbids reading unseen state.
- Accepted: bots will be *good*, not indistinguishable, at v1.0. The `IAgentBrain` seam is the
  documented upgrade path (doc 19 §11).

---

## R9 — Performance regression accumulates  ·  L3 × I3 = **9 (medium)**

**Mitigation:** static scene budgets checkable without an Editor (doc 26 §9), an automated perf
test from P3 onward, zero-allocation discipline in hot paths, and split UI canvases by design.

---

## R10 — Asset pipeline blocked on the user's model  ·  L3 × I2 = **6 (medium)**

`Male_Body_BaseMesh.fbx` is unrigged; the user intends to upload a model later.

**Mitigation:** `meshkit` generates a rigged operator so nothing is blocked (doc 23 §5, route 1);
the ingest pipeline normalises whatever arrives (route 3); everything is rig-normalised so a swap
doesn't invalidate poses.

---

## R11 — Sandbox environment loss  ·  L3 × I2 = **6 (medium)**

The Chromium install lives in `.cache` and won't survive a fresh sandbox.

**Mitigation:** `Tools/setup-headless-browser.sh` reproduces it from npm in one command; it is
committed and documented in doc 07 §1.

---

## R12 — Intellectual property  ·  L2 × I5 = **10 (high)**

Building in a well-known genre creates a temptation to copy.

**Mitigation:** hard rules in doc 01 §5 — original operators, weapons, maps, streaks, UI art and
audio; no extracted assets; no trademarked names; reference imagery gitignored and used for
principles only; the `Three-FPS` clone treated as a feature spec, not a source of code or art.
Reviewed at every asset addition.

---

## R13 — Verification theatre  ·  L3 × I4 = **12 (high)**

The subtlest risk: producing screenshots and tests that look thorough but don't actually prove
anything, so quality claims drift from reality.

**Mitigation**
- Acceptance criteria are written **before** the work, in the spec doc, and are specific
  (e.g. "hands visible in every frame of the 0.22 s blend", not "looks good").
- The three-category handoff rule (verified / needs Editor / known gap) — doc 27 §9.
- Contact sheets over single screenshots: a range is much harder to cherry-pick than a frame.

---

## Top five to watch

| Rank | Risk | Score |
|---|---|---|
| 1 | R1 — cannot compile C# | 25 |
| 2 | R3 — scope | 20 |
| 3 | R2 — harness divergence | 16 |
| 3 | R5 — animation ceiling | 16 |
| 5 | R6 — netcode feel | 15 |

R1, R2 and R13 are all the same underlying problem — **the feedback loop is indirect** — which is
why P0 (doc 06, doc 07) is the first thing built and why the handoff protocol is a hard rule.
