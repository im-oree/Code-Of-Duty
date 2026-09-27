# 01 — Vision, Pillars, Quality Bar

## 1. The product

A **fast, smooth, readable modern military multiplayer shooter**. Sessions are 6–12 minutes.
The moment-to-moment loop is: move well → see the enemy first → win a short, decisive gunfight →
reposition. Everything in this plan exists to serve that loop.

Target feel, in the user's words: *"not so high poly but smooth, fast, fun to play, lowkey
realistic — CoD-ish but not real-realistic, and must be fully CoD gameplay 1:1."*

Translated into engineering targets:

| Dimension | Target |
|---|---|
| Art fidelity | **Stylised-realistic.** Low-to-mid poly (8–20k tris per operator), strong silhouettes, PBR materials, restrained detail. Readability beats detail. |
| Frame rate | 120 fps on a mid-range desktop GPU at 1080p; 60 fps floor on integrated graphics at 1080p/low. |
| Input latency | ≤ 2 frames from input sample to camera response. Never buffer camera look. |
| Time-to-kill | 250–450 ms at effective range for automatics — short, but with enough time to react and break line of sight. |
| Movement | Momentum-light and responsive. Sprint-out and ADS-in times are the primary balancing levers. |
| Net | Playable at 80 ms RTT / 2% loss. LAN is the reference case and must feel local. |

## 2. Design pillars

### P1 — Movement is the skill floor *and* the skill ceiling
Anyone can walk and shoot. The gap between good and great players is slide-cancels, mantles, and
knowing when tac sprint is worth the sprint-out penalty. Movement must therefore be **precise,
predictable, and animated honestly** — the third-person body must tell the truth about what the
first-person player is doing, because that is how opponents read you.

### P2 — Readability over realism
Silhouettes read at 60 m. Muzzle flashes read but don't blind. The HUD says exactly what you need
and nothing else. Enemy vs friendly is never ambiguous. We choose the readable option every time
there's a tension with realism.

### P3 — Networking is the spine, not a layer
No system is written single-player-first and "networked later". Every gameplay system is written
as: *server simulates, clients present*. LAN is our first-class transport; a dedicated server is
the same code path with a different bind address.

### P4 — Bots are players
A bot is not an AI system bolted onto a character. A bot is an implementation of the *same*
`IInputSource` a human uses. It presses keys. If a bot can do it, a player could have done it, and
vice versa. This makes bots honest, makes them testable, and means improving bot smartness later
never requires touching the character.

### P5 — Everything is data, nothing is hardcoded
Weapons, attachments, perks, streaks, maps, modes, operators, keybinds, UI theme — all data assets
edited in the Editor. Adding a gun is adding a row, not writing code.

### P6 — Every screen is the same product
One design system. One navigation model. One focus/selection behaviour. The main menu, the pause
menu, the settings panel and the end-of-match screen must look and behave like they were designed
by the same person on the same day.

## 3. The quality bar (definition of done)

A feature is **done** when all of these are true:

- [ ] It is driven by data assets, not literals in code.
- [ ] It is server-authoritative where it affects outcomes; purely cosmetic where it doesn't.
- [ ] It works with keyboard+mouse **and** gamepad.
- [ ] It has been *seen* — a screenshot from the verification harness is attached to the PR/notes.
- [ ] It reads correctly in first **and** third person (if it has any visual body component).
- [ ] The C# compiles clean (`Tools/verify-csharp.sh`, 0 errors, 0 new warnings).
- [ ] Its numbers live in a spec document in `Docs/plan/`, and the doc was updated if they changed.
- [ ] It degrades safely: if a subsystem it depends on is missing, it logs and continues.

## 4. What "1:1 CoD gameplay" means here (and doesn't)

**It means** the *mechanics grammar* is the same: ADS-based gunplay with hipfire spread, sprint
with a sprint-out penalty, tactical sprint as a committed speed burst, slide, mantle, two primary
slots with a gunsmith, perks, lethal/tactical equipment, field upgrades, streak rewards, the
standard mode rotation, per-map spawn logic, killcams, and a tabbed front-end hub.

**It does not mean** copying any company's content. Our weapons, operators, maps, streaks, UI art
and audio are original creations that follow genre conventions. Where this plan cites a
genre-standard *number* (e.g. "automatics usually sit around 300 ms TTK"), that's a design datum
we validate and tune ourselves, not an asset we import.

## 5. Originality & IP rules (hard constraints)

These are non-negotiable and apply to every asset, string, and texture we create:

1. **No third-party game assets.** No models, textures, animations, sounds, fonts, icons, or UI
   art extracted from, or traced from, any commercial game.
2. **No trademarked names.** Our operators, weapons, streaks, maps, perks and modes get original
   names. Real-world firearm *designations* are avoided in favour of in-fiction names
   (e.g. `SAGA-7`, `N4 Carbine`, `P6 Vector`) — the project already follows this convention.
3. **No logos or wordmarks** from any existing franchise, anywhere — including placeholder art.
4. **Reference is for study only.** Images under `Reference/ui-research/` are gitignored and used
   to extract *principles* (spacing, hierarchy, contrast), never pixels.
5. **The `Three-FPS` clone is a feature spec.** It lives in gitignored `Reference/` and is read to
   enumerate features. We write our own C#.
6. **Generated art is ours.** Models produced by our procedural mesh pipeline
   (`23-ASSET-PIPELINE-MODELS.md`) are original geometry authored by our own code.

## 6. Scope guardrails

**In scope for v1.0:** core movement, gunplay, gunsmith, loadouts, perks, equipment, 5 modes,
3 maps, bots, LAN + dedicated multiplayer, full front-end + HUD, progression, killcam.

**Explicitly deferred past v1.0:** vehicles, battle-royale systems (gas circle, loot, buy
stations, gulag), theatre mode, campaign, cross-play matchmaking service, anti-cheat.
These are designed for (the architecture must not preclude them) but not built.

## 7. The project's north star test

> A new player opens the game, sees a front-end that looks like a real product, picks a loadout,
> presses PLAY, joins a LAN match against a mix of humans and bots, and cannot immediately tell
> which is which. They slide around a corner, tac sprint down a lane, mantle a crate, and win a
> gunfight. Nothing in that sequence looks or feels like a prototype.

Everything in this plan is in service of that sentence.
