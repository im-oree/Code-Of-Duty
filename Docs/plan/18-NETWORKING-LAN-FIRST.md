# 18 — Networking (LAN First, Everything Else Second)

> User requirement: *"can support LAN and our FishNet networking system. LAN is first, external is
> second, but networking should be developed in mind for all."*

## 1. The four deployments, one code path

| Deployment | How it starts | Used for |
|---|---|---|
| **Internal host** | Client starts server+client in-process, loopback | Solo play, offline testing, bot matches |
| **LAN host** | Same as above + UDP broadcast advertisement | The primary multiplayer case |
| **LAN client** | Discovery → direct connect | Joining a LAN host |
| **Dedicated server** | Headless build, `-batchmode -server` | Future online play |

**There is exactly one server implementation.** The only difference between these is the bind
address, whether a local client attaches, and whether discovery broadcasts. Any code that asks
"am I in solo mode?" is a bug.

This is already largely true in the project (`CODNetworkManager`) and is its best feature. We keep
the design and harden it.

## 2. Transport & stack

- **FishNet** with the **Tugboat** (UDP/LiteNetLib) transport.
- Channels: `Reliable Ordered` for events/RPCs, `Unreliable Sequenced` for snapshots/input.
- MTU-aware snapshot packing; never rely on fragmentation for per-tick data.
- `Multipass` transport is the upgrade path if we later want simultaneous UDP + WebSocket
  (browser clients) — the architecture doesn't preclude it.

## 3. LAN discovery

Already implemented (`CODNetworkDiscovery`) as transport-agnostic raw UDP broadcast. Hardening
work:

- Broadcast on **all** interfaces, not just the default (multi-NIC machines are common).
- Include in the beacon: room name, map, mode, player count/max, version hash, passworded flag,
  build platform.
- **Version hash gate** — a client with a mismatched protocol version sees the server greyed out
  with "version mismatch", instead of connecting and desyncing.
- Respond to directed probes as well as broadcasts (some networks block broadcast).
- Fall back to **mDNS/Bonjour-style multicast** if broadcast yields nothing after 3 s.
- Discovery beacons every 1 s; entries expire after 4 s.

## 4. Authority model

| Decision | Owner |
|---|---|
| Movement position/velocity/state | **Server** (simulated from input) |
| Weapon fire, hit detection, damage, death | **Server** |
| Match state, score, objectives, spawns | **Server** |
| Streaks, equipment, field upgrades | **Server** |
| Loadout validity | **Server** |
| Camera, animation, VFX, audio, HUD, shake | Client |
| Input | Client → Server |

**Clients send input, not results.** A client never says "I killed X"; it says "at tick N my aim
was θ and I pulled the trigger."

## 5. Movement: prediction & reconciliation

```
CLIENT                                      SERVER
  sample InputFrame(tick N)
  apply locally (predict)  ──────────────►  buffer input for tick N
  store (tick N, resulting state)           simulate tick N authoritatively
                           ◄──────────────  snapshot { tick N, state }
  compare predicted[N] vs authoritative
  if error > 0.02 m:
      rewind to authoritative state
      re-simulate inputs N+1..current
      smooth the visual correction over 0.20 s (never snap the camera)
```

- Input buffer on the server absorbs jitter (target 2 frames, adaptive).
- Corrections move the **collider immediately** but the **visual mesh smoothly** — this is why a
  correction is invisible when done right.
- The camera is *never* corrected; only position is. Look is client-authoritative (it can't cheat
  anything — the server still validates shots against its own state).

## 6. Remote player representation

- Snapshot interpolation with a **100 ms** buffer (tunable; 50 ms on LAN).
- Extrapolation capped at 120 ms, then freeze + blend on resume.
- Interpolate position, torso yaw/pitch, and animation parameters — **not** discrete states, which
  are event-driven.
- Hitboxes follow the *interpolated* pose so what you see is what you shoot.

## 7. Lag compensation (fair hit registration)

The server keeps a **rewind buffer** of every player's hitbox transforms for the last 1 s (at tick
rate, ~30 entries).

```
on FireRequest(tick, aimOrigin, aimDir, weaponId):
   validate: fire rate, ammo, state (not sprinting/reloading/dead)
   rewindTime = clamp(clientRenderTime, now - maxCompensation, now)
   restore all other players' hitboxes to rewindTime
   raycast / ballistic-solve
   restore present
   apply damage
```
`maxCompensation` = 250 ms. Beyond that, no compensation (protects against lag abuse).
Compensation is **disabled** for a shooter whose reported time is implausible.

## 8. The protocol surface

One file enumerates every message, like the reference's `Protocol.ts`:

```
Assets/Scripts/Net/Protocol/NetProtocol.cs
  ProtocolVersion = <hash of the message set>
  enum MsgId { ClientInput, Snapshot, MatchState, SpawnPlayer, DespawnPlayer,
               FireRequest, FireConfirm, DamageEvent, KillEvent, LoadoutSet,
               StreakCallIn, ObjectiveState, ChatMessage, Ping, … }
```
Rules: no ad-hoc RPCs outside this enum; every message has a documented owner, direction, channel,
and expected rate; the version hash is generated from the enum + payload shapes and checked at
connect.

## 9. Bandwidth budget

Target for a 12-player LAN match:

| Stream | Rate | Size | Per client |
|---|---|---|---|
| Client input → server | 30 Hz | ~18 B | ~0.5 KB/s up |
| Snapshot → client | 20 Hz | ~40 B × 11 players | ~9 KB/s down |
| Events (fire/damage/kill) | bursty | ~16–40 B | ~1 KB/s |
| Match/objective state | 5 Hz | ~30 B | ~0.2 KB/s |
| **Total** | | | **~11 KB/s down, ~1 KB/s up** |

Techniques: quantise positions (1 cm) and angles (0.5°), delta-compress against the last acked
snapshot, bitmask changed fields, cull players outside relevance range (not on small maps), and
never send what the client can derive.

## 10. Failure handling

| Failure | Behaviour |
|---|---|
| Client timeout | 10 s grace, body remains killable for 3 s, then despawn; slot freed |
| Reconnect | Same profile id rejoins with score intact within 60 s |
| Host quits (LAN) | Match ends gracefully with a clear message; **host migration is explicitly out of scope for v1.0** and documented as such |
| Server overload | Tick rate degrades gracefully; never drops input silently |
| Version mismatch | Rejected at connect with a readable reason |
| Packet loss | Unreliable channels tolerate; reliable events retry; the sim never blocks on a late packet |

## 11. Security posture (honest scope)

LAN play trusts the local network. For the dedicated-server path we still do the cheap, correct
things from day one because retrofitting them is expensive:
- Server validates all rates, states, and geometry.
- No client-authoritative damage, position, or score, ever.
- Sanity bounds on input deltas (a 4000°/frame look delta is rejected).
- Loadout/unlock validation server-side.
- **Anti-cheat proper is out of scope for v1.0** and noted in the risk register.

## 12. Testing

| Test | Method |
|---|---|
| Convergence | Headless harness: N simulated clients, assert state agreement |
| Reconciliation error | Assert p99 prediction error < 0.15 m at 80 ms/2% |
| Bandwidth | Measured per-tick byte counts asserted against §9 |
| Lag comp fairness | Scripted duel at varying RTT; hit attribution must match the shooter's view |
| Discovery | Multi-NIC and broadcast-blocked network simulations |
| Late join / reconnect | Every match state, automated |
| Real clients | ParrelSync (vendored) — 2–4 Editor clones, run by the user |

## 13. Acceptance (P5)

- [ ] 4-client LAN TDM to completion at simulated 80 ms RTT / 2% loss with no desync.
- [ ] Prediction error within budget; corrections invisible in captured video.
- [ ] Hit registration fair in both directions in the duel test.
- [ ] Discovery finds hosts on a multi-NIC machine and rejects version mismatches.
- [ ] Bandwidth within §9.
- [ ] A dedicated-server build runs headless with `Presentation` and `UI` stripped.
