# Build progress

## Verification method (important)
There is no `dotnet` CLI on this machine, but Unity ships a working Roslyn compiler. Use:

```bash
Tools/verify-csharp.sh                 # compiles all game code against Unity's refs
Tools/verify-csharp.sh path/To.cs ...  # compiles specific files
```

It compiles `Assets/Scripts/**` (excluding Editor folders) against the installed Unity
`6000.6.3f1` reference assemblies plus every prebuilt assembly in `Library/ScriptAssemblies`,
without opening the Editor. **Current state: 0 errors across all 115 source files.**

Editor-only code (`Assets/Scripts/**/Editor/*.cs`) additionally needs the UnityEditor reference
set; the in-Editor compile covers it.

---

## Done

### Phase 0 — groundwork
- Reference repo cloned to `~/Documents/dev/Three-FPS`, deps installed, `npm run typecheck` passes.
- `Docs/THREE_FPS_FEATURE_MAP.md` — verified inventory from the real `src/` tree (README is stale).
- `Docs/research/*` — research-first notes for scorestreaks, gunsmith, loadouts, game modes,
  warzone, movement, UI (primary sources: Activision MW2019 guides, Infinity Ward UI artist portfolio).
- `Docs/MCP_SETUP.md` — Unity MCP + Chrome MCP setup; `mcp-chrome-bridge` installed user-local.
- `Tools/verify-csharp.sh` — fast C# compile check.

### Phase 1 (in progress) — input + character authority

#### First build increment — deterministic perspective weapon posing
- `WeaponMovementPose` now applies third-person sprint/tac-sprint offsets from a cached authored rest pose in local space instead of accumulating world rotation and position every frame.
- Disabling/despawning the pose component restores the original third-person weapon transform.
- This removes a high-impact source of apparent weapon switching/detachment during tactical sprint and keeps first- and third-person presentation reversible.
| File | What it is |
|---|---|
| `Assets/Scripts/Input/IInputSource.cs` | `IInputSource` + `InputActionId` — the only vocabulary gameplay reads |
| `Assets/Scripts/Input/PlayerInputSource.cs` | Input System implementation: keyboard + mouse + gamepad, rebindable `InputMap` persisted in PlayerPrefs |
| `Assets/Scripts/Input/BotInputSource.cs` | Bot implementation of the same contract — the seam that makes bots indistinguishable |
| `Assets/Scripts/Character/CharacterState.cs` | THE authority: 5 orthogonal channels, transition tables, cross-channel interaction rules, bounded accept/rejection logs, derived helpers |
| `Assets/Scripts/Character/Editor/CharacterStateVerifier.cs` | `COD / Verify Character State` — fails if a channel's transition table is incomplete |
| `Assets/Scripts/Player/move/CharacterMove.cs` | now owns a `CharacterState` and reports every move-state change + grounded fact through it |

### Data layers (researched, 1:1 numbers)
| File | What it is |
|---|---|
| `Assets/Scripts/Match/GameModeDefinition.cs` | FFA (8p/30/10min/top-3) + TDM (6v6/75/10min), custom-override clamping |
| `Assets/Scripts/Match/ScorestreakDefinition.cs` | all 20 MW2019 streaks with real kill costs, categories, piloted/hidden/large-map flags, Hardline cost |
| `Assets/Scripts/Weapons/Gunsmith/WeaponStats.cs` | 6-axis stats, `AttachmentSlot` (8 areas + weapon perk), `AttachmentDefinition`, 5/8 slot rules |

---

## Next
1. **Movement:** tac sprint, slide, stamina, head bob, surface footsteps, vault/mantle — all routed
   through `CharacterState`; replace `Input_Handler`'s legacy reads with `IInputSource`.
2. **Gunplay:** `WeaponProfile` ScriptableObject, fire modes, recoil pattern assets, ballistic
   falloff, projectiles, casings, variable scope, sway.
3. **Match:** `MatchSystem` on the server + scoreboard + spawn selection; verify LAN with 2 clients.
4. **UI:** `ScreenFramework` + redesigned hub menu + full HUD.
5. **Maps:** level metadata schema + one arena with spawns.
6. **Unity MCP:** enable the Editor bridge (needs the Editor open).
