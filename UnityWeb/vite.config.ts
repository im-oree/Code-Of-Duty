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

      // Project tree: Assets, Packages and ProjectSettings, for the Project
      // browser. Returns directories and files with sizes and guids so the
      // browser can render Unity's two-pane layout without extra round trips.
      server.middlewares.use('/api/tree', (req, res) => {
        const url = new URL(req.url ?? '', 'http://localhost');
        const rel = url.searchParams.get('path') ?? 'Assets';
        const abs = safeJoin(rel);
        const out: Array<Record<string, unknown>> = [];
        if (abs && fs.existsSync(abs) && fs.statSync(abs).isDirectory()) {
          for (const item of fs.readdirSync(abs, { withFileTypes: true })) {
            if (item.name.startsWith('.') || item.name.endsWith('.meta')) continue;
            const full = path.join(abs, item.name);
            let size = 0;
            try { size = item.isFile() ? fs.statSync(full).size : 0; } catch { /* ignore */ }
            let guid: string | undefined;
            try {
              const meta = fs.readFileSync(`${full}.meta`, 'utf8');
              guid = /^guid:\s*([0-9a-fA-F]{32})\s*$/m.exec(meta)?.[1];
            } catch { /* no meta */ }
            out.push({
              name: item.name,
              path: path.relative(PROJECT_ROOT, full).split(path.sep).join('/'),
              dir: item.isDirectory(),
              size,
              guid,
            });
          }
        }
        out.sort((a, b) => (a.dir === b.dir
          ? String(a.name).localeCompare(String(b.name))
          : (a.dir ? -1 : 1)));
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ path: rel, entries: out }));
      });

      // Roots the Project browser offers.
      server.middlewares.use('/api/roots', (_req, res) => {
        const roots = ['Assets', 'Packages', 'ProjectSettings']
          .filter((r) => fs.existsSync(path.join(PROJECT_ROOT, r)));
        let unityVersion = 'unknown';
        try {
          const pv = fs.readFileSync(path.join(PROJECT_ROOT, 'ProjectSettings/ProjectVersion.txt'), 'utf8');
          unityVersion = /m_EditorVersion:\s*(.+)/.exec(pv)?.[1]?.trim() ?? 'unknown';
        } catch { /* ignore */ }
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({ roots, unityVersion }));
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
        const binary = ['.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr', '.fbx', '.glb',
                        '.gltf', '.bin', '.ttf', '.otf', '.wav', '.mp3', '.ogg', '.dll'];
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
