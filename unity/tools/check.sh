#!/usr/bin/env bash
# Checks for the Unity port that run without Unity:
#   tools/check.sh core          build + run the Core tests (C# vs the JavaScript original)
#   tools/check.sh Render        compile one module (Render | InputAudio | UI | Editor) on its own
#   tools/check.sh all           lint + compile everything as the WebGL player, as the Editor, and the Editor scripts
# The compile checks need tools/.refs (see tools/README.md).
set -uo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/.." && pwd)
src="$root/LifeAsDuburi"
fail=0

lint() {
  echo "== lint"
  local hits
  check() { # message, then a command whose output means a violation
    local msg=$1; shift
    hits=$("$@" 2>/dev/null || true)
    if [ -n "$hits" ]; then echo "$hits"; echo "lint: $msg"; fail=1; fi
  }
  check "Core must not use UnityEngine (it is compiled with noEngineReferences)" grep -rnE '^\s*using UnityEngine|UnityEngine\.' --include=*.cs "$src/Core"
  check "legacy UnityEngine.Input is disabled in Input System projects" grep -rnE '(^|[^.A-Za-z])Input\.(Get[A-Z]|mousePosition|anyKey|touch|inputString)|UnityEngine\.Input\.' --include=*.cs "$src/Runtime" "$src/Core"
  check "no threads on WebGL" grep -rnE 'new Thread\(|Task\.Run\(|Parallel\.For|ThreadPool\.' --include=*.cs "$src/Runtime" "$src/Core"
  check "no file IO in the player (use PlayerPrefs)" grep -rnE 'System\.IO\.(File|Directory)|File\.(Read|Write|Exists)' --include=*.cs "$src/Runtime" "$src/Core"
  check "use UnityEngine.UI.Text with Resources/Duburi/Fonts, not TextMeshPro" grep -rnE 'TMPro|TextMeshPro' --include=*.cs "$src/Runtime"
  check "Arial.ttf is not a built-in font in Unity 6 (LegacyRuntime.ttf is)" grep -rn '"Arial.ttf"' --include=*.cs "$src"
  check "keep references; do not search the scene" grep -rnE 'FindObjectOfType|FindObjectsOfType|GameObject\.Find\(' --include=*.cs "$src/Runtime"
  check "no custom shaders (they cannot be compiled or tested outside Unity)" find "$src" -name '*.shader' -o -name '*.shadergraph' -o -name '*.hlsl' -o -name '*.cginc'
  check "materials come from Render/Mats.cs only" sh -c "grep -rn 'Shader\.Find(' --include=*.cs '$src/Runtime' | grep -v 'Render/Mats.cs'"
  check "OnAudioFilterRead and streamed AudioClips do not work on WebGL" grep -rnE 'OnAudioFilterRead|PCMReaderCallback|PCMSetPositionCallback' --include=*.cs "$src/Runtime"
  check "UnityEditor in runtime code must be inside #if UNITY_EDITOR" sh -c "grep -rln 'UnityEditor' --include=*.cs '$src/Runtime' | xargs -r grep -L 'UNITY_EDITOR'"
}

build() { # project, extra args...
  local proj=$1; shift
  echo "== dotnet build $(basename "$proj") $*"
  local out
  out=$(dotnet build "$proj" -nologo -v q "$@" 2>&1)
  local rc=$?
  echo "$out" | grep -E ' (error|warning) [A-Z]+[0-9]+' | sed -E "s#$root/##" | sort -u | head -150
  echo "$out" | grep -E '^\s+[0-9]+ (Warning|Error)\(s\)'
  [ $rc -eq 0 ] || fail=1
}

what=${1:-all}
case "$what" in
  core)
    build "$here/CoreTests/CoreTests.csproj"
    if [ $fail -eq 0 ]; then echo "== Core tests"; dotnet run --project "$here/CoreTests/CoreTests.csproj" --no-build -- "${@:2}" || fail=1; fi ;;
  Render|InputAudio|UI|Core)
    build "$here/CompileCheck/Runtime/Runtime.csproj" -p:Module="$what" ;;
  Editor)
    build "$here/CompileCheck/Editor/Editor.csproj" -p:Module=Editor ;;
  all)
    lint
    build "$here/CompileCheck/Runtime/Runtime.csproj"
    build "$here/CompileCheck/Runtime/Runtime.csproj" -p:EditorMode=true
    if ls "$src/Editor"/*.cs >/dev/null 2>&1; then build "$here/CompileCheck/Editor/Editor.csproj"; fi ;;
  lint) lint ;;
  *) echo "usage: $0 core|Render|InputAudio|UI|Editor|Core|all|lint"; exit 2 ;;
esac
[ $fail -eq 0 ] && echo "CHECK PASSED ($what)" || echo "CHECK FAILED ($what)"
exit $fail
