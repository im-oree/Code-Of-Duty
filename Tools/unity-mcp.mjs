// Unity Editor control from the terminal, via the Unity AI Assistant relay.
//
// The relay binary speaks MCP over stdio. Running it with "--mcp" attaches it to
// the Unity Editor that is open, which gives this agent the same tools the
// built-in assistant has (camera capture, play-mode control, GameObject editing,
// console logs, profiler...).
//
// A relay process only lives as long as the command that spawned it, so rather
// than a background daemon this connects once per invocation and supports a
// batch mode: many tool calls over a single connection.
//
//   node Tools/unity-mcp.mjs tools                 # list tools
//   node Tools/unity-mcp.mjs schema <Tool>         # full input schema
//   node Tools/unity-mcp.mjs call <Tool> '{...}'   # one call
//   node Tools/unity-mcp.mjs batch calls.json      # [{ "name": ..., "arguments": ... }]
//   node Tools/unity-mcp.mjs batch -               # same, JSON on stdin
//
// Image results are written into Screenshots/ and the path is printed.
import { spawn } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import readline from "node:readline";
import { fileURLToPath } from "node:url";

const ROOT = resolve(fileURLToPath(new URL("..", import.meta.url)));
const SHOTS = resolve(ROOT, "Screenshots");
const RELAY = process.env.RELAY_BIN ||
  "/Users/oreoluwadaramola/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64";

async function connect() {
  const child = spawn(RELAY, ["--mcp"], { stdio: ["pipe", "pipe", "pipe"] });
  let stderr = "";
  child.stderr.on("data", (d) => { stderr += d.toString(); });

  const rl = readline.createInterface({ input: child.stdout });
  const pending = new Map();
  let nextId = 1;

  rl.on("line", (line) => {
    if (!line.trim()) return;
    let msg;
    try { msg = JSON.parse(line); } catch { return; }
    if (msg.id !== undefined && pending.has(msg.id)) {
      const { resolve: done } = pending.get(msg.id);
      pending.delete(msg.id);
      done(msg);
    }
  });

  const rpc = (method, params) => {
    const id = nextId++;
    return new Promise((done, fail) => {
      pending.set(id, { resolve: done });
      child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params: params ?? {} }) + "\n");
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          fail(new Error(`timeout: ${method}\nrelay stderr tail:\n${stderr.slice(-800)}`));
        }
      }, 180000);
    });
  };

  const init = await rpc("initialize", {
    protocolVersion: "2024-11-05",
    capabilities: {},
    clientInfo: { name: "codebuff-unity", version: "1.0.0" }
  });
  if (!init.result) throw new Error("relay did not initialize (is the Unity Editor open?)");
  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized", params: {} }) + "\n");

  const close = () => {
    try { rl.close(); } catch {}
    try { child.stdin.end(); } catch {}
    try { child.kill(); } catch {}
  };

  return { rpc, close };
}

// MCP content blocks -> terminal text, spilling images to disk.
function render(result, tag = "mcp") {
  const out = [];
  const files = [];
  for (const part of result?.content ?? []) {
    if (part.type === "text") out.push(part.text);
    else if (part.type === "image" && part.data) {
      mkdirSync(SHOTS, { recursive: true });
      const ext = (part.mimeType || "image/png").includes("jpeg") ? "jpg" : "png";
      const p = resolve(SHOTS, `${tag}-${Date.now()}.${ext}`);
      writeFileSync(p, Buffer.from(part.data, "base64"));
      files.push(p);
    } else if (part.type === "resource") out.push(JSON.stringify(part.resource));
    else out.push(JSON.stringify(part));
  }
  if (result?.isError) out.unshift("!! TOOL REPORTED AN ERROR");
  if (result?.error) out.unshift(`!! ${result.error}`);
  return { text: out.join("\n"), files };
}

const [cmd, ...rest] = process.argv.slice(2);
const { rpc, close } = await connect();

try {
  if (cmd === "tools") {
    const r = await rpc("tools/list");
    for (const t of r.result?.tools ?? []) console.log(`${t.name}\n    ${(t.description || "").split("\n")[0].slice(0, 150)}`);
  } else if (cmd === "schema") {
    const r = await rpc("tools/list");
    const all = r.result?.tools ?? [];
    for (const name of rest) {
      const t = all.find((x) => x.name === name);
      console.log(t
        ? JSON.stringify({ name: t.name, description: t.description, inputSchema: t.inputSchema }, null, 2)
        : `no such tool: ${name}`);
    }
  } else if (cmd === "call") {
    const args = rest[1] && rest[1] !== "-" ? JSON.parse(rest[1]) : {};
    const res = await rpc("tools/call", { name: rest[0], arguments: args });
    const { text, files } = render(res.result ?? res.error ?? {}, rest[0].replace(/[^\w.-]/g, "_"));
    if (text) console.log(text);
    for (const f of files) console.log(`[image saved] ${f}`);
  } else if (cmd === "raw") {
    // Print the untouched JSON-RPC envelope — for diagnosing empty/odd replies.
    const args = rest[1] && rest[1] !== "-" ? JSON.parse(rest[1]) : {};
    const res = await rpc("tools/call", { name: rest[0], arguments: args });
    console.log(JSON.stringify(res, null, 2).slice(0, 8000));
  } else if (cmd === "batch") {
    const raw = rest[0] === "-" ? readFileSync(0, "utf8") : readFileSync(rest[0], "utf8");
    const calls = JSON.parse(raw);
    for (const c of calls) {
      console.log(`\n===== ${c.name} =====`);
      const res = await rpc("tools/call", { name: c.name, arguments: c.arguments ?? {} });
      const { text, files } = render(res.result ?? res.error ?? {}, c.name);
      if (text) console.log(text.slice(0, 20000));
      for (const f of files) console.log(`[image saved] ${f}`);
    }
  } else {
    console.error("usage: node Tools/unity-mcp.mjs {tools|schema <Tool>|call <Tool> <json>|raw <Tool> <json>|batch <file|->}");
    process.exitCode = 2;
  }
} finally {
  // The relay's stdio pipes keep Node's event loop alive after the work is
  // done, so shut them down and exit deliberately (with a beat to let any
  // buffered stdout reach a pipe).
  close();
  await new Promise((r) => setTimeout(r, 120));
  process.exit(process.exitCode ?? 0);
}
