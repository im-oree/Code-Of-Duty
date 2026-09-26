/**
 * Reading Unity `.controller` assets.
 *
 * An AnimatorController is a YAML file of linked objects:
 *
 *   91   AnimatorController         parameters, layers
 *   1107 AnimatorStateMachine       states, default state, any-state transitions
 *   1102 AnimatorState              motion, speed, cycle offset, mirror, loop
 *   1101 AnimatorStateTransition    conditions, duration, exit time
 *   206  BlendTree                  1D/2D blending over child motions
 *
 * We read the structure faithfully — states, transitions, conditions,
 * parameters and blend trees — because the *shape* of a controller is often
 * exactly what you need to see when an animation is not playing. Evaluating
 * transition logic at runtime is a separate concern handled by AnimatorRuntime,
 * which currently drives states directly rather than simulating conditions.
 */

import { assets } from '../unity/AssetDatabase.ts';
import { arrayOf, asRef, mapOf, num, str, type UnityDocument, type UnityFile } from '../unity/YamlParser.ts';

export type ParameterType = 'float' | 'int' | 'bool' | 'trigger';

export interface AnimatorParameter {
  name: string;
  type: ParameterType;
  defaultFloat: number;
  defaultInt: number;
  defaultBool: boolean;
}

export interface TransitionCondition {
  /** 1 = If, 2 = IfNot, 3 = Greater, 4 = Less, 6 = Equals, 7 = NotEqual. */
  mode: number;
  parameter: string;
  threshold: number;
}

export interface AnimatorTransition {
  destination: string | null;
  conditions: TransitionCondition[];
  hasExitTime: boolean;
  exitTime: number;
  duration: number;
  offset: number;
  isExit: boolean;
}

export interface BlendTreeChild {
  motionName: string | null;
  threshold: number;
  positionX: number;
  positionY: number;
  timeScale: number;
}

export interface BlendTreeInfo {
  name: string;
  /** 0 = Simple1D, 1 = FreeformDirectional2D, 2 = FreeformCartesian2D, 4 = Direct. */
  blendType: number;
  parameterX: string;
  parameterY: string;
  children: BlendTreeChild[];
}

export interface AnimatorStateInfo {
  name: string;
  /** Clip name when the motion is a clip, else null. */
  motionName: string | null;
  /** Populated when the motion is a BlendTree rather than a clip. */
  blendTree: BlendTreeInfo | null;
  speed: number;
  cycleOffset: number;
  mirror: boolean;
  /** Unity's field is `m_Loop`; absent means the clip's own wrap mode wins. */
  loop: boolean;
  transitions: AnimatorTransition[];
  tag: string;
}

export interface AnimatorLayerInfo {
  name: string;
  defaultWeight: number;
  states: AnimatorStateInfo[];
  defaultState: string | null;
  anyStateTransitions: AnimatorTransition[];
}

export interface AnimatorControllerInfo {
  path: string;
  name: string;
  parameters: AnimatorParameter[];
  layers: AnimatorLayerInfo[];
  /** Clip names referenced anywhere in the controller. */
  referencedClips: string[];
}

const controllerCache = new Map<string, AnimatorControllerInfo | null>();

const PARAM_TYPES: Record<number, ParameterType> = {
  1: 'float', 3: 'int', 4: 'bool', 9: 'trigger',
};

/** Resolve a `{fileID}` inside this file, or a clip name from an external ref. */
function motionNameOf(file: UnityFile, value: unknown): string | null {
  const ref = asRef(value as never);
  if (!ref) return null;

  // Internal: a BlendTree lives in the same file.
  const local = file.byFileID.get(ref.fileID);
  if (local) return str(local.body.m_Name, '') || null;

  // External: a clip inside an FBX. The GUID names the model; the fileID names
  // the take. We cannot resolve the take without the model loaded, so record
  // the path and let the runtime match by index or name.
  if (ref.guid) {
    const p = assets.pathForGuid(ref.guid);
    if (p) return `${p.split('/').pop()}#${ref.fileID}`;
  }
  return null;
}

function readConditions(doc: UnityDocument): TransitionCondition[] {
  return arrayOf(doc.body.m_Conditions).map((c) => {
    const m = mapOf(c);
    return {
      mode: num(m?.m_ConditionMode, 0),
      parameter: str(m?.m_ConditionEvent, ''),
      threshold: num(m?.m_EventTreshold, 0), // Unity's own spelling
    };
  });
}

function readTransition(file: UnityFile, doc: UnityDocument): AnimatorTransition {
  const dst = asRef(doc.body.m_DstState);
  const dstDoc = dst ? file.byFileID.get(dst.fileID) : undefined;
  return {
    destination: dstDoc ? str(dstDoc.body.m_Name, '') : null,
    conditions: readConditions(doc),
    hasExitTime: num(doc.body.m_HasExitTime, 0) !== 0,
    exitTime: num(doc.body.m_ExitTime, 0),
    duration: num(doc.body.m_TransitionDuration, 0),
    offset: num(doc.body.m_TransitionOffset, 0),
    isExit: num(doc.body.m_IsExit, 0) !== 0,
  };
}

function readBlendTree(file: UnityFile, doc: UnityDocument): BlendTreeInfo {
  const children: BlendTreeChild[] = arrayOf(doc.body.m_Childs).map((c) => {
    const m = mapOf(c);
    const pos = mapOf(m?.m_Position);
    return {
      motionName: motionNameOf(file, m?.m_Motion),
      threshold: num(m?.m_Threshold, 0),
      positionX: num(pos?.x, 0),
      positionY: num(pos?.y, 0),
      timeScale: num(m?.m_TimeScale, 1),
    };
  });
  return {
    name: str(doc.body.m_Name, 'Blend Tree'),
    blendType: num(doc.body.m_BlendType, 0),
    parameterX: str(doc.body.m_BlendParameter, ''),
    parameterY: str(doc.body.m_BlendParameterY, ''),
    children,
  };
}

function readState(file: UnityFile, doc: UnityDocument): AnimatorStateInfo {
  const motionRef = asRef(doc.body.m_Motion);
  const motionDoc = motionRef ? file.byFileID.get(motionRef.fileID) : undefined;
  const isBlendTree = motionDoc?.classId === 206;

  return {
    name: str(doc.body.m_Name, 'State'),
    motionName: isBlendTree ? null : motionNameOf(file, doc.body.m_Motion),
    blendTree: isBlendTree && motionDoc ? readBlendTree(file, motionDoc) : null,
    speed: num(doc.body.m_Speed, 1),
    cycleOffset: num(doc.body.m_CycleOffset, 0),
    mirror: num(doc.body.m_Mirror, 0) !== 0,
    loop: num(doc.body.m_Loop, 1) !== 0,
    tag: str(doc.body.m_Tag, ''),
    transitions: arrayOf(doc.body.m_Transitions)
      .map((t) => asRef(t))
      .map((r) => (r ? file.byFileID.get(r.fileID) : undefined))
      .filter((d): d is UnityDocument => !!d)
      .map((d) => readTransition(file, d)),
  };
}

/** Parse an AnimatorController asset. Cached per path. */
export async function loadController(path: string): Promise<AnimatorControllerInfo | null> {
  if (controllerCache.has(path)) return controllerCache.get(path) ?? null;

  let info: AnimatorControllerInfo | null = null;
  try {
    const file = await assets.readYaml(path);
    const controllerDoc = file.documents.find((d) => d.classId === 91);
    if (controllerDoc) {
      const parameters: AnimatorParameter[] = arrayOf(controllerDoc.body.m_AnimatorParameters)
        .map((p) => {
          const m = mapOf(p);
          return {
            name: str(m?.m_Name, ''),
            type: PARAM_TYPES[num(m?.m_Type, 1)] ?? 'float',
            defaultFloat: num(m?.m_DefaultFloat, 0),
            defaultInt: num(m?.m_DefaultInt, 0),
            defaultBool: num(m?.m_DefaultBool, 0) !== 0,
          };
        })
        .filter((p) => p.name);

      const layers: AnimatorLayerInfo[] = [];
      for (const layerValue of arrayOf(controllerDoc.body.m_AnimatorLayers)) {
        const layerMap = mapOf(layerValue);
        if (!layerMap) continue;
        const smRef = asRef(layerMap.m_StateMachine);
        const sm = smRef ? file.byFileID.get(smRef.fileID) : undefined;

        const states: AnimatorStateInfo[] = [];
        let defaultState: string | null = null;
        const anyState: AnimatorTransition[] = [];

        if (sm) {
          for (const childValue of arrayOf(sm.body.m_ChildStates)) {
            const childMap = mapOf(childValue);
            const stateRef = asRef(childMap?.m_State);
            const stateDoc = stateRef ? file.byFileID.get(stateRef.fileID) : undefined;
            if (stateDoc) states.push(readState(file, stateDoc));
          }
          const defRef = asRef(sm.body.m_DefaultState);
          const defDoc = defRef ? file.byFileID.get(defRef.fileID) : undefined;
          defaultState = defDoc ? str(defDoc.body.m_Name, '') : null;

          for (const t of arrayOf(sm.body.m_AnyStateTransitions)) {
            const r = asRef(t);
            const d = r ? file.byFileID.get(r.fileID) : undefined;
            if (d) anyState.push(readTransition(file, d));
          }
        }

        layers.push({
          name: str(layerMap.m_Name, 'Base Layer'),
          defaultWeight: num(layerMap.m_DefaultWeight, 1),
          states,
          defaultState,
          anyStateTransitions: anyState,
        });
      }

      const clips = new Set<string>();
      for (const l of layers) {
        for (const s of l.states) {
          if (s.motionName) clips.add(s.motionName);
          for (const c of s.blendTree?.children ?? []) {
            if (c.motionName) clips.add(c.motionName);
          }
        }
      }

      info = {
        path,
        name: path.split('/').pop()?.replace(/\.controller$/, '') ?? 'Controller',
        parameters,
        layers,
        referencedClips: [...clips],
      };
    }
  } catch (err) {
    console.warn(`[animator] failed to read ${path}`, err);
  }

  controllerCache.set(path, info);
  return info;
}

export function clearControllerCache(): void {
  controllerCache.clear();
}
