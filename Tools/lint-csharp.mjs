#!/usr/bin/env node
/**
 * Structural checks for C# sources, for environments with no Unity and no dotnet.
 *
 * This is not a compiler and does not pretend to be one. It is a seatbelt for
 * mechanical edits: when you delete a method or a `#if UNITY_EDITOR` block by
 * hand, the way you break the file is almost always an unbalanced brace, a
 * stranded `#endif`, or a call to something you just removed. Those are exactly
 * the errors that a human reading a diff does not see and a compiler would
 * catch in a second.
 *
 * What it checks:
 *   - brace / paren / bracket balance, ignoring strings, chars, comments,
 *     verbatim strings and interpolated strings
 *   - preprocessor directive nesting (#if / #else / #elif / #endif)
 *   - #region / #endregion nesting
 *   - calls to identifiers that look like methods of this file but are not
 *     declared anywhere in the project (opt-in, --refs)
 *
 *   ./Tools/lint-csharp.mjs Assets/Scripts/**\/*.cs
 *   ./Tools/lint-csharp.mjs --refs Assets/Scripts/UI/MainMenu/CODMainMenu.cs
 */

import fs from 'node:fs';
import path from 'node:path';

const OPEN = { '{': '}', '(': ')', '[': ']' };
const CLOSE = { '}': '{', ')': '(', ']': '[' };

/**
 * Strip strings and comments, preserving offsets so reported lines stay true.
 * Everything removed is replaced with a space of the same length.
 */
function blank(src) {
  const out = src.split('');
  let i = 0;
  const n = src.length;
  const erase = (from, to) => {
    for (let k = from; k < to && k < n; k++) if (out[k] !== '\n') out[k] = ' ';
  };

  while (i < n) {
    const c = src[i];
    const c2 = src[i + 1];

    if (c === '/' && c2 === '/') {
      const end = src.indexOf('\n', i);
      erase(i, end < 0 ? n : end);
      i = end < 0 ? n : end;
      continue;
    }
    if (c === '/' && c2 === '*') {
      const end = src.indexOf('*/', i + 2);
      erase(i, end < 0 ? n : end + 2);
      i = end < 0 ? n : end + 2;
      continue;
    }
    // Verbatim string: @"..." where "" is an escaped quote.
    if (c === '@' && c2 === '"') {
      let j = i + 2;
      while (j < n) {
        if (src[j] === '"') {
          if (src[j + 1] === '"') { j += 2; continue; }
          j++;
          break;
        }
        j++;
      }
      erase(i, j);
      i = j;
      continue;
    }
    // Interpolated string. Braces inside the holes are real code to the
    // compiler, but they always balance within the literal, so blanking the
    // whole thing keeps the outer count correct.
    if ((c === '$' && c2 === '"') || c === '"' || c === "'") {
      const quote = c === '$' ? '"' : c;
      let j = (c === '$' ? i + 2 : i + 1);
      while (j < n) {
        if (src[j] === '\\') { j += 2; continue; }
        if (src[j] === quote) { j++; break; }
        if (src[j] === '\n') break; // unterminated; let the brace check report
        j++;
      }
      erase(i, j);
      i = j;
      continue;
    }
    i++;
  }
  return out.join('');
}

const lineOf = (src, index) => src.slice(0, index).split('\n').length;

function checkBalance(file, src) {
  const code = blank(src);
  const stack = [];
  const problems = [];

  for (let i = 0; i < code.length; i++) {
    const c = code[i];
    if (OPEN[c]) stack.push({ c, i });
    else if (CLOSE[c]) {
      const top = stack.pop();
      if (!top) {
        problems.push(`${file}:${lineOf(src, i)}: stray '${c}'`);
      } else if (top.c !== CLOSE[c]) {
        problems.push(
          `${file}:${lineOf(src, i)}: '${c}' closes '${top.c}' opened at line ${lineOf(src, top.i)}`,
        );
      }
    }
  }
  for (const left of stack) {
    problems.push(`${file}:${lineOf(src, left.i)}: '${left.c}' is never closed`);
  }
  return problems;
}

function checkDirectives(file, src) {
  const problems = [];
  const ifs = [];
  const regions = [];
  src.split('\n').forEach((line, idx) => {
    const m = /^\s*#\s*(if|ifdef|else|elif|endif|region|endregion)\b/.exec(line);
    if (!m) return;
    const ln = idx + 1;
    switch (m[1]) {
      case 'if': case 'ifdef': ifs.push(ln); break;
      case 'else': case 'elif':
        if (!ifs.length) problems.push(`${file}:${ln}: #${m[1]} with no open #if`);
        break;
      case 'endif':
        if (!ifs.pop()) problems.push(`${file}:${ln}: #endif with no open #if`);
        break;
      case 'region': regions.push(ln); break;
      case 'endregion':
        if (!regions.pop()) problems.push(`${file}:${ln}: #endregion with no open #region`);
        break;
    }
  });
  for (const ln of ifs) problems.push(`${file}:${ln}: #if is never closed`);
  for (const ln of regions) problems.push(`${file}:${ln}: #region is never closed`);
  return problems;
}

/**
 * Declared member names, so we can spot calls to something just deleted.
 *
 * Matches `Type Name(` at the start of a line, with or without modifiers: C#
 * members default to private, and this project writes plenty of them that way
 * (`bool canAimCheck()`), so requiring a modifier missed real declarations and
 * reported their call sites as dangling.
 */
const TYPE = String.raw`[\w\.\?]+(?:<[^()=;{}]*>)?(?:\[\])?`;
const DECL_RE = new RegExp(
  String.raw`^[ \t]*(?:\[[^\]]*\][ \t]*)*` +
  String.raw`(?:(?:public|private|protected|internal|static|virtual|override|sealed|abstract|` +
  String.raw`async|extern|partial|unsafe|new|readonly|delegate|event)[ \t]+)*` +
  TYPE + String.raw`[ \t]+(\w+)(?:<[^()=;{}]*>)?[ \t]*\(`,
  'gm',
);

function collectDeclarations(files) {
  const names = new Set();
  for (const f of files) {
    const code = blank(fs.readFileSync(f, 'utf8'));
    for (let m = DECL_RE.exec(code); m; m = DECL_RE.exec(code)) names.add(m[1]);
    DECL_RE.lastIndex = 0;
    // Local functions and lambdas assigned to fields are common too.
    for (const m of code.matchAll(/\b(\w+)\s*\([^)]*\)\s*=>/g)) names.add(m[1]);
    // Parameter names, because a delegate parameter is invoked like a method:
    // `void CyclerRow(..., System.Action<int> cycle)` is then called as
    // `cycle(-1)`, which is not a dangling call.
    for (const m of code.matchAll(/\(([^()]*)\)/g)) {
      for (const part of m[1].split(',')) {
        const p = /([\w<>,\[\]\.\?]+)\s+(\w+)\s*$/.exec(part.trim());
        if (p) names.add(p[2]);
      }
    }
  }
  return names;
}

/**
 * Report `Foo(` calls whose name is declared nowhere in the scanned set.
 *
 * Heuristic by nature: it cannot see Unity's own API or any package, so it only
 * complains about names that *were* declared in the scanned files before an
 * edit. Run it with --refs over the same file set before and after a deletion.
 */
function checkDanglingCalls(file, src, declared, known, unity) {
  // Preprocessor lines are not code. `#region Live refresh (called by the menu)`
  // otherwise reads as a call to `refresh(`.
  const code = blank(src)
    .split('\n')
    .map((l) => (/^\s*#/.test(l) ? ' '.repeat(l.length) : l))
    .join('\n');
  const problems = [];
  const seen = new Set();
  for (const m of code.matchAll(/(?<![.\w])([A-Za-z_]\w*)\s*\(/g)) {
    const name = m[1];
    if (seen.has(name) || declared.has(name) || known.has(name) || unity.has(name)) continue;
    const before = code.slice(Math.max(0, m.index - 12), m.index);
    // `new Foo(...)` is a constructor, not a call to a method of ours.
    if (/\bnew\s+$/.test(before)) continue;
    // `[Tooltip("...")]` is an attribute. Same syntax, different thing.
    if (/\[\s*$/.test(before)) continue;
    seen.add(name);
    problems.push(`${file}:${lineOf(src, m.index)}: call to '${name}' which is declared nowhere`);
  }
  return problems;
}

const KEYWORDS = new Set([
  'if', 'for', 'foreach', 'while', 'switch', 'catch', 'lock', 'using', 'return',
  'new', 'typeof', 'sizeof', 'nameof', 'default', 'checked', 'unchecked', 'fixed',
  'do', 'else', 'yield', 'await', 'when', 'is', 'as', 'in', 'out', 'ref', 'get', 'set',
  'value', 'var', 'this', 'base', 'throw', 'stackalloc', 'delegate', 'and', 'or', 'not',
  // Built-in type names, which appear as `new object()`, `new string(...)`.
  'object', 'string', 'bool', 'byte', 'sbyte', 'char', 'decimal', 'double', 'float',
  'int', 'uint', 'long', 'ulong', 'short', 'ushort', 'void', 'dynamic',
]);

/**
 * Members inherited from MonoBehaviour / UnityEngine.Object and called
 * unqualified. Without these every `Instantiate(` and `GetComponent<` in the
 * project reads as a dangling call. The list is short because qualified calls
 * (`Debug.Log`, `Mathf.Clamp`) are already excluded by the lookbehind.
 */
const UNITY_INHERITED = new Set([
  'Instantiate', 'Destroy', 'DestroyImmediate', 'DontDestroyOnLoad',
  'GetComponent', 'GetComponents', 'GetComponentInChildren', 'GetComponentsInChildren',
  'GetComponentInParent', 'GetComponentsInParent', 'TryGetComponent', 'AddComponent',
  'FindAnyObjectByType', 'FindFirstObjectByType', 'FindObjectsByType', 'FindObjectOfType',
  'FindObjectsOfType', 'CompareTag', 'SendMessage', 'BroadcastMessage',
  'StartCoroutine', 'StopCoroutine', 'StopAllCoroutines', 'Invoke', 'InvokeRepeating',
  'CancelInvoke', 'IsInvoking', 'print', 'ToString', 'Equals', 'GetHashCode', 'GetType',
  'nameof', 'sizeof', 'typeof',
  // Inherited from base classes that live in packages, so their declarations
  // are not in the scanned set: EditorWindow.Repaint, object.MemberwiseClone,
  // FishNet's NetworkBehaviour.Despawn. Extend this list when a new base class
  // is adopted rather than widening the matcher.
  'Repaint', 'MemberwiseClone', 'Despawn', 'Spawn',
]);

function main() {
  const args = process.argv.slice(2);
  const wantRefs = args.includes('--refs');
  const inputs = args.filter((a) => !a.startsWith('--'));
  if (!inputs.length) {
    console.error('usage: lint-csharp.mjs [--refs] <file.cs|dir> ...');
    process.exit(2);
  }

  const files = [];
  const walk = (p) => {
    const st = fs.statSync(p);
    if (st.isDirectory()) {
      for (const e of fs.readdirSync(p)) walk(path.join(p, e));
    } else if (p.endsWith('.cs')) {
      files.push(p);
    }
  };
  for (const inp of inputs) walk(inp);

  const declared = wantRefs ? collectDeclarations(files) : new Set();
  let problems = [];
  for (const f of files) {
    const src = fs.readFileSync(f, 'utf8');
    problems.push(...checkBalance(f, src));
    problems.push(...checkDirectives(f, src));
    if (wantRefs) problems.push(...checkDanglingCalls(f, src, declared, KEYWORDS, UNITY_INHERITED));
  }

  if (problems.length) {
    for (const p of problems) console.log(p);
    console.log(`\n${problems.length} problem(s) in ${files.length} file(s)`);
    process.exit(1);
  }
  console.log(`${files.length} file(s) structurally OK`);
}

main();
