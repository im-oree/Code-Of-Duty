import { defineConfig, type Plugin } from 'vite';
import fs from 'node:fs';
import path from 'node:path';

/** The Unity project root (one level above UnityWeb/). */
const PROJECT_ROOT = path.resolve(__dirname, '..');
const ASSETS_ROOT = path.join(PROJECT_ROOT, 'Assets');

/** Guard against path traversal: every served path must stay inside the project. */
function safeJoin(rel: string): string | null {
  const clean = rel.replace(/^\/+/, '');
  const abs = path.resolve(PROJECT_ROOT, clean);
  if (!abs.startsWith(PROJECT_ROOT + path.sep)) return null;
  return abs;
}

interface IndexEntry { path: string; guid: string }

let cachedIndex: { built: number; entries: IndexEntry[] } | null = null;

/**
 * Walk Assets/** for .meta files and build the guid -> path map.
 * Done in Node rather than the browser: one pass over ~2,000 small files is
 * fast here and would be thousands of round trips over HTTP.
 */
function buildGuidIndex(): IndexEntry[] {
  const entries: IndexEntry[] = [];
  const skipDirs = new Set(['Library', 'Temp', 'Logs', 'obj', 'node_modules']);

  const walk = (dir: string) => {
    let items: fs.Dirent[];
    try { items = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const item of items) {
      if (item.name.startsWith('.')) continue;
      const full = path.join(dir, item.name);
      if (item.isDirectory()) {
        if (skipDirs.has(item.name)) continue;
        walk(full);
      } else if (item.name.endsWith('.meta')) {
        let src: string;
        try { src = fs.readFileSync(full, 'utf8'); } catch { continue; }
        const m = /^guid:\s*([0-9a-fA-F]{32})\s*$/m.exec(src);
        if (!m) continue;
        const assetPath = path.relative(PROJECT_ROOT, full.slice(0, -5)).split(path.sep).join('/');
        entries.push({ path: assetPath, guid: m[1] });
      }
    }
  };
  walk(ASSETS_ROOT);
  return entries;
}

function unityProjectPlugin(): Plugin {
  return {
    name: 'unity-project-bridge',
    configureServer(server) {
      // GUID -> asset path index
      server.middlewares.use('/api/index', (_req, res) => {
        if (!cachedIndex || Date.now() - cachedIndex.built > 30_000) {
          cachedIndex = { built: Date.now(), entries: buildGuidIndex() };
        }
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ entries: cachedIndex.entries }));
      });

      // Directory listing, used by the viewer's scene picker
      server.middlewares.use('/api/list', (req, res) => {
        const url = new URL(req.url ?? '', 'http://localhost');
        const ext = (url.searchParams.get('ext') ?? '.unity').split(',');
        const out: string[] = [];
        const walk = (dir: string) => {
          let items: fs.Dirent[];
          try { items = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
          for (const item of items) {
            if (item.name.startsWith('.')) continue;
            const full = path.join(dir, item.name);
            if (item.isDirectory()) walk(full);
            else if (ext.some((e) => item.name.endsWith(e))) {
              out.push(path.relative(PROJECT_ROOT, full).split(path.sep).join('/'));
            }
          }
        };
        walk(ASSETS_ROOT);
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ files: out.sort() }));
      });

      // Raw asset bytes (text or binary)
      server.middlewares.use('/api/file', (req, res) => {
        const url = new URL(req.url ?? '', 'http://localhost');
        const rel = url.searchParams.get('path') ?? '';
        const abs = safeJoin(rel);
        if (!abs || !fs.existsSync(abs) || !fs.statSync(abs).isFile()) {
          res.statusCode = 404;
          res.end('not found');
          return;
        }
        const ext = path.extname(abs).toLowerCase();
        const binary = ['.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr', '.fbx', '.glb', '.gltf', '.bin'];
        res.setHeader('Content-Type', binary.includes(ext) ? 'application/octet-stream' : 'text/plain; charset=utf-8');
        res.setHeader('Cache-Control', 'no-cache');
        res.end(fs.readFileSync(abs));
      });
    },
  };
}

export default defineConfig({
  plugins: [unityProjectPlugin()],
  server: {
    host: '0.0.0.0',
    port: 5180,
    strictPort: true,
    // The Arena live preview proxies through https://{port}-{sandbox}.e2b.app,
    // so every host must be accepted or the dev server rejects the request.
    allowedHosts: true,
    fs: { allow: [PROJECT_ROOT] },
    hmr: { clientPort: 443, protocol: 'wss' },
  },
  preview: { host: '0.0.0.0', port: 5180, allowedHosts: true },
});
