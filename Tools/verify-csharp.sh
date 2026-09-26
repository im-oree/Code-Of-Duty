#!/usr/bin/env bash
# Compile-check Code Of Duty's C# sources against the installed Unity's reference
# assemblies, without opening the Editor. This catches real compile errors fast.
#
# Usage:
#   Tools/verify-csharp.sh [file.cs ...]
# With no arguments it checks the whole Assets/Scripts tree (excluding Editor-only
# folders, which need the UnityEditor reference set — pass them explicitly if needed).
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

UNITY_ROOT="${UNITY_ROOT:-/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents}"
S="$UNITY_ROOT/Resources/Scripting"

if [[ ! -d "$S" ]]; then
  echo "Unity scripting resources not found at: $S" >&2
  echo "Set UNITY_ROOT to your Unity.app/Contents path." >&2
  exit 2
fi

DOTNET="$S/DotNetSdk/dotnet"
CSC="$(find "$S/DotNetSdk/sdk" -name csc.dll -path '*Roslyn*' | head -1)"
if [[ -z "$CSC" ]]; then
  echo "Roslyn csc.dll not found under $S/DotNetSdk/sdk" >&2
  exit 2
fi

ENGINE="$S/Managed/UnityEngine"
NETREF="$S/NetStandard/ref/2.1.0/netstandard.dll"

if [[ $# -gt 0 ]]; then
  SOURCES=("$@")
else
  # game code only; skip Editor/ folders (they need UnityEditor refs)
  # (no mapfile: portable to macOS bash 3.2)
  SOURCES=()
  while IFS= read -r line; do
    [[ -n "$line" ]] && SOURCES+=("$line")
  done < <(find Assets/Scripts -name '*.cs' -not -path '*/Editor/*' | sort)
fi

refs=(-r:"$NETREF")
for f in "$ENGINE"/UnityEngine*.dll; do refs+=(-r:"$f"); done

# Reference every prebuilt game assembly EXCEPT Assembly-CSharp itself:
# including it while also compiling the same sources would duplicate every type.
for dll in Library/ScriptAssemblies/*.dll; do
  case "$(basename "$dll")" in
    Assembly-CSharp.dll|Assembly-CSharp-Editor.dll|Assembly-CSharp-Editor-firstpass.dll|Assembly-CSharp-firstpass.dll) continue ;;
  esac
  refs+=(-r:"$dll")
done

# Explicit file lists are how editor-only scripts get checked, so give them the
# UnityEditor reference set. The whole-tree run deliberately omits it, which keeps
# runtime code honest: touching UnityEditor there would fail a player build.
if [[ $# -gt 0 ]]; then
  # UnityEditor modules ship beside the UnityEngine ones in Managed/UnityEngine.
  for f in "$ENGINE"/UnityEditor*.dll; do [[ -e "$f" ]] && refs+=(-r:"$f"); done
fi

# When checking a subset of files (not the whole tree), the existing Assembly-CSharp
# build resolves already-compiled project types (GameSettings, etc.).
if [[ $# -gt 0 && -f Library/ScriptAssemblies/Assembly-CSharp.dll ]]; then
  refs+=(-r:Library/ScriptAssemblies/Assembly-CSharp.dll)
fi

echo "Compiling ${#SOURCES[@]} file(s)..."
"$DOTNET" exec "$CSC" -nologo -target:library -langversion:9.0 -nostdlib \
  -out:/tmp/verify-csharp.dll \
  "${refs[@]}" "${SOURCES[@]}"
echo "OK"
