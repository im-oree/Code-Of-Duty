/**
 * Unity YAML parser tests.
 *
 * These run on Node's built-in test runner with type stripping, so there is no
 * bundler or extra dependency in the loop — `npm test` is honest about whether
 * the parser handles the shapes Unity actually emits.
 *
 * Every case here came from a real file in this project. When the viewer
 * disagrees with the Editor, the fix starts by adding the offending snippet.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseUnityYaml, num, str, asRef, arrayOf, mapOf, vec2, color } from '../src/unity/YamlParser.ts';

test('parses the scene header and skips it', () => {
  const file = parseUnityYaml(`%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &123
GameObject:
  m_Name: Player
  m_IsActive: 1
`);
  assert.equal(file.documents.length, 1);
  assert.equal(file.documents[0].classId, 1);
  assert.equal(file.documents[0].fileID, 123);
  assert.equal(str(file.documents[0].body.m_Name), 'Player');
});

test('decodes double-quoted escapes, including TMP zero-width space', () => {
  const file = parseUnityYaml(`--- !u!114 &1
MonoBehaviour:
  m_text: "\\u200B"
  withNewline: "A\\nB"
  withQuote: "say \\"hi\\""
  withBackslash: "a\\\\b"
  accented: "caf\\u00e9"
`);
  const b = file.documents[0].body;
  assert.equal(str(b.m_text), '\u200b');
  assert.equal(str(b.withNewline), 'A\nB');
  assert.equal(str(b.withQuote), 'say "hi"');
  assert.equal(str(b.withBackslash), 'a\\b');
  assert.equal(str(b.accented), 'café');
});

test('reads object references with guid and type', () => {
  const file = parseUnityYaml(`--- !u!33 &1
MeshFilter:
  m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}
  m_None: {fileID: 0}
`);
  const ref = asRef(file.documents[0].body.m_Mesh);
  assert.notEqual(ref, null);
  assert.equal(ref?.fileID, 10202);
  // Regression guard: this GUID parses as the number 0 if scalar resolution
  // treats it as scientific notation.
  assert.equal(ref?.guid, '0000000000000000e000000000000000');
  assert.equal(typeof ref?.guid, 'string');
  const none = asRef(file.documents[0].body.m_None);
  assert.equal(none?.fileID, 0);
});

test('reads sequences of references (m_Children, m_Bones)', () => {
  const file = parseUnityYaml(`--- !u!224 &1
RectTransform:
  m_Children:
  - {fileID: 111}
  - {fileID: 222}
  m_Father: {fileID: 999}
`);
  const kids = arrayOf(file.documents[0].body.m_Children);
  assert.equal(kids.length, 2);
  assert.equal(asRef(kids[0])?.fileID, 111);
  assert.equal(asRef(kids[1])?.fileID, 222);
  assert.equal(asRef(file.documents[0].body.m_Father)?.fileID, 999);
});

test('reads vectors and colours', () => {
  const file = parseUnityYaml(`--- !u!224 &1
RectTransform:
  m_AnchorMin: {x: 0, y: 1}
  m_SizeDelta: {x: 34, y: 30}
  m_Color: {r: 0.08627451, g: 0.105882354, b: 0.13333334, a: 0.8627451}
  m_NoAlpha: {r: 1, g: 0.5, b: 0}
`);
  const b = file.documents[0].body;
  assert.deepEqual(vec2(b.m_AnchorMin), { x: 0, y: 1 });
  assert.deepEqual(vec2(b.m_SizeDelta), { x: 34, y: 30 });
  const c = color(b.m_Color);
  assert.ok(c && Math.abs(c.r - 0.08627451) < 1e-6);
  // Unity omits alpha in some places; it must default to opaque, not 0.
  assert.equal(color(b.m_NoAlpha)?.a, 1);
});

test('handles negative fileIDs', () => {
  const file = parseUnityYaml(`--- !u!23 &-8938111378311941879
MeshRenderer:
  m_Enabled: 1
`);
  assert.equal(file.documents[0].fileID, -8938111378311941879);
});

test('handles stripped prefab documents', () => {
  const file = parseUnityYaml(`--- !u!4 &5171338329323126641 stripped
Transform:
  m_CorrespondingSourceObject: {fileID: 8890124922690834122, guid: abc, type: 3}
`);
  assert.equal(file.documents[0].stripped, true);
});

test('parses nested maps and lists together', () => {
  const file = parseUnityYaml(`--- !u!114 &1
MonoBehaviour:
  m_Navigation:
    m_Mode: 3
    m_SelectOnUp: {fileID: 0}
  m_Materials:
  - {fileID: 2100000, guid: aaa, type: 2}
  m_Colors:
    m_NormalColor: {r: 1, g: 1, b: 1, a: 1}
    m_ColorMultiplier: 1
`);
  const b = file.documents[0].body;
  assert.equal(num(mapOf(b.m_Navigation)?.m_Mode), 3);
  assert.equal(arrayOf(b.m_Materials).length, 1);
  assert.equal(num(mapOf(b.m_Colors)?.m_ColorMultiplier), 1);
});

test('indexes documents by fileID', () => {
  const file = parseUnityYaml(`--- !u!1 &100
GameObject:
  m_Name: A
--- !u!4 &200
Transform:
  m_GameObject: {fileID: 100}
`);
  assert.equal(file.byFileID.get(100)?.classId, 1);
  assert.equal(file.byFileID.get(200)?.classId, 4);
});

test('num/str/bool coerce the way Unity fields expect', () => {
  const file = parseUnityYaml(`--- !u!1 &1
GameObject:
  a: 1
  b: 0
  c: 1.5
  d: -0.0008
  e: hello
`);
  const b = file.documents[0].body;
  assert.equal(num(b.a), 1);
  assert.equal(num(b.c), 1.5);
  assert.equal(num(b.d), -0.0008);
  assert.equal(num(b.missing, 7), 7);
  assert.equal(str(b.e), 'hello');
});
