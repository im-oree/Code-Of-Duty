# Seeing the game: the Unity visual loop

This project is worked on by an agent that cannot open the Unity Editor window. It
needs to *look* at things to judge animation, pose and UI work, so there is a
bridged path from the Editor's framebuffer into an image the agent can actually
see. It is fragile in specific ways — this file records how it works and what
breaks it.

## 1. Editor control — `Tools/unity-mcp.mjs`

Unity's AI Assistant ships a relay:

```
~/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64
```

Running it with `--mcp` speaks MCP over stdio and attaches to whichever Editor is
open (it discovers the editor's Unix socket, e.g. `/tmp/unity-mcp-<id>-<pid>`).
That exposes ~54 tools: `Unity_Camera_Capture`, `Unity_ManageEditor` (Play/Stop),
`Unity_ManageGameObject`, `Unity_ManageScene`, `Unity_GetConsoleLogs`,
`Unity_ManageMenuItem`, the profiler, and more.

`Tools/unity-mcp.mjs` wraps it so it can be driven from a shell:

```bash
node Tools/unity-mcp.mjs tools                  # list all 54 tools
node Tools/unity-mcp.mjs schema <Tool> [<Tool>] # full input schemas
node Tools/unity-mcp.mjs call <Tool> '{...}'    # one call
node Tools/unity-mcp.mjs batch calls.json       # many calls, one connection
```

`batch` matters: each invocation spawns its own relay, so batching N calls into
one process turns N × ~4 s into ~4 s total.

Two things bite:

- **Do not run a background daemon.** A detached `serve` process dies when the
  spawning shell session ends. One-shot connections per invocation are reliable.
- **The process will not exit on its own.** The relay's stdio pipes keep Node's
  event loop alive after the work is finished, so the script calls
  `process.exit()` deliberately (after a short delay so buffered stdout flushes
  to a pipe). Without this, every command appears to hang forever even though its
  output is already complete.

## 2. Turning a capture into something visible

`Unity_Camera_Capture` returns a base64 PNG. Dropped in a file, the agent still
cannot see it. The route that works:

1. Capture via `Unity_Camera_Capture` (give a camera instance ID for a game
   camera/viewmodel; omit it for the Scene View).
2. Build a **self-contained** HTML page that inlines the PNG as a
   `data:image/png;base64,...` URI.
3. `register_preview` that page.
4. `preview_resize` it to a real viewport, then `preview_screenshot`.

Two failure modes, both encountered:

- **Sibling assets do not resolve.** The preview server serves the HTML file
  only; a relative `src="shot.png"` 404s and the image silently reports
  `naturalWidth: 0`. Inlining the image as a data URI is the fix.
- **"the preview webview is not being composited".** `preview_screenshot` fails
  until the Preview tab is actually being displayed; `preview_status` shows
  `viewport: null` in that state. Calling `preview_resize` (then screenshotting)
  brings it up.

## 3. Registering the relay in Freebuff

To let the agent call these tools natively (instead of shelling out), add to
Freebuff's MCP server list:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "/Users/oreoluwadaramola/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64",
      "args": ["--mcp"]
    }
  }
}
```

Note this is the *client-side* config. `UserSettings/mcp.json` in this repo is the
Unity-side (AI Assistant) config and is not what registers tools with the agent.

## 4. Play mode disables the tools

**While the Editor is in play mode, the relay reports zero tools.**

```
{"jsonrpc":"2.0","id":2,"error":{"code":-32603,
 "message":"Tool 'Unity_ManageEditor' not found. Available tools: "}}
```

Note the empty list — the bridge is connected, but nothing is registered. This is
reproducible and persists while play mode runs (polled for 24 s). Diagnose it with
`node Tools/unity-mcp.mjs raw <Tool> '{}'`, which prints the untouched envelope. A
silent empty result from `call`/`batch` means this, not a crash — always reach for
`raw` when a call returns nothing.

Consequences, and the way round them:

- **Never plan to drive the Editor from inside play mode.** Do setup, captures and
  teardown in edit mode, and let an Editor-side script (still running during play)
  do anything time-critical in-game.
- **Entering play mode is easy; leaving is not.** Once in play, the tools that could
  issue Stop are gone, so a play session can only be ended by the user pressing Stop
  in the Editor, or by a script that sets `EditorApplication.isPlaying = false`
  itself. Any automated play-mode harness must stop play mode on its own.
- The likely cause is that the AI Assistant registers its tool set when the Editor
  UI repaints/regains focus, and a script recompile plus entering play mode happens
  with the Editor unfocused.

## 5. Safety when driving the Editor

- Check `Unity_ManageEditor → GetState` (`IsPlaying`, `IsCompiling`) before acting.
- `Unity_ManageScene → GetActive` reports `isDirty`. **Never load another scene
  while the open scene is dirty** — unsaved work would be lost. Save first, or ask.
- Read `Unity_GetConsoleLogs` after edits to confirm the recompile succeeded.
  `Unity_ManageMenuItem → Execute → "Assets/Refresh"` forces one.
