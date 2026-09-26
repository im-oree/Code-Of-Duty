/**
 * Prefab expansion.
 *
 * The cases here are the ones that actually bit: a scene that looks almost
 * empty because its content is behind a PrefabInstance, overrides that have to
 * land on the right clone, and the id collisions that made naive numeric
 * keying unsafe.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseUnityYaml, asRef, str, num, type UnityFile } from '../src/unity/YamlParser.ts';
import { expandPrefabInstances, type PrefabSource } from '../src/unity/PrefabExpander.ts';

/** A prefab with one root and one child, so parenting and renaming are visible. */
const SIMPLE_PREFAB = `--- !u!1 &100
GameObject:
  m_Name: Root
  m_IsActive: 1
  m_Component:
  - component: {fileID: 101}
--- !u!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_Father: {fileID: 0}
  m_Children:
  - {fileID: 201}
--- !u!1 &200
GameObject:
  m_Name: Child
  m_IsActive: 1
  m_Component:
  - component: {fileID: 201}
--- !u!4 &201
Transform:
  m_GameObject: {fileID: 200}
  m_LocalPosition: {x: 1, y: 2, z: 3}
  m_Father: {fileID: 101}
  m_Children: []
`;

function sceneWith(body: string): UnityFile {
  return parseUnityYaml(body);
}

const loaderFor = (files: Record<string, string>) =>
  async (guid: string): Promise<PrefabSource | null> => {
    const src = files[guid];
    return src ? { kind: 'prefab', file: parseUnityYaml(src) } : null;
  };

test('a PrefabInstance becomes real objects', async () => {
  const scene = sceneWith(`--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
`);

  const report = await expandPrefabInstances(scene, loaderFor({ aaa: SIMPLE_PREFAB }));

  assert.equal(report.instances, 1);
  assert.equal(report.added, 4);
  assert.equal(report.warnings.length, 0);
  // The placeholder is gone.
  assert.equal(scene.documents.filter((d) => d.classId === 1001).length, 0);

  const root = scene.byFileID.get('101@9000');
  const child = scene.byFileID.get('201@9000');
  assert.ok(root && child, 'both transforms were spliced in under renamed ids');
  // Internal references were rewritten to point at the clones, not the source.
  assert.equal(asRef(child!.body.m_Father)?.fileIDText, '101@9000');
  assert.equal(asRef(root!.body.m_GameObject)?.fileIDText, '100@9000');
  // The root is parented where the instance said.
  assert.equal(asRef(root!.body.m_Father)?.fileIDText, '0');
});

test('overrides land on the matching clone', async () => {
  const scene = sceneWith(`--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: 201, guid: aaa, type: 3}
      propertyPath: m_LocalPosition.y
      value: 42
      objectReference: {fileID: 0}
    - target: {fileID: 200, guid: aaa, type: 3}
      propertyPath: m_Name
      value: Renamed
      objectReference: {fileID: 0}
    - target: {fileID: 201, guid: aaa, type: 3}
      propertyPath: m_RootOrder
      value: 3
      objectReference: {fileID: 0}
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
`);

  const report = await expandPrefabInstances(scene, loaderFor({ aaa: SIMPLE_PREFAB }));
  assert.equal(report.warnings.length, 0);

  const child = scene.byFileID.get('201@9000')!;
  const go = scene.byFileID.get('200@9000')!;
  // Overridden, and the untouched components of the vector survive.
  assert.equal(num((child.body.m_LocalPosition as Record<string, never>).y, -1), 42);
  assert.equal(num((child.body.m_LocalPosition as Record<string, never>).x, -1), 1);
  assert.equal(str(go.body.m_Name, ''), 'Renamed');
});

test('the same prefab used twice yields independent objects', async () => {
  const scene = sceneWith(`--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: 201, guid: aaa, type: 3}
      propertyPath: m_LocalPosition.y
      value: 11
      objectReference: {fileID: 0}
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
--- !u!1001 &9001
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: 201, guid: aaa, type: 3}
      propertyPath: m_LocalPosition.y
      value: 22
      objectReference: {fileID: 0}
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
`);

  await expandPrefabInstances(scene, loaderFor({ aaa: SIMPLE_PREFAB }));

  const a = scene.byFileID.get('201@9000')!;
  const b = scene.byFileID.get('201@9001')!;
  assert.equal(num((a.body.m_LocalPosition as Record<string, never>).y, -1), 11);
  assert.equal(num((b.body.m_LocalPosition as Record<string, never>).y, -1), 22);
});

test('an instance parented into the scene joins its parent\'s child list', async () => {
  const scene = sceneWith(`--- !u!1 &500
GameObject:
  m_Name: Mount
  m_Component:
  - component: {fileID: 501}
--- !u!4 &501
Transform:
  m_GameObject: {fileID: 500}
  m_Father: {fileID: 0}
  m_Children: []
--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 501}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
`);

  await expandPrefabInstances(scene, loaderFor({ aaa: SIMPLE_PREFAB }));

  const mount = scene.byFileID.get('501')!;
  const root = scene.byFileID.get('101@9000')!;
  assert.equal(asRef(root.body.m_Father)?.fileIDText, '501');
  // m_Father alone is not enough -- the builder walks m_Children.
  const kids = (mount.body.m_Children as unknown[]).map((k) => asRef(k as never)?.fileIDText);
  assert.deepEqual(kids, ['101@9000']);
});

test('a model used as a prefab is reported, not silently dropped', async () => {
  const scene = sceneWith(`--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications:
    - target: {fileID: -8679921383154817045, guid: mmm, type: 3}
      propertyPath: m_LocalPosition.y
      value: 1
      objectReference: {fileID: 0}
  m_SourcePrefab: {fileID: 100100000, guid: mmm, type: 3}
`);

  const report = await expandPrefabInstances(scene, async () => ({
    kind: 'model', path: 'Assets/Models/Character/MonKent.fbx',
  }));

  assert.deepEqual(report.modelInstances, ['Assets/Models/Character/MonKent.fbx']);
  // One honest report, not one warning per unresolvable override.
  assert.equal(report.warnings.length, 0);
  assert.equal(report.added, 0);
});

test('nested prefabs stay distinct across two copies of the outer prefab', async () => {
  const outer = `--- !u!1 &10
GameObject:
  m_Name: Outer
  m_Component:
  - component: {fileID: 11}
--- !u!4 &11
Transform:
  m_GameObject: {fileID: 10}
  m_Father: {fileID: 0}
  m_Children: []
--- !u!1001 &12
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 11}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: aaa, type: 3}
`;
  const scene = sceneWith(`--- !u!1001 &9000
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: bbb, type: 3}
--- !u!1001 &9001
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: bbb, type: 3}
`);

  const report = await expandPrefabInstances(
    scene, loaderFor({ aaa: SIMPLE_PREFAB, bbb: outer }),
  );
  assert.equal(report.warnings.length, 0);

  // The inner prefab's transform exists once per outer copy, under distinct ids,
  // and each is parented to its own outer transform.
  const first = scene.byFileID.get('101@12@9000');
  const second = scene.byFileID.get('101@12@9001');
  assert.ok(first && second, 'nested ids carry the whole instance chain');
  assert.equal(asRef(first!.body.m_Father)?.fileIDText, '11@9000');
  assert.equal(asRef(second!.body.m_Father)?.fileIDText, '11@9001');
});
