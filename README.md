# Life as Duburi

![Life as Duburi title screen](screenshots/title.jpg)

**Life as Duburi** is an underwater robotics game by **Team BRACU DUBURI**. You are an
autonomous underwater vehicle on a real AUV-competition course: pass the gate, run the slalom,
drop markers in the bin, fire torpedoes through a board, clean up trash at the octagon and
come home. 1 to 4 players in the same water on one screen, keyboards or gamepads, with
optional CPU opponents.

There are two versions in this repository:

* **2.0 — Unity version** ([`unity/`](unity)): the same game in Unity 6 (URP), built for the
  web (WebGL), the Unity Editor, desktop or Android. See below.
* **1.1 — browser version** ([`src/`](src), [`dist/life-as-duburi.html`](dist/life-as-duburi.html)):
  the original Three.js game, playable in a browser with nothing to install. See
  [Browser version (v1.1)](#browser-version-v11).

## Life as Duburi 2.0 — Unity version

Version 2.0 of *Life as Duburi* is a **Unity 6** version of the game, in the
[`unity/`](unity) folder: an underwater robotics game for 1 to 4 players on one
screen (split screen), with keyboards and gamepads. It plays the same as the
browser game (v1.1, described further down): same robots, tasks, waters, menus,
CPU autopilot, controls screen and sounds. The browser game in `src/` is the
reference; the game rules and physics in C# give exactly the same numbers.

The main target is **WebGL**: you build it once and share a link. It also runs
in the Unity Editor (Play) and should work as a desktop or Android build.

### What you need

* **Unity Hub** (free): https://unity.com/download
* **Unity 6.3 LTS** (version 6000.3.x), installed from Unity Hub, with the module
  **Web Build Support** (called *WebGL Build Support* in older Unity versions; tick it when you install, or add it later:
  Unity Hub > Installs > ⚙ > Add modules).

### Open it

1. Unity Hub > **New project** > Unity 6.3 LTS > template **Universal 3D** > Create.
2. In your file manager, copy the folder `unity/LifeAsDuburi` into the
   new project's `Assets` folder (so you have `Assets/LifeAsDuburi`).
3. Back in Unity, wait for it to import. If Unity asks to enable the new
   **Input System** and restart, say **Yes**.
4. Menu **Life as Duburi > Set Up Project**. It checks the project and creates
   the scene, materials and settings. A window lists what it did. It is safe to
   run again.
5. Press **Play**.

### Build for the web

Menu **Life as Duburi > Build WebGL**. It checks the project, offers to switch
to the WebGL platform (the first switch takes a few minutes), and builds into
`<your project>/Builds/WebGL`. At the end it shows the size and the folder.

To test the build on your computer it must come from a web server (opening
`index.html` directly does not work). In the build folder run
`python -m http.server 8000` and open http://localhost:8000. Or use
File > Build And Run.

### Share the build

The build is a normal static website (it uses Gzip with a fallback, so any
host works, no special server settings):

* **itch.io** — zip the *contents* of `Builds/WebGL` (index.html at the top of
  the zip), create a project, kind "HTML", upload the zip, tick "This file will
  be played in the browser".
* **GitHub Pages** — put the contents of `Builds/WebGL` in a repository (or a
  `gh-pages` branch / `docs` folder) and turn on Pages in the repository settings.
* **Netlify Drop** — drag the `Builds/WebGL` folder onto https://app.netlify.com/drop.

#### What a build gives away

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

### Controls

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

### First playtest checklist

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

### Known risks / first things to check in Unity

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

### Troubleshooting

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

### Files

```
unity/
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
  tools/                           checks that run without Unity (see unity/tools/README.md)
  PORTING.md                       rules for the people porting the code
```

---

## Browser version (v1.1)

The original game, written in JavaScript with three.js. It runs in any modern browser with
nothing to install. The vehicles run on the **DUBURI ROV simulator**'s own physics: 6-DOF hydrodynamics,
Blue Robotics T200 / T500 thruster curves, ArduSub-style stabilise + depth hold, EOS-80 sea
water. The physics modules in `src/physics`, `src/alloc`, `src/core` and `src/hydro` come
unchanged from the [DUBURI simulator](https://github.com/jarifalamclaude-max/simulator).

### ▶ Play

* **Offline, no install:** download [`dist/life-as-duburi.html`](dist/life-as-duburi.html) and
  double-click it. Everything (code, textures, logo, sound) is inside that one file.
* **On GitHub Pages:** in the repository's *Settings → Pages* choose branch `main`, folder
  `/docs`; the game is then at `https://<owner>.github.io/<repository>/`, for example
  `https://jarifalamclaude-max.github.io/Life_in_DUBURI/`.

Chrome, Edge or Firefox with WebGL2. Gamepads (PlayStation / Xbox) work in Chrome and Edge.
If a laptop struggles, set *Graphics* to Medium or Low in the pause menu.

### Promo video

[![Life as Duburi — promo video (40 s)](screenshots/promo.jpg)](promo/life-as-duburi-promo.mp4)

[▶ Watch the 40-second promo](promo/life-as-duburi-promo.mp4) — recorded frame by frame from the game
itself (CPU autopilots flying the course), with an original synthesised soundtrack.

### Join like on a console

![Lobby](screenshots/lobby.jpg)

Press **✕ / A** on a controller, **Space** (left keyboard) or **Enter** (right keyboard) to join.
Each player picks a vehicle with **◀ ▶** and a colour with **▲ ▼**, **Share / View** shows the
controller layout, **△ / Y** adds a CPU opponent, **Options / Start** starts the match. The screen splits by itself: one player full
screen, two side by side, three or four in quarters (with three, the fourth quarter is a TV
camera that follows the leader, with live standings).

![Split screen](screenshots/split-screen.jpg)

### Vehicles

| Vehicle | Build | Character |
|---|---|---|
| **Duburi** | open frame, 8 × T200 vectored (BlueROV2 Heavy layout) | full 6-DOF, good at everything, clean rolls for gate style |
| **Dubo Mini** | mini AUV, **5** small thrusters: 2 drive, 1 lateral tunnel, 2 vertical tunnels | light and quick to strafe, does pitch flips, gets pushed by current |
| **Barracuda** | torpedo AUV, 5 × T200 | fastest (2 m/s) but turns slowly and cannot roll |
| **Kraken** | box frame, 6 × T500 | heavy, very stable, shrugs off current, no pitch control |
| **Manta** | flat wing, 8 × T200 | the stunt bot — rolls fast, a bit twitchy in waves |

Speeds, turn rates and controllable axes in the lobby are computed from each design's
thrusters and drag, not typed in.

### Water

| Swimming pool | Pacific Ocean | Arctic Ocean |
|---|---|---|
| ![](screenshots/pool.jpg) | ![](screenshots/pacific.jpg) | ![](screenshots/arctic.jpg) |
| 7 m deep, 26 °C fresh water, tiled pool | 12 m, 22 °C reef lagoon, sand and coral, swell | 15 m, −1.5 °C under the sea ice, marine snow |

Depth can also be set to 5–20 m in the lobby. The water is physical, not just a look: density
comes from temperature and salinity (EOS-80), so the same vehicle is ~3 % more buoyant in the
Arctic than in the pool, and current, turbulence and waves really push you around.

### Graphics and sound

* Post-processing on every split-screen view: bloom, underwater colour absorption that gets
  stronger with depth and murk, refraction wobble, vignette, chromatic aberration, film grain.
* Sun shadows, image-based reflections on metal and acrylic, caustics on the floor, god rays,
  thruster bubbles that rise and pop at the surface, marine snow, coral, fish, jellyfish and sea ice.
* Synthesised underwater sound: each thruster's blade-pass hum and motor whine follow its real
  rpm, prop wash and cavitation, muffled water acoustics with a dark room echo, bubbles and
  ambience, hydrophone pings from the pingers, splashes, thuds and gripper servos. Other
  vehicles are heard by distance and direction.
* Graphics quality High / Medium / Low in the pause menu.

### The tasks

| | Task | What to do |
|---|---|---|
| 0 | **Heading out** | Optional coin flip sets your start heading. Dive before leaving the start zone. |
| 1 | **Gate** | Pass under the reef-shark or sawfish side: that is *your* animal for the run. Roll / pitch / yaw quarter-turns in the gate zone score style. |
| 2 | **Slalom** | 3 sets of white / red / white pipes near the floor. Keep the red pipe on the same side as at the gate. |
| 3 | **Bin** | Drop 2 markers in the 12 × 24 in bin, in your animal's half (use the down camera). |
| 4 | **Torpedoes** | Fire 2 torpedoes through the 4 in openings, your animal first, from behind the 2 m line. |
| 5 | **Octagon** | Follow the 30 kHz pinger. Put the cup and spoon in their baskets, spin once per item, surface inside the octagon, face your animal. |
| 6 | **Return home** | Back through the gate under water. |

| Gate | Torpedo board | Bin (down camera) |
|---|---|---|
| ![](screenshots/gate.jpg) | ![](screenshots/torpedo-board.jpg) | ![](screenshots/bin.jpg) |

**Rules:** the gate must come first; surfacing anywhere except inside the octagon is a *breach*
and ends the run; the best run counts; a time bonus (100 points per minute left) needs gate +
bin or torpedo + octagon surface.

#### Skip, forfeit, leave, quit

![Pause menu](screenshots/pause.jpg)

Next to the clock there are always two buttons: **⏸ Pause** and **✕ Quit match** (it asks
before quitting). Each player also has their own pause menu (**Options / Menu**, **Tab** or
**Backspace**; **Esc** for player 1), driven with their own controller:

* **Quit match** — the match ends and everyone goes back to the lobby.
* **Skip task** — no points for it; you are moved to the start of the next task.
* **End run** — stop here and keep the points.
* **Forfeit run** — lose this run's points and start again from the dock.
* **Leave match** — you drop out, the others keep playing and the screen re-splits.
* **Restart match** — same players, same water, from the start.

### Controls — see them, change them

![Controls screen](screenshots/controls.jpg)

Pick up a controller and the game shows you its layout: every player gets a card with *their*
controller (or keys) at the start of a match, and the **Controls** screen (title screen, lobby,
**Share / View** on a controller in the lobby, or *Controls & customise* in the pause menu)
draws the PlayStation or Xbox controller with what every button does — buttons light up live
as you press them.

Everything can be changed and is remembered in the browser, separately for each controller and
each keyboard side:

* **Buttons / keys:** select an action and press the new button or key; one already in use is
  swapped.
* **Sticks:** ArduSub / QGC (left: up-down + turn, right: forward + strafe), Game (left:
  forward + strafe, right: up-down + turn) or Racing (left: forward + turn, right: up-down +
  strafe); invert Y; triggers for up / down on or off.
* **Reset** brings back the defaults.

![Start-of-match controls card](screenshots/controls-card.jpg)

Defaults:

| | Keyboard left | Keyboard right | Gamepad (PS / Xbox) |
|---|---|---|---|
| Forward / back | W / S | ↑ / ↓ | right stick ↕ |
| Turn | A / D | ← / → | left stick ↔ |
| Strafe | Q / E | J / L | right stick ↔ |
| Up / down | R / F | I / K | left stick ↕, L2 / R2 |
| Roll / pitch (style) | Z X / T G | U O / Y H | L1 R1 / D-pad ↑ ↓ |
| Drop marker / fire torpedo / gripper | V / B / C | N / M / , | ✕ A / ○ B / □ X |
| Camera (chase → front → down) | 1 | 7 | △ Y |
| Depth hold on / off | 2 | 8 | Create / View |
| End run / forfeit / skip task | 3 / 4 / 5 | 9 / 0 / − | L3 / R3 / D-pad → |
| Pause menu | Tab or Esc | Backspace | Options / Menu |

Hold Shift for fine control. The HUD shows depth, heading, speed, net buoyancy, an attitude
ball, the hydrophone ring pointing at the 30 kHz (octagon) and 25 kHz (torpedo) pingers, a
mini-map and your task list.

![Results](screenshots/results.jpg)

### Develop

```bash
npm install
npm run build      # -> dist/life-as-duburi.html and docs/index.html
npm test           # headless: physics, rules, skip / forfeit / leave, full CPU runs in every water
npm run e2e        # browser tests with screenshots, incl. a simulated DualSense (needs Playwright + Chromium)
```

```
src/
  main.js       entry: renderer, split screen, pause menus, game loop
  sim/          vehicles, course layout, task judging, autopilot (no graphics, runs in node)
  render/       scenery, course elements, vehicle models, post-processing, bubbles, shadows
  ui/           lobby and menus, HUD, keyboard / gamepad input and re-binding, controls screen, sound engine
  physics/ alloc/ core/ hydro/   from the DUBURI simulator: water, thrusters, allocation, maths
assets/         DUBURI logo and the task pictures
tests/          node tests + browser test
dist/           the built game (one HTML file)
docs/           the same file for GitHub Pages
screenshots/    pictures for this README
```

### What each file does

#### Top level

| File | What it is for |
|---|---|
| `README.md` | This guide. |
| `LICENSE` | GPL-3.0-or-later licence. |
| `package.json` / `package-lock.json` | npm project: dependencies (three.js, esbuild) and the `build`, `test` and `e2e` scripts. |
| `build.mjs` | Build script: bundles `src/` with esbuild and inlines the CSS, images and code into one HTML file. |
| `.gitignore` | Files git ignores (`node_modules/`, test output). |
| `dist/life-as-duburi.html` | **The built game** in one file. Download and double-click to play offline. |
| `docs/index.html` | The same built game, served by GitHub Pages. |
| `docs/.nojekyll` | Tells GitHub Pages to serve the file as it is. |
| `unity/` | **Version 2.0**, the Unity 6 version of the game (see [Life as Duburi 2.0 — Unity version](#life-as-duburi-20--unity-version)). |

#### `assets/` — pictures packed into the game

| File | What it is for |
|---|---|
| `logo.jpg` | BRACU DUBURI logo: title screen, HUD, dock sign, pool banners, Duburi bot side plates. |
| `shark.jpg`, `sawfish.jpg` | Animal prints hanging on the gate and the octagon. |
| `bin-shark.jpg`, `bin-sawfish.jpg` | The two halves inside the bin; also the "chosen animal" icon on the HUD. |
| `board-shark-top.jpg`, `board-sawfish-top.jpg` | The two versions of the torpedo board picture. |

#### `src/` — the game code

| File | What it is for |
|---|---|
| `main.js` | Entry point. Creates the renderer, runs the game loop (120 Hz physics, interpolated drawing), splits the screen per player, handles the pause menus, the controls screen and re-binding, results, bubbles, sound and the TV camera. |

**`src/sim/` — game logic (no graphics; also runs in node for the tests)**

| File | What it is for |
|---|---|
| `game.js` | One match: clock, players, runs, markers / torpedoes / trash, and the judging of every task and rule (gate first, breach, skip, forfeit, leave, time bonus). Point values are in `POINTS`. |
| `vehicle.js` | Vehicle physics (6-DOF body in water, buoyancy, drag, thrusters) and the pilot loop (stabilise, depth hold, roll / pitch flips). |
| `bots.js` | The five vehicles: Duburi, Dubo Mini, Barracuda, Kraken, Manta — mass, size, drag, thruster layout. |
| `course.js` | Where every course element is (gate, path markers, slalom, bin, torpedo board, octagon, pingers, dock) and its sizes. |
| `collide.js` | Collisions of vehicles with the course, the floor, the walls and each other. |
| `scenarios.js` | The three waters (pool, Pacific, Arctic): temperature, salinity, current, waves, depth, colours. |
| `autopilot.js` | CPU opponent: flies the whole course with the same controls a player uses. |
| `stats.js` | Speed, turn rate, axes and buoyancy shown in the lobby, worked out from the physics. |

**`src/render/` — 3D graphics (three.js)**

| File | What it is for |
|---|---|
| `scenery.js` | The water world: surface and waves, floor and caustics, pool walls, coral, ice, fish, jellyfish, particles, light rays, fog. |
| `course3d.js` | 3D models of the course elements. |
| `bots3d.js` | 3D models of the five vehicles (spinning props, gripper, markers, torpedoes) and of the payload items. |
| `art.js` | Loads the animal pictures as textures. |
| `post.js` | Post-processing per split-screen view: bloom, underwater colour and wobble, vignette, grain, tone mapping. |
| `bubbles.js` | Bubble particles from thrusters, torpedoes, markers and splashes. |
| `graphics.js` | Graphics quality presets (High / Medium / Low), reflections and sun shadows. |

**`src/ui/` — menus, HUD, input, sound**

| File | What it is for |
|---|---|
| `menu.js` | Title screen, lobby (join, vehicle, colour, water, depth, clock), how-to-play, results table. |
| `hud.js` | In-game HUD per player: score, depth, heading, attitude ball, hydrophone, mini-map, task list, messages, controls card, leaderboard. |
| `input.js` | Keyboard and gamepad reading, default and saved button / key bindings, stick presets. |
| `controls.js` | Controls screen: the controller drawing, the list of bindings to change, and the start-of-match card. |
| `audio.js` | Synthesised sound: thrusters, underwater ambience, pingers, splashes, hits, menu sounds. |
| `style.css` | Look of all menus and the HUD. |

**From the DUBURI simulator (unchanged)**

| File | What it is for |
|---|---|
| `physics/environment.js` | The water body: pressure, density with depth, current, turbulence, waves. |
| `physics/thruster.js` | T200 / T500 / small thruster models: thrust curves, rotor lag, in-flow, ventilation. |
| `alloc/allocation.js`, `alloc/frames.js` | Thruster allocation (which thruster pushes how much for each move) and ArduSub frame layouts. |
| `hydro/water.js` | Sea-water density and viscosity (EOS-80). |
| `core/math.js`, `core/rng.js` | Maths helpers (vectors, quaternions, matrices) and a seeded random number generator. |

#### `tests/`

| File | What it is for |
|---|---|
| `game.test.mjs` | Node tests (`npm test`): physics, buoyancy, rules, skip / forfeit / leave, depth, full CPU runs for every vehicle and water. |
| `e2e.mjs` | Browser test with screenshots: menus, all waters, 1–4 players, pause menu, lobby join, leave, forfeit. |
| `controls-e2e.mjs` | Browser test with a simulated DualSense: join, layout screen, button and key re-binding. |

#### `promo/` — the promo video

| File | What it is for |
|---|---|
| `life-as-duburi-promo.mp4` | The 40-second promo video (1280×720, 30 fps). |
| `music.mp3` | Its soundtrack: an original 120 BPM track, synthesised by `music.py`. |
| `music.py` | Makes the soundtrack (Python + numpy): drums, bass, arpeggio, risers and impacts timed to the cuts. |
| `capture.mjs` | Records the video frame by frame from the built game (Playwright + Chromium) with the captions; the ffmpeg command to join frames and music is at its top. |

#### `screenshots/`

Pictures used in this README.

### Credits

Team **BRACU DUBURI**, BRAC University. Built on the DUBURI ROV simulator. Task layout, sizes
and pictures follow the 2025 AUV-competition team handbook published by RoboNation. Licence:
GPL-3.0-or-later (see [LICENSE](LICENSE)).
