/**
 * Unity YAML parser.
 *
 * Unity's serialized format is YAML 1.1 with a custom tag scheme and several
 * habits that break general-purpose YAML libraries:
 *
 *   %YAML 1.1
 *   %TAG !u! tag:unity3d.com,2011:
 *   --- !u!1 &1234567890            <- classID 1 (GameObject), anchor = fileID
 *   GameObject:
 *     m_Component:
 *     - component: {fileID: 1234567891}
 *     m_Name: MenuStage
 *
 * Quirks we handle deliberately:
 *   - the `!u!<classID> &<fileID>` document header (and the `stripped` suffix
 *     used by prefab instances)
 *   - duplicate keys inside a mapping (Unity emits them; last one wins)
 *   - inline flow maps `{fileID: 0}` and flow sequences `[1, 2, 3]`
 *   - sequences of maps where the first key sits on the `-` line
 *   - unquoted scalars containing `:` inside flow braces
 *
 * This is a purpose-built subset parser: faster than a general YAML engine on
 * multi-thousand-document scenes, and tolerant of the parts we do not need.
 */

export type UnityValue = string | number | boolean | null | UnityMap | UnityValue[];
export interface UnityMap { [key: string]: UnityValue }

/** A Unity file reference: `{fileID: N}` or `{fileID: N, guid: G, type: T}`. */
export interface UnityRef {
  fileID: number;
  guid?: string;
  type?: number;
}

export interface UnityDocument {
  /** Unity class id, e.g. 1 = GameObject, 4 = Transform, 114 = MonoBehaviour. */
  classId: number;
  /** Local file id (the YAML anchor). Unique within the file. */
  fileID: number;
  /** Top-level type name, e.g. "GameObject", "MonoBehaviour". */
  typeName: string;
  /** The document body (the value under `typeName`). */
  body: UnityMap;
  /** True for `stripped` prefab-instance placeholder documents. */
  stripped: boolean;
}

export interface UnityFile {
  documents: UnityDocument[];
  /** fileID -> document, for O(1) reference resolution. */
  byFileID: Map<number, UnityDocument>;
}

const DOC_HEADER = /^---\s+!u!(\d+)\s+&(-?\d+)(\s+stripped)?/;

/** True when `v` looks like a Unity object reference. */
export function isRef(v: UnityValue): v is UnityMap & { fileID: number } {
  return !!v && typeof v === 'object' && !Array.isArray(v) && 'fileID' in (v as UnityMap);
}

export function asRef(v: UnityValue | undefined): UnityRef | null {
  if (!isRef(v as UnityValue)) return null;
  const m = v as UnityMap;
  return {
    fileID: num(m.fileID, 0),
    guid: typeof m.guid === 'string' ? m.guid : undefined,
    type: m.type !== undefined ? num(m.type, 0) : undefined,
  };
}

export function num(v: UnityValue | undefined, fallback = 0): number {
  if (typeof v === 'number') return v;
  if (typeof v === 'string') {
    const n = parseFloat(v);
    return Number.isFinite(n) ? n : fallback;
  }
  if (typeof v === 'boolean') return v ? 1 : 0;
  return fallback;
}

export function str(v: UnityValue | undefined, fallback = ''): string {
  if (typeof v === 'string') return v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  return fallback;
}

export function bool(v: UnityValue | undefined, fallback = false): boolean {
  if (typeof v === 'boolean') return v;
  if (typeof v === 'number') return v !== 0;
  if (typeof v === 'string') return v === '1' || v.toLowerCase() === 'true';
  return fallback;
}

export function mapOf(v: UnityValue | undefined): UnityMap | null {
  if (v && typeof v === 'object' && !Array.isArray(v)) return v as UnityMap;
  return null;
}

export function arrayOf(v: UnityValue | undefined): UnityValue[] {
  if (Array.isArray(v)) return v;
  return [];
}

/* ------------------------------------------------------------------ */
/* Scalar parsing                                                      */
/* ------------------------------------------------------------------ */

function parseScalar(raw: string): UnityValue {
  const s = raw.trim();
  if (s === '') return '';
  if (s === '~' || s === 'null') return null;

  // Quoted strings
  if (s.length >= 2) {
    const a = s[0];
    if ((a === '"' || a === "'") && s[s.length - 1] === a) {
      const inner = s.slice(1, -1);
      return a === '"' ? inner.replace(/\\"/g, '"').replace(/\\\\/g, '\\') : inner.replace(/''/g, "'");
    }
  }

  // Flow collections
  if (s[0] === '{') return parseFlowMap(s);
  if (s[0] === '[') return parseFlowSeq(s);

  // Numbers. Unity writes plain decimals, exponents and `inf`/`-inf`.
  if (/^-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?$/.test(s)) {
    const n = parseFloat(s);
    if (Number.isFinite(n)) return n;
  }
  if (s === '.inf' || s === 'inf') return Infinity;
  if (s === '-.inf' || s === '-inf') return -Infinity;
  if (s === '.nan' || s === 'nan') return NaN;

  return s;
}

/** Split a flow body on commas that are not nested inside braces/brackets/quotes. */
function splitFlow(body: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let start = 0;
  for (let i = 0; i < body.length; i++) {
    const c = body[i];
    if (quote) {
      if (c === quote && body[i - 1] !== '\\') quote = null;
      continue;
    }
    if (c === '"' || c === "'") { quote = c; continue; }
    if (c === '{' || c === '[') depth++;
    else if (c === '}' || c === ']') depth--;
    else if (c === ',' && depth === 0) {
      parts.push(body.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(body.slice(start));
  return parts.map((p) => p.trim()).filter((p) => p.length > 0);
}

function parseFlowMap(s: string): UnityMap {
  const out: UnityMap = {};
  const body = s.slice(1, s.lastIndexOf('}') === -1 ? undefined : s.lastIndexOf('}'));
  for (const part of splitFlow(body)) {
    const ci = findFlowColon(part);
    if (ci < 0) continue;
    out[part.slice(0, ci).trim()] = parseScalar(part.slice(ci + 1));
  }
  return out;
}

function parseFlowSeq(s: string): UnityValue[] {
  const body = s.slice(1, s.lastIndexOf(']') === -1 ? undefined : s.lastIndexOf(']'));
  return splitFlow(body).map(parseScalar);
}

/** Index of the key/value colon in a flow entry, ignoring nested + quoted regions. */
function findFlowColon(s: string): number {
  let depth = 0;
  let quote: string | null = null;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (quote) { if (c === quote && s[i - 1] !== '\\') quote = null; continue; }
    if (c === '"' || c === "'") { quote = c; continue; }
    if (c === '{' || c === '[') depth++;
    else if (c === '}' || c === ']') depth--;
    else if (c === ':' && depth === 0) return i;
  }
  return -1;
}

/** Index of the key/value colon in a block line (must be followed by space or EOL). */
function findBlockColon(s: string): number {
  let quote: string | null = null;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (quote) { if (c === quote && s[i - 1] !== '\\') quote = null; continue; }
    if (c === '"' || c === "'") { quote = c; continue; }
    if (c === '{' || c === '[') return -1; // a flow value started before any colon
    if (c === ':' && (i + 1 >= s.length || s[i + 1] === ' ')) return i;
  }
  return -1;
}

/* ------------------------------------------------------------------ */
/* Block parsing                                                       */
/* ------------------------------------------------------------------ */

interface Line { indent: number; text: string }

function tokenize(src: string): Line[] {
  const out: Line[] = [];
  for (const raw of src.split('\n')) {
    // Strip trailing CR and comments that begin a line.
    const line = raw.replace(/\r$/, '');
    const trimmedStart = line.replace(/^\s*/, '');
    if (trimmedStart === '' || trimmedStart.startsWith('#')) continue;
    out.push({ indent: line.length - trimmedStart.length, text: trimmedStart.replace(/\s+$/, '') });
  }
  return out;
}

/**
 * Parse a block-style mapping or sequence starting at `i`, consuming every line
 * indented deeper than `parentIndent`. Returns the value and the next index.
 */
function parseBlock(lines: Line[], i: number, parentIndent: number): [UnityValue, number] {
  if (i >= lines.length || lines[i].indent <= parentIndent) return [null, i];

  const indent = lines[i].indent;

  // Sequence
  if (lines[i].text.startsWith('- ') || lines[i].text === '-') {
    const seq: UnityValue[] = [];
    while (i < lines.length && lines[i].indent === indent &&
           (lines[i].text.startsWith('- ') || lines[i].text === '-')) {
      const rest = lines[i].text === '-' ? '' : lines[i].text.slice(2).trim();

      if (rest === '') {
        i++;
        const [v, ni] = parseBlock(lines, i, indent);
        seq.push(v);
        i = ni;
        continue;
      }

      const ci = findBlockColon(rest);
      if (ci >= 0) {
        // `- key: value` — a map whose first entry shares the dash line. The
        // remaining entries are indented to the position of `key`.
        const item: UnityMap = {};
        const key = rest.slice(0, ci).trim();
        const valueText = rest.slice(ci + 1).trim();
        const childIndent = indent + 2;
        i++;
        if (valueText === '') {
          const [v, ni] = parseBlock(lines, i, childIndent - 1);
          item[key] = v;
          i = ni;
        } else {
          item[key] = parseScalar(valueText);
        }
        while (i < lines.length && lines[i].indent >= childIndent) {
          const [m, ni] = parseMappingEntries(lines, i, childIndent, item);
          if (ni === i) break;
          i = ni;
          void m;
        }
        seq.push(item);
      } else {
        seq.push(parseScalar(rest));
        i++;
      }
    }
    return [seq, i];
  }

  // Mapping
  const map: UnityMap = {};
  while (i < lines.length && lines[i].indent === indent) {
    const [, ni] = parseMappingEntries(lines, i, indent, map);
    if (ni === i) break;
    i = ni;
  }
  return [map, i];
}

/** Parse ONE `key: value` entry at `indent` into `into`. Returns the next index. */
function parseMappingEntries(lines: Line[], i: number, indent: number, into: UnityMap): [UnityMap, number] {
  if (i >= lines.length || lines[i].indent !== indent) return [into, i];
  const text = lines[i].text;
  if (text.startsWith('- ') || text === '-') return [into, i];

  const ci = findBlockColon(text);
  if (ci < 0) { return [into, i + 1]; } // not a mapping line we understand; skip

  const key = text.slice(0, ci).trim();
  const valueText = text.slice(ci + 1).trim();
  i++;

  if (valueText === '' || valueText === '|' || valueText === '>') {
    // Block scalar or nested block. Unity uses `|` rarely (e.g. text assets).
    if (valueText === '|' || valueText === '>') {
      const buf: string[] = [];
      while (i < lines.length && lines[i].indent > indent) { buf.push(lines[i].text); i++; }
      into[key] = buf.join('\n');
      return [into, i];
    }
    // A nested sequence may be written at the SAME indent as its key.
    if (i < lines.length && lines[i].indent === indent &&
        (lines[i].text.startsWith('- ') || lines[i].text === '-')) {
      const [v, ni] = parseBlock(lines, i, indent - 1);
      into[key] = v;
      return [into, ni];
    }
    const [v, ni] = parseBlock(lines, i, indent);
    into[key] = v === null ? {} : v;
    return [into, ni];
  }

  into[key] = parseScalar(valueText);
  return [into, i];
}

/* ------------------------------------------------------------------ */
/* Public entry points                                                 */
/* ------------------------------------------------------------------ */

/** Parse a full Unity YAML file (`.unity`, `.prefab`, `.asset`, `.mat`, ...). */
export function parseUnityYaml(source: string): UnityFile {
  const documents: UnityDocument[] = [];
  const byFileID = new Map<number, UnityDocument>();

  // Split into documents on the `--- !u!N &M` headers.
  const rawLines = source.split('\n');
  let current: { classId: number; fileID: number; stripped: boolean; start: number } | null = null;

  const flush = (endExclusive: number) => {
    if (!current) return;
    const chunk = rawLines.slice(current.start, endExclusive).join('\n');
    const lines = tokenize(chunk);
    if (lines.length === 0) { current = null; return; }

    // The first line is `TypeName:`; the body is everything indented under it.
    const first = lines[0].text;
    const ci = findBlockColon(first);
    const typeName = ci >= 0 ? first.slice(0, ci).trim() : first.trim();
    const [body] = parseBlock(lines, 1, lines[0].indent);

    const doc: UnityDocument = {
      classId: current.classId,
      fileID: current.fileID,
      typeName,
      body: (body && typeof body === 'object' && !Array.isArray(body)) ? (body as UnityMap) : {},
      stripped: current.stripped,
    };
    documents.push(doc);
    byFileID.set(doc.fileID, doc);
    current = null;
  };

  for (let i = 0; i < rawLines.length; i++) {
    const m = DOC_HEADER.exec(rawLines[i]);
    if (m) {
      flush(i);
      current = {
        classId: parseInt(m[1], 10),
        fileID: parseInt(m[2], 10),
        stripped: !!m[3],
        start: i + 1,
      };
    }
  }
  flush(rawLines.length);

  return { documents, byFileID };
}

/** Read the `guid` out of a `.meta` file without a full parse. */
export function parseMetaGuid(source: string): string | null {
  const m = /^guid:\s*([0-9a-fA-F]{32})\s*$/m.exec(source);
  return m ? m[1] : null;
}
