/**
 * The Console pane.
 *
 * Collapses repeated messages with a count, the way Unity's does — a scene that
 * warns once per object would otherwise bury everything else. Build warnings
 * from SceneBuilder land here, as do uncaught page errors, so a broken render
 * explains itself instead of just looking wrong.
 */

export type LogLevel = 'info' | 'warn' | 'err';

interface Entry { level: LogLevel; message: string; count: number }

/** Severity badges, drawn as SVG so they survive a font-less headless browser. */
const LEVEL_ICON: Record<'err' | 'warn' | 'info', string> = {
  err: '<svg viewBox="0 0 16 16" width="11" height="11" xmlns="http://www.w3.org/2000/svg">' +
       '<circle cx="8" cy="8" r="6.4" fill="#d4705a"/>' +
       '<path d="M5.4 5.4l5.2 5.2M10.6 5.4l-5.2 5.2" stroke="#1d1d1d" stroke-width="1.6"/></svg>',
  warn: '<svg viewBox="0 0 16 16" width="11" height="11" xmlns="http://www.w3.org/2000/svg">' +
        '<path d="M8 1.8l6.4 11.4H1.6z" fill="#e0c05a"/>' +
        '<path d="M8 6v3.4" stroke="#1d1d1d" stroke-width="1.5"/>' +
        '<circle cx="8" cy="11.4" r=".9" fill="#1d1d1d"/></svg>',
  info: '<svg viewBox="0 0 16 16" width="11" height="11" xmlns="http://www.w3.org/2000/svg">' +
        '<circle cx="8" cy="8" r="6.4" fill="#5a9fd4"/>' +
        '<path d="M8 7v4.2" stroke="#1d1d1d" stroke-width="1.6"/>' +
        '<circle cx="8" cy="4.8" r=".95" fill="#1d1d1d"/></svg>',
};

export class ConsolePane {
  private body: HTMLElement;
  private entries = new Map<string, Entry>();

  constructor() {
    this.body = document.getElementById('console-body')!;

    window.addEventListener('error', (e) => {
      this.log('err', `${e.message} (${e.filename}:${e.lineno})`);
    });
    window.addEventListener('unhandledrejection', (e) => {
      this.log('err', `Unhandled rejection: ${String((e as PromiseRejectionEvent).reason)}`);
    });
  }

  clear(): void {
    this.entries.clear();
    this.render();
  }

  log(level: LogLevel, message: string): void {
    const key = `${level}:${message}`;
    const existing = this.entries.get(key);
    if (existing) existing.count++;
    else this.entries.set(key, { level, message, count: 1 });
    this.render();
  }

  /** Bulk-load a build's warnings, replacing anything from the previous build. */
  setWarnings(warnings: string[]): void {
    for (const key of [...this.entries.keys()]) {
      if (key.startsWith('warn:')) this.entries.delete(key);
    }
    for (const w of warnings) this.log('warn', w);
  }

  counts(): { info: number; warn: number; err: number } {
    const out = { info: 0, warn: 0, err: 0 };
    for (const e of this.entries.values()) out[e.level] += e.count;
    return out;
  }

  private render(): void {
    this.body.innerHTML = '';
    const order: LogLevel[] = ['err', 'warn', 'info'];
    const sorted = [...this.entries.values()]
      .sort((a, b) => order.indexOf(a.level) - order.indexOf(b.level));

    for (const e of sorted) {
      const row = document.createElement('div');
      row.className = `crow ${e.level}`;
      row.innerHTML =
        `<span class="t">${LEVEL_ICON[e.level]}</span>` +
        '<span class="msg"></span>' +
        (e.count > 1 ? `<span class="n">${e.count}</span>` : '');
      row.querySelector('.msg')!.textContent = e.message;
      this.body.appendChild(row);
    }

    if (sorted.length === 0) {
      this.body.innerHTML = '<div class="empty">Console is clear</div>';
    }
  }
}
