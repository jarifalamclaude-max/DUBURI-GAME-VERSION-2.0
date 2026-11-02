# Life as Duburi — Unity version

This folder is the Unity version of our browser game *Life as Duburi*: an
underwater robotics game for 1 to 4 players on one screen (split screen), with
keyboards and gamepads. It plays the same as the browser game: same robots,
tasks, waters, menus, CPU autopilot, controls screen and sounds. The browser
game in `../src` is the reference; this is a port of it to Unity 6.

The main target is **WebGL**: you build it once and share a link. It also runs
in the Unity Editor (Play) and should work as a desktop or Android build.

## What you need

* **Unity Hub** (free): https://unity.com/download
* **Unity 6.3 LTS** (version 6000.3.x), installed from Unity Hub, with the module
  **WebGL Build Support** (tick it when you install, or add it later:
  Unity Hub > Installs > ⚙ > Add modules).

## Open it

1. Unity Hub > **New project** > Unity 6.3 LTS > template **Universal 3D** > Create.
2. In your file manager, copy the folder `LifeAsDuburi` into the
   new project's `Assets` folder (so you have `Assets/LifeAsDuburi`).
3. Back in Unity, wait for it to import. If Unity asks to enable the new
   **Input System** and restart, say **Yes**.
4. Menu **Life as Duburi > Set Up Project**. It checks the project and creates
   the scene, materials and settings. A window lists what it did. It is safe to
   run again.
5. Press **Play**.

## Build for the web

Menu **Life as Duburi > Build WebGL**. It checks the project, offers to switch
to the WebGL platform (the first switch takes a few minutes), and builds into
`<your project>/Builds/WebGL`. At the end it shows the size and the folder.

To test the build on your computer it must come from a web server (opening
`index.html` directly does not work). In the build folder run
`python -m http.server 8000` and open http://localhost:8000. Or use
File > Build And Run.

## Share the build

The build is a normal static website (it uses Gzip with a fallback, so any
host works, no special server settings):

* **itch.io** — zip the *contents* of `Builds/WebGL` (index.html at the top of
  the zip), create a project, kind "HTML", upload the zip, tick "This file will
  be played in the browser".
* **GitHub Pages** — put the contents of `Builds/WebGL` in a repository (or a
  `gh-pages` branch / `docs` folder) and turn on Pages in the repository settings.
* **Netlify Drop** — drag the `Builds/WebGL` folder onto https://app.netlify.com/drop.

### What a build gives away

Be aware of what people can get from a shared WebGL build:

* The **C# source files are not in the build.** The code is compiled to
  WebAssembly (IL2CPP).
* But IL2CPP keeps class and method names and all text strings in the build
  (`*.data` / `global-metadata`), and tools exist that recover a readable
  outline of the code from that. Treat the game logic as readable by a
  determined person.
* **Assets can be extracted**: textures, images, fonts, meshes and sounds can be
  pulled out of the data file with free tools.
* So: never put passwords, API keys or private data in the project.

## Controls

Each player picks a device in the join screen. Every key and button can be
changed in the game's Controls screen (saved per device).

| | Keyboard left | Keyboard right | Gamepad (default "ArduSub" sticks) |
|---|---|---|---|
| Forward / back | W / S | ↑ / ↓ | right stick up / down |
| Turn | A / D | ← / → | left stick left / right |
| Strafe | Q / E | J / L | right stick left / right |
| Up / down | R / F | I / K | left stick, or RT / LT |
| Roll | Z / X | U / O | LB / RB |
| Pitch | T / G | Y / H | D-pad ↑ / ↓ |
| Fine control (hold) | Left Shift | Right Shift | — |
| Drop marker | V | N | A / ✕ |
| Fire torpedo | B | M | B / ○ |
| Gripper | C | , | X / □ |
| Camera | 1 | 7 | Y / △ |
| Depth hold | 2 | 8 | View / Create |
| End run | 3 | 9 | LS click |
| Forfeit: new run | 4 | 0 | RS click |
| Skip task | 5 | - | D-pad → |
| Pause | Tab | Backspace | Menu / Options |

Other stick layouts ("Game", "Racing"), inverted Y and triggers-for-up/down are
in the Controls screen.

## Troubleshooting

* **Everything is pink / magenta** — the materials are missing or the project is
  not using URP. Run **Life as Duburi > Set Up Project** and read its window. The
  project must be made from the *Universal 3D* template.
* **"Input System" restart prompt** — answer Yes; Unity restarts once. If
  keys do nothing: Edit > Project Settings > Player > Other Settings > Active
  Input Handling = *Input System Package (New)* (or *Both*).
* **Errors about `UnityEngine.InputSystem` right after copying** — install the
  package: Window > Package Manager > Unity Registry > Input System > Install.
* **No WebGL in Build WebGL / "module not installed"** — Unity Hub > Installs >
  your Unity 6.3 > ⚙ > Add modules > WebGL Build Support, then restart Unity.
* **Gamepad not seen in the browser** — browsers only show a gamepad after you
  press a button on it while the page has focus. Press any button.
* **No sound in the browser** — browsers start audio only after the first click
  or key press on the page. Click once.
* **The build page stays blank when opened from disk** — serve it from a web
  server (see "Build for the web").

## Files

```
LifeAsDuburi/                    copy this folder into Assets/
  Core/                          the game rules and physics, plain C# (no Unity), tested against the JS game
  Runtime/                       the Unity part (namespace Duburi.Game)
    App/                         Boot (the scene's one component), the game loop and screens, JSON, colours
    Render/                      3D scene: robots, pool and sea, lights, cameras, split screen (URP)
    UI/                          menus, HUD, controls screen, overlays (uGUI built in code)
    Input/                       keyboards and gamepads (Input System), key bindings
    Audio/                       sound: Web Audio in the browser, synthesised AudioClips elsewhere
  Plugins/WebGL/DuburiAudio.jslib  the browser game's sound engine for the WebGL build (generated)
  Editor/                        the "Life as Duburi" menu: Set Up Project, Build WebGL
  Resources/Duburi/Art           logo and handbook images
  Resources/Duburi/Fonts         Saira Semi Condensed and IBM Plex Mono (SIL Open Font License)
  Resources/Duburi/Materials     created by Set Up Project
  Scenes/LifeAsDuburi.unity      created by Set Up Project
  ThirdParty/                    font licences
tools/                           checks that run without Unity (see tools/README.md)
PORTING.md                       rules for the people porting the code
```
