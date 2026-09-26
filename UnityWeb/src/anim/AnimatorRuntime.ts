/**
 * Playing animation on a scene's characters.
 *
 * One `AnimatorInstance` per Unity `Animator` component. It owns a three.js
 * `AnimationMixer` rooted at the Animator's GameObject, which is where the bone
 * Transforms live — the same objects the SkinnedMeshRenderers are bound to, so
 * driving them moves the mesh.
 *
 * Deliberately *not* a state-machine simulator. Transition conditions depend on
 * gameplay parameters that no script is setting here, so guessing which state
 * "should" be active would invent behaviour. Instead the controller's structure
 * is exposed and states are driven explicitly — by the default state on load,
 * or by an agent calling `play()`. That makes it a tool for examining a pose at
 * a chosen moment, which is what animation work actually needs.
 */

import * as THREE from 'three';
import type { AnimatorControllerInfo } from './AnimatorController.ts';
import type { ClipInfo } from './ClipLoader.ts';

export interface AnimatorInstanceInfo {
  /** Hierarchy path of the GameObject carrying the Animator. */
  path: string;
  name: string;
  controllerPath: string | null;
  controllerName: string | null;
  /** Short alias for the controller, whichever of name/path resolved. */
  controller: string | null;
  clips: Array<{ name: string; duration: number; tracks: number }>;
  states: string[];
  defaultState: string | null;
  playing: string | null;
  time: number;
  duration: number;
  speed: number;
  paused: boolean;
  applyRootMotion: boolean;
  /** Node whose baked translation was discarded, when root motion is off. */
  rootMotionStrippedFrom: string | null;
  /** How the default state resolved to a clip, and whether it was guessed. */
  defaultResolution: string | null;
}

export class AnimatorInstance {
  private mixer: THREE.AnimationMixer;
  private actions = new Map<string, THREE.AnimationAction>();
  private current: THREE.AnimationAction | null = null;
  private currentName: string | null = null;
  private speed = 1;
  paused = false;
  /** How the default state was resolved, guesses included. */
  defaultResolution: string | null = null;

  /** Name of the topmost animated node, if root motion had to be stripped. */
  readonly rootMotionNode: string | null = null;

  constructor(
    readonly root: THREE.Object3D,
    readonly path: string,
    readonly clips: ClipInfo[],
    readonly controller: AnimatorControllerInfo | null,
    readonly controllerPath: string | null,
    readonly applyRootMotion = false,
  ) {
    this.mixer = new THREE.AnimationMixer(root);
    if (!applyRootMotion) this.rootMotionNode = this.stripRootMotion();
  }

  /**
   * Remove baked root translation when the Animator has Apply Root Motion off.
   *
   * These FBX takes carry the character's travel on the top bone. Unity strips
   * that when root motion is disabled — the animation plays in place and the
   * GameObject stays where the scene put it. Playing the track verbatim instead
   * launched the operator 1.2 m into the air, which is not a rendering bug but
   * a missing piece of Animator semantics.
   *
   * Only the position channel goes. Root *rotation* is part of the pose (a lean
   * or a turn-in-place reads wrong without it), and Unity keeps it for the
   * common case of an Animator that is not steering the transform.
   */
  private stripRootMotion(): string | null {
    // The topmost node any track targets, by depth below the Animator.
    const depthOf = (name: string): number => {
      let node: THREE.Object3D | undefined = this.root.getObjectByName(name);
      if (!node) return Infinity;
      let d = 0;
      while (node && node !== this.root) { node = node.parent ?? undefined; d++; }
      return node === this.root ? d : Infinity;
    };

    let rootName: string | null = null;
    let best = Infinity;
    for (const info of this.clips) {
      for (const track of info.clip.tracks) {
        const name = track.name.slice(0, track.name.lastIndexOf('.'));
        const d = depthOf(name);
        if (d < best) { best = d; rootName = name; }
      }
    }
    if (!rootName) return null;

    const prefix = `${rootName}.position`;
    let removed = 0;
    for (const info of this.clips) {
      const kept = info.clip.tracks.filter((t) => t.name !== prefix);
      removed += info.clip.tracks.length - kept.length;
      info.clip.tracks = kept;
    }
    return removed > 0 ? rootName : null;
  }

  /** Clip names available to this Animator. */
  clipNames(): string[] {
    return this.clips.map((c) => c.name);
  }

  /**
   * Resolve a requested name to a clip.
   *
   * Accepts a clip name, a controller state name (mapped through its motion),
   * or a case-insensitive substring — animation names in FBX takes are rarely
   * what a controller calls them, and failing on an exact-match miss would make
   * the tool tedious to drive.
   */
  private resolveClip(nameOrState: string): ClipInfo | null {
    const want = nameOrState.toLowerCase();

    const exact = this.clips.find((c) => c.name.toLowerCase() === want);
    if (exact) return exact;

    for (const layer of this.controller?.layers ?? []) {
      const state = layer.states.find((s) => s.name.toLowerCase() === want);
      if (state?.motionName) {
        const viaMotion = this.clips.find((c) => c.name.toLowerCase() === state.motionName!.toLowerCase());
        if (viaMotion) return viaMotion;
        // Controller motions referencing FBX takes carry `file.fbx#fileID`;
        // fall back to the state name against clip names.
      }
    }

    const partial = this.clips.find((c) => c.name.toLowerCase().includes(want));
    return partial ?? null;
  }

  play(nameOrState: string, opts: { loop?: boolean; speed?: number; fade?: number } = {}): boolean {
    const clip = this.resolveClip(nameOrState);
    if (!clip) return false;

    let action = this.actions.get(clip.name);
    if (!action) {
      action = this.mixer.clipAction(clip.clip);
      this.actions.set(clip.name, action);
    }

    action.reset();
    action.setLoop(opts.loop === false ? THREE.LoopOnce : THREE.LoopRepeat, Infinity);
    action.clampWhenFinished = opts.loop === false;
    action.enabled = true;
    action.setEffectiveWeight(1);
    action.timeScale = opts.speed ?? 1;

    const fade = opts.fade ?? 0;
    if (this.current && this.current !== action && fade > 0) {
      this.current.crossFadeTo(action, fade, false);
      action.play();
    } else {
      if (this.current && this.current !== action) this.current.stop();
      action.play();
    }

    this.current = action;
    this.currentName = clip.name;
    this.speed = opts.speed ?? 1;
    // Advance zero seconds so the first pose is applied before the next frame,
    // which matters when a screenshot follows immediately.
    this.mixer.update(0);
    return true;
  }

  /**
   * Health of the clip that is currently playing.
   *
   * Every obvious signal can look correct while a character stands perfectly
   * still: the Animator resolves, the clip is found, the action runs, its time
   * advances, and every track binds to a real bone. The menu operator did all
   * of that for weeks. The clip itself was flat -- 22 keyframes of the same
   * pose -- and nothing above the file could tell.
   *
   * So this reports the two things that actually distinguish the cases:
   * whether tracks reached a node (`bound`), and whether their values ever
   * change (`varying`). A clip with `bound` high and `varying` zero is not a
   * rendering problem, it is an asset problem -- run
   * `Tools/fbx-animation-report.mjs` on the source model.
   */
  bindingReport(): {
    clip: string | null; tracks: number; bound: number; unbound: string[];
    running: boolean; weight: number; time: number;
    varying: number; constant: number; movingNodes: string[];
  } {
    const action = this.current as (THREE.AnimationAction & {
      _propertyBindings?: Array<{ binding?: { node?: unknown; path?: string } }>;
    }) | null;
    const empty = {
      clip: null, tracks: 0, bound: 0, unbound: [], running: false,
      weight: 0, time: 0, varying: 0, constant: 0, movingNodes: [],
    };
    if (!action) return empty;

    const bindings = action._propertyBindings ?? [];
    const unbound: string[] = [];
    let bound = 0;
    for (const b of bindings) {
      if (b.binding?.node) bound++;
      else if (unbound.length < 8) unbound.push(b.binding?.path ?? '<unknown>');
    }

    let varying = 0;
    let constant = 0;
    const movingNodes: string[] = [];
    for (const track of action.getClip().tracks) {
      const values = track.values;
      const size = track.getValueSize();
      let moves = false;
      for (let i = size; i < values.length && !moves; i++) {
        if (Math.abs(values[i] - values[i % size]) > 1e-4) moves = true;
      }
      if (moves) {
        varying++;
        if (movingNodes.length < 10) movingNodes.push(track.name);
      } else constant++;
    }

    return {
      clip: this.currentName,
      tracks: bindings.length,
      bound,
      unbound,
      running: action.isRunning(),
      weight: action.getEffectiveWeight(),
      time: +action.time.toFixed(4),
      varying,
      constant,
      movingNodes,
    };
  }

  stop(): void {
    this.current?.stop();
    this.current = null;
    this.currentName = null;
  }

  /** Jump to an absolute time in seconds, without advancing the clock. */
  setTime(seconds: number): void {
    if (!this.current) return;
    this.current.time = Math.max(0, seconds);
    this.mixer.update(0);
  }

  /** Jump to a normalised position in the current clip, 0..1. */
  setNormalizedTime(t: number): void {
    if (!this.current) return;
    const duration = this.current.getClip().duration;
    this.setTime(Math.max(0, Math.min(1, t)) * duration);
  }

  setSpeed(speed: number): void {
    this.speed = speed;
    if (this.current) this.current.timeScale = speed;
  }

  update(dt: number): void {
    if (!this.paused) this.mixer.update(dt);
  }

  /** Play the controller's default state, if it maps to a clip we have. */
  /**
   * Start the controller's default state.
   *
   * How this resolved is recorded rather than hidden. A guessed clip looks
   * exactly like a correct one — the character stands there breathing either
   * way — so the only way to notice the controller was misread is to say so.
   */
  playDefault(): boolean {
    const layer = this.controller?.layers[0];
    const state = layer?.defaultState
      ? layer.states.find((s) => s.name === layer.defaultState)
      : undefined;

    if (state?.motionName) {
      const clip = this.clips.find((c) => c.name === state.motionName);
      if (clip) {
        this.defaultResolution = `state "${state.name}" -> clip "${clip.name}"`;
        return this.play(clip.name, { loop: state.loop, speed: state.speed });
      }
      this.defaultResolution =
        `state "${state.name}" names motion "${state.motionName}", which is not a clip in this model`;
    } else if (layer?.defaultState) {
      this.defaultResolution = `state "${layer.defaultState}" has no resolvable motion`;
    } else {
      this.defaultResolution = 'no controller';
    }

    // Falling back is better than a T-pose, but it is a guess and is labelled.
    const idle = this.clips.find((c) => /idle|stand|rest/i.test(c.name));
    const pick = idle ?? this.clips[0];
    if (!pick) return false;
    this.defaultResolution += ` — guessed "${pick.name}"`;
    console.warn(`[animator] ${this.path}: ${this.defaultResolution}`);
    return this.play(pick.name, { loop: true });
  }

  info(): AnimatorInstanceInfo {
    const states = (this.controller?.layers ?? []).flatMap((l) => l.states.map((s) => s.name));
    return {
      path: this.path,
      name: this.root.name,
      controllerPath: this.controllerPath,
      controllerName: this.controller?.name ?? null,
      controller: this.controller?.name ?? this.controllerPath ?? null,
      clips: this.clips.map((c) => ({
        name: c.name,
        duration: +c.duration.toFixed(4),
        tracks: c.trackCount,
      })),
      states,
      defaultState: this.controller?.layers[0]?.defaultState ?? null,
      playing: this.currentName,
      time: +(this.current?.time ?? 0).toFixed(4),
      duration: +(this.current?.getClip().duration ?? 0).toFixed(4),
      speed: this.speed,
      paused: this.paused,
      applyRootMotion: this.applyRootMotion,
      rootMotionStrippedFrom: this.rootMotionNode,
      defaultResolution: this.defaultResolution,
    };
  }
}

/** All Animators in the loaded scene, addressable by name or path. */
export class AnimatorSet {
  private instances: AnimatorInstance[] = [];

  add(instance: AnimatorInstance): void {
    this.instances.push(instance);
  }

  clear(): void {
    this.instances = [];
  }

  all(): AnimatorInstance[] {
    return this.instances;
  }

  /** Find by exact path, then by object name, then by substring. */
  find(selector?: string | null): AnimatorInstance | null {
    if (!selector) return this.instances[0] ?? null;
    const want = selector.toLowerCase();
    return this.instances.find((a) => a.path.toLowerCase() === want)
      ?? this.instances.find((a) => a.root.name.toLowerCase() === want)
      ?? this.instances.find((a) => a.path.toLowerCase().includes(want))
      ?? null;
  }

  update(dt: number): void {
    for (const a of this.instances) a.update(dt);
  }

  info(): AnimatorInstanceInfo[] {
    return this.instances.map((a) => a.info());
  }
}
