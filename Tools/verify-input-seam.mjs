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

// ---------------------------------------------------------------------------
// Check 2: anything that READS player intent must be gated to the local player.
//
// A component that reads intent and runs on a remote body drives that body from
// this machine's keyboard. It is invisible while testing solo and obvious the
// moment a second player joins, which is the worst combination a bug can have.
//
// Three ways to be gated, all accepted:
//   1. implement ILocalOnly            -> NetComponentEnabler switches it off
//   2. be a NetworkBehaviour that checks IsOwner
//   3. check NetOwnership.IsLocal
// ---------------------------------------------------------------------------

const INTENT = [
  /\bI?InputSource\b/,   // IInputSource, PlayerInputSource, BotInputSource, .InputSource
  /\bCharacterInput\b/,
];

// Files that define or implement the seam itself, plus non-character consumers.
const INTENT_ALLOWED = new Map([
  ["Input/IInputSource.cs", "declares the interface"],
  ["Input/BotInputSource.cs", "is a source, not a consumer"],
  ["Input/BotBrain.cs", "writes the bot source; only ever added to bot bodies"],
  ["Player/move/StandState.cs", "plain class owned by CharacterMove, which is gated"],
  ["Player/move/CrouchState.cs", "plain class owned by CharacterMove, which is gated"],
  ["Player/move/RollState.cs", "plain class owned by CharacterMove, which is gated"],
]);

const ungated = [];
const unusedIntentExemptions = new Set(INTENT_ALLOWED.keys());

for (const file of walk(SCRIPTS)) {
  const rel = relative(SCRIPTS, file).split("\\").join("/");
  const source = strip(readFileSync(file, "utf8"));
  if (!INTENT.some((p) => p.test(source))) continue;

  if (INTENT_ALLOWED.has(rel)) { unusedIntentExemptions.delete(rel); continue; }

  const gated =
    /:\s*[^{}\n]*\bILocalOnly\b/.test(source) ||
    /\bILocalOnly\b/.test(source) ||
    /\bIsOwner\b/.test(source) ||
    /NetOwnership\s*\.\s*IsLocal/.test(source) ||
    /\bIsLocal\b/.test(source);

  if (!gated) ungated.push(rel);
}

if (ungated.length) {
  console.error("Components that read player intent must be gated to the local player.\n");
  for (const rel of ungated) console.error(`  ${rel}`);
  console.error(
    "\nOtherwise they run on every remote body and drive it from this machine's input." +
    "\nFix: implement ILocalOnly, or check IsOwner / NetOwnership.IsLocal," +
    "\nor add a justified entry to INTENT_ALLOWED."
  );
  process.exit(1);
}

for (const stale of unusedIntentExemptions) {
  console.warn(`note: '${stale}' is exempt from the ownership check but no longer reads intent.`);
}
console.log(
  `ownership gating intact — ${INTENT_ALLOWED.size} justified exemption(s), 0 ungated consumers`
);
