/**
 * The Project pane.
 *
 * Unity's two-pane browser: a folder tree on the left over Assets, Packages and
 * ProjectSettings, and the contents of the selected folder on the right. Double
 * clicking a `.unity` file loads it; any other file is shown in the Inspector
 * with its guid, size and — for text assets — a preview of the raw source.
 *
 * Packages matter here because half of what a Unity project references lives
 * outside Assets, and being unable to see it was a real gap when tracking down
 * which script GUID belonged to which package type.
 */

import { iconForFile, namedIcon } from './Icons.ts';

export interface ProjectEntry {
  name: string;
  path: string;
  dir: boolean;
  size: number;
  guid?: string;
}

export interface ProjectCallbacks {
  onOpenScene(path: string): void;
  onSelectAsset(entry: ProjectEntry): void;
}

function humanSize(bytes: number): string {
  if (bytes <= 0) return '';
  const units = ['B', 'KB', 'MB', 'GB'];
  let i = 0;
  let n = bytes;
  while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
  return `${n < 10 && i > 0 ? n.toFixed(1) : Math.round(n)} ${units[i]}`;
}

export class ProjectBrowser {
  private treeEl: HTMLElement;
  private listEl: HTMLElement;
  private pathEl: HTMLElement;
  private countEl: HTMLElement;
  private expanded = new Set<string>(['Assets']);
  private childrenCache = new Map<string, ProjectEntry[]>();
  private currentDir = 'Assets';
  private selected: string | null = null;
  private roots: string[] = ['Assets'];

  constructor(private cb: ProjectCallbacks) {
    this.treeEl = document.getElementById('proj-tree')!;
    this.listEl = document.getElementById('proj-list')!;
    this.pathEl = document.getElementById('proj-path')!;
    this.countEl = document.getElementById('proj-count')!;
  }

  async init(): Promise<void> {
    try {
      const res = await fetch('/api/roots');
      const data = await res.json() as { roots: string[]; unityVersion: string };
      this.roots = data.roots;
      const v = document.getElementById('unity-version');
      if (v) v.textContent = `Unity ${data.unityVersion}`;
    } catch { /* keep defaults */ }
    await this.openDir('Assets');
    await this.renderTree();
  }

  private async fetchDir(dir: string): Promise<ProjectEntry[]> {
    const cached = this.childrenCache.get(dir);
    if (cached) return cached;
    try {
      const res = await fetch(`/api/tree?path=${encodeURIComponent(dir)}`);
      const data = await res.json() as { entries: ProjectEntry[] };
      this.childrenCache.set(dir, data.entries);
      return data.entries;
    } catch {
      return [];
    }
  }

  async openDir(dir: string): Promise<void> {
    this.currentDir = dir;
    const entries = await this.fetchDir(dir);
    this.pathEl.textContent = dir;
    this.countEl.textContent = `${entries.length} item${entries.length === 1 ? '' : 's'}`;
    this.listEl.innerHTML = '';

    for (const e of entries) {
      const item = document.createElement('div');
      item.className = 'pitem';
      if (this.selected === e.path) item.classList.add('sel');
      item.innerHTML =
        `<span class="ico">${iconForFile(e.name, e.dir, 30)}</span>` +
        `<span class="nm"></span>` +
        `<span class="sz">${e.dir ? '' : humanSize(e.size)}</span>`;
      item.querySelector('.nm')!.textContent = e.name;
      item.title = `${e.path}${e.guid ? `\nguid: ${e.guid}` : ''}`;

      item.addEventListener('click', () => {
        this.selected = e.path;
        this.cb.onSelectAsset(e);
        void this.openDir(this.currentDir);
      });
      item.addEventListener('dblclick', () => {
        if (e.dir) {
          this.expanded.add(e.path);
          void this.openDir(e.path).then(() => this.renderTree());
        } else if (e.name.endsWith('.unity')) {
          this.cb.onOpenScene(e.path);
        }
      });
      this.listEl.appendChild(item);
    }
  }

  private async renderTree(): Promise<void> {
    this.treeEl.innerHTML = '';
    for (const root of this.roots) {
      await this.renderBranch(root, root, 0);
    }
  }

  private async renderBranch(dir: string, name: string, depth: number): Promise<void> {
    const entries = await this.fetchDir(dir);
    const hasDirs = entries.some((e) => e.dir);
    const open = this.expanded.has(dir);

    const row = document.createElement('div');
    row.className = 'hrow';
    if (this.currentDir === dir) row.classList.add('sel');
    row.style.paddingLeft = `${depth * 13}px`;
    row.innerHTML =
      `<span class="htwist${hasDirs ? '' : ' leaf'}">${open ? '▼' : '▶'}</span>` +
      `<span class="hicon">${namedIcon('folder')}</span><span class="hname"></span>`;
    row.querySelector('.hname')!.textContent = name;

    row.querySelector('.htwist')!.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!hasDirs) return;
      if (open) this.expanded.delete(dir); else this.expanded.add(dir);
      void this.renderTree();
    });
    row.addEventListener('click', () => {
      this.expanded.add(dir);
      void this.openDir(dir).then(() => this.renderTree());
    });
    this.treeEl.appendChild(row);

    if (open) {
      for (const e of entries.filter((x) => x.dir)) {
        await this.renderBranch(e.path, e.name, depth + 1);
      }
    }
  }
}
