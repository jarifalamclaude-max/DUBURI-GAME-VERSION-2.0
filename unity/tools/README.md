# tools/ — checks for the Unity port without Unity

Unity cannot run here, so the port is checked with the .NET SDK (8+) and Node.

```
tools/check.sh core         # Core logic (C#) against the JavaScript original
tools/check.sh Render       # compile one module: Render | InputAudio | UI | Editor
tools/check.sh lint         # project rules (below)
tools/check.sh all          # lint + all Runtime code as the WebGL player + as the Editor + the Editor scripts
```

## What the checks do

* **core** — `golden/` runs the JavaScript game (`../src`) in Node and writes
  golden JSON (`golden/out/`); `CoreTests/` runs `LifeAsDuburi/Core` on the same
  inputs and compares.
* **Render / InputAudio / UI / Core** — `CompileCheck/Runtime/Runtime.csproj`
  compiles that module (plus `Runtime/App/Json.cs` and `Colors.cs`) as
  `Assembly-CSharp` against the reference assemblies in `.refs/`, the URP stubs
  (`stubs/URP`) and the Unity 6 shims (`stubs/Shims`). UI compiles against the
  contract stubs of Input, Sound and Render (`stubs/Contracts`), so modules can be
  written in parallel. Defines are those of a Unity 6.3 WebGL player
  (`UNITY_WEBGL`, `ENABLE_INPUT_SYSTEM`, `UNITY_6000_3_OR_NEWER`, ...);
  `-p:EditorMode=true` compiles as the Editor (`UNITY_EDITOR`).
* **Editor** — `CompileCheck/Editor/Editor.csproj` compiles
  `LifeAsDuburi/Editor` as `Assembly-CSharp-Editor` against the UnityEditor stubs
  (`stubs/UnityEditor`).
* Obsolete-API warnings (CS0618/CS0612/CS0619) are errors: they may be gone in Unity 6.
* **lint** — greps the sources for things that break in Unity or on WebGL:
  UnityEngine in Core, legacy `UnityEngine.Input`, threads, file IO in the player,
  TextMeshPro, `Arial.ttf`, scene searches (`FindObjectOfType`, `GameObject.Find`),
  custom shaders, `Shader.Find` outside `Render/Mats.cs`, `OnAudioFilterRead` /
  streamed clips, and `UnityEditor` outside `#if UNITY_EDITOR`.

## Stubs

Hand-written, never shipped. Only members that exist in Unity 6000.3 / URP 17
with the same signatures, each with a `// <source file>:<line>` comment pointing
into UnityCsReference 6000.3 or the URP 17 package. Classes are `partial`, so
another file can add members. Add a member only after reading it in the real
source.

## `.refs/` — the reference assemblies

Not in git (`.gitignore`). `tools/fetch-refs.sh` rebuilds the folder from scratch:

1. `UnityEngine*.dll` — the **UnityEngine.Modules 2021.3.33** NuGet package from
   nuget.org (`lib/net45`). These are reference assemblies (API only). Members that
   are newer than 2021.3 but exist in 6000.3 go in `stubs/Shims`.
2. `UnityEngine.UI.dll` — **uGUI 2.0** compiled from
   `github.com/Unity-Technologies/uGUI`, branch `6000.3`
   (`com.unity.ugui/Runtime/UGUI`), with `refs-build/Unity6EngineShims.cs` and
   these patches for engine members the 2021.3 assemblies lack:
   * `GraphicRaycaster.cs`: `UnityEngineInternal.DisplayInternal.IsASecondaryDisplayIndex(displayIndex)` → `displayIndex > 0`
   * `Image.cs`: `GetSecondaryTextureCount()` → `0` / `true`; drop `GetSecondaryTextures`,
     `SetSecondaryTextureCount`, `SetSecondaryTexture`; compare `SecondarySpriteTexture` with `Equals`
   * `LayoutGroup.cs`: drop the `rectTransform.sendChildDimensionsChange` lines
   * `Slider.cs`: drop `MarkDirty();`
3. `Unity.InputSystem.dll` — **Input System 1.14.2** (tag `1.14.2` of
   `github.com/Unity-Technologies/InputSystem`), compiled from
   `Packages/com.unity.inputsystem/InputSystem` without its `Editor` folders, plus
   the shim `namespace UnityEngine.InputSystem.Editor { internal static class CompileCheckShim {} }`.

The defines used are in the generated `.csproj` files inside the script.
Needs curl, git, unzip and dotnet. For offline use set `MODULES_NUPKG`,
`UGUI_REPO`, `INPUTSYSTEM_REPO` to local copies; `REFS_OUT` writes elsewhere.

## `build-audio-jslib.mjs`

```
node unity/tools/build-audio-jslib.mjs
```

Bundles the browser game's sound engine (`src/ui/audio.js`) with esbuild into
`LifeAsDuburi/Plugins/WebGL/DuburiAudio.jslib`, so the WebGL build plays exactly
the browser game's Web Audio sound. The generated file is committed; run the
script again whenever `src/ui/audio.js` changes. The vehicle data layout it reads
(13 floats per vehicle) must match `Runtime/Audio/Sound.cs`.
