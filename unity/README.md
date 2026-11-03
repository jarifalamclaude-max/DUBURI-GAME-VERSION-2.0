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
  **Web Build Support** (called *WebGL Build Support* in older Unity versions; tick it when you install, or add it later:
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

## First playtest checklist

Do this once in the Editor (Play) and once in a WebGL build. If a red box
appears at the bottom of the screen, take a screenshot of it (it names the
error and where it happened) and send it to the team; click it to hide it.

1. **Menu** — the title screen shows over the Pacific backdrop: three robots
   circle slowly with spinning props and bubbles, the camera drifts. Click
   *How to play* and back; the menu sounds play after the first click / key.
2. **1 player** — open the lobby, join with Space (keyboard left), pick a robot
   and colour, start. The key card shows for a few seconds. Drive with
   W/S/A/D/R/F: bubbles come from the thrusters, the camera follows and stays
   under the surface and inside the pool. Press 1 to cycle chase / front /
   down camera (front and down hide your own robot). Drop a marker (V), fire a
   torpedo (B), grab (C): each has a sound, the HUD shows toasts and points.
3. **4 players, split screen, with a gamepad** — plug in a controller (the menu
   flashes "Controller 1 connected"), press ✕ / A to join, add keyboard right
   (Enter) and a CPU (△ / Y). Start with Options / Menu. Check the quarters, the
   divider lines and that each HUD sits in its own quarter; with exactly 3
   players the 4th quarter is the TV camera with the live standings. Resize the
   window (or rotate a phone): views and HUD follow, 2 players switch between
   side by side and stacked.
4. **Pause / quit / results** — Tab, Backspace or a pad's Options opens the
   pause menu of that player (only their pad moves in it; D-pad + ✕ / A,
   ○ / B resumes); Esc and the HUD's **Pause** button open the first player's. Try Skip task, Restart match, Controls & customise (re-bind a
   button, then back), Sound on / off. The always-visible **Quit** button asks
   first; *Yes* goes back to the lobby. End the match early: in a match
   without CPUs, end every player's run (3 / 9 / LS click); the match ends a
   few seconds later and the results appear 2.5 s after that. *Play again* /
   *Lobby* / *Main menu* work. Quitting in those 2.5 s must not show results.
5. **Quality switch** — pause > Graphics: Ultra (4K) → High → Medium → Low → Ultra. The
   picture changes (shadows, bloom, sharpness) without a hitch and the choice is
   kept after a restart of the game.
6. **WebGL build** — Life as Duburi > Build WebGL, serve the folder (see "Build
   for the web"), open it in Chrome and Firefox. Check sound after the first
   click, a gamepad after its first button press, the frame rate with 4 players
   on High and Low, and that a page reload keeps controls, lobby setup,
   quality and mute. Note: Set Up Project sets Project Settings > Player > WebGL >
   Publishing Settings > Enable Exceptions to *Full Without Stacktrace*: a null
   reference or index error is caught by the game, which keeps running and shows
   the red box (without the "at ..." line; for that, pick *Full With Stacktrace*
   for a debugging build — it is much slower). With *Explicitly Thrown
   Exceptions Only* such an error would stop the whole WebGL player instead.

## Known risks / first things to check in Unity

Nothing here has run inside Unity yet: the code compiles against Unity 6.3's
reference assemblies and the game rules match the browser game bit for bit, but
these could only be checked against Unity's source, not seen. Look at them first
(Console warnings that start with "Life as Duburi:" point at most of them):

1. **Set Up Project report** — every line should be ✓. Then open
   `Resources/Duburi/Materials/*.mat` and `Resources/Duburi/PostProfile.asset` in the
   Inspector: the materials should show Surface Type Opaque / Transparent as their
   names say (no pink), the profile should list Bloom, Tonemapping (ACES), Color
   Adjustments and Vignette.
2. **Fog per view** — in a 2-player match put one robot at the surface and one deep:
   each view should get its own fog (above / under water). The fog colour and density
   are switched in `RenderPipelineManager.beginCameraRendering`; if Unity sets the
   fog shader constants once per frame instead of per camera, both views get the
   same fog (Render/Scenery.cs `OnBeginCamera`).
3. **Post-processing per view** — bloom, vignette and the under-water colour grade are
   one Volume per view on layers 24–27. If a view looks ungraded, check its camera's
   Volume Mask (Inspector, while playing) and that the URP Renderer has
   Post-processing ticked.
4. **Shadows and floor caustics** — the sun casts shadows on High / Medium and carries
   an animated caustic cookie. They need *Main Light: Cast Shadows* and *Light Cookies*
   on in the URP asset of the active quality level (Project Settings > Quality; on
   WebGL usually the "Mobile" asset). The game cannot switch these on from code.
5. **Bot thumbnails** in the lobby (240×150, transparent background) are rendered with
   `Camera.Render` into an MSAA RenderTexture. If they show black or empty boxes, the
   render-scale / MSAA path of URP is the suspect (Render/BotThumbs.cs).
6. **Hiding your own robot** in the front / down cameras uses
   `Renderer.forceRenderingOff` per camera; check that the other players still see it.
7. **WebGL sound** — the browser's Web Audio engine runs from
   `Plugins/WebGL/DuburiAudio.jslib`; it starts on the first click / key. If the build
   is silent, open the browser console: "Life as Duburi audio unavailable" means the
   plugin failed to load.
8. **WebGL resolution** — High caps the pixel ratio at 1.25 using `Screen.dpi / 96` as
   the browser's devicePixelRatio. If Unity reports another dpi on WebGL, a hi-DPI
   screen renders at full resolution (slower) or too soft.
9. **Gamepads in the browser** — only pads the browser reports with the "standard"
   mapping become Input System `Gamepad`s; others are ignored by the game.
10. **Frame rate with 4 players** — four cameras each render shadows, MSAA and post.
    If WebGL is slow on High, compare with Medium / Low first.
11. **Leaving Play mode** should log nothing. The game puts the URP asset of the
    quality level back when it stops (it plays on a private copy of it); if Project
    Settings > Quality shows a missing pipeline asset after Play, re-assign it.
12. **Enter Play Mode Options** — the code resets its static state for "no domain
    reload", but that mode was not tried; if a second Play misbehaves, turn domain
    reload back on (Project Settings > Editor).

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
  your Unity 6.3 > ⚙ > Add modules > **Web Build Support**, then restart Unity.
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
    App/                         the game itself (= src/main.js):
      Boot.cs                      the scene's one component: starts the App, drives it every frame
      App.cs                       menu backdrop, match start / restart / quit / results, split-screen views
                                   and cameras, the fixed 120 Hz physics loop, input, sounds, bubbles
      ErrorPanel.cs                red box at the bottom of the screen when something throws (screenshot it)
      Json.cs, Colors.cs           small helpers (saved settings, "#rrggbb" colours)
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
