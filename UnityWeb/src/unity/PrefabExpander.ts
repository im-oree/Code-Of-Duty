/**
 * Prefab instance expansion.
 *
 * A Unity scene does not contain the objects of a prefab it uses. It contains a
 * single `PrefabInstance` (class 1001) holding a reference to the prefab asset
 * plus a list of per-property overrides. Unity assembles the real hierarchy at
 * load time. Anything that reads the scene file literally — as this viewer did
 * — sees a nearly empty scene: `OfflineTest.unity` has seven objects in the
 * file and a whole player rig missing.
 *
 * This module does that assembly ahead of the scene build, by splicing the
 * prefab's documents into the file as if they had been written there. Once it
 * has run, nothing downstream needs to know prefabs exist.
 *
 * Three things make this tractable:
 *
 *  - **Ids are strings.** Cloned documents are filed under `origId@instance`,
 *    so a prefab used twice yields two independent sets and no numeric
 *    collision is possible. (Prefab ids are 64-bit and routinely collide as
 *    doubles — see `UnityDocument.id`.)
 *  - **Only internal references are rewritten.** A reference carrying a `guid`
 *    points at another asset and is left exactly as it was; a reference without
 *    one is prefab-local and gets remapped.
 *  - **Parent links are repaired once, at the end.** Rather than thread parent
 *    bookkeeping through the recursion, `m_Father` is authoritative and the
 *    matching `m_Children` entries are rebuilt in a final pass.
 */

import {
  arrayOf,
  asRef,
  mapOf,
  type UnityDocument,
  type UnityFile,
  type UnityMap,
  type UnityValue,
} from './YamlParser.ts';

const PREFAB_INSTANCE = 1001;
const TRANSFORM_CLASS_IDS = new Set([4, 224]);

/**
 * What a prefab GUID turned out to point at.
 *
 * Unity treats an imported model as a prefab asset too, but a model prefab has
 * no YAML: its GameObjects and Transforms are synthesised by the importer and
 * identified by ids that appear in no file. Those instances cannot be expanded
 * by reading documents, so they are reported once and skipped rather than
 * producing an override warning per property.
 */
export type PrefabSource =
  | { kind: 'prefab'; file: UnityFile }
  | { kind: 'model'; path: string };

/** Resolves a prefab asset by GUID. Supplied by the caller so this stays testable. */
export type PrefabLoader = (guid: string) => Promise<PrefabSource | null>;

/** Guards against a prefab that contains itself. Unity forbids it; files on disk may not. */
const MAX_DEPTH = 8;

export interface ExpansionReport {
  /** How many PrefabInstance documents were expanded. */
  instances: number;
  /** How many documents the expansion added to the file. */
  added: number;
  /** Model files referenced as prefabs, which this cannot assemble yet. */
  modelInstances: string[];
  warnings: string[];
}

/**
 * Expand every `PrefabInstance` in `file`, in place.
 *
 * The 1001 documents are removed and replaced by the objects they stood for.
 */
export async function expandPrefabInstances(
  file: UnityFile,
  load: PrefabLoader,
  depthLimit = MAX_DEPTH,
): Promise<ExpansionReport> {
  const report: ExpansionReport = { instances: 0, added: 0, modelInstances: [], warnings: [] };
  const instances = file.documents.filter((d) => d.classId === PREFAB_INSTANCE);
  if (instances.length === 0) return report;

  const produced: UnityDocument[] = [];
  for (const instance of instances) {
    const docs = await expandInstance(instance, (id) => id, load, report, 0, depthLimit, instance.id);
    produced.push(...docs);
    report.instances++;
  }

  // Swap the placeholders for the real thing.
  file.documents = file.documents.filter((d) => d.classId !== PREFAB_INSTANCE);
  for (const d of instances) file.byFileID.delete(d.id);
  for (const d of produced) {
    file.documents.push(d);
    file.byFileID.set(d.id, d);
  }
  report.added = produced.length;

  repairChildLists(file);
  return report;
}

/**
 * Expand one PrefabInstance into a flat list of documents.
 *
 * `resolveOwnerId` maps a reference that lives in the *containing* file to its
 * final id, which matters for nested prefabs: a prefab's own PrefabInstance
 * parents itself to a transform of the outer prefab, and that transform has
 * already been renamed by the time we get here.
 */
async function expandInstance(
  instance: UnityDocument,
  resolveOwnerId: (id: string) => string,
  load: PrefabLoader,
  report: ExpansionReport,
  depth: number,
  depthLimit: number,
  /**
   * Suffix that makes this instance's cloned ids unique. It accumulates down
   * the nesting chain, so the same prefab nested inside two copies of an outer
   * prefab still yields two independent sets of objects.
   */
  prefix: string,
): Promise<UnityDocument[]> {
  if (depth > depthLimit) {
    report.warnings.push(`prefab nesting deeper than ${depthLimit}; stopped at instance ${instance.id}`);
    return [];
  }

  const modification = mapOf(instance.body.m_Modification);
  const source = asRef(instance.body.m_SourcePrefab);
  if (!source?.guid) {
    report.warnings.push(`prefab instance ${instance.id} has no source prefab`);
    return [];
  }

  const resolved = await load(source.guid);
  if (!resolved) {
    report.warnings.push(`prefab asset not found for guid ${source.guid}`);
    return [];
  }
  if (resolved.kind === 'model') {
    // Nothing to splice: the objects live in the importer, not in a file.
    report.modelInstances.push(resolved.path);
    return [];
  }

  const prefab = resolved.file;
  const local = new Set(prefab.byFileID.keys());
  const rename = (id: string) => `${id}@${prefix}`;

  const docs: UnityDocument[] = [];
  for (const doc of prefab.documents) {
    if (doc.classId === PREFAB_INSTANCE) continue;
    docs.push(cloneDocument(doc, rename, local));
  }

  // Nested prefabs: their parent reference is prefab-local, so it goes through
  // the same rename the rest of this prefab just did.
  for (const doc of prefab.documents) {
    if (doc.classId !== PREFAB_INSTANCE) continue;
    docs.push(...await expandInstance(doc, rename, load, report, depth + 1, depthLimit,
      `${doc.id}@${prefix}`));
  }

  const byId = new Map(docs.map((d) => [d.id, d]));
  applyModifications(byId, arrayOf(modification?.m_Modifications), rename, report);

  // Attach this prefab's root(s) where the instance says they belong.
  const parentRef = asRef(modification?.m_TransformParent);
  const parentId = parentRef && parentRef.fileIDText !== '0'
    ? resolveOwnerId(parentRef.fileIDText)
    : '0';
  for (const doc of docs) {
    if (!TRANSFORM_CLASS_IDS.has(doc.classId)) continue;
    const father = asRef(doc.body.m_Father);
    // A root inside the prefab file is one with no parent; it becomes a child
    // of whatever the instance is parented to in the containing file.
    if (!father || father.fileIDText === '0') {
      doc.body.m_Father = { fileID: parentId };
    }
  }

  return docs;
}

/** Deep-copy a document, renaming every reference that points inside this prefab. */
function cloneDocument(
  doc: UnityDocument,
  rename: (id: string) => string,
  local: Set<string>,
): UnityDocument {
  return {
    classId: doc.classId,
    fileID: doc.fileID,
    id: rename(doc.id),
    typeName: doc.typeName,
    stripped: doc.stripped,
    body: cloneValue(doc.body, rename, local) as UnityMap,
  };
}

function cloneValue(
  value: UnityValue,
  rename: (id: string) => string,
  local: Set<string>,
): UnityValue {
  if (Array.isArray(value)) return value.map((v) => cloneValue(v, rename, local));
  if (!value || typeof value !== 'object') return value;

  const map = value as UnityMap;
  // A reference with a guid points at another asset; only guid-less references
  // are prefab-local and need renaming.
  if ('fileID' in map && !('guid' in map)) {
    const text = typeof map.fileID === 'string' ? map.fileID.trim() : String(map.fileID);
    if (local.has(text)) return { ...map, fileID: rename(text) };
    return { ...map };
  }

  const out: UnityMap = {};
  for (const [k, v] of Object.entries(map)) out[k] = cloneValue(v, rename, local);
  return out;
}

/**
 * Apply the instance's property overrides to the freshly cloned documents.
 *
 * `propertyPath` is a dotted path with two wrinkles: Unity writes array
 * elements as `foo.Array.data[3]`, and some built-in fields have spaces in
 * them (`field of view`, `near clip plane`).
 */
function applyModifications(
  byId: Map<string, UnityDocument>,
  modifications: UnityValue[],
  rename: (id: string) => string,
  report: ExpansionReport,
): void {
  for (const entry of modifications) {
    const mod = mapOf(entry);
    if (!mod) continue;

    const target = asRef(mod.target);
    const path = typeof mod.propertyPath === 'string' ? mod.propertyPath : '';
    if (!target || !path) continue;

    // Sibling order is a scene-authoring concern; the hierarchy we build is
    // driven by m_Father/m_Children, so applying it would do nothing.
    if (path === 'm_RootOrder') continue;

    const doc = byId.get(rename(target.fileIDText));
    if (!doc) {
      report.warnings.push(`override targets ${target.fileIDText}, which is not in the prefab`);
      continue;
    }

    const reference = asRef(mod.objectReference);
    const value: UnityValue = reference && reference.fileIDText !== '0'
      ? (mapOf(mod.objectReference) as UnityMap)
      : (mod.value ?? null);

    setPath(doc.body, path, value, report);
  }
}

function setPath(root: UnityMap, path: string, value: UnityValue, report: ExpansionReport): void {
  const parts = path.split('.');
  let cursor: UnityValue = root;

  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    const last = i === parts.length - 1;

    // `foo.Array.data[3]` -- the `Array` hop is Unity's serialisation noise.
    const indexed = /^data\[(\d+)\]$/.exec(part);
    if (part === 'Array' && !last) continue;

    if (indexed) {
      const list = cursor as UnityValue[];
      if (!Array.isArray(list)) {
        report.warnings.push(`override path "${path}" expected an array`);
        return;
      }
      const at = Number(indexed[1]);
      if (last) { list[at] = value; return; }
      if (list[at] == null || typeof list[at] !== 'object') list[at] = {};
      cursor = list[at];
      continue;
    }

    const map = cursor as UnityMap;
    if (!map || typeof map !== 'object' || Array.isArray(map)) {
      report.warnings.push(`override path "${path}" ran off the end of the document`);
      return;
    }
    if (last) { map[part] = value; return; }
    if (map[part] == null || typeof map[part] !== 'object') map[part] = {};
    cursor = map[part];
  }
}

/**
 * Make `m_Children` agree with `m_Father`.
 *
 * `m_Father` is set by the expansion; the corresponding entry in the parent's
 * child list is not, and the scene builder walks children. Existing entries are
 * left in place so authored sibling order survives — this only appends what is
 * missing.
 */
function repairChildLists(file: UnityFile): void {
  const childrenOf = new Map<string, Set<string>>();
  for (const doc of file.documents) {
    if (!TRANSFORM_CLASS_IDS.has(doc.classId)) continue;
    const seen = new Set<string>();
    for (const entry of arrayOf(doc.body.m_Children)) {
      const ref = asRef(entry);
      if (ref) seen.add(ref.fileIDText);
    }
    childrenOf.set(doc.id, seen);
  }

  for (const doc of file.documents) {
    if (!TRANSFORM_CLASS_IDS.has(doc.classId)) continue;
    const father = asRef(doc.body.m_Father);
    if (!father || father.fileIDText === '0') continue;

    const parent = file.byFileID.get(father.fileIDText);
    if (!parent) continue;
    const seen = childrenOf.get(parent.id);
    if (!seen || seen.has(doc.id)) continue;

    const list = Array.isArray(parent.body.m_Children) ? parent.body.m_Children : [];
    list.push({ fileID: doc.id });
    parent.body.m_Children = list;
    seen.add(doc.id);
  }
}
