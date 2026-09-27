# 27 — Coding Standards & Workflow

## 1. C# conventions

| Item | Rule |
|---|---|
| Namespaces | `CodeOfDuty.<Area>` matching the assembly (`Contracts`, `Sim`, `Presentation`, `UI`, `Net`) |
| Files | One public type per file; file name == type name |
| Naming | `PascalCase` types/methods/properties, `camelCase` locals/params, `_camelCase` private fields, `UPPER_SNAKE` const |
| Serialized fields | `[SerializeField] private` with an explicit `[Tooltip]`; never `public` for Inspector wiring |
| Nullability | Reference fields that may be absent are checked once at init and reported, not null-checked everywhere |
| `var` | Only when the type is obvious from the right-hand side |
| Regions | Banned. If a file needs regions, it needs splitting. |
| File length | Soft limit 400 lines; hard limit 600. `CODMainMenu.cs` at 1,033 is the counter-example. |
| Comments | Explain *why*, never *what*. XML docs on every public type and non-obvious member. |
| Magic numbers | Banned in gameplay. Values live in tuning assets. |

## 2. Banned constructs (lint-enforced)

| Banned | Why | Exception |
|---|---|---|
| `DestroyImmediate` at runtime | Causes bug D1 | `#if UNITY_EDITOR` only |
| Mixing `Destroy` and `DestroyImmediate` in one routine | Frame-ordering races | none |
| `Input.` / `Keyboard.current` / `Gamepad.current` / `Mouse.current` | Breaks bots + controller parity | `PlayerInputSource` only |
| `FindObjectOfType` / `FindObjectsByType` in `Update`/`LateUpdate` | Performance | none |
| `[ExecuteAlways]` that mutates or dirties the scene | Causes bugs D1–D4 | read-only gizmo/preview components |
| Empty `catch` or `catch` that logs and continues during init | Hides D2-class failures | `catch` that logs **and aborts** |
| Hardcoded colours/sizes in UI code | Breaks the design system | none |
| Presentation writing to `CharacterState` | Breaks authority | none |
| `GetComponent` in hot paths | Performance | cached in `Awake` |
| `async void` | Unobservable exceptions | Unity event handlers only |

## 3. The linter

`Tools/lint-csharp.mjs` — Node, no dependencies, runs in < 2 s over the project.

```bash
node Tools/lint-csharp.mjs                 # whole project
node Tools/lint-csharp.mjs --changed       # only files changed vs main
node Tools/lint-csharp.mjs --fix-trivial   # whitespace, using order
```

Checks:
1. **Structural** — balanced `{}`/`()`/`[]`, string/comment aware; unterminated literals.
2. **Declaration** — `namespace`/`class`/`struct`/`enum` well-formed; file name matches the type.
3. **Banned tokens** — §2, with per-file allow comments (`// lint:allow <rule> <reason>`) that
   require a reason.
4. **Using hygiene** — unused usings; `using` referencing an assembly this asmdef can't see.
5. **Symbol sanity** — references to types not defined in the project or in a known Unity/FishNet
   symbol index get flagged as *probable* typos (warning, not error — it's a heuristic).
6. **Asmdef legality** — a file in `Sim/` referencing a `Presentation/` type is an error.
7. **Doc coverage** — public types without XML docs.

**This is not a compiler.** It catches the errors that actually happen when writing C# without one.
The authoritative compile happens in the user's Editor, and every handoff says so explicitly.

## 4. Verification before every commit

```bash
node Tools/lint-csharp.mjs --changed      # Gate A
node Tools/validate-assets.mjs            # Gate B
cd UnityWeb && npm run harness -- <seq>   # Gate C (if visual)
node Tools/imgdiff.mjs <before> <after>   # Gate C regression
```
Full definitions in doc 07 §2.

## 5. Git workflow

- **All work happens on `arena/01a0ded1-code-of-duty`.** This session is bound to that branch.
- Small, focused commits. One concern per commit.
- Commit message format:

```
<area>: <imperative summary>

Why: <the problem being solved>
What: <the change, in bullets>
Verified: <lint | screenshot path | test name>
Needs Editor: <yes/no — what still requires a Unity compile or play-mode check>
```

- Never commit: `Library/`, `Temp/`, `Logs/`, `Artifacts/`, `Reference/`, `UnityWeb/node_modules/`.
- Unity `.meta` files **are** committed — always alongside their asset, never orphaned.
- Large binaries go through the existing `.gitattributes` LFS rules or stay out of the repo.

## 6. Unity-specific practices

| Practice | Rule |
|---|---|
| Prefabs | Prefer composition; avoid nested prefab variants more than 2 deep |
| Scene content | The menu is **saved scene objects** (doc 21 §2) — not runtime-generated |
| References | Explicit serialized references over runtime lookup |
| Managers | Created by `Boot`, not by scattered `RuntimeInitializeOnLoadMethod` |
| Execution order | Avoid dependence on it; where unavoidable, set it explicitly and document why |
| `LateUpdate` | All procedural pose/camera work — never `Update` (bug D5/F2) |
| ScriptableObjects | Treated as immutable at runtime; runtime state lives elsewhere |
| Coroutines | Avoid for gameplay timing; use tick-based timers so they're deterministic and networkable |

## 7. Testing

| Kind | Where | Runs |
|---|---|---|
| Pure logic (EditMode) | `Assets/Tests/EditMode` | Unity Test Framework (user's Editor) |
| Integration (PlayMode) | `Assets/Tests/PlayMode` | Unity Test Framework |
| Subtle math mirrors | `UnityWeb/tests/sim` | Node, in the sandbox |
| Visual | `UnityWeb/harness` | Node + headless Chromium |
| Network | `Tools/net-harness` | Node |

Every bug fixed gets a test that would have caught it. D1 (duplicate operator) gets an EditMode
test asserting exactly one `MenuStage` and one `OperatorDisplay` after scene load and after Play.

## 8. Documentation discipline

- Every phase's design doc in `Docs/plan/` is **updated when the code diverges from it**. A doc
  that lies is worse than no doc.
- Each doc carries its acceptance criteria; ticking them off is how a phase closes.
- New systems get a doc before the code, not after.
- `Docs/plan/00-INDEX.md` stays current.

## 9. Handoff protocol

Because the agent cannot compile C# or enter play mode, every handoff states, explicitly and
separately:

1. **What was verified and how** (lint / screenshot path / test name).
2. **What needs the Editor** (compile, play-mode behaviour, animation import, NavMesh bake).
3. **What is a known gap** and when it's scheduled.

No blurring of those three categories. This is the single most important workflow rule in the
project.

## 10. Definition of done

Restated from doc 01 §3 because it belongs here too:

- [ ] Data-driven, not hardcoded
- [ ] Server-authoritative where it affects outcomes
- [ ] Works on keyboard+mouse **and** gamepad
- [ ] Seen in a screenshot
- [ ] Reads correctly in first **and** third person
- [ ] Lint clean
- [ ] Spec doc updated
- [ ] Degrades safely
