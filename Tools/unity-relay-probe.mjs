// Minimal MCP stdio client that launches the Unity relay in --mcp mode and
// asks it what tools it exposes. Used to work out whether this agent can reach
// the open Unity Editor without the app-level MCP registry.
//
// Usage: node Tools/unity-relay-probe.mjs
import { spawn } from "node:child_process";
import readline from "node:readline";

const RELAY = process.env.RELAY_BIN ||
  "/Users/oreoluwadaramola/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64";

const child = spawn(RELAY, ["--mcp"], { stdio: ["pipe", "pipe", "pipe"] });

child.stderr.on("data", (d) => process.stderr.write(`[relay stderr] ${d}`));

const rl = readline.createInterface({ input: child.stdout });
const pending = new Map();
let nextId = 1;

rl.on("line", (line) => {
  if (!line.trim()) return;
  let msg;
  try { msg = JSON.parse(line); } catch { console.log(`[non-json] ${line}`); return; }
  if (msg.id !== undefined && pending.has(msg.id)) {
    const { resolve } = pending.get(msg.id);
    pending.delete(msg.id);
    resolve(msg);
  } else if (msg.method) {
    console.log(`[notify] ${msg.method}`);
  }
});

function send(method, params) {
  const id = nextId++;
  const payload = { jsonrpc: "2.0", id, method, params: params ?? {} };
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    child.stdin.write(JSON.stringify(payload) + "\n");
    setTimeout(() => {
      if (pending.has(id)) { pending.delete(id); reject(new Error(`timeout on ${method}`)); }
    }, 20000);
  });
}

function notify(method, params) {
  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", method, params: params ?? {} }) + "\n");
}

const timer = setTimeout(() => { console.log("FATAL: no response in 25s"); child.kill(); process.exit(2); }, 25000);

try {
  const init = await send("initialize", {
    protocolVersion: "2024-11-05",
    capabilities: {},
    clientInfo: { name: "freebuff-probe", version: "1.0.0" }
  });
  console.log("=== initialize ===");
  console.log(JSON.stringify(init.result ?? init, null, 2));
  notify("notifications/initialized");

  const list = await send("tools/list");
  const tools = list.result?.tools ?? [];
  console.log(`\n=== tools (${tools.length}) ===`);
  for (const t of tools) console.log(`- ${t.name}: ${(t.description || "").split("\n")[0].slice(0, 110)}`);
} catch (e) {
  console.log("PROBE FAILED:", e.message);
} finally {
  clearTimeout(timer);
  child.kill();
  process.exit(0);
}
