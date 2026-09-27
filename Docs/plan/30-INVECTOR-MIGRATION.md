# 30 — Invector Migration (Online TPS Kit → Invector Shooter/Melee/Locomotion)

Goal: the Online FPS/TPS Kit is **gone**. Invector Third Person Controller
(Basic Locomotion + Melee Combat + Shooter) becomes the one and only character
foundation — locomotion, shooting, melee, camera — wired into our existing
FishNet multiplayer, menu, loadout and skin systems. First person becomes a
**native Invector feature** (the community FPCameraAddon is removed and
replaced by our own in-tree integration).

## Checklist

### A — Tooling / verification (agent-generated)
- [ ] UnityWeb clone renders current scenes headlessly (chromium bootstrap)
- [ ] C# compile harness without a local Unity install (best effort refs)
- [ ] Extend UnityWeb where 1:1 Unity support is missing for this work
      (FBX skinned-model rendering for headshots, prefab-in-scene resolution)

### B — Invector-native code systems (agent-generated)
- [ ] Native first person camera built INTO Invector (no separate addon):
      `FirstPerson` camera state support in `vThirdPersonCamera` +
      `vFirstPersonController` (head-mount, body hide, pitch aim, V key switch)
- [ ] FishNet bridge for Invector: `CODInvectorPlayer` (ownership gating),
      shooting/reload/melee replication, server-authoritative damage,
      respawn, kill feed — replaces `NetCMDs`
- [ ] Input routed through our `InputBindings` (rebindable) into Invector
- [ ] Loadout system re-targeted: WeaponDatabase entries now reference
      Invector `vShooterWeapon` prefabs, spawned into any character's slots

### C — Prefabs & scenes, all editor-authored (agent-generated)
- [ ] Character prefabs contain NO camera and NO UI — controller, animator,
      hitboxes, net components only (VBot, MonKent, MaleBase)
- [ ] One `GameplayRig` prefab = vThirdPersonCamera (+ FP state) + full HUD
      canvas + aim canvas — dropped once per scene, editable on its own
- [ ] Invector shooter weapons set up as standalone prefabs (not baked into
      any character): work on any character prefab via loadout slots
- [ ] DMArena1 + StartMenu rebuilt on the Invector stack; everything visible
      and editable at edit time (nothing constructed at runtime only)
- [ ] Headshot studio scene (lighting + camera saved as assets)
- [ ] CharacterDatabase asset: every character, headshot sprite, variant-of
      links, playable flag — edited in our own menu UI (COD > Characters)
- [ ] Invector character-creation flow extended with auto-headshot capture
      that registers the new character into the CharacterDatabase
- [ ] Skin/operator selection + main-menu preview driven by CharacterDatabase

### D — Kit removal (agent-generated)
- [ ] Delete kit gameplay scripts (Input_Handler, CharacterMove/states,
      WeaponController stack, CameraController, body handlers, rigs)
- [ ] Delete kit player prefabs, FPCameraAddon folder, kit docs pointer
- [ ] Everything that referenced the kit re-pointed at Invector

### E — User-mandated (verbatim requirements)
- [ ] Multiplayer must work and be synced (FishNet, host/client/LAN flow kept)
- [ ] First person camera + switching works, right position, part of the
      standard character setup (never applied per-character by hand)
- [ ] Main menu character preview works
- [ ] Scenes built without runtime-only object creation; edit mode == play mode
      (camera position/FOV in the editor matches play)
- [ ] Invector weapons set up so they are NOT baked into a character
- [ ] Two project models + Invector VBot as character prefabs in the skin
      selection system with headshot previews
- [ ] Headshot studio scene with good lighting/angle, saved, reused by the
      character selector AND by the extended character-creation auto-headshot
- [ ] Character database of all added characters (image, variant links, etc.)
      editable in our custom menu, saved as assets, working at runtime

## Verification method
- `Tools/verify-csharp.sh` (rebuilt for this sandbox: npm-sourced Roslyn +
  GitHub-sourced UnityEngine refs + stub assemblies for URP/TMP/Input System)
- `UnityWeb` headless screenshots for every scene/prefab claim
- FishNet flows reviewed against FishNet 4 API (source lives in-repo)

## Notes / decisions
- `Male_Body_BaseMesh.fbx` is an unrigged static mesh (0 deformers — verified
  with `Tools/fbx-animation-report.mjs`). It IS registered in the database +
  has a prefab + headshot, but is flagged `playable = false` until rigged.
- MonKent.fbx is fully rigged (65 nodes, 46 takes) → imported as Humanoid so
  Invector's animator controller retargets to it.
- The FishNet networking layer (CODNetworkManager / discovery / lobby) is kit
  agnostic and survives as-is; only the player-facing half was rebuilt.
