# MCP tooling setup — drive the Editor and the browser

Two MCP servers give this project its live loop: control the Unity Editor, and control the browser
for research + visual comparison. Both run locally on your machine.

---

## 1. Unity Editor MCP

Goal: create/select objects, edit scripts, enter play mode, screenshot Game/Scene views, run tests,
read the console — so changes are verified live rather than assumed.

### Option A — Official Unity AI MCP server (try first)
`com.unity.ai.assistant` **2.20.0-pre.1 is already installed** in this project (see
`Packages/manifest.json`). Unity's AI open beta exposes an MCP server that connects Claude/Codex/
Copilot-style agents to the Editor.

1. Open the project in Unity `6000.6.3f1`.
2. Open the AI Assistant window and enable its **MCP server / bridge** (naming varies by beta build).
3. Note the server URL/port it prints and register it with this agent's MCP config.
4. Smoke test: read the hierarchy → enter play → screenshot → exit play.

### Option B — CoplayDev `unity-mcp` (fallback 1)
Bridge with asset/scene/script/test tools and broad client support.
1. Unity → Window → Package Manager → **Install package from git URL**:
   `https://github.com/CoplayDev/unity-mcp.git?path=UnityMcpBridge`
   (verify the exact UPM path against the repo's current README before installing).
2. In Unity, open the MCP window and start the bridge (default local HTTP port is shown there).
3. Register that server with this agent's MCP config and smoke-test as above.

### Option C — IvanMurzak `Unity-MCP` (fallback 2)
70+ tools, editor **and** runtime AI. Install via UPM from
`https://github.com/IvanMurzak/Unity-MCP.git?path=/UnityMcpPlugin` (verify path), start the server,
register, smoke test.

> After registering any new MCP server the agent host may need a restart before the tools appear.

---

## 2. Chrome MCP (hangwin/mcp-chrome)

Goal: read docs/patch notes, pull UI reference screenshots, extract exact numbers, and compare our
build's UI against references side by side — using your real, logged-in Chrome.

### Install
```bash
# Node >= 20 required (confirmed: Node 24.11.0)
npm install -g mcp-chrome-bridge
```
1. Download the extension from the repo Releases: https://github.com/hangwin/mcp-chrome/releases
2. Chrome → `chrome://extensions` → enable **Developer mode** → **Load unpacked** → select the
   extension folder.
3. Click the extension icon → **Connect**.

### Register with the agent
Recommended (Streamable HTTP):
```json
{ "mcpServers": { "chrome-mcp-server": { "type": "streamableHttp", "url": "http://127.0.0.1:12306/mcp" } } }
```

### Useful tools
`chrome_navigate`, `chrome_screenshot`, `chrome_get_web_content`,
`chrome_get_interactive_elements`, `chrome_click_element`, `chrome_fill_or_select`,
`chrome_keyboard`, `search_tabs_content`, `chrome_network_capture_start/stop`.

### Fallback if mcp-chrome is flaky
Use Microsoft **`playwright-mcp`** (fresh Chromium, very reliable automation) and/or this app's
built-in Preview tab tools.

---

## 3. The live loop

1. **Probe** — Unity MCP reads hierarchy/console/state; Chrome MCP pulls the reference image/numbers.
2. **Change** — edit scripts/assets.
3. **Run** — enter play / run tests; capture a Game-view screenshot.
4. **Compare** — put our frame beside the reference; list deltas.
5. **Fix** — apply deltas; re-run; screenshot.
6. **Record** — before/after note in the feature's research doc.

## 4. Status
- Reference repo: **cloned to `~/Documents/dev/Three-FPS`**, `npm install` complete
  (145 packages), `npm run typecheck` passes. Run it with `npm run dev`.
- Chrome MCP bridge: **installed** to `$HOME/.local` (no admin needed):
  ```bash
  npm install -g --prefix "$HOME/.local" mcp-chrome-bridge
  ```
  Binaries: `~/.local/bin/mcp-chrome-bridge`, `~/.local/bin/chrome-mcp-stdio`.
  Stdio entry: `~/.local/lib/node_modules/mcp-chrome-bridge/dist/mcp/mcp-server-stdio.js`.
- Still manual, because they need your browser/editor UI:
  1. Download the mcp-chrome extension from the repo's Releases, load it at
     `chrome://extensions` (Developer mode → Load unpacked), then click the icon → **Connect**.
  2. Add the server to Freebuff's MCP settings (the app owns `~/.freebuff/mcp.json`, which is
     hash-managed — do not hand-edit it; add via the MCP settings UI). Use either the HTTP form
     `{ "type": "streamableHttp", "url": "http://127.0.0.1:12306/mcp" }` or the stdio form
     pointing at the script path above.
  3. Unity MCP: open the project in Unity `6000.6.3f1` and enable the AI Assistant's MCP server
     (Option A), or install CoplayDev `unity-mcp` (Option B).
- A newly registered MCP server may need an agent-host restart before its tools appear.
