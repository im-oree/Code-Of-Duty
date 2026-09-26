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
  }
  return out;
}

const args = parseArgs(process.argv.slice(2));
if (!args.scene) {
  console.error('usage: shot --scene Assets/Scenes/X.unity [--out out.png] [--camera name|orbit] ' +
                '[--orbit yaw,pitch,dist] [--size WxH]');
  process.exit(2);
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

  // Arbitrary probes against the live page — the fastest way to diagnose a
  // scene without adding one-off code to the viewer.
  for (const expr of args.evals ?? []) {
    try {
      const value = await page.evaluate(`(() => (${expr}))()`);
      console.log(`[eval] ${expr} =>`, JSON.stringify(value, null, 2));
    } catch (err) {
      console.log(`[eval] ${expr} => ERROR ${err.message}`);
    }
  }

  // Frame the whole scene, or one named object, before any manual orbit.
  if (args.frame !== undefined) {
    const found = await page.evaluate(
      (sel) => window.uw.frameObject(sel),
      args.frame === '' ? null : args.frame,
    );
    if (!found) console.warn(`[shot] frame target not found: ${args.frame}`);
  }

  if (args.orbit) {
    const [yaw, pitch, dist, tx = 0, ty = 1.2, tz = 0] = args.orbit;
    await page.evaluate(
      ([y, p, d, x, yy, z]) => window.uw.setOrbit(y, p, d, x, yy, z),
      [yaw, pitch, dist, tx, ty, tz],
    );
  }

  // Let textures finish decoding and a few frames settle.
  await page.waitForTimeout(args.wait);
  // Fonts and sprites load lazily, so paint the UI twice: the first pass warms
  // the caches, the second renders with everything resident.
  if (args.noUi) await page.evaluate('window.uw.setUiVisible(false)');
  await page.evaluate('window.uw.paintUi()');
  await page.evaluate('window.uw.paintUi()');
  await page.evaluate('window.uw.renderOnce()');

  const report = await page.evaluate('window.uw.report()');
  const cameras = await page.evaluate('window.uw.cameras()');
  const uiStats = await page.evaluate('window.uw.uiStats()');

  fs.mkdirSync(path.dirname(outPath), { recursive: true });
  const canvas = await page.$('#viewport canvas');
  await (canvas ?? page).screenshot({ path: outPath });

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
