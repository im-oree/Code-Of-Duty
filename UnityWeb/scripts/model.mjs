#!/usr/bin/env node
/**
 * Build a model, export it into the Unity project, and preview it — headless.
 *
 *   npm run model -- --script models/crate.model.ts
 *   npm run model -- --script models/crate.model.ts --preview
 *   npm run model -- --script models/crate.model.ts --into Assets/Scenes/StartMenu.unity
 *
 * A model script is an ES module whose default export takes a `Modeler` and
 * builds into it. Geometry is generated in Node — no browser needed — and the
 * `.glb` plus a `.meta` with a stable GUID are written under `Assets/`.
 *
 * `--preview` then renders it *inside a real Unity scene* through the Unity
 * Web Clone, so scale, orientation and materials are judged in context. That
 * is the whole point of binding the two together: a weapon that is secretly
 * 10x too big looks perfectly fine on its own.
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB_ROOT = path.resolve(HERE, '..');
const PROJECT_ROOT = path.resolve(WEB_ROOT, '..');

function parseArgs(argv) {
  const out = { preview: false, verify: true };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => argv[++i];
    if (a === '--script') out.script = next();
    else if (a === '--out') out.out = next();
    else if (a === '--preview') out.preview = true;
    else if (a === '--into') { out.into = next(); out.preview = true; }
    else if (a === '--at') out.at = next();
    else if (a === '--shot') { out.shot = next(); out.preview = true; }
    else if (a === '--size') out.size = next();
    else if (a === '--no-verify') out.verify = false;
    else if (a === '--help' || a === '-h') out.help = true;
    else if (!out.script) out.script = a;
  }
  return out;
}

const HELP = `
build a model headlessly and preview it inside a Unity scene

  --script <file>   model script (.ts or .js) exporting a default builder
  --out <path>      where to write the .glb, project-relative
                    (default: Assets/Models/Generated/<name>.glb)
  --preview         render the result and write a PNG
  --into <scene>    scene to preview inside (default Assets/Scenes/StartMenu.unity)
  --at <x,y,z>      Unity-space position to place it at (default 0,0,0)
  --shot <path>     PNG path (default Artifacts/models/<name>.png)
  --size <WxH>      preview size (default 900x700)
  --no-verify       skip the Unity round-trip check
`;

const args = parseArgs(process.argv.slice(2));
if (args.help || !args.script) {
  console.log(HELP);
  process.exit(args.help ? 0 : 1);
}

/* ---------------------------------------------------------------- build --- */

const scriptPath = path.resolve(process.cwd(), args.script);
if (!fs.existsSync(scriptPath)) {
  console.error(`[model] no such script: ${scriptPath}`);
  process.exit(1);
}

const { Modeler } = await import('../src/model/Modeler.ts');
const builder = (await import(pathToFileURL(scriptPath).href)).default;
if (typeof builder !== 'function') {
  console.error('[model] the script must default-export a function taking a Modeler');
  process.exit(1);
}

const name = path.basename(scriptPath).replace(/\.(model\.)?(ts|js|mjs)$/, '');
const modeler = new Modeler(name);
await builder(modeler);

const summary = modeler.summary();
console.log(`[model] ${summary.name}: ${summary.totals.vertices} verts, `
  + `${summary.totals.faces} faces, ${summary.parts.length} part(s)`);
for (const part of summary.parts) {
  console.log(`         ${part.name.padEnd(18)} ${String(part.vertices).padStart(5)} v  `
    + `${String(part.faces).padStart(5)} f`);
}
if (summary.rig) {
  console.log(`[model] rig: ${summary.rig.bones} bones, skinned part = ${summary.rig.skinned}`);
}

/* --------------------------------------------------------------- export --- */

const outRel = args.out ?? `Assets/Models/Generated/${name}.glb`;
const outAbs = path.join(PROJECT_ROOT, outRel);
fs.mkdirSync(path.dirname(outAbs), { recursive: true });

const { glb, report } = args.verify
  ? await modeler.toGlbVerified()
  : { glb: await modeler.toGlb(), report: null };

fs.writeFileSync(outAbs, glb);
console.log(`[model] wrote ${outRel} (${(glb.length / 1024).toFixed(1)} KB)`);

if (report) {
  console.log(`[model] unity round-trip: ${report.ok ? 'OK' : 'FAILED'} — ${report.detail}`);
  if (!report.ok) process.exitCode = 1;
}

// A .meta with a deterministic GUID, so regenerating the model does not break
// every scene and prefab that references it.
const { stableGuid, modelMeta } = await import('../src/model/UnityExport.ts');
const metaPath = `${outAbs}.meta`;
if (!fs.existsSync(metaPath)) {
  fs.writeFileSync(metaPath, modelMeta(stableGuid(outRel), { rig: !!summary.rig }));
  console.log(`[model] wrote ${outRel}.meta (guid ${stableGuid(outRel)})`);
}

/* -------------------------------------------------------------- preview --- */

if (args.preview) {
  const scene = args.into ?? 'Assets/Scenes/StartMenu.unity';
  const shot = args.shot ?? `../Artifacts/models/${name}.png`;
  const at = args.at ?? '0,0,0';
  const [x, y, z] = at.split(',').map(Number);

  console.log(`[model] previewing in ${scene} at Unity (${at})`);
  const result = spawnSync('node', [
    path.join(HERE, 'shot.mjs'),
    '--scene', scene,
    '--out', shot,
    '--size', args.size ?? '900x700',
    '--no-ui', '--no-gizmos', '--keep-server',
    // --pre-eval runs before --frame, so the framing sees the new object.
    '--pre-eval', `await window.uw.addModel(${JSON.stringify(outRel)},`
      + `{position:{x:${x},y:${y},z:${z}},name:${JSON.stringify(name)}})`,
    '--frame', name,
  ], { cwd: WEB_ROOT, stdio: 'inherit' });
  process.exitCode = result.status ?? 0;
}
