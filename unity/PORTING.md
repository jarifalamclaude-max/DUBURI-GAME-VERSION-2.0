# Life as Duburi — Unity port: working rules

This folder is the Unity version of the browser game in `../src` (Three.js +
plain JavaScript). The JavaScript game is the **reference**: same rules, same
physics, same bots, tasks, waters, menus, 1–4 player split screen, CPU
autopilot, controller layouts with re-binding, sounds, quality settings and the
always-visible Pause / Quit buttons. When in doubt, do what the JS does.

Unity cannot run in this container. Everything is checked with the .NET SDK:
the Core logic is run and compared with the JS; the Unity code is compiled
against Unity's real reference assemblies (see `tools/`). Write code that is
correct the first time it opens in Unity: no guessing at APIs — look them up in
the real sources listed below.

## Target

* Unity **6.3 LTS** (6000.3), project created from the **Universal 3D** (URP)
  template, Input System package 1.14 ("Input System Package (New)" active),
  uGUI 2.0 (com.unity.ugui). C# 9 (`LangVersion 9`), .NET Standard 2.1 API.
* Main build target: **WebGL** (shared as a link). Also works in the Editor
  (Play mode) and should work on desktop / Android builds.
* How the user installs it (code must fit this):
  1. Unity Hub → New project → Unity 6.3 LTS → *Universal 3D*.
  2. Copy `LifeAsDuburi/` into the project's `Assets/`.
  3. Menu **Life as Duburi → Set Up Project** (Editor script): creates
     `Assets/LifeAsDuburi/Scenes/LifeAsDuburi.unity` with one GameObject that has
     the `Duburi.Game.Boot` component, the template materials in
     `Resources/Duburi/Materials/`, texture import settings, fog shader
     stripping, WebGL player settings, and puts the scene first in Build Settings.
  4. Press Play, or **Life as Duburi → Build WebGL**.

## Layout

```
unity/
  PORTING.md                this file
  README.md                 for the team (how to open, play, build)
  LifeAsDuburi/             -> copied to Assets/LifeAsDuburi
    Core/                   pure C# game logic, namespace Duburi.Core, NO UnityEngine
      Duburi.Core.asmdef    noEngineReferences: true
    Runtime/                Unity code, namespace Duburi.Game (Assembly-CSharp, no asmdef)
      App/                  Boot, App (= src/main.js), views, game loop, Json.cs, Colors.cs
      Render/               = src/render/*  (URP, procedural meshes, no custom shaders)
      UI/                   = src/ui/menu.js, hud.js, controls.js, style.css, overlays of main.js (uGUI built in code)
      Input/                = src/ui/input.js (Input System)
      Audio/                = src/ui/audio.js
    Editor/                 Set Up Project / Build WebGL menu items, namespace Duburi.EditorTools
    Resources/Duburi/Art    logo + handbook images (jpg)  -> Resources.Load<Texture2D>("Duburi/Art/logo")
    Resources/Duburi/Fonts  Saira Semi Condensed + IBM Plex Mono (OFL) -> Resources.Load<Font>("Duburi/Fonts/SairaSemiCondensed-SemiBold")
    Resources/Duburi/Materials  created by Set Up Project (see Materials)
  tools/
    check.sh                core | Render | InputAudio | UI | all | lint
    CoreTests/              dotnet console app: Core vs JS golden data
    golden/                 node scripts that run the JS original and write golden JSON
    CompileCheck/           compile-only projects (Runtime, Editor)
    stubs/URP               hand-written URP stubs (only members that exist in URP 17.3)
    stubs/UnityEditor       hand-written UnityEditor stubs (only members that exist in 6000.3)
    stubs/Contracts         Input / Sound / Render API other modules may use (see Contracts)
    stubs/Shims             Unity 6 engine types missing from the 2021.3 reference DLLs
    .refs/                  UnityEngine*.dll (2021.3 reference assemblies), UnityEngine.UI.dll
                            (uGUI 2.0.0 from the 6000.3 branch), Unity.InputSystem.dll (1.14.2)
```

Real sources to look APIs up in (read-only, not part of the project):
`/tmp/claude-0/-home-user/eff2f4c5-7fb0-55f1-849d-9b24246e8f56/scratchpad/unitysrc/`
* `graphics/Packages/com.unity.render-pipelines.universal/{Runtime,Shaders,Editor/ShaderGUI}` — URP 17 for Unity 6000.3
* `graphics/Packages/com.unity.render-pipelines.core/Runtime` — Volume, VolumeProfile, parameters
* `inputsystem/Packages/com.unity.inputsystem/InputSystem` — Input System 1.14.2
* `ugui/com.unity.ugui/Runtime/UGUI` — uGUI 2.0.0 (6000.3)
* `csref/` — UnityCsReference 6000.3.27f2 (UnityEditor + engine managed code), sparse

## Rules for all C#

* Port faithfully: same constants, same order of operations, same names
  (C# style: classes PascalCase; keep JS member names recognisable, e.g. JS
  `veh.p` -> `Veh.P` or `veh.p` — pick one style per module and keep it).
  Keep the JS comments that explain physics / rules.
* Core uses `double` (JS numbers are doubles). Unity layer uses `float`.
* No `UnityEngine.Input` (legacy input throws in Input System projects), no
  threads, no `System.IO` files, no TextMeshPro, no custom `.shader`/Shader
  Graph files, no `OnAudioFilterRead`, no `FindObjectOfType`/`GameObject.Find`,
  no `Resources.GetBuiltinResource<Font>("Arial.ttf")` (Unity 6 has
  `LegacyRuntime.ttf`; we ship our own fonts anyway). `tools/check.sh lint` enforces this.
* Persistence: `PlayerPrefs` (works on WebGL) with the same keys and JSON as the
  browser's localStorage: `life-as-duburi-controls-v1`, `life-as-duburi-setup-v2`,
  `life-as-duburi-quality`. JSON: `Duburi.Game.Json` (Runtime/App/Json.cs).
* Colours: `Duburi.Game.Colors.Hex("#ffb547")`, `Colors.Rgb(0x2f9fc4)`.
* Keep per-frame allocations low (WebGL GC); cache lists, no LINQ in Update paths.
* Obsolete APIs are compile errors in the checks (they may be removed in Unity 6).
* Anything Unity-version-specific you are not sure about: read the real source
  above. If a member is missing from the reference DLLs because it is newer than
  2021.3 but exists in 6000.3, add it to `tools/stubs/Shims` with a comment
  citing the 6000.3 source file and line.

## Coordinates

The physics is NED (x north, y east, z down), body axes FRD (x forward, y
right, z down), quaternions `[w, x, y, z]` body->world. The JS renders with
three.js (right-handed, y up): `three = (y, -z, -x)`, quaternion
`three (x,y,z,w) = (qy, -qz, -qx, qw)`.

Unity is left-handed (x right, y up, z forward). The port renders in
**three space under a mirrored root**: the Render module creates one root
GameObject `World` with `localScale = (1, 1, -1)`; every mesh object is a child
of it and uses the JS three.js positions / rotations / geometry literally
(Unity flips the culling of negatively scaled objects, so the image is the
same as the browser's). This keeps the ~2,800 lines of scene code a close port.

Things that do NOT live under the mirrored root and need converting
(`Duburi.Game.Space` in Runtime/Render/Space.cs):
* positions: `Space.ToUnity(three) = (x, y, -z)`
* rotations, including cameras (three cameras look down -Z, Unity cameras down +Z —
  the same formula handles it): `Space.RotToUnity(q3) = new Quaternion(-q3.x, -q3.y, q3.z, q3.w)`
* directions (lights shine along `transform.forward`): `(x, y, -z)` then `Quaternion.LookRotation`
* cameras, lights, particle systems (bubbles), audio listener
* never call `Transform.LookAt` on children of the mirrored root (it works in
  world space and cannot express a mirror); port three's `Object3D.lookAt` as a
  quaternion in three space instead.
* NED -> Unity world directly: position `(y, -z, x)`, quaternion
  `new Quaternion(-qy, qz, -qx, qw)`.

## Materials (URP, no custom shaders)

Shader variants are stripped from builds unless a material in the build uses
them, and `Shader.Find` only finds shaders that are in the build. So:
* Set Up Project creates template materials in `Resources/Duburi/Materials/`:
  `Lit` (URP/Lit opaque), `LitTransparent` (URP/Lit, alpha blend, ZWrite off),
  `LitEmissive` (URP/Lit opaque with `_EMISSION`), `LitCutout` (alpha clip),
  `Unlit`, `UnlitTransparent`, `UnlitAdditive`, `Particle` (URP/Particles/Unlit, alpha blend),
  `ParticleAdditive`.
* Runtime code gets materials only through `Render/Mats.cs`, which loads these
  templates and clones them (`new Material(template)`), changing only
  properties that need no new keywords: `_BaseColor`, `_BaseMap` (+ `_MainTex`),
  `_EmissionColor` (emissive template only), `_Smoothness`, `_Metallic`,
  `_Cutoff`, render queue offsets. `Mats` falls back to `Shader.Find` with a
  logged warning when a template is missing (Editor without Set Up).
* Fog: Set Up Project saves the scene with fog on (Exponential Squared) and sets
  Graphics fog stripping to keep all fog modes, so URP fog works in the build.
* Post-processing: Set Up Project also creates `Resources/Duburi/PostProfile.asset`,
  a VolumeProfile with Bloom, Tonemapping (ACES), ColorAdjustments and Vignette
  (all overrides active), so their shader variants are in the build. Render
  loads it with `Resources.Load<VolumeProfile>("Duburi/PostProfile")` and clones
  it per view (`Object.Instantiate`). Per-view grading uses one global Volume per
  view on its own layer (layers 24..27) and the camera's `volumeLayerMask`.
  Bot thumbnails render on layer 29 (camera culling mask + light culling mask).
* Material keywords in Set Up Project: set the URP properties by hand (`_Surface`,
  `_Blend`, `_SrcBlend`, `_DstBlend`, `_ZWrite`, `_AlphaClip`, keywords, render
  queue, RenderType tag) exactly as URP 17's `BaseShaderGUI.SetupMaterialBlendMode`
  does, then call `UnityEditor.BaseShaderGUI.SetMaterialKeywords` **through
  reflection** if it exists (no compile-time dependency on the URP editor assembly).

## Contracts between modules (parallel work)

UI is written at the same time as Input, Audio and Render, against the stubs in
`tools/stubs/Contracts/*.cs`. Input, Audio and Render must implement every
member there with exactly that signature (more members are fine). UI may use
only those members of the other three. App (`src/main.js`) is written last and
may use everything.

## Checks

```
tools/check.sh core          # Core tests (C# vs JS golden data)
tools/check.sh Render        # one module alone (Render | InputAudio | UI)
tools/check.sh all           # lint + whole Runtime as WebGL player + as Editor + Editor scripts
```
