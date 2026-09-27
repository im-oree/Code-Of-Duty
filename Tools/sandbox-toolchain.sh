#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Bootstrap a C# compile-check toolchain in a sandbox with NO Unity install
# and most CDNs blocked (only npm + github.com/codeload/api reachable).
# GitHub LFS is ALSO blocked (media.githubusercontent.com), which rules out
# ~every repo that vendors Unity dlls — everything below is plain-blob only.
#
# Pieces:
#   - .NET SDK 5.0.208 linux-x64 (dotnet muxer + net5 runtime + Roslyn C# 9.0
#     — the same language level Unity 6 compiles with):
#       github.com/meng-xu-cs/mvp-artifact              dep/dotnet/**
#   - libssl/libcrypto 1.1 (net5 needs OpenSSL 1.x, sandbox ships 3.x):
#       github.com/myPorteus/Porteus                    x86_64 001-core lib64
#   - Unity 6000.0.58 engine module reference dlls:
#       github.com/LavaGang/Unity-Runtime-Libraries     6000.0.58.zip (plain blob)
#   - UnityEngine.UI / TextMeshPro / InputSystem: compiled FROM SOURCE out of
#     the needle-mirror UPM mirrors (com.unity.ugui HEAD≈1.0.0 + small Unity 6
#     Vector4-UV patches, com.unity.textmeshpro 3.2.0-pre.15,
#     com.unity.inputsystem 1.20.0 minus the InputForUI plugin).
#   - URP / Cinemachine / EntityId: hand stubs (stubs/UrpCinemachineStubs.cs)
#     covering exactly the surface the project touches.
#
# Everything lands in /tmp/toolchain (outside the repo).
# After this, Tools/verify-csharp.sh compiles the whole game in ~3 s.
# ---------------------------------------------------------------------------
set -euo pipefail

T=/tmp/toolchain
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mkdir -p "$T"

sparse() { # repo dir paths...
  local repo=$1 dir=$2; shift 2
  [ -e "$T/$dir/.git" ] && { echo "[toolchain] $dir already present"; return; }
  git clone --filter=blob:none --no-checkout --depth 1 "https://github.com/$repo" "$T/$dir"
  ( cd "$T/$dir" && git sparse-checkout init --no-cone \
    && printf '%s\n' "$@" > .git/info/sparse-checkout && git checkout )
}

# ---- 1. .NET 5 SDK (muxer + runtime + Roslyn) ------------------------------
sparse meng-xu-cs/mvp-artifact sdk5 \
  "dep/dotnet/dotnet" "dep/dotnet/host/*" "dep/dotnet/shared/*" \
  "dep/dotnet/sdk/5.0.208/Roslyn/bincore/*" "dep/dotnet/sdk/5.0.208/ref/*"
chmod +x "$T/sdk5/dep/dotnet/dotnet"
DN="$T/sdk5/dep/dotnet"
SH="$DN/shared/Microsoft.NETCore.App/5.0.11"

# ---- 2. OpenSSL 1.1 --------------------------------------------------------
if [ ! -f "$T/ssl/libssl.so.1.1" ]; then
  sparse myPorteus/Porteus porteus \
    "x86_64/porteus/base/001-core/usr/lib64/*" \
    "x86_64/porteus/base/001-core/lib64/libssl*" \
    "x86_64/porteus/base/001-core/lib64/libcrypto*"
  mkdir -p "$T/ssl"
  cp -L "$T"/porteus/x86_64/porteus/base/001-core/lib64/libssl.so.1.1 \
        "$T"/porteus/x86_64/porteus/base/001-core/lib64/libcrypto.so.1.1 "$T/ssl/"
fi

# ---- 3. Unity 6000.0.58 engine modules -------------------------------------
if [ ! -f "$T/u6000/UnityEngine.CoreModule.dll" ]; then
  sha=$(gh api "repos/LavaGang/Unity-Runtime-Libraries/git/trees/HEAD" \
        --jq '.tree[] | select(.path=="6000.0.58.zip") | .sha')
  gh api "repos/LavaGang/Unity-Runtime-Libraries/git/blobs/$sha" --jq .content \
    | base64 -d > "$T/u6000.zip"
  mkdir -p "$T/u6000" && (cd "$T/u6000" && python3 -m zipfile -e ../u6000.zip .)
fi

# ---- 4. Package sources -----------------------------------------------------
[ -d "$T/src-ugui" ]        || git clone --depth 1 https://github.com/needle-mirror/com.unity.ugui "$T/src-ugui"
[ -d "$T/src-tmp" ]         || git clone --depth 1 --branch 3.2.0-pre.15 https://github.com/needle-mirror/com.unity.textmeshpro "$T/src-tmp"
[ -d "$T/src-inputsystem" ] || git clone --depth 1 --branch 1.20.0 https://github.com/needle-mirror/com.unity.inputsystem "$T/src-inputsystem"

# ugui 1.0.0 predates Unity's Vector4 UV channels — patch to the Unity 6 API.
python3 - "$T" <<'EOF'
import sys
p = sys.argv[1] + '/src-ugui/Runtime/UI/Core/Utility/VertexHelper.cs'
s = open(p).read()
if 'List<Vector2> m_Uv0S' in s:
    for i in range(4):
        s = s.replace(f'private List<Vector2> m_Uv{i}S;', f'private List<Vector4> m_Uv{i}S;')
        s = s.replace(f'm_Uv{i}S = ListPool<Vector2>.Get();', f'm_Uv{i}S = ListPool<Vector4>.Get();')
        s = s.replace(f'ListPool<Vector2>.Release(m_Uv{i}S);', f'ListPool<Vector4>.Release(m_Uv{i}S);')
    for i, ch in [(0, 'uv'), (1, 'uv2'), (2, 'uv3'), (3, 'uv4')]:
        s = s.replace(f'm_Uv{i}S.AddRange(m.{ch});', f'foreach (var _uv in m.{ch}) m_Uv{i}S.Add(_uv);')
    open(p, 'w').write(s)
    print('[toolchain] patched VertexHelper.cs for Vector4 UVs')
EOF

# ---- 5. Build the package dlls ----------------------------------------------
mkdir -p "$T/built" "$T/stubs"
cp "$HERE/sandbox/UrpCinemachineStubs.cs" "$T/stubs/" 2>/dev/null || true

refs=()
for f in "$SH"/*.dll; do case "$(basename "$f")" in mscorlib.dll) ;; *) refs+=("-r:$f");; esac; done
for f in "$T"/u6000/UnityEngine.*.dll; do refs+=("-r:$f"); done

csc() { LD_LIBRARY_PATH="$T/ssl" "$DN/dotnet" "$DN/sdk/5.0.208/Roslyn/bincore/csc.dll" \
        -nologo -nostdlib -langversion:9.0 -target:library -unsafe \
        -nowarn:0169,0649,0618,0414,0108 "$@"; }

DEF_OLD="UNITY_2019_1_OR_NEWER;UNITY_2019_3_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_1_OR_NEWER"
DEF_NEW="$DEF_OLD;UNITY_2021_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2023_2_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_STANDALONE;UNITY_STANDALONE_LINUX;ENABLE_INPUT_SYSTEM"

if [ ! -f "$T/built/UnityEngine.UI.dll" ]; then
  mapfile -t S < <(find "$T/src-ugui/Runtime" -name '*.cs' | sort)
  csc "${refs[@]}" -define:"$DEF_OLD" -out:"$T/built/UnityEngine.UI.dll" "${S[@]}"
  echo "[toolchain] built UnityEngine.UI.dll"
fi
if [ ! -f "$T/built/Unity.TextMeshPro.dll" ]; then
  mapfile -t S < <(find "$T/src-tmp/Scripts/Runtime" -name '*.cs' | sort)
  csc "${refs[@]}" -r:"$T/built/UnityEngine.UI.dll" -define:"$DEF_NEW" \
      -out:"$T/built/Unity.TextMeshPro.dll" "${S[@]}"
  echo "[toolchain] built Unity.TextMeshPro.dll"
fi
if [ ! -f "$T/built/Unity.InputSystem.dll" ]; then
  mapfile -t S < <(find "$T/src-inputsystem/InputSystem" -name '*.cs' -not -path "*Plugins/InputForUI*" | sort)
  csc "${refs[@]}" -r:"$T/built/UnityEngine.UI.dll" -define:"$DEF_NEW" \
      -out:"$T/built/Unity.InputSystem.dll" "${S[@]}"
  echo "[toolchain] built Unity.InputSystem.dll"
fi
csc "${refs[@]}" -out:"$T/built/CodStubs.dll" "$T/stubs/UrpCinemachineStubs.cs"
echo "[toolchain] built CodStubs.dll"

echo "[toolchain] done — run Tools/verify-csharp.sh"
