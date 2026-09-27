#!/usr/bin/env node
/**
 * Guards the rule that makes bots indistinguishable from players.
 *
 * Gameplay code must read intent from IInputSource, never from a device. The moment one
 * movement or weapon script calls Input.GetAxis or InputBindings.Held directly, bots stop
 * behaving like players in that one respect — and that is exactly the kind of regression that
 * is invisible in review and maddening to debug at runtime.
 *
 * Usage: node Tools/verify-input-seam.mjs
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

const ROOT = new URL("..", import.meta.url).pathname;
const SCRIPTS = join(ROOT, "Assets/Scripts");

/**
 * Files allowed to touch a device, each for a stated reason. Anything not listed here is a
 * failure, so adding an exemption is a deliberate, reviewable act.
 */
const ALLOWED = new Map([
  ["Input/PlayerInputSource.cs", "the one device reader; this is its job"],
  ["Settings/InputBindings.cs", "the keybinding registry itself"],
  ["UI/SettingsPanel.cs", "captures raw keypresses to rebind them"],
  ["UI/MainMenu/CODMainMenu.cs", "menu navigation, not character intent"],
  ["UI/PauseMenu.cs", "global menu toggle, not character intent"],
  ["Player/CameraController.cs", "click-to-recapture the cursor, a window concern"],
  ["Rig/LocalRigs/WeaponSlotRig.cs", "editor-only debug key behind a serialized flag"],
]);

const PATTERNS = [
  /(?<![.\w])Input\s*\.\s*Get(Axis|AxisRaw|Key|KeyDown|KeyUp|Button|ButtonDown|ButtonUp|MouseButton|MouseButtonDown|MouseButtonUp)\b/,
  /(?<![.\w])Input\s*\.\s*mouseScrollDelta\b/,
  /(?<![.\w])InputBindings\s*\.\s*(Down|Held|Up)\s*\(/,
  /(?<![.\w])(Keyboard|Mouse|Gamepad)\s*\.\s*current\b/,
];

/** Strip comments and string literals so a mention in prose is not a violation. */
function strip(src) {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/\/\/[^\n]*/g, "")
    .replace(/"(?:\\.|[^"\\])*"/g, '""');
}

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith(".cs")) out.push(full);
  }
  return out;
}

const violations = [];
const unusedExemptions = new Set(ALLOWED.keys());

for (const file of walk(SCRIPTS)) {
  const rel = relative(SCRIPTS, file).split("\\").join("/");
  const lines = strip(readFileSync(file, "utf8")).split("\n");

  const hits = [];
  lines.forEach((line, i) => {
    if (PATTERNS.some((p) => p.test(line))) hits.push({ line: i + 1, text: line.trim() });
  });

  if (hits.length === 0) continue;
  if (ALLOWED.has(rel)) { unusedExemptions.delete(rel); continue; }
  violations.push({ rel, hits });
}

if (violations.length) {
  console.error("Gameplay scripts must read intent from IInputSource, not from a device.\n");
  for (const { rel, hits } of violations) {
    console.error(`  ${rel}`);
    for (const h of hits) console.error(`    :${h.line}  ${h.text}`);
  }
  console.error(`\n${violations.length} file(s) bypass the input seam.`);
  console.error("Use CharacterInput.For(this).Source, or add a justified entry to ALLOWED.");
  process.exit(1);
}

for (const stale of unusedExemptions) {
  console.warn(`note: '${stale}' is exempt but no longer reads a device — drop the exemption.`);
}
console.log(`input seam intact — ${ALLOWED.size} justified exemption(s), 0 violations`);
