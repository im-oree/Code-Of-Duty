/**
 * The Hierarchy pane.
 *
 * Mirrors Unity's: indented tree, expand/collapse twisties, inactive objects
 * greyed and italic, icons derived from the components present, and a search
 * field that flattens to matches. Selection is shared with the Inspector.
 */

import type { NodeInfo } from '../runtime/SceneBuilder.ts';
import { iconForComponents } from './Icons.ts';

export interface HierarchyCallbacks {
  onSelect(node: NodeInfo): void;
}

export class HierarchyPane {
  private root: HTMLElement;
  private searchEl: HTMLInputElement;
  private countEl: HTMLElement;
  private nodes: NodeInfo[] = [];
  private expanded = new Set<number>();
  private selected: number | null = null;
  private filter = '';

  constructor(private cb: HierarchyCallbacks) {
    this.root = document.getElementById('hierarchy-body')!;
    this.searchEl = document.getElementById('hier-search') as HTMLInputElement;
    this.countEl = document.getElementById('hier-count')!;
    this.searchEl.addEventListener('input', () => {
      this.filter = this.searchEl.value.trim().toLowerCase();
      this.render();
    });
  }

  setScene(nodes: NodeInfo[]): void {
    this.nodes = nodes;
    this.expanded.clear();
    // Unity opens the roots by default; deep trees stay collapsed.
    for (const n of nodes) this.expanded.add(n.fileID);
    this.selected = null;
    this.render();
  }

  select(fileID: number): void {
    this.selected = fileID;
    // Open every ancestor so the selection is actually on screen.
    const path: number[] = [];
    const find = (list: NodeInfo[], trail: number[]): boolean => {
      for (const n of list) {
        if (n.fileID === fileID) { path.push(...trail); return true; }
        if (find(n.children, [...trail, n.fileID])) return true;
      }
      return false;
    };
    find(this.nodes, []);
    for (const id of path) this.expanded.add(id);
    this.render();
  }

  private countAll(list: NodeInfo[]): number {
    let n = 0;
    for (const x of list) n += 1 + this.countAll(x.children);
    return n;
  }

  private render(): void {
    this.root.innerHTML = '';
    const frag = document.createDocumentFragment();

    if (this.filter) {
      const hits: Array<{ node: NodeInfo; path: string }> = [];
      const walk = (list: NodeInfo[], prefix: string) => {
        for (const n of list) {
          const p = prefix ? `${prefix}/${n.name}` : n.name;
          if (n.name.toLowerCase().includes(this.filter)) hits.push({ node: n, path: p });
          walk(n.children, p);
        }
      };
      walk(this.nodes, '');
      for (const hit of hits.slice(0, 500)) {
        frag.appendChild(this.row(hit.node, 0, false, hit.path));
      }
      this.countEl.textContent = `${hits.length} match${hits.length === 1 ? '' : 'es'}`;
    } else {
      const walk = (list: NodeInfo[], depth: number) => {
        for (const n of list) {
          const open = this.expanded.has(n.fileID);
          frag.appendChild(this.row(n, depth, n.children.length > 0));
          if (open && n.children.length) walk(n.children, depth + 1);
        }
      };
      walk(this.nodes, 0);
      this.countEl.textContent = `${this.countAll(this.nodes)} objects`;
    }

    this.root.appendChild(frag);
  }

  private row(node: NodeInfo, depth: number, hasKids: boolean, pathLabel?: string): HTMLElement {
    const el = document.createElement('div');
    el.className = 'hrow';
    if (!node.active) el.classList.add('inactive');
    if (this.selected === node.fileID) el.classList.add('sel');
    el.style.paddingLeft = `${depth * 14}px`;

    const tw = document.createElement('span');
    tw.className = `htwist${hasKids ? '' : ' leaf'}`;
    tw.textContent = this.expanded.has(node.fileID) ? '▼' : '▶';
    tw.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!hasKids) return;
      if (this.expanded.has(node.fileID)) this.expanded.delete(node.fileID);
      else this.expanded.add(node.fileID);
      this.render();
    });

    const ic = document.createElement('span');
    ic.className = 'hicon';
    ic.innerHTML = iconForComponents(node.components.map((c) => c.scriptName ?? c.className));

    const nm = document.createElement('span');
    nm.className = 'hname';
    nm.textContent = pathLabel ?? node.name;

    el.append(tw, ic, nm);

    if (node.children.length) {
      const b = document.createElement('span');
      b.className = 'hbadge';
      b.textContent = String(node.children.length);
      el.appendChild(b);
    }

    el.addEventListener('click', () => {
      this.selected = node.fileID;
      this.render();
      this.cb.onSelect(node);
    });

    return el;
  }
}
