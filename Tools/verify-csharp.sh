#!/usr/bin/env bash
# Compile-check Code Of Duty's C# without opening the Unity Editor.
#
#   Tools/verify-csharp.sh            # whole runtime tree (game + Invector + FishNet)
#   Tools/verify-csharp.sh file.cs …  # subset, compiled against the full tree result
#
# Two backends:
#   1. A real Unity install (UNITY_ROOT, macOS layout) — original behaviour.
#   2. The sandbox toolchain in /tmp/toolchain (built by Tools/sandbox-toolchain.sh):
#      Roslyn C#9 + Unity 6 player refs + Unity 6000.2 package assemblies.
# Backend 2 compiles three assemblies in dependency order:
#      GameKit.Dependencies -> FishNet.Runtime -> Assembly-CSharp (game+Invector)
# Editor-only folders are skipped (no UnityEditor reference assembly here);
# they are reviewed via Tools/lint-csharp.mjs instead.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

T=/tmp/toolchain
if [[ ! -x "$T/sdk5/dep/dotnet/dotnet" ]]; then
  echo "Sandbox toolchain missing — run Tools/sandbox-toolchain.sh first." >&2
  exit 2
fi

DN="$T/sdk5/dep/dotnet"
CSC=("$DN/sdk/5.0.208/Roslyn/bincore/csc.dll")
UREF="$T/u6000"                 # Unity 6000.0.58 engine modules (LavaGang zip)
PREF="$T/built"                 # UI/TMP/InputSystem compiled from needle-mirror source + stubs
BCL="$DN/shared/Microsoft.NETCore.App/5.0.11"
OUT=/tmp/cod-build
mkdir -p "$OUT"

DEFINES="UNITY_5_3_OR_NEWER;UNITY_5_4_OR_NEWER;UNITY_2017_1_OR_NEWER;UNITY_2018_1_OR_NEWER;UNITY_2018_2_OR_NEWER;UNITY_2018_3_OR_NEWER;UNITY_2019_1_OR_NEWER;UNITY_2019_3_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2020_3_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2023_2_OR_NEWER;UNITY_2023_3_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_6000_0;UNITY_64;UNITY_STANDALONE;UNITY_STANDALONE_LINUX;NET_4_6;NET_STANDARD_2_0;CSHARP_7_3_OR_NEWER;ENABLE_LEGACY_INPUT_MANAGER;ENABLE_INPUT_SYSTEM;FISHNET;FISHNET_V4;INVECTOR_BASIC;INVECTOR_MELEE;INVECTOR_SHOOTER;UNITY_POST_PROCESSING_STACK_V2;UNITY_SOCKET_FIX"

run_csc() {
  LD_LIBRARY_PATH="$T/ssl" "$DN/dotnet" "${CSC[@]}" -nologo -nostdlib -unsafe -nowarn:0169,0649,0414,0108,0618,0114,0472,0162,0067,0219,0429 \
    -langversion:9.0 -define:"$DEFINES" -target:library "$@"
}
FN_CA=Assets/External/FishNet/Runtime/Plugins/CodeAnalysis/FishNet.CodeAnalysis.dll

refs=()
for f in "$BCL"/*.dll; do
  case "$(basename "$f")" in mscorlib.dll) ;; *) refs+=(-r:"$f");; esac
done
for f in "$UREF"/UnityEngine.*.dll; do refs+=(-r:"$f"); done
for f in "$PREF"/*.dll; do refs+=(-r:"$f"); done

srcs() { # find .cs excluding Editor folders
  find "$@" -name '*.cs' -not -path '*/Editor/*' -not -name '*Editor.cs' | sort
}

POLY="Tools/sandbox/CompilerPolyfills.cs"

# ---- 1. GameKit.Dependencies (FishNet plugin dependency)
if [[ ! -f "$OUT/GameKit.Dependencies.dll" || -n "$(find Assets/External/FishNet/Runtime/Plugins/GameKit -name '*.cs' -newer "$OUT/GameKit.Dependencies.dll" 2>/dev/null | head -1)" ]]; then
  echo "[1/3] GameKit.Dependencies"
  # NOTE: GameKit's "Editor" folder is inside its asmdef, so it is NOT an
  # editor-only folder — it holds the Sirenix placeholder attributes.
  mapfile -t S < <(find Assets/External/FishNet/Runtime/Plugins/GameKit -name '*.cs' | sort)
  run_csc "${refs[@]}" -out:"$OUT/GameKit.Dependencies.dll" "${S[@]}" "$POLY"
fi

# ---- 2. FishNet.Runtime
if [[ ! -f "$OUT/FishNet.Runtime.dll" || -n "$(find Assets/External/FishNet/Runtime -name '*.cs' -newer "$OUT/FishNet.Runtime.dll" 2>/dev/null | head -1)" ]]; then
  echo "[2/3] FishNet.Runtime"
  mapfile -t S < <(find Assets/External/FishNet/Runtime -name '*.cs' \
      -not -path '*Plugins/GameKit*' \
      -not -path '*Transports/Synapse*' | sort)
  run_csc "${refs[@]}" -r:"$OUT/GameKit.Dependencies.dll" -r:"$FN_CA" -out:"$OUT/FishNet.Runtime.dll" "${S[@]}" "$POLY"
fi

# ---- 3. Game + Invector (Assembly-CSharp)
echo "[3/3] Assembly-CSharp (game + Invector)"
GAME_DIRS=(Assets/Scripts Assets/Invector-3rdPersonController)
[[ -d Assets/FPCameraAddon ]] && GAME_DIRS+=(Assets/FPCameraAddon)
mapfile -t S < <(srcs "${GAME_DIRS[@]}")
run_csc "${refs[@]}" -r:"$OUT/GameKit.Dependencies.dll" -r:"$OUT/FishNet.Runtime.dll" -r:"$FN_CA" \
  -out:"$OUT/Assembly-CSharp.dll" "${S[@]}" "$POLY"

# ---- optional: explicit files on top of the tree build
if [[ $# -gt 0 ]]; then
  echo "[extra] $*"
  run_csc "${refs[@]}" -r:"$OUT/GameKit.Dependencies.dll" -r:"$OUT/FishNet.Runtime.dll" -r:"$OUT/Assembly-CSharp.dll" \
    -out:/tmp/verify-extra.dll "$@" "$POLY"
fi

echo "OK"
