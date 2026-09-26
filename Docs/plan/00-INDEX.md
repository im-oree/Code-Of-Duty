# 00 — Master Index

**Project:** CODE OF DUTY — an original, modern military multiplayer shooter built in Unity 6
(`6000.6.3f1`) on URP + FishNet, with a companion TypeScript/three.js **Unity Web Clone** used as
the agent-facing verification harness.

**Branch:** `arena/01a0ded1-code-of-duty`
**Plan authored:** 2026-09-26

---

## How to read this plan

Documents are grouped. Read `01` and `04` first; everything else is reference you dip into when
you start the matching phase.

| # | Document | Group | Read when |
|---|---|---|---|
| [00](00-INDEX.md) | Master index | Meta | now |
| [01](01-VISION-AND-PILLARS.md) | Vision, pillars, quality bar, originality rules | Meta | now |
| [02](02-REFERENCE-FEATURE-MAP.md) | Three-FPS reference — complete feature inventory | Research | now |
| [03](03-CURRENT-STATE-AUDIT.md) | What the Unity project actually is today + bug list | Research | now |
| [04](04-ROADMAP-PHASES.md) | The staged roadmap P0→P12 with exit criteria | Meta | now |
| [05](05-ARCHITECTURE-OVERVIEW.md) | Runtime architecture, assemblies, module boundaries | Eng | P1 |
| [06](06-UNITY-WEB-CLONE.md) | The TS/three.js Unity runtime clone | Tooling | P0 |
| [07](07-VERIFICATION-AND-VISUAL-LOOP.md) | Headless browser harness, screenshot gates | Tooling | P0 |
| [08](08-INPUT-AND-CONTROLLER.md) | Input abstraction, gamepad day-one, rebinding, UI nav | Eng | P1 |
| [09](09-MOVEMENT-SPEC.md) | Full movement spec with numbers | Design | P2 |
| [10](10-TACTICAL-SPRINT-REWORK.md) | Tac-sprint: diagnosis, pose spec, acceptance shots | Design | P1 |
| [11](11-ANIMATION-SYSTEM.md) | Layered animation, procedural rig, FP/TP parity | Eng | P2 |
| [12](12-GUNPLAY-AND-BALLISTICS.md) | Weapons, fire modes, recoil, ballistics, ADS | Design | P3 |
| [13](13-GUNSMITH-ATTACHMENTS.md) | Attachment slots, stat aggregation, sockets | Design | P4 |
| [14](14-LOADOUT-PERKS-EQUIPMENT.md) | Classes, perks, lethals/tacticals, field upgrades | Design | P4 |
| [15](15-STREAKS-AND-PROGRESSION.md) | Streak system, XP, unlocks | Design | P7 |
| [16](16-GAME-MODES-AND-MATCH-FLOW.md) | Modes, match state machine, scoring, spawns | Design | P5 |
| [17](17-MAPS-AND-LEVEL-PIPELINE.md) | Map schema, greybox→art, spawns, callouts | Design | P6 |
| [18](18-NETWORKING-LAN-FIRST.md) | FishNet architecture, LAN discovery, prediction | Eng | P5 |
| [19](19-BOTS-AS-PLAYERS.md) | Bots that drive the same input contract as humans | Eng | P8 |
| [20](20-UI-DESIGN-SYSTEM.md) | Design tokens, components, layout grid, consistency | Design | P1 |
| [21](21-UI-SCREEN-SPECS.md) | Every front-end screen, incl. redesigned main menu | Design | P1 |
| [22](22-HUD-SPEC.md) | In-match HUD | Design | P3 |
| [23](23-ASSET-PIPELINE-MODELS.md) | Procedural GLB/FBX builder, rigging, scale, swapping | Tooling | P2 |
| [24](24-UI-TRANSPILER-HTML-TO-UGUI.md) | HTML/TS UI → Unity uGUI converter | Tooling | P6 |
| [25](25-AUDIO-SPEC.md) | Audio design and mix | Design | P7 |
| [26](26-PERFORMANCE-AND-QUALITY.md) | Budgets, LODs, quality presets, cross-platform | Eng | P9 |
| [27](27-CODING-STANDARDS-AND-WORKFLOW.md) | Conventions, verification, git workflow | Meta | now |
| [28](28-RISK-REGISTER.md) | Risks and mitigations | Meta | now |
| [29](29-PHASE-1-WORKPLAN.md) | The immediate, task-level Phase 1 plan | Exec | now |

---

## The one-paragraph summary

We are rebuilding an existing half-finished Unity kit into a genuinely good modern military
shooter. The reference implementation (`Three-FPS`, 261 TypeScript files / 52k LOC) is a
**feature specification**, not code to port — it tells us *what* a complete version of this game
contains. Networking is the spine: LAN-first over FishNet, but every system is written
server-authoritative so a dedicated/online backend is a deployment choice, not a rewrite. Bots
drive the *same* input interface as humans. The UI is one design system applied consistently to
every screen, controller-navigable from day one. Because no Unity Editor is available to the
agent, we build a **Unity Web Clone** — a TypeScript/three.js runtime that parses real Unity
scenes/prefabs and renders them in a headless browser — so that every visual change is verified
against a screenshot instead of assumed.

---

## Non-negotiables

1. **Originality.** Everything we ship is our own: our operators, our weapon names, our maps, our
   UI art. We study the genre; we do not copy any publisher's assets, logos, characters, or art.
   See `01-VISION-AND-PILLARS.md` §5.
2. **Verify, don't assume.** No "this should look right". Screenshot or it didn't happen.
   See `07-VERIFICATION-AND-VISUAL-LOOP.md`.
3. **Fix before build.** Each phase starts by fixing what the previous phase broke or left broken.
4. **Both perspectives, always.** Every animation/pose change is judged in first *and* third
   person before it is called done.
5. **Cross-platform from day one.** Keyboard+mouse and gamepad are peers, not a port.
