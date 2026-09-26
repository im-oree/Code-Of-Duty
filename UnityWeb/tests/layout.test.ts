/**
 * RectTransform layout tests.
 *
 * uGUI's anchor maths is the single easiest thing to get subtly wrong in a
 * Unity viewer, and a wrong menu layout looks plausible enough to be believed.
 * Each case below was worked through by hand against Unity's documented
 * relationship between anchors, pivot, sizeDelta and anchoredPosition.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseUnityYaml } from '../src/unity/YamlParser.ts';
import { solveRect, canvasScaleFactor, intersectRect } from '../src/ui/UguiLayout.ts';

const SCREEN = { x: 0, y: 0, width: 1920, height: 1080 };

function rt(yaml: string) {
  return parseUnityYaml(`--- !u!224 &1\nRectTransform:\n${yaml}`).documents[0].body;
}

test('top-left anchored element (the real TopBar button case)', () => {
  // Taken verbatim from Assets/Scenes/StartMenu.unity.
  const r = solveRect(rt(`  m_AnchorMin: {x: 0, y: 1}
  m_AnchorMax: {x: 0, y: 1}
  m_AnchoredPosition: {x: 310, y: -2}
  m_SizeDelta: {x: 34, y: 30}
  m_Pivot: {x: 0, y: 1}`), SCREEN);

  assert.equal(r.width, 34);
  assert.equal(r.height, 30);
  assert.equal(r.x, 310);
  // Pivot is the top edge, 2px below the top of a 1080-tall screen.
  assert.equal(r.y + r.height, 1078);
});

test('centre-anchored element', () => {
  const r = solveRect(rt(`  m_AnchorMin: {x: 0.5, y: 0.5}
  m_AnchorMax: {x: 0.5, y: 0.5}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 200, y: 100}
  m_Pivot: {x: 0.5, y: 0.5}`), SCREEN);

  assert.equal(r.width, 200);
  assert.equal(r.height, 100);
  assert.equal(r.x, 860);   // (1920 - 200) / 2
  assert.equal(r.y, 490);   // (1080 - 100) / 2
});

test('full-stretch element: sizeDelta acts as a margin, not a size', () => {
  const r = solveRect(rt(`  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: -40, y: -60}
  m_Pivot: {x: 0.5, y: 0.5}`), SCREEN);

  assert.equal(r.width, 1880);   // 1920 - 40
  assert.equal(r.height, 1020);  // 1080 - 60
  assert.equal(r.x, 20);
  assert.equal(r.y, 30);
});

test('horizontal stretch with vertical fixed height (a typical top bar)', () => {
  const r = solveRect(rt(`  m_AnchorMin: {x: 0, y: 1}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: -32}
  m_SizeDelta: {x: 0, y: 64}
  m_Pivot: {x: 0.5, y: 0.5}`), SCREEN);

  assert.equal(r.width, 1920);
  assert.equal(r.height, 64);
  assert.equal(r.y, 1080 - 64);
});

test('bottom-right anchored element', () => {
  const r = solveRect(rt(`  m_AnchorMin: {x: 1, y: 0}
  m_AnchorMax: {x: 1, y: 0}
  m_AnchoredPosition: {x: -30, y: 20}
  m_SizeDelta: {x: 100, y: 40}
  m_Pivot: {x: 1, y: 0}`), SCREEN);

  assert.equal(r.x + r.width, 1890); // 30px in from the right edge
  assert.equal(r.y, 20);
});

test('nested rects resolve against their parent, not the screen', () => {
  const parent = solveRect(rt(`  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 0, y: 0}
  m_AnchoredPosition: {x: 100, y: 100}
  m_SizeDelta: {x: 400, y: 200}
  m_Pivot: {x: 0, y: 0}`), SCREEN);
  assert.deepEqual(parent, { x: 100, y: 100, width: 400, height: 200 });

  const child = solveRect(rt(`  m_AnchorMin: {x: 0, y: 0}
  m_AnchorMax: {x: 1, y: 1}
  m_AnchoredPosition: {x: 0, y: 0}
  m_SizeDelta: {x: 0, y: 0}
  m_Pivot: {x: 0.5, y: 0.5}`), parent);
  assert.deepEqual(child, parent); // full stretch with no margin
});

test('CanvasScaler match-width-or-height', () => {
  const base = {
    mode: 1, referenceWidth: 1920, referenceHeight: 1080,
    screenMatchMode: 0, scaleFactor: 1, matchWidthOrHeight: 0,
  };
  // Exact reference resolution is always 1:1 regardless of match.
  assert.equal(canvasScaleFactor(base, 1920, 1080), 1);
  assert.equal(canvasScaleFactor({ ...base, matchWidthOrHeight: 1 }, 1920, 1080), 1);

  // Half width, matching on width -> half scale.
  assert.equal(canvasScaleFactor(base, 960, 1080), 0.5);
  // Half width, matching on height -> unchanged.
  assert.equal(canvasScaleFactor({ ...base, matchWidthOrHeight: 1 }, 960, 1080), 1);
});

test('CanvasScaler constant pixel size ignores screen size', () => {
  const s = {
    mode: 0, referenceWidth: 1920, referenceHeight: 1080,
    screenMatchMode: 0, scaleFactor: 2, matchWidthOrHeight: 0,
  };
  assert.equal(canvasScaleFactor(s, 800, 600), 2);
});

test('rect intersection for masks', () => {
  const a = { x: 0, y: 0, width: 100, height: 100 };
  const b = { x: 50, y: 50, width: 100, height: 100 };
  assert.deepEqual(intersectRect(a, b), { x: 50, y: 50, width: 50, height: 50 });
  assert.equal(intersectRect(a, { x: 200, y: 200, width: 10, height: 10 }), null);
});
