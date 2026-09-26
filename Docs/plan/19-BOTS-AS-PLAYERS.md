# 19 — Bots as Networked Players

> User requirement: *"bots that are networked players and have no difference from real player
> features and have like real player inputs and smart — only the script controlling the character,
> using any scripts a normal user input will go through. So when I get a better AI agent it can now
> make it smarter."*

This is the correct architecture, and it is the one we build.

## 1. The core rule

```
A bot is a NetworkPlayer whose IInputSource happens to be an algorithm.
```

Concretely:
- Same player prefab. Same `NetworkObject`. Same `CharacterMove`, `WeaponController`,
  `PlayerHealth`, `PlayerLoadout`, `CharacterState`.
- Same spawn path, same loadout system, same damage model, same killfeed, same scoreboard.
- The **only** difference is which `IInputSource` implementation is attached.

If a bot can do something a player can't (or vice versa), that is a bug, not a feature.

## 2. Why this matters beyond "nice architecture"

1. **Bots are automatically correct.** Anything added to the character — slide, mantle, gunsmith,
   perks, streaks — is available to bots with zero AI work.
2. **Bots become the test harness.** 12 bots playing for 10 minutes exercises every system in the
   game, headlessly, in CI.
3. **Swappable brains.** `AgentController` is a pure function
   `(perception, state) → InputFrame`. Replacing it with something smarter later touches nothing
   else. That is exactly the user's stated goal.
4. **Honest difficulty.** Difficulty is expressed as *human-shaped limitations* (reaction time,
   aim error, awareness) rather than cheats (wall-hacks, damage multipliers).

## 3. Layer structure

```
            ┌──────────────── Perception ────────────────┐
            │ FOV cone · LOS raycasts · audio cues ·      │
            │ memory (last known pos + decay) · threat map│
            └───────────────────┬────────────────────────┘
                                ▼
            ┌──────────── Planner (utility) ─────────────┐
            │ NeedsModel scores goals:                    │
            │  engage · reposition · take cover · reload  │
            │  push objective · retreat · resupply · hunt │
            └───────────────────┬────────────────────────┘
                                ▼
            ┌──────────── Capabilities ──────────────────┐
            │ MoveTo · StrafeFight · TakeCover · Peek ·   │
            │ Reload · SwitchWeapon · ThrowEquipment ·    │
            │ Mantle · Slide · CallStreak                 │
            └───────────────────┬────────────────────────┘
                                ▼
            ┌──────── AgentController → InputFrame ───────┐
            │ converts the chosen action into MOVE/LOOK/  │
            │ BUTTON values — with human limits applied   │
            └────────────────────┬───────────────────────┘
                                 ▼
                          BotInputSource
                                 ▼
                    the identical character stack
```

## 4. Perception (no cheating)

A bot may only know what a player in its position could know:

| Sense | Model |
|---|---|
| Vision | 100° horizontal FOV cone (matching a player's screen), LOS raycast to 5 body points, range falloff for recognition |
| Recognition delay | 80–400 ms depending on difficulty, target size on screen, and whether the target is moving |
| Hearing | Footsteps (radius by state, doc 09 §12), gunfire, reloads, equipment — direction with error |
| Memory | Last known position, decaying confidence over 6 s; bots search where you *were* |
| Team knowledge | `SquadBlackboard` — teammates share contacts, exactly like callouts would |
| Minimap/recon | Only what the HUD would show that player |

**Forbidden:** reading transforms of unseen enemies, perfect aim, knowing health, knowing loadouts.

## 5. Aiming like a person

The single biggest tell of a fake bot is inhuman aim. The model:

```
1. target point  = chosen body part (weighted by difficulty: legs/torso/head)
2. add aim error = gaussian, σ scales with difficulty, distance, target speed, own movement
3. add lead      = imperfect prediction of target velocity (error scales with difficulty)
4. slew the aim  = turn toward the target at a limited angular rate with acceleration,
                   NOT a snap — this is what makes it look human
5. add micro-jitter and a slow drift so the reticle is never perfectly still
6. reaction gate = do not begin (4) until recognitionDelay has elapsed
7. trigger       = fire in bursts appropriate to the weapon and range, with
                   inter-burst pauses; stop to reload/reposition like a player
```

Aim slew rate and error are **per-difficulty**, and they are the same limits a controller player
faces — which is why bots at high difficulty feel tough but fair.

## 6. `BotProfile` — personality

```
BotProfile
  name, operatorId, preferredLoadoutTags
  aggression        0..1   push vs hold
  patience          0..1   willingness to hold an angle
  mobility          0..1   how much they slide/mantle/tac-sprint
  teamwork          0..1   how much they weight the blackboard
  preferredRange    metres
  accuracyBase      σ at 20 m
  reactionMs        base recognition delay
  trigger           burstLength, burstPauseMs
  awareness         how often they check flanks
```

Profiles are assets. A match rolls a varied set so bots don't feel like clones. Bot **names** come
from an original name pool and are indistinguishable from player names in the killfeed.

## 7. Difficulty

| Tier | reactionMs | accuracy σ @20 m | slew rate | awareness |
|---|---|---|---|---|
| Recruit | 420 | 4.5° | 90°/s | low |
| Regular | 300 | 2.6° | 160°/s | medium |
| Hardened | 210 | 1.5° | 240°/s | high |
| Veteran | 150 | 0.9° | 320°/s | very high |
| Elite | 110 | 0.6° | 400°/s | full blackboard |

Difficulty modifies the **profile**, never the game rules. An Elite bot takes the same damage,
moves at the same speed, and has the same weapons as a Recruit.

## 8. Navigation

- Unity NavMesh for pathing, with **jump links and mantle hints** authored per map (doc 17 §5) so
  bots use the same traversal players do.
- Local steering + avoidance produces a `moveDirection`, which is written into `InputFrame.Move` —
  **bots do not set velocity or position directly.** They press the stick.
- Path smoothing and corner-cutting so movement doesn't look robotic.
- Bots use slide and tac sprint when the situation matches (long open rotation → tac sprint;
  entering a contested doorway → slide), governed by `mobility`.

## 9. Networking

- Bots are spawned **on the server only** and are full `NetworkObject`s.
- Clients cannot tell them apart: same prefab, same replication, same animation sync.
- Server cost budget: 12 bots must fit in < 2 ms/tick. Perception raycasts are **budgeted and
  round-robined** across ticks rather than all running every tick.
- Bots fill and vacate slots as humans join/leave, with a configurable target player count.

## 10. Bot match director

An optional server-side director keeps matches feeling good: keeps score within a band by adjusting
*bot aggression and difficulty within limits* — never by changing damage. Off by default in
competitive modes, on in casual/solo.

## 11. The extensibility contract (explicit, for future AI work)

```csharp
public interface IAgentBrain {
    // Everything a brain may see. Deliberately narrow.
    InputFrame Think(in AgentPerception perception, in AgentSelfState self, float dt);
}
```
`AgentController` implements `IAgentBrain`. To upgrade bots later, write a new `IAgentBrain` and
register it in `BotProfile.brainId`. **No other file changes.** The perception struct is the
documented, stable surface — this is the "stepping stone" the user asked for.

## 12. Acceptance (P8)

- [ ] A 12-bot match runs 10 minutes unattended with no errors and a plausible scoreboard.
- [ ] Given a killfeed and a scoreboard, a human cannot reliably identify which players are bots.
- [ ] Bots use slide, mantle, tac sprint, equipment, and streaks.
- [ ] Bots never access information a player couldn't (audited by a perception lint).
- [ ] Difficulty changes only the profile, verified by a test asserting rule parity.
- [ ] Server cost < 2 ms/tick for 12 bots.
- [ ] Replacing `IAgentBrain` with a stub that returns empty input produces bots that stand still —
      proving the seam is clean.
