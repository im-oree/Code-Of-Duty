#!/usr/bin/env node
/**
 * Headless capture: render a real Unity scene and write a PNG.
 *
 *   npm run shot -- --scene Assets/Scenes/StartMenu.unity --out ../Artifacts/p0/menu.png
 *   npm run shot -- --scene Assets/Scenes/DMArena1.unity --camera orbit \
 *                   --orbit 0.9,0.35,40 --size 1280x720
 *
 * It starts the vite dev server itself (so the agent and the user look at the
 * exact same page), drives it through `window.uw`, screenshots the canvas, and
 * shuts everything down. See Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md.
 */

import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB_ROOT = path.resolve(HERE, '..');

const CHROME = process.env.UW_CHROME || '/home/user/.cache/chromium/bin/chromium';
const CHROME_LIBS = process.env.UW_CHROME_LIBS || '/home/user/.cache/chromium/lib/lib';

/* ---------------- args ---------------- */

function parseArgs(argv) {
  const out = { size: [1280, 720], camera: null, orbit: null, wait: 1200, port: 5180 };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => argv[++i];
    if (a === '--scene') out.scene = next();
    else if (a === '--out') out.out = next();
    else if (a === '--camera') out.camera = next();
    else if (a === '--orbit') out.orbit = next().split(',').map(Number);
    else if (a === '--size') out.size = next().split('x').map(Number);
    else if (a === '--wait') out.wait = parseInt(next(), 10);
    else if (a === '--port') out.port = parseInt(next(), 10);
    else if (a === '--keep-server') out.keepServer = true;
    else if (a === '--overlay') out.overlay = true;
    else if (a === '--chrome') out.chrome = true;
    else if (a === '--no-ui') out.noUi = true;
    else if (a === '--frame') out.frame = next();
    else if (a === '--eval') out.evals = [...(out.evals ?? []), next()];
    else if (a === '--no-orphan') out.noOrphan = true;
    else if (a === '--no-gizmos') out.noGizmos = true;
    else if (a === '--wire') out.wire = true;
    else if (a === '--fov') out.fov = Number(next());
    else if (a === '--look') out.look = next().split(',').map(Number);
    else if (a === '--select') out.select = next();
    else if (a === '--anim') out.anim = next();
    else if (a === '--anim-target') out.animTarget = next();
    else if (a === '--anim-time') out.animTime = Number(next());
    else if (a === '--anim-speed') out.animSpeed = Number(next());
    else if (a === '--advance') out.advance = Number(next());
    else if (a === '--list-anims') out.listAnims = true;
    else if (a === '--inspect') out.inspect = [...(out.inspect ?? []), next()];
    else if (a === '--dump-ui') out.dumpUi = true;
    else if (a === '--json') out.json = next();
    else if (a === '--help' || a === '-h') out.help = true;
  }
  return out;
}

const USAGE = `
Unity Web Clone — headless capture and inspection.

  npm run shot -- --scene <path> [options]

Scene / output
  --scene <Assets/...>     scene to open (required)
  --out <file.png>         screenshot path (default ../Artifacts/<scene>.png)
  --size WxH               viewport size (default 1280x720)
  --wait <ms>              settle time before capture (default 1200)
  --chrome                 keep the full editor UI in the shot
  --overlay                keep the corner overlay when --chrome is off
  --port <n>               dev server port (default 5180)
  --keep-server            leave vite running afterwards
  --json <file.json>       write the full report (scene + anim + ui) to a file

Camera
  --camera <name|orbit>    pick a scene camera, or the free orbit camera
  --orbit y,p,d[,tx,ty,tz] orbit yaw/pitch/distance and target
  --look fx,fy,fz,tx,ty,tz place the camera at f, aimed at t (three coords)
  --frame <name|''>        frame an object (empty string = whole scene)
  --fov <deg>              override the active camera's vertical FOV

View
  --no-ui                  hide the uGUI layer
  --no-orphan              hide UI that has no Canvas ancestor
  --no-gizmos              hide grid and light markers
  --wire                   wireframe every material

Animation
  --list-anims             print animators, clips and controller states
  --anim <clip|state>      play a clip or controller state
  --anim-target <name>     which animator (default: the first one)
  --anim-time <0..1>       freeze at a normalised position in the clip
  --anim-speed <x>         playback speed
  --advance <seconds>      step the animation forward before capturing

Inspection
  --select <name>          select an object (shown in the Inspector)
  --inspect <name>         print an object's full component dump (repeatable)
  --dump-ui                print the solved UI layout rects
  --eval "<js>"            evaluate an expression on the page (repeatable)
`.trim();

const args = parseArgs(process.argv.slice(2));
if (args.help || !args.scene) {
  console.log(USAGE);
  process.exit(args.help ? 0 : 2);
}
const outPath = path.resolve(WEB_ROOT, args.out ?? `../Artifacts/${path.basename(args.scene, '.unity')}.png`);

/* ---------------- dev server ---------------- */

async function waitForServer(url, timeoutMs = 60_000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(2000) });
      if (res.ok) return true;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 300));
  }
  return false;
}

let server = null;
const baseUrl = `http://127.0.0.1:${args.port}`;

async function ensureServer() {
  if (await waitForServer(baseUrl, 1500)) {
    console.log(`[shot] reusing dev server on ${args.port}`);
    return false;
  }
  console.log('[shot] starting vite…');
  server = spawn('npx', ['vite', '--host', '127.0.0.1', '--port', String(args.port), '--strictPort'], {
    cwd: WEB_ROOT, stdio: ['ignore', 'pipe', 'pipe'], env: process.env,
  });
  server.stdout.on('data', (d) => process.env.UW_VERBOSE && process.stdout.write(`[vite] ${d}`));
  server.stderr.on('data', (d) => process.stderr.write(`[vite] ${d}`));
  if (!(await waitForServer(baseUrl))) throw new Error('vite failed to start');
  return true;
}

/* ---------------- capture ---------------- */

async function capture() {
  const { chromium } = await import('playwright-core');

  const browser = await chromium.launch({
    executablePath: CHROME,
    headless: true,
    env: { ...process.env, LD_LIBRARY_PATH: CHROME_LIBS },
    args: [
      '--no-sandbox', '--disable-dev-shm-usage',
      '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
      '--in-process-gpu', '--disable-gpu-sandbox', '--hide-scrollbars',
    ],
  });

  const page = await browser.newPage({ viewport: { width: args.size[0], height: args.size[1] } });
  const logs = [];
  page.on('console', (m) => logs.push(`[${m.type()}] ${m.text()}`));
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));

  const url = `${baseUrl}/?scene=${encodeURIComponent(args.scene)}` +
              (args.camera ? `&camera=${encodeURIComponent(args.camera)}` : '') +
              (args.chrome ? '' : '&clean=1') +
              (args.overlay ? '&overlay=1' : '');
  await page.goto(url, { waitUntil: 'domcontentloaded' });

  // Wait for the scene build to finish (or fail with a readable reason).
  try {
    await page.waitForFunction('window.__uwReady === true || window.__uwError', null, { timeout: 120_000 });
  } catch {
    console.error('[shot] timed out waiting for the scene to build');
  }

  const err = await page.evaluate('window.__uwError ?? null');
  if (err) {
    console.error(`[shot] scene build failed: ${err}`);
    logs.slice(-20).forEach((l) => console.error('   ' + l));
    await browser.close();
    process.exitCode = 1;
    return;
  }

  if (args.orbit) {
    const [yaw, pitch, dist, tx = 0, ty = 1.2, tz = 0] = args.orbit;
    await page.evaluate(
      ([y, p, d, x, yy, z]) => window.uw.setOrbit(y, p, d, x, yy, z),
      [yaw, pitch, dist, tx, ty, tz],
    );
  }

  if (args.look) {
    const [fx, fy, fz, tx = 0, ty = 1, tz = 0] = args.look;
    const placed = await page.evaluate(
      ([a, b]) => window.uw.lookAt(a, b),
      [[fx, fy, fz], [tx, ty, tz]],
    );
    console.log(`[shot] camera ${JSON.stringify(placed)}`);
  }

  if (args.fov !== undefined) await page.evaluate((f) => window.uw.setFov(f), args.fov);
  if (args.noOrphan) await page.evaluate('window.uw.setOrphanUi(false)');
  if (args.noGizmos) await page.evaluate('window.uw.setGizmos(false)');
  if (args.wire) await page.evaluate('window.uw.setWireframe(true)');
  if (args.select) {
    const sel = await page.evaluate((n) => window.uw.select(n), args.select);
    console.log(`[shot] selected ${sel ? sel.name : 'NOTHING (' + args.select + ')'}`);
  }

  /* ---- animation ---- */

  const animators = await page.evaluate('window.uw.animators()');
  if (args.listAnims) {
    if (!animators.length) console.log('[anim] no Animator components in this scene');
    for (const a of animators) {
      console.log(`[anim] ${a.path}`);
      console.log(`         controller: ${a.controller ?? '(none)'}`);
      console.log(`         playing:    ${a.playing ?? '(nothing)'}`);
      if (a.states?.length) console.log(`         states:     ${a.states.join(', ')}`);
      console.log(`         clips (${a.clips.length}):`);
      for (const c of a.clips) console.log(`           - ${c.name}  ${c.duration.toFixed(2)}s`);
    }
  }

  if (args.anim) {
    const ok = await page.evaluate(
      ([clip, who]) => window.uw.play(clip, who ?? undefined),
      [args.anim, args.animTarget ?? null],
    );
    console.log(`[anim] play "${args.anim}" => ${ok ? 'ok' : 'FAILED'}`);
    if (!ok && animators[0]) {
      console.log(`[anim] available: ${animators[0].clips.map((c) => c.name).join(', ')}`);
    }
  }
  if (args.animSpeed !== undefined) {
    await page.evaluate(([s, w]) => window.uw.setAnimSpeed(s, w ?? undefined),
                        [args.animSpeed, args.animTarget ?? null]);
  }
  if (args.advance !== undefined) {
    await page.evaluate((d) => window.uw.stepAnim(d), args.advance);
  }
  if (args.animTime !== undefined) {
    // Freeze the pose so the screenshot is deterministic, then sample it.
    await page.evaluate('window.uw.setPaused(true)');
    await page.evaluate(([t, w]) => window.uw.setAnimNormalized(t, w ?? undefined),
                        [args.animTime, args.animTarget ?? null]);
    console.log(`[anim] frozen at normalised t=${args.animTime}`);
  }

  // Frame the whole scene, or one named object, before any manual orbit.
  if (args.frame !== undefined) {
    const found = await page.evaluate(
      (sel) => window.uw.frameObject(sel),
      args.frame === '' ? null : args.frame,
    );
    if (!found) console.warn(`[shot] frame target not found: ${args.frame}`);
  }

  // Let textures finish decoding and a few frames settle.
  await page.waitForTimeout(args.wait);
  // Fonts and sprites load lazily, so paint the UI twice: the first pass warms
  // the caches, the second renders with everything resident.
  if (args.noUi) await page.evaluate('window.uw.setUiVisible(false)');
  await page.evaluate('window.uw.paintUi()');
  await page.evaluate('window.uw.paintUi()');
  await page.evaluate('window.uw.renderOnce()');

  if (args.animTime !== undefined) {
    // Re-apply after the settle wait, since the render loop may have advanced.
    await page.evaluate(([t, w]) => window.uw.setAnimNormalized(t, w ?? undefined),
                        [args.animTime, args.animTarget ?? null]);
    await page.evaluate('window.uw.renderOnce()');
  }

  const report = await page.evaluate('window.uw.report()');
  const cameras = await page.evaluate('window.uw.cameras()');
  const uiStats = await page.evaluate('window.uw.uiStats()');
  const cameraState = await page.evaluate('window.uw.cameraState()');

  // Arbitrary probes against the live page — the fastest way to diagnose a
  // scene without adding one-off code to the viewer. Run last, so what they
  // report is the state that was actually captured.
  for (const expr of args.evals ?? []) {
    try {
      const value = await page.evaluate(`(() => (${expr}))()`);
      console.log(`[eval] ${expr} =>`, JSON.stringify(value, null, 2));
    } catch (err) {
      console.log(`[eval] ${expr} => ERROR ${err.message}`);
    }
  }

  for (const needle of args.inspect ?? []) {
    const data = await page.evaluate((n) => window.uw.inspect(n), needle);
    console.log(`[inspect] ${needle} =>`, JSON.stringify(data, null, 2));
  }
  if (args.dumpUi) {
    const layout = await page.evaluate('window.uw.uiLayout()');
    console.log(`[ui] ${layout.length} nodes`);
    for (const n of layout) {
      console.log(`  ${'  '.repeat(n.depth)}${n.active ? '' : '(off) '}${n.name} ` +
                  `[${n.rect.join(', ')}] ${n.components.join(',')}`);
    }
  }

  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  // With the editor chrome on we want the whole window; otherwise just the
  // viewport canvas, so the PNG is pure rendered scene with no borders.
  if (args.chrome) {
    await page.screenshot({ path: outPath });
  } else {
    const canvas = await page.$('#viewport canvas');
    await (canvas ?? page).screenshot({ path: outPath });
  }

  console.log(`[shot] wrote ${outPath}`);
  if (uiStats && uiStats.widgets) {
    console.log(`[shot] ui: ${uiStats.canvases} canvas, ${uiStats.widgets} widgets, ` +
                `${uiStats.images} images, ${uiStats.texts} texts`);
  }
  if (report) {
    console.log(`[shot] models=${report.modelMeshes} skinned=${report.skinnedMeshes} ` +
                `resolution=${JSON.stringify(report.meshResolution ?? {})}`);
    console.log(`[shot] objects=${report.gameObjects} meshes=${report.meshes} ` +
                `lights=${report.lights} cameras=${report.cameras} canvases=${report.canvases}`);
    if (report.warnings?.length) {
      const counts = new Map();
      for (const w of report.warnings) {
        const key = w.replace(/:.*$/, '');
        counts.set(key, (counts.get(key) ?? 0) + 1);
      }
      console.log(`[shot] ${report.warnings.length} warning(s):`);
      for (const [k, v] of counts) console.log(`         ${v}x ${k}`);
    }
  }
  if (cameras?.length) console.log(`[shot] cameras: ${cameras.map((c) => c.name).join(', ')}`);
  console.log(`[shot] view: ${JSON.stringify(cameraState)}`);
  if (animators?.length) {
    console.log(`[shot] animators: ${animators.map((a) => `${a.path}(${a.clips.length} clips` +
                `${a.playing ? ', playing ' + a.playing : ''})`).join(', ')}`);
  }

  if (args.json) {
    const jsonPath = path.resolve(WEB_ROOT, args.json);
    fs.mkdirSync(path.dirname(jsonPath), { recursive: true });
    fs.writeFileSync(jsonPath, JSON.stringify({
      scene: args.scene, out: outPath, report, cameras, cameraState, uiStats, animators,
    }, null, 2));
    console.log(`[shot] wrote ${jsonPath}`);
  }
  if (process.env.UW_VERBOSE) logs.slice(-40).forEach((l) => console.log('   ' + l));

  await browser.close();
}

/* ---------------- main ---------------- */

let startedServer = false;
try {
  startedServer = await ensureServer();
  await capture();
} catch (e) {
  console.error('[shot] ' + (e?.stack ?? e));
  process.exitCode = 1;
} finally {
  if (server && startedServer && !args.keepServer) {
    server.kill('SIGTERM');
    setTimeout(() => server?.kill('SIGKILL'), 2000).unref();
  }
  setTimeout(() => process.exit(process.exitCode ?? 0), 300).unref();
}
