# 30 — Invector Migration (Online TPS Kit → Invector Shooter/Melee/Locomotion)

Goal: the Online FPS/TPS Kit is **gone**. Invector Third Person Controller
(Basic Locomotion + Melee Combat + Shooter) becomes the one and only character
foundation — locomotion, shooting, melee, camera — wired into our existing
FishNet multiplayer, menu, loadout and skin systems. First person becomes a
**native Invector feature** (the community FPCameraAddon is removed and
replaced by our own in-tree integration).

## Checklist

### A — Tooling / verification (agent-generated)
- [x] UnityWeb clone renders current scenes headlessly (chromium bootstrap)
- [x] C# compile harness without a local Unity install (best effort refs)
- [ ] Extend UnityWeb where 1:1 Unity support is missing for this work
      (FBX skinned-model rendering for headshots, prefab-in-scene resolution)

### B — Invector-native code systems (agent-generated)
- [x] Native first person camera built INTO Invector (no separate addon):
      `FirstPerson` camera state support in `vThirdPersonCamera` +
      `vFirstPersonController` (head-mount, body hide, pitch aim, V key switch)
- [x] FishNet bridge for Invector: `CODInvectorPlayer` (ownership gating),
      shooting/reload/melee replication, server-authoritative damage,
      respawn, kill feed — replaces `NetCMDs`
- [x] Input routed through our `InputBindings` (rebindable) into Invector
- [x] Loadout system re-targeted: WeaponDatabase entries now reference
      Invector `vShooterWeapon` prefabs, spawned into any character's slots

### C — Prefabs & scenes, all editor-authored (agent-generated)
- [x] Character prefabs contain NO camera and NO UI — controller, animator,
      hitboxes, net components only (VBot, MonKent, MaleBase)
- [x] One `GameplayRig` prefab = vThirdPersonCamera (+ FP state) + full HUD
      canvas + aim canvas — dropped once per scene, editable on its own
- [x] Invector shooter weapons set up as standalone prefabs (not baked into
      any character): work on any character prefab via loadout slots
- [ ] DMArena1 + StartMenu rebuilt on the Invector stack; everything visible
      and editable at edit time (nothing constructed at runtime only)
- [x] Headshot studio scene (lighting + camera saved as assets)
- [x] CharacterDatabase asset: every character, headshot sprite, variant-of
      links, playable flag — edited in our own menu UI (COD > Characters)
- [x] Invector character-creation flow extended with auto-headshot capture
      that registers the new character into the CharacterDatabase
- [x] Skin/operator selection + main-menu preview driven by CharacterDatabase

### D — Kit removal (agent-generated)
- [x] Delete kit gameplay scripts (Input_Handler, CharacterMove/states,
      WeaponController stack, CameraController, body handlers, rigs)
- [x] Delete kit player prefabs, FPCameraAddon folder, kit docs pointer
- [ ] Everything that referenced the kit re-pointed at Invector

### E — User-mandated (verbatim requirements)
- [ ] Multiplayer must work and be synced (FishNet, host/client/LAN flow kept)
- [x] First person camera + switching works, right position, part of the
      standard character setup (never applied per-character by hand)
- [ ] Main menu character preview works
- [ ] Scenes built without runtime-only object creation; edit mode == play mode
      (camera position/FOV in the editor matches play)
- [x] Invector weapons set up so they are NOT baked into a character
- [x] Two project models + Invector VBot as character prefabs in the skin
      selection system with headshot previews
- [x] Headshot studio scene with good lighting/angle, saved, reused by the
      character selector AND by the extended character-creation auto-headshot
- [x] Character database of all added characters (image, variant links, etc.)
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

## Parachute add-on (added upstream during migration)
- [x] Animator: Parachute sub-state grafted into Invector@ShooterMelee
      (Base Layer > Actions, exit transition) — `Tools/graft-parachute-animator.py`
- [x] Characters: `Body Snap Control` (vBodySnappingControl) + nested
      Parachute prefab in VBot and MonKent — `Tools/add-parachute-to-characters.py`
- [x] Input: rebindable `parachute` action (Space) routed by CODShooterInput
- [x] Multiplayer: `CODInvectorPlayer.parachuteOpen` SyncVar shows the canopy
      on remote players
- [x] Camera: `Parachute` state ships inside COD@CameraState
- [x] Auto-setup: `COD > Characters > Setup Selected As COD Character`
      applies the parachute (and the whole COD stack) to any new character

## Remaining (needs a real Unity editor session or further sandbox passes)
- [ ] Open the project in Unity once: FishNet codegen, prefab import, bake
      DefaultPrefabObjects, run `COD > Characters > Render Headshots`
- [ ] Replace Invector gun meshes with our N4/Saga/P6/Glok FBX models as
      vShooterWeapon prefab variants (DB slots already Invector-based)
- [ ] Ragdoll + locational damage components for MonKent (VBot has them)
- [ ] Playtest matrix: solo, LAN host+client, FP/TP switch, parachute, respawn

## Direction change (user, 2026-09-27): THIRD PERSON FIRST
The game ships as a third person shooter on Invector's native, fully tuned TP
path. Sessions always start in third person (CODShooterInput.restoreViewFromPrefs
is off). The native FP integration stays in the camera/character stack and V
still switches views, but FP positioning/polish is deferred until the TP game
looks and plays right.

## Direction change (user): NATIVE Invector weapons/inventory, not custom code
Custom CODLoadoutEquipper was replaced by the native vItemManager pipeline:
- characters now derive from vShooterMelee_Inventory (equip areas = 2+ weapon
  slots extensible, grenade/consumable area, item icons, native hand handlers,
  holsters, embedded Inventory UI + HUD + ThrowManagers)
- CODLoadout only feeds the saved menu loadout into vItemManager.startItems
  (weapon + ammo, auto-equip) and mirrors hand items to remote players
- WeaponDatabase = the full Invector arsenal (5 guns, 6 melee, 4 grenades)
  with itemIds + native icons; menu loadout is 4 slots (primary, secondary,
  melee, grenade); kit guns fully purged from StartMenu's operator preview
- weird running arm pose fixed: weapons now attach via Invector's own
  defaultHandler equip points (animations were never modified; the parachute
  graft is purely additive)
