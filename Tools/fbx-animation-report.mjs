#!/usr/bin/env node
/**
 * Report what animation a binary FBX actually contains.
 *
 * Why this exists
 * ---------------
 * The menu operator stood perfectly still and every status readout said the
 * animation was fine: the Animator resolved, the clip was found, the action
 * was running, its time advanced, and all 188 tracks bound to real bones. The
 * clips themselves were the problem -- `Root|Aim_W_Idle` has 22 keyframes and
 * every one of them holds the same pose. A flat curve is indistinguishable
 * from a working one at every level above the file, so the only way to settle
 * it is to read the bytes.
 *
 * Run this on any model before trusting its animation, and especially on
 * anything re-exported from a DCC tool, where "bake animation" being off
 * produces exactly this: correct take names, correct durations, correct key
 * counts, no movement.
 *
 * Usage
 *   node Tools/fbx-animation-report.mjs <file.fbx> [--take <name>] [--json] [--all]
 *
 * Reads the FBX binary format directly (versions 7100-7700, 32- and 64-bit
 * offset variants) with no dependency on a loader, so its answer is
 * independent of whatever three.js, Unity or Blender make of the same file.
 */

import { readFileSync } from 'node:fs';
import { inflateSync } from 'node:zlib';

/* ------------------------------------------------------------------ */
/* Binary FBX reader                                                    */
/* ------------------------------------------------------------------ */

const MAGIC = 'Kaydara FBX Binary';

function readProperty(view, cur) {
  const type = String.fromCharCode(view.getUint8(cur.o));
  cur.o += 1;
  switch (type) {
    case 'Y': { const v = view.getInt16(cur.o, true); cur.o += 2; return v; }
    case 'C': { const v = view.getUint8(cur.o) !== 0; cur.o += 1; return v; }
    case 'I': { const v = view.getInt32(cur.o, true); cur.o += 4; return v; }
    case 'F': { const v = view.getFloat32(cur.o, true); cur.o += 4; return v; }
    case 'D': { const v = view.getFloat64(cur.o, true); cur.o += 8; return v; }
    case 'L': { const v = Number(view.getBigInt64(cur.o, true)); cur.o += 8; return v; }
    case 'S': case 'R': {
      const len = view.getUint32(cur.o, true); cur.o += 4;
      const bytes = new Uint8Array(view.buffer, view.byteOffset + cur.o, len);
      cur.o += len;
      return type === 'S' ? new TextDecoder('utf-8').decode(bytes) : bytes.slice();
    }
    case 'f': case 'd': case 'l': case 'i': case 'b': {
      const count = view.getUint32(cur.o, true); cur.o += 4;
      const encoding = view.getUint32(cur.o, true); cur.o += 4;
      const byteLength = view.getUint32(cur.o, true); cur.o += 4;
      let bytes = new Uint8Array(view.buffer, view.byteOffset + cur.o, byteLength);
      cur.o += byteLength;
      // Array payloads are usually zlib-deflated; encoding 0 means raw.
      if (encoding === 1) bytes = new Uint8Array(inflateSync(bytes));
      const ab = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
      switch (type) {
        case 'f': return new Float32Array(ab, 0, count);
        case 'd': return new Float64Array(ab, 0, count);
        case 'l': return new BigInt64Array(ab, 0, count);
        case 'i': return new Int32Array(ab, 0, count);
        default: return new Uint8Array(ab, 0, count);
      }
    }
    default:
      throw new Error(`unknown FBX property type '${type}' at byte ${cur.o - 1}`);
  }
}

function readNode(view, cur, wide) {
  const readOffset = () => {
    const v = wide ? Number(view.getBigUint64(cur.o, true)) : view.getUint32(cur.o, true);
    cur.o += wide ? 8 : 4;
    return v;
  };
  const endOffset = readOffset();
  const numProperties = readOffset();
  readOffset(); // property list length, not needed: properties are self-describing
  const nameLength = view.getUint8(cur.o); cur.o += 1;
  const name = new TextDecoder().decode(
    new Uint8Array(view.buffer, view.byteOffset + cur.o, nameLength),
  );
  cur.o += nameLength;

  // A zero end offset is the sentinel that closes a list of siblings.
  if (endOffset === 0) return null;

  const props = [];
  for (let i = 0; i < numProperties; i++) props.push(readProperty(view, cur));

  const children = [];
  // 13 bytes of null terminator follow a nested list.
  while (cur.o < endOffset - 13) {
    const child = readNode(view, cur, wide);
    if (child) children.push(child);
  }
  cur.o = endOffset;
  return { name, props, children };
}

export function parseFbx(buffer) {
  const view = new DataView(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  const header = new TextDecoder().decode(new Uint8Array(buffer.buffer, buffer.byteOffset, 18));
  if (header !== MAGIC) {
    throw new Error('not a binary FBX (ASCII FBX is not supported by this tool)');
  }
  const version = view.getUint32(23, true);
  const wide = version >= 7500;

  const cur = { o: 27 };
  const roots = [];
  while (cur.o < buffer.length - 100) {
    const node = readNode(view, cur, wide);
    if (!node) break;
    roots.push(node);
  }
  return { version, roots };
}

/* ------------------------------------------------------------------ */
/* Animation analysis                                                   */
/* ------------------------------------------------------------------ */

/** FBX stores key times in units of 1/46186158000 of a second. */
const FBX_TIME_UNIT = 46186158000;

/** Names carry a null-separated "\0\x01Type" suffix; keep the readable half. */
const cleanName = (value) => String(value).split('\u0000')[0];

export function analyseAnimation(parsed, { epsilon = 1e-4 } = {}) {
  const objects = parsed.roots.find((r) => r.name === 'Objects');
  const connections = parsed.roots.find((r) => r.name === 'Connections');
  if (!objects || !connections) throw new Error('FBX has no Objects/Connections sections');

  const byId = new Map();
  for (const child of objects.children) byId.set(child.props[0], child);

  // Connection records are ["OO"|"OP", childId, parentId, propertyName?].
  const childrenOf = new Map();
  const parentsOf = new Map();
  for (const c of connections.children) {
    const [kind, childId, parentId, property] = c.props;
    const link = { id: childId, property: kind === 'OP' ? property : null };
    if (!childrenOf.has(parentId)) childrenOf.set(parentId, []);
    childrenOf.get(parentId).push(link);
    if (!parentsOf.has(childId)) parentsOf.set(childId, []);
    parentsOf.get(childId).push({ id: parentId, property: link.property });
  }

  const takes = [];
  for (const stack of objects.children.filter((c) => c.name === 'AnimationStack')) {
    const name = cleanName(stack.props[1]);
    const layerIds = (childrenOf.get(stack.props[0]) ?? [])
      .map((l) => l.id)
      .filter((id) => byId.get(id)?.name === 'AnimationLayer');

    const movingBones = new Set();
    const staticBones = new Set();
    let animatedChannels = 0;
    let totalChannels = 0;
    let maxKeys = 0;
    let duration = 0;

    for (const layerId of layerIds) {
      const curveNodeIds = (childrenOf.get(layerId) ?? [])
        .map((l) => l.id)
        .filter((id) => byId.get(id)?.name === 'AnimationCurveNode');

      for (const curveNodeId of curveNodeIds) {
        // Which model does this curve node drive, and which property?
        const owner = (parentsOf.get(curveNodeId) ?? [])
          .find((p) => byId.get(p.id)?.name === 'Model');
        if (!owner) continue;
        const boneName = cleanName(byId.get(owner.id).props[1]);

        let boneMoves = false;
        for (const link of childrenOf.get(curveNodeId) ?? []) {
          const curve = byId.get(link.id);
          if (curve?.name !== 'AnimationCurve') continue;
          const values = curve.children.find((c) => c.name === 'KeyValueFloat')?.props[0];
          const times = curve.children.find((c) => c.name === 'KeyTime')?.props[0];
          if (!values) continue;

          totalChannels++;
          maxKeys = Math.max(maxKeys, values.length);
          if (times?.length) {
            duration = Math.max(duration, Number(times[times.length - 1]) / FBX_TIME_UNIT);
          }

          let varies = false;
          for (let i = 1; i < values.length; i++) {
            if (Math.abs(values[i] - values[0]) > epsilon) { varies = true; break; }
          }
          if (varies) { animatedChannels++; boneMoves = true; }
        }
        if (boneMoves) movingBones.add(boneName); else staticBones.add(boneName);
      }
    }

    for (const bone of movingBones) staticBones.delete(bone);
    takes.push({
      name,
      animatedChannels,
      totalChannels,
      percentAnimated: totalChannels ? +(100 * animatedChannels / totalChannels).toFixed(1) : 0,
      keys: maxKeys,
      duration: +duration.toFixed(3),
      movingBones: [...movingBones].sort(),
      staticBoneCount: staticBones.size,
    });
  }

  const bones = objects.children.filter((c) => c.name === 'Model').length;
  return { version: parsed.version, bones, takes };
}

/* ------------------------------------------------------------------ */
/* CLI                                                                  */
/* ------------------------------------------------------------------ */

function main(argv) {
  const args = { file: null, take: null, json: false, all: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--json') args.json = true;
    else if (a === '--all') args.all = true;
    else if (a === '--take') args.take = argv[++i];
    else if (a === '--help' || a === '-h') { usage(); return 0; }
    else args.file = a;
  }
  if (!args.file) { usage(); return 1; }

  const report = analyseAnimation(parseFbx(readFileSync(args.file)));

  if (args.json) {
    console.log(JSON.stringify(report, null, 2));
    return 0;
  }

  const flat = report.takes.filter((t) => t.animatedChannels === 0);
  const best = [...report.takes].sort((a, b) => b.percentAnimated - a.percentAnimated);

  console.log(`${args.file}`);
  console.log(`  FBX version ${report.version}, ${report.bones} models, ${report.takes.length} take(s)`);
  console.log(`  ${flat.length} take(s) contain no movement at all`);
  if (best[0]) {
    console.log(`  most animated take: "${best[0].name}" at ${best[0].percentAnimated}% of channels`);
  }
  console.log('');

  if (args.take) {
    const take = report.takes.find((t) => t.name === args.take)
      ?? report.takes.find((t) => t.name.toLowerCase().includes(args.take.toLowerCase()));
    if (!take) { console.error(`no take matching "${args.take}"`); return 1; }
    console.log(`take "${take.name}"`);
    console.log(`  duration      ${take.duration}s, up to ${take.keys} keys per channel`);
    console.log(`  animated      ${take.animatedChannels}/${take.totalChannels} channels (${take.percentAnimated}%)`);
    console.log(`  bones moving  ${take.movingBones.length} (${take.staticBoneCount} static)`);
    if (take.movingBones.length) console.log(`  ${take.movingBones.join(', ')}`);
    return 0;
  }

  const rows = args.all ? report.takes : best.slice(0, 12);
  const width = Math.max(...rows.map((t) => t.name.length));
  console.log(`${'take'.padEnd(width)}  animated   keys   dur    bones moving`);
  for (const t of rows) {
    const flagged = t.animatedChannels === 0 ? '  <-- no movement' : '';
    console.log(
      `${t.name.padEnd(width)}  ${String(t.animatedChannels).padStart(4)}/${String(t.totalChannels).padEnd(4)}`
      + ` ${String(t.keys).padStart(5)}  ${String(t.duration).padStart(5)}s  ${String(t.movingBones.length).padStart(3)}${flagged}`,
    );
  }
  if (!args.all && report.takes.length > rows.length) {
    console.log(`\n(${report.takes.length - rows.length} more; pass --all to list every take)`);
  }
  return 0;
}

function usage() {
  console.log(`Report what animation a binary FBX actually contains.

  node Tools/fbx-animation-report.mjs <file.fbx> [options]

  --take <name>   detail one take, including which bones move
  --all           list every take rather than the twelve most animated
  --json          machine-readable output

A take whose channels are all constant has keyframes but no movement. That is
what an export with baking disabled looks like, and it is invisible to every
check above the file itself.`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  process.exit(main(process.argv.slice(2)));
}
