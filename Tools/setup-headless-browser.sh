#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Install a headless Chromium with working WebGL into this sandbox.
#
# Why this is not just `npx playwright install chromium`:
#   - Playwright's CDN (playwright.azureedge.net / cdn.playwright.dev) is blocked
#   - Google's storage.googleapis.com is blocked
#   - Debian apt (deb.debian.org) is blocked
#   - the npm registry and GitHub ARE reachable
#
# So we pull the binary out of the `@sparticuz/chromium` npm package, which
# ships it inside the tarball, and assemble the pieces it needs:
#   - swiftshader *.so must sit NEXT TO the chromium binary (it dlopen()s by path)
#   - libnss3 and friends come from the bundled al2023 lib archive
#
# Verified 2026-09-26: Chromium 131.0.6778.0, WebGL 2.0, ~1.1 s per 640x360 frame.
# See Docs/plan/07-VERIFICATION-AND-VISUAL-LOOP.md
# ---------------------------------------------------------------------------
set -euo pipefail

DEST="${UW_CHROME_DIR:-$HOME/.cache/chromium}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [ -x "$DEST/bin/chromium" ]; then
  echo "[setup] chromium already present at $DEST/bin/chromium"
  LD_LIBRARY_PATH="$DEST/lib/lib" "$DEST/bin/chromium" --version || true
  exit 0
fi

echo "[setup] fetching @sparticuz/chromium from npm…"
mkdir -p "$WORK/pkg" && cd "$WORK/pkg"
npm init -y >/dev/null 2>&1
npm i --no-audit --no-fund @sparticuz/chromium@131.0.1 >/dev/null

BIN_SRC="$WORK/pkg/node_modules/@sparticuz/chromium/bin"
mkdir -p "$DEST/bin"

echo "[setup] decompressing chromium (~180 MB)…"
node -e "
const fs=require('fs'),zlib=require('zlib');
fs.writeFileSync('$DEST/bin/chromium', zlib.brotliDecompressSync(fs.readFileSync('$BIN_SRC/chromium.br')));
"
chmod +x "$DEST/bin/chromium"

for pack in swiftshader al2023; do
  echo "[setup] unpacking $pack…"
  node -e "
const fs=require('fs'),zlib=require('zlib');
fs.writeFileSync('$WORK/$pack.tar', zlib.brotliDecompressSync(fs.readFileSync('$BIN_SRC/$pack.tar.br')));
"
  case "$pack" in
    swiftshader) mkdir -p "$DEST/swiftshader" && tar xf "$WORK/$pack.tar" -C "$DEST/swiftshader" ;;
    al2023)      mkdir -p "$DEST/lib"         && tar xf "$WORK/$pack.tar" -C "$DEST/lib" ;;
  esac
done

# SwiftShader's libEGL/libGLESv2 are resolved relative to the chromium binary.
cp "$DEST/swiftshader/"*.so "$DEST/bin/" 2>/dev/null || true
cp "$DEST/swiftshader/"*.so.1 "$DEST/bin/" 2>/dev/null || true
cp "$DEST/swiftshader/vk_swiftshader_icd.json" "$DEST/bin/" 2>/dev/null || true

echo "[setup] verifying…"
LD_LIBRARY_PATH="$DEST/lib/lib" "$DEST/bin/chromium" --version

cat <<EOF

[setup] done.

  binary : $DEST/bin/chromium
  libs   : LD_LIBRARY_PATH=$DEST/lib/lib

Launch flags required for software WebGL:
  --no-sandbox --disable-dev-shm-usage
  --use-gl=angle --use-angle=swiftshader --enable-unsafe-swiftshader
  --in-process-gpu --disable-gpu-sandbox

Content must be served over http:// — file:// fails ES-module CORS.

Render a Unity scene:
  cd UnityWeb && npm install
  npm run shot -- --scene Assets/Scenes/DMArena1.unity --out ../Artifacts/arena.png
EOF
