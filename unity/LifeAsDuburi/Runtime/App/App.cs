// Life as Duburi — the game app (port of src/main.js). One screen split into one
// viewport per player, PS5-couch style (a TV camera with the leaderboard fills the
// 4th quarter for three players). Physics runs at a fixed 120 Hz; rendering
// interpolates poses. Each view gets its own post-processing volume (bloom,
// under-water grade); URP renders the shadows itself.
//
// Started by Boot (the scene's one component), which forwards Update / LateUpdate /
// OnDestroy. Per frame (main.js frame()): InputHub.Poll -> overlays (keys + pads) ->
// game frame or menu frame -> InputHub.EndFrame. Unity renders the view cameras
// after the scripts; Views.DrawView records what each camera needs (RenderHooks).

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using CoreGame = Duburi.Core.Game;
using Env = Duburi.Core.Environment;

namespace Duburi.Game
{
    public sealed class App : IDisposable
    {
        static readonly string[] CAMS = { "chase", "front", "down" };
        static readonly string[] MENU_BOTS = { "duburi", "manta", "dubomini" };
        static readonly ClearZone[] NoClearZones = new ClearZone[0];
        static readonly BotState MenuBotState = new BotState { gripClosed = false, holding = false, markers = 2, torpedoes = 2 };
        const float ResultsDelay = 2.5f;

        static string SizeOf(string preset) =>
            preset != null && preset.StartsWith("t500", StringComparison.Ordinal) ? "t500" :
            preset != null && preset.StartsWith("generic", StringComparison.Ordinal) ? "small" : "t200";

        static float MaxRpm(string size) => size == "t500" ? 3200f : size == "small" ? 4000f : 3500f;

        // JS Math.round for the (positive) view rects (Mathf.Round rounds half to even)
        static float Round(float v) => Mathf.Floor(v + 0.5f);

        /// one vehicle on screen (main.js this.bots[i])
        sealed class BotView
        {
            public BotMesh m;
            public ContactShadow shadow;
            public V3 prevP;
            public Quat prevQ;
            public readonly BotState state = new BotState();
            public float[] bub;
            public bool wasSurf = true;
            public string size;
            public VehicleSound voice;
            // interpolated pose in three.js space (group.position / quaternion)
            public Vector3 pos3;
            public Quaternion rot3 = Quaternion.identity;
        }

        /// one viewport (main.js this.views[i])
        sealed class View
        {
            public int pi;            // player index, -1 = TV camera
            public bool spectator;
            public ViewCamera cam;
            public bool hasSmooth;    // view.smooth != null
            public Vector3 smooth;
            public Rect rect;
        }

        /// a robot circling in the menu backdrop
        sealed class MenuBot
        {
            public BotMesh m;
            public int i;
            public float ph;
            public BotDesign d;
            public double[] n;        // rotor speeds (0.6 each)
        }

        readonly ErrorPanel errors;
        readonly InputHub input;
        readonly Sound sound;
        readonly UiRoot ui;
        readonly Overlays overlays;
        readonly Menu menu;
        readonly Post post;
        readonly Transform world;

        string state = "menu";
        MatchSetup cfg;
        CoreGame game;
        Scenery scenery;
        SceneExtras extras;
        Course3D course3d;
        Hud hud;

        // menu backdrop
        ViewCamera menuCam;
        readonly List<MenuBot> menuBots = new List<MenuBot>(3);
        float menuTime;

        // match
        string[] devices;
        Autopilot[] pilots;
        BotView[] bots;
        string[] cams;
        List<View> views;
        readonly List<int> viewPlayers = new List<int>(4);
        readonly Dictionary<string, GameObject> itemMeshes = new Dictionary<string, GameObject>();
        readonly HashSet<string> seenItems = new HashSet<string>();
        readonly List<string> goneItems = new List<string>(8);
        readonly List<Rect> hudRects = new List<Rect>(4);
        double acc;
        double[] pingNext;
        bool paused, overShown;
        CoreGame resultsFor;     // main.js setTimeout(showResults, 2500) for this match
        float resultsAt;

        // frame bits
        int W, H;
        bool layoutDirty;
        readonly SoundFrame soundFrame = new SoundFrame();
        readonly SoundFrame menuSound = new SoundFrame { underwater = true, depth = 1.5f, paused = false };
        readonly Func<float, float, float> waveY;
        Env waveEnv;
        readonly bool[] padHeld = new bool[InputHub.MaxPads];
        readonly HashSet<string> loggedErrors = new HashSet<string>();
        // scene / view changes and the last version drawn: a change made after this frame's draw
        // (a uGUI click runs after Boot.Update) is drawn in LateUpdate, so a new camera never renders
        // a frame without its pose and per-camera state
        int sceneVersion, drawnVersion = -1;

        /// smoothed frames per second (main.js this.fps)
        public float Fps { get; private set; } = 60;
        public string State => state;
        public CoreGame CurrentGame => game;

        public App(Transform host, ErrorPanel errors)
        {
            this.errors = errors;
            GraphicsQuality.Apply();          // applyQuality(): render scale, MSAA, shadow map of the URP asset
            post = new Post();                // createPost: follows GraphicsQuality.Set by itself
            world = Space.CreateWorld("World");
            input = new InputHub();
            sound = Sound.Create(host);
            ui = UiRoot.Create(host);
            overlays = new Overlays(ui, input, sound);
            // a controller plugged in while the menu is up: Menu flashes "Controller n connected …" itself
            menu = new Menu(ui, input, sound, StartMatch, dev => overlays.OpenControlsFromMenu(menu, dev));
            waveY = (x, z) => waveEnv != null ? (float)waveEnv.SurfaceElevation(-z, x) : 0f;
            ui.Resized += OnUiResized;
            W = Screen.width; H = Screen.height;
            MenuScene();
            menu.Show("title");
            Resize();
        }

        void OnUiResized() => layoutDirty = true;

        // ------------------------------------------------------------------ per frame
        /// main.js frame(): Boot.Update
        public void Frame()
        {
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            Fps += (1f / Mathf.Max(dt, 1e-3f) - Fps) * 0.05f;
            try
            {
                step = "input"; input.Poll();
                step = "audio unlock"; UnlockAudio();
                step = "resize";
                if (Screen.width != W || Screen.height != H) { Resize(); layoutDirty = true; } // the canvas scale settles later: lay out again
                else if (layoutDirty) { layoutDirty = false; if (views != null) LayoutViews(); }
                step = "overlays"; overlays.Tick(); // capture keys, menu navigation keys, navPoll, live pad drawing
                if (state == "game" && game != null)
                {
                    // main.js onKey: Escape opens the pause menu of the first viewed player
                    step = "pause key";
                    if (!overlays.IsOpen && input.Consume("Escape")) OpenPause(viewPlayers.Count > 0 ? viewPlayers[0] : 0);
                    if (game != null) GameFrame(dt);
                }
                else
                {
                    step = "menu"; menu.Poll();
                    MenuFrame(dt);
                }
                step = "results";
                // showResults 2.5 s after the match ended: only for the match on screen, never after quitting
                if (resultsFor != null && Time.unscaledTime >= resultsAt)
                {
                    var g = resultsFor;
                    resultsFor = null;
                    if (game == g && state == "game") ShowResults();
                }
            }
            catch (Exception e) { Report(e); }
            finally { input.EndFrame(); }
        }

        /// Boot.LateUpdate: the HUD layout after the canvas scaler applied a new screen size
        public void LateFrame()
        {
            try
            {
                step = "late layout";
                if (layoutDirty && Screen.width == W && Screen.height == H)
                {
                    layoutDirty = false;
                    if (views != null) LayoutViews();
                }
                if (drawnVersion != sceneVersion)
                {
                    step = "late draw";
                    if (state == "game" && game != null && views != null)
                    {
                        SyncMeshes((float)Math.Min(1, acc / Vehicle.DT), 0);
                        for (int i = 0; i < views.Count; i++) RenderView(views[i], 0);
                        drawnVersion = sceneVersion;
                    }
                    else if (menuCam != null && scenery != null && extras != null) MenuBackdrop(0);
                }
            }
            catch (Exception e) { Report(e); }
        }

        // which part of the frame is running: shown with an error, because WebGL builds have no stack traces
        string step = "start";

        // keep the loop going after an exception: log each kind of error once with where it happened (the
        // console; the error panel too in the Editor / Development builds) and count repeats, so a fault
        // that happens every frame (or two that alternate) does not flood the log
        void Report(Exception e)
        {
            string key = Faults.Key(e, step);
            if (loggedErrors.Contains(key)) { errors?.Repeat(); return; }
            if (loggedErrors.Count >= 64) loggedErrors.Clear();
            loggedErrors.Add(key);
            Debug.LogError(Faults.Prefix + key + "\n" + e);
        }

        // browsers start audio only after a user gesture: any key, click, tap or new pad button
        void UnlockAudio()
        {
            bool gesture = input.Pressed.Count > 0;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) gesture = true;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) gesture = true;
            var pads = input.Pads();
            for (int k = 0; k < pads.Count; k++)
            {
                var pad = pads[k];
                if (pad == null || pad.index < 0 || pad.index >= padHeld.Length) continue;
                bool any = false;
                for (int b = 0; b < pad.pressed.Length; b++) if (pad.pressed[b]) { any = true; break; }
                if (any && !padHeld[pad.index]) gesture = true;
                padHeld[pad.index] = any;
            }
            if (gesture) sound.Ensure();
        }

        void Resize()
        {
            W = Screen.width; H = Screen.height;
            menuCam?.SetRect(0, 0, W, H, H);
            if (views != null) LayoutViews();
        }

        // ------------------------------------------------------------------ menu background
        void MenuScene()
        {
            DisposeScene();
            var sc = Scenarios.SCENARIOS["pacific"];
            var arena = Course.ARENA.Clone();
            arena.depth = 12;
            scenery = Scenery.Create(world, sc, Art.Logo, arena, NoClearZones);
            extras = SceneExtras.Create(scenery, sc.look, arena);
            menuCam = new ViewCamera("menu camera", 60, 0.05f, 400, 0);
            menuCam.SetRect(0, 0, W, H, H);
            for (int i = 0; i < MENU_BOTS.Length; i++)
            {
                var d = Bots.BOTS[MENU_BOTS[i]];
                var m = Bots3D.CreateBotMesh(d, d.color, Art.Logo, world);
                Graphics3D.CastShadows(m.group);
                var n = new double[d.thrusters.Length];
                for (int k = 0; k < n.Length; k++) n[k] = 0.6;
                menuBots.Add(new MenuBot { m = m, i = i, ph = i * 2.1f, d = d, n = n });
            }
            menuTime = 0; // fakeEnv.time
            waveEnv = null;
            sceneVersion++;
        }

        void MenuFrame(float dt)
        {
            if (menuCam != null && scenery != null && extras != null) MenuBackdrop(dt);
            step = "menu sound";
            try { sound.Tick(dt, menuSound); } catch (Exception e) { Report(e); }
        }

        void MenuBackdrop(float dt)
        {
            float t = menuTime += dt;
            var B = extras.Bubbles;
            step = "menu robots";
            for (int k = 0; k < menuBots.Count; k++)
            {
                var b = menuBots[k];
                if (b.m == null || b.m.group == null) continue;
                float a = t * 0.25f + b.ph;
                float rad = 2.2f + b.i * 0.8f;
                var pos = new Vector3(Mathf.Cos(a) * rad, -1.4f - b.i * 0.5f + Mathf.Sin(t * 0.7f + b.i) * 0.15f, Mathf.Sin(a) * rad - 2);
                var rot = Space.Euler(Mathf.Sin(t * 0.5f + b.i) * 0.08f, -a, 0);
                b.m.SetPose(pos, rot);
                b.m.Update(dt, b.n, MenuBotState);
                // bubbles streaming from the thrusters
                if (B != null && UnityEngine.Random.value < dt * 30)
                {
                    var th = b.d.thrusters;
                    var d = th[Mathf.Min(th.Length - 1, (int)(UnityEngine.Random.value * th.Length))];
                    Vector3 p = rot * Space.Body(d.pos) + pos;
                    Vector3 v = rot * -Space.Body(d.axis) * 0.6f;
                    B.Emit(p.x, p.y, p.z, v.x, v.y, v.z, 2, 0.006f);
                }
            }
            step = "menu bubbles";
            try { B?.Update(dt, t); } catch (Exception e) { Report(e); }
            step = "menu camera";
            menuCam.Position = new Vector3(Mathf.Sin(t * 0.05f) * 1.5f, -1.2f, 4.5f);
            menuCam.LookAt(new Vector3(0, -1.6f, -2));
            // a scenery animation that fails must not keep the camera from being set up for this frame
            step = "menu scenery";
            try { scenery.Update(t, new WaveState { A = 0.1f, K = 0.2f, W = 1.4f, T = t, D = new Vector2(0.5f, 0.86f) }); }
            catch (Exception e) { Report(e); }
            step = "menu view"; Views.DrawView(menuCam, scenery, extras, post, null);
            drawnVersion = sceneVersion;
        }

        // ------------------------------------------------------------------ game
        /// start(cfg): the menu hands over a copy of its whole setup; players are the slots that are not "off"
        void StartMatch(MatchSetup S)
        {
            if (S == null) return;
            var opts = new GameOptions { scenario = S.scenario, runMinutes = S.minutes, coinFlip = S.coinFlip, depth = S.depth, players = new List<PlayerConfig>() };
            foreach (var s in S.slots)
                if (s != null && s.type != "off") opts.players.Add(new PlayerConfig { name = s.name, bot = s.bot, color = s.color, device = s.type });
            if (opts.players.Count == 0) return;

            menu.Hide();
            overlays.Close();
            cfg = S;
            DisposeScene();
            var g = new CoreGame(opts);
            game = g;
            var c = g.course;
            var sc = Scenarios.Get(g.scenario);
            // keep rocks, kelp and ice away from the course; the dock gets extra room for the start cameras
            var clear = new List<ClearZone>
            {
                Zone(c.dock.pos, 8), Zone(c.gate.pos, 5), Zone(c.slalom.pos, 5), Zone(c.bin.pos, 4), Zone(c.board.pos, 4), Zone(c.octagon.pos, 5),
            };
            scenery = Scenery.Create(world, sc, Art.Logo, g.arena, clear);
            course3d = Course3D.Create(c, Art.Logo, world);
            Graphics3D.CastShadows(course3d.group);
            extras = SceneExtras.Create(scenery, sc.look, g.arena);
            waveEnv = g.env;

            int n = g.players.Count;
            devices = new string[n];
            for (int i = 0; i < n; i++) devices[i] = i < opts.players.Count ? opts.players[i].device : "cpu";
            overlays.Devices = devices;
            // a button still held from the lobby (Options to start) must not fire in the game
            foreach (var d in devices) if (IsPad(d)) input.Read(d);
            pilots = new Autopilot[n];
            for (int i = 0; i < n; i++) if (devices[i] == "cpu") pilots[i] = new Autopilot(g, i);
            bots = new BotView[n];
            for (int i = 0; i < n; i++)
            {
                var p = g.players[i];
                var m = Bots3D.CreateBotMesh(p.design, p.color, Art.Logo, world);
                Graphics3D.CastShadows(m.group);
                int nt = p.design.thrusters.Length;
                string size = SizeOf(nt > 0 ? p.design.thrusters[0].preset : null);
                bots[i] = new BotView
                {
                    m = m,
                    // soft contact shadow (ambient occlusion) under the vehicle
                    shadow = new ContactShadow(world, p.design),
                    prevP = p.veh.p,
                    prevQ = p.veh.q,
                    bub = new float[nt],
                    size = size,
                    voice = new VehicleSound { n = new float[nt], maxRpm = MaxRpm(size), blades = 3, size = size },
                };
            }
            cams = new string[n];
            for (int i = 0; i < n; i++) cams[i] = "chase";
            hud = new Hud(ui, g, cams, () => OpenPause(viewPlayers.Count > 0 ? viewPlayers[0] : 0), ConfirmQuit, input, devices);
            // each player sees their own controller / key layout for the first seconds
            for (int i = 0; i < n; i++)
                if (devices[i] != "cpu") hud.ShowCard(i, devices[i], IsPad(devices[i]) ? input.Pad(devices[i]) : null, 12);
            BuildViews();
            acc = 0;
            pingNext = new double[c.pingers.Count];
            paused = false;
            state = "game";
            overShown = false;
            resultsFor = null;
        }

        static ClearZone Zone(V2 p, float r) => new ClearZone((float)p.y, (float)-p.x, r);
        static bool IsPad(string d) => d != null && d.StartsWith("pad", StringComparison.Ordinal);

        // one view per human player still in the match (a CPU match shows player 1)
        void BuildViews()
        {
            var g = game;
            viewPlayers.Clear();
            for (int i = 0; i < g.players.Count; i++) if (devices[i] != "cpu" && !g.players[i].left) viewPlayers.Add(i);
            if (viewPlayers.Count == 0)
            {
                int first = 0;
                for (int i = 0; i < g.players.Count; i++) if (!g.players[i].left) { first = i; break; }
                viewPlayers.Add(first);
            }
            var old = views ?? new List<View>();
            var next = new List<View>(4);
            bool[] usedIndex = new bool[Post.MaxViews];
            // keep the cameras (and their post volume index) of players that still have a view
            foreach (int pi in viewPlayers)
            {
                View keep = null;
                foreach (var v in old) if (!v.spectator && v.pi == pi) { keep = v; break; }
                if (keep != null) usedIndex[keep.cam.view] = true;
                next.Add(keep);
            }
            View spec = null;
            if (next.Count == 3)
            {
                foreach (var v in old) if (v.spectator) { spec = v; break; }
                if (spec != null) usedIndex[spec.cam.view] = true;
            }
            for (int k = 0; k < next.Count; k++)
            {
                if (next[k] != null) continue;
                int pi = viewPlayers[k];
                next[k] = new View { pi = pi, cam = new ViewCamera("view " + (pi + 1), 66, 0.03f, 300, FreeIndex(usedIndex)) };
            }
            if (next.Count == 3) next.Add(spec ?? new View { pi = -1, spectator = true, cam = new ViewCamera("TV camera", 55, 0.05f, 300, FreeIndex(usedIndex)) });
            foreach (var v in old) if (!next.Contains(v)) v.cam.Dispose();
            views = next;
            sceneVersion++;
            for (int i = 0; i < g.players.Count; i++) hud.SetPanelHidden(i, !viewPlayers.Contains(i));
            LayoutViews();
        }

        static int FreeIndex(bool[] used)
        {
            for (int i = 0; i < used.Length; i++) if (!used[i]) { used[i] = true; return i; }
            return used.Length - 1;
        }

        void LayoutViews()
        {
            float w = W, h = H;
            int n = views.Count;
            for (int i = 0; i < n; i++)
            {
                Rect r;
                if (n == 1) r = new Rect(0, 0, w, h);
                else if (n == 2)
                {
                    if (w >= h) r = i == 0 ? new Rect(0, 0, w / 2, h) : new Rect(w / 2, 0, w / 2, h);
                    else r = i == 0 ? new Rect(0, 0, w, h / 2) : new Rect(0, h / 2, w, h / 2);
                }
                else r = new Rect(i % 2 == 0 ? 0 : w / 2, i < 2 ? 0 : h / 2, w / 2, h / 2);
                r = new Rect(Round(r.x), Round(r.y), Round(r.width), Round(r.height));
                var v = views[i];
                v.rect = r;
                v.cam.SetRect(r.x, r.y, r.width, r.height, h);
            }
            if (hud != null)
            {
                hudRects.Clear();
                for (int i = 0; i < game.players.Count; i++) hudRects.Add(new Rect(0, 0, 0, 0));
                View spec = null;
                foreach (var v in views)
                {
                    if (v.pi >= 0 && v.pi < hudRects.Count) hudRects[v.pi] = v.rect;
                    if (v.spectator) spec = v;
                }
                hud.Layout(hudRects);
                hud.SetBoard(spec != null ? spec.rect : (Rect?)null);
            }
            ui.SetSplit(n, w >= h);
        }

        void DisposeScene()
        {
            if (hud != null) { hud.Dispose(); hud = null; }
            ui.SetSplit(1, true);
            if (bots != null)
            {
                foreach (var b in bots) { b.m?.Dispose(); b.shadow?.Dispose(); }
                bots = null;
            }
            foreach (var kv in itemMeshes) Bots3D.DisposeObject(kv.Value);
            itemMeshes.Clear();
            foreach (var b in menuBots) b.m?.Dispose();
            menuBots.Clear();
            course3d?.Dispose();
            course3d = null;
            extras?.Dispose();
            extras = null;
            scenery?.Dispose();
            scenery = null;
            if (views != null) { foreach (var v in views) v.cam.Dispose(); views = null; }
            menuCam?.Dispose();
            menuCam = null;
            viewPlayers.Clear();
            pilots = null;
            waveEnv = null;
            game = null;
        }

        // ------------------------------------------------------------------ overlays
        // the pause menu belongs to the player who opened it
        void OpenPause(int pi)
        {
            if (state != "game" || game == null || game.over) return;
            var g = game;
            var p = pi >= 0 && pi < g.players.Count ? g.players[pi] : g.players[0];
            paused = true;
            hud?.HideCard(p.index);
            sound.Ensure();
            overlays.OpenPause(g, pi, devices[p.index], HumansLeft(g), a => OnPauseAction(g, pi, a));
        }

        void OnPauseAction(CoreGame g, int pi, string a)
        {
            if (g != game) return;
            switch (a)
            {
                case "resume": Resume(); break;
                case "skip": g.Action(pi, "skip"); Resume(); break;
                case "end": g.Action(pi, "endRun"); Resume(); break;
                case "forfeit": g.Action(pi, "forfeit"); Resume(); break;
                case "leave": Leave(pi); break;
                case "restart": StartMatch(cfg); break;
                case "quit": ToMenu(); break;
                // "quality": GraphicsQuality.Set already ran; Post and SceneExtras (shadows) follow its
                // Changed event, URP materials need no refresh. "mute": Sound already toggled.
                default: break;
            }
        }

        int HumansLeft(CoreGame g)
        {
            int n = 0;
            for (int i = 0; i < g.players.Count; i++) if (devices[i] != "cpu" && !g.players[i].left) n++;
            return n;
        }

        // the always-visible Quit button: ask first
        void ConfirmQuit()
        {
            if (state != "game" || game == null || game.over) return;
            paused = true;
            overlays.ConfirmQuit(Resume, ToMenu);
        }

        void Resume()
        {
            paused = false;
            overlays.Close();
            // forget buttons pressed in the menu so they don't fire a game action
            input.Pressed.Clear();
            if (devices != null) foreach (var d in devices) if (IsPad(d)) input.Read(d);
        }

        void Leave(int pi)
        {
            var g = game;
            g.Action(pi, "leave");
            Resume();
            if (HumansLeft(g) == 0)
            {
                foreach (var p in g.players) if (p.run.state == "running") g.EndRun(p, "Match ended");
                g.over = true;
                return;
            }
            BuildViews();
        }

        void ToMenu()
        {
            overlays.Close();
            paused = false;
            state = "menu";
            resultsFor = null;
            sound.Motor(0);
            MenuScene();
            menu.Show("setup");
        }

        void ShowResults()
        {
            overShown = true;
            overlays.ShowResults(game, a =>
            {
                if (a == "again") StartMatch(cfg);
                else if (a == "lobby") ToMenu();
                else if (a == "menu") { ToMenu(); menu.Show("title"); }
            });
        }

        // ------------------------------------------------------------------ game frame
        void HandleInput()
        {
            var g = game;
            for (int i = 0; i < g.players.Count; i++)
            {
                var p = g.players[i];
                string dev = devices[i];
                if (dev == "cpu" || p.left) continue;
                var r = input.Read(dev);
                var vin = p.veh.input;
                vin.surge = r.surge; vin.sway = r.sway; vin.heave = r.heave;
                vin.yaw = r.yaw; vin.roll = r.roll; vin.pitch = r.pitch;
                var acts = r.actions;
                for (int k = 0; k < acts.Count; k++)
                {
                    string a = acts[k];
                    if (a == "camera") cams[i] = CAMS[(Array.IndexOf(CAMS, cams[i]) + 1) % CAMS.Length];
                    else if (a == "pause") OpenPause(i);
                    else
                    {
                        bool run = p.run.state == "running";
                        if (a == "marker" && p.run.markers > 0 && run) sound.Drop();
                        if (a == "torpedo" && p.run.torpedoes > 0 && run) sound.Launch();
                        if (a == "grip" && run) { if (p.run.held != null) sound.Release(); else sound.Grab(); }
                        g.Action(i, a);
                    }
                }
            }
        }

        void GameFrame(float dt)
        {
            var g = game;
            if (!paused && !g.over)
            {
                step = "game input"; HandleInput();
                step = "physics";
                acc += dt;
                int n = 0;
                while (acc >= Vehicle.DT && n < 10)
                {
                    for (int i = 0; i < g.players.Count; i++) { bots[i].prevP = g.players[i].veh.p; bots[i].prevQ = g.players[i].veh.q; }
                    for (int i = 0; i < pilots.Length; i++) pilots[i]?.Update(Vehicle.DT);
                    g.Step(Vehicle.DT);
                    acc -= Vehicle.DT;
                    n++;
                }
                if (n == 10) acc = 0;
            }
            float alpha = (float)Math.Min(1, acc / Vehicle.DT);
            step = "events"; Events();
            step = "robots"; SyncMeshes(alpha, paused ? 0 : dt);
            // the decorative / secondary parts run on their own: one that fails is reported (once) and the
            // others, the HUD and the cameras still run this frame
            step = "bubbles";
            try { Effects(paused ? 0 : dt); } catch (Exception e) { Report(e); }
            step = "sound";
            try { Audio(dt); } catch (Exception e) { Report(e); }
            step = "course";
            try { course3d?.Update(g.time); } catch (Exception e) { Report(e); }
            step = "scenery";
            try { scenery?.Update((float)g.env.time, g.env); } catch (Exception e) { Report(e); }
            step = "hud";
            try { hud?.Update(dt); } catch (Exception e) { Report(e); }
            step = "cameras";
            for (int i = 0; i < views.Count; i++) RenderView(views[i], dt);
            drawnVersion = sceneVersion;
            if (g.over && !overShown) { overShown = true; resultsFor = g; resultsAt = Time.unscaledTime + ResultsDelay; }
        }

        // game events -> HUD toasts and score / bad sounds (main.js: g.events.splice(0))
        void Events()
        {
            var ev = game.events;
            int count = ev.Count;
            for (int k = 0; k < count; k++)
            {
                var e = ev[k];
                if (e.player >= 0) hud.Toast(e.player, e.text, e.pts, e.kind);
                bool viewed = e.player < 0 || viewPlayers.Contains(e.player);
                if (!viewed) continue;
                if (e.kind == "score" && e.pts > 0) sound.Score();
                else if (e.kind == "bad") sound.Bad();
            }
            ev.RemoveRange(0, count);
        }

        void SyncMeshes(float alpha, float dt)
        {
            var g = game;
            for (int i = 0; i < g.players.Count; i++)
            {
                var p = g.players[i];
                var b = bots[i];
                var v = p.veh;
                b.m.Visible = !p.left;
                b.shadow.Visible = !p.left;
                if (p.left) continue;
                V3 P0 = b.prevP, P1 = v.p;
                double x = P0.x + (P1.x - P0.x) * alpha, y = P0.y + (P1.y - P0.y) * alpha, z = P0.z + (P1.z - P0.z) * alpha;
                b.pos3 = new Vector3((float)y, (float)-z, (float)-x);
                b.rot3 = Quaternion.Slerp(Space.NedQuat(b.prevQ), Space.NedQuat(v.q), alpha);
                b.m.SetPose(b.pos3, b.rot3);
                b.state.holding = p.run.held != null;
                b.state.gripClosed = p.run.held != null;
                b.state.markers = p.run.markers;
                b.state.torpedoes = p.run.torpedoes;
                b.m.Update(dt, v, b.state);
                b.shadow.Update(x, y, z, g.course.floor);
                if (v.lastImpact > 0.25 && viewPlayers.Contains(i)) sound.Bump((float)v.lastImpact);
                v.lastImpact = 0;
            }
            // payload items
            seenItems.Clear();
            var items = g.items;
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k];
                seenItems.Add(it.id);
                if (!itemMeshes.TryGetValue(it.id, out var m))
                {
                    m = Bots3D.CreateItemMesh(it.kind, it.owner != null ? it.owner.color : "#ffffff", world);
                    Graphics3D.CastShadows(m);
                    itemMeshes[it.id] = m;
                }
                Views.PlaceItem(m, it);
            }
            goneItems.Clear();
            foreach (var kv in itemMeshes) if (!seenItems.Contains(kv.Key)) goneItems.Add(kv.Key);
            for (int k = 0; k < goneItems.Count; k++)
            {
                Bots3D.DisposeObject(itemMeshes[goneItems[k]]);
                itemMeshes.Remove(goneItems[k]);
            }
        }

        // bubbles from the thrusters (rate ~ thrust), torpedo wakes, sinking markers, splashes
        void Effects(float dt)
        {
            var g = game;
            var B = extras.Bubbles;
            if (B == null) return;
            if (dt > 0)
            {
                for (int i = 0; i < g.players.Count; i++)
                {
                    var p = g.players[i];
                    if (p.left) continue;
                    var b = bots[i];
                    var v = p.veh;
                    for (int k = 0; k < v.th.Length; k++)
                    {
                        float n = (float)Math.Abs(v.th[k].n);
                        if (n < 0.12f) continue;
                        var d = p.design.thrusters[k];
                        Vector3 pos = b.rot3 * Space.Body(d.pos) + b.pos3;
                        if (pos.y > -0.05f) continue; // out of the water
                        b.bub[k] += dt * n * n * (b.size == "t500" ? 70 : b.size == "small" ? 25 : 45);
                        if (b.bub[k] < 1) continue;
                        int cnt = (int)b.bub[k];
                        b.bub[k] -= cnt;
                        // the jet leaves opposite to the push direction
                        double tn = v.th[k].n;
                        float s = -(tn > 0 ? 1 : tn < 0 ? -1 : 0) * (0.6f + 1.6f * n);
                        Vector3 jet = b.rot3 * Space.Body(d.axis) * s;
                        B.Emit(pos.x, pos.y, pos.z, jet.x, jet.y, jet.z, cnt, b.size == "t500" ? 0.009f : 0.006f);
                    }
                    // surfacing / diving: splash + bubbles
                    bool surf = v.depth < p.design.dims.z * 0.45;
                    if (surf != b.wasSurf && viewPlayers.Contains(i))
                    {
                        sound.Splash((float)Math.Min(1, Math.Abs(v.WorldVel().z) * 2 + 0.3));
                        B.Emit(b.pos3.x, b.pos3.y - 0.1f, b.pos3.z, 0, 0.2f, 0, 30, 0.01f);
                    }
                    b.wasSurf = surf;
                }
                var items = g.items;
                for (int k = 0; k < items.Count; k++)
                {
                    var it = items[k];
                    bool torp = it.kind == "torpedo";
                    if (it.state != "fall" || UnityEngine.Random.value > dt * (torp ? 50 : 8)) continue;
                    Vector3 q = Space.Ned(it.pos);
                    B.Emit(q.x, q.y, q.z, 0, 0.05f, 0, torp ? 2 : 1, 0.005f);
                }
            }
            B.Update(dt, (float)g.env.time, waveY);
        }

        void Audio(float dt)
        {
            var g = game;
            var me = g.players[viewPlayers[0]];
            V3 myPos = me.veh.p;
            double myYaw = me.veh.Euler().z;
            var list = soundFrame.vehicles;
            list.Clear();
            for (int i = 0; i < g.players.Count; i++)
            {
                var p = g.players[i];
                if (p.left) continue;
                var voice = bots[i].voice;
                var v = p.veh;
                for (int k = 0; k < v.th.Length && k < voice.n.Length; k++) voice.n[k] = (float)v.th[k].n;
                bool own = viewPlayers.Contains(i);
                double dx = v.p.x - myPos.x, dy = v.p.y - myPos.y, dz = v.p.z - myPos.z;
                voice.dist = own ? 0f : (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                voice.pan = own ? 0f : (float)Math.Sin(Math.Atan2(dy, dx) - myYaw);
                list.Add(voice);
            }
            Vector3 cam = views[0].cam.Position;
            soundFrame.underwater = cam.y < g.env.SurfaceElevation(-cam.z, cam.x);
            soundFrame.depth = (float)Math.Max(0, me.veh.depth);
            soundFrame.paused = paused || g.over;
            sound.Tick(dt, soundFrame);
            // pingers heard on the first viewed vehicle's hydrophone
            var pingers = g.course.pingers;
            for (int k = 0; k < pingers.Count; k++)
            {
                var pg = pingers[k];
                if (g.time < pingNext[k]) continue;
                pingNext[k] = g.time + pg.period;
                if (paused) continue;
                var bearing = g.GetPingerBearing(me, pg.id);
                sound.Ping((float)pg.freq, (float)Math.Min(1, 6 / (bearing.dist + 2)), (float)Math.Sin(bearing.rel));
            }
        }

        void RenderView(View view, float dt)
        {
            var g = game;
            var cam = view.cam;
            float k = 1 - Mathf.Exp(-dt * 6);
            BotMesh own = null;
            if (view.spectator)
            {
                // TV camera: follows the leader from the side
                Player lead = null;
                double best = double.NegativeInfinity;
                foreach (var p in g.players)
                {
                    if (p.left) continue;
                    double tot = g.Total(p);
                    if (lead == null || tot > best) { lead = p; best = tot; }
                }
                if (lead == null) lead = g.players[0];
                Vector3 gp = bots[lead.index].pos3;
                float t = (float)(g.time * 0.08);
                var want = new Vector3(gp.x + Mathf.Sin(t) * 4, Mathf.Min(gp.y + 1.6f, -0.3f), gp.z + Mathf.Cos(t) * 4);
                if (!view.hasSmooth) { view.smooth = want; view.hasSmooth = true; }
                view.smooth += (want - view.smooth) * (k * 0.5f);
                cam.Position = view.smooth;
                cam.LookAt(gp);
            }
            else
            {
                var p = g.players[view.pi];
                var v = p.veh;
                string mode = cams[view.pi];
                var b = bots[view.pi];
                var d = p.design;
                if (mode == "chase")
                {
                    float yaw = (float)v.ProjectedHeading(v.q);
                    float back = Mathf.Max(1.4f, (float)d.dims.x * 2.2f), up = 0.55f + (float)d.dims.z;
                    var want = new Vector3(b.pos3.x - Mathf.Sin(yaw) * back, b.pos3.y + up, b.pos3.z + Mathf.Cos(yaw) * back);
                    // keep the camera inside the arena and above the floor
                    want.y = Mathf.Max(want.y, (float)-g.course.floor + 0.3f);
                    // stay under the water: from above, the surface hides the vehicle (a floating start
                    // or a surfacing vehicle is filmed from just below the surface)
                    want.y = Mathf.Min(want.y, (float)g.env.SurfaceElevation(-want.z, want.x) - 0.22f);
                    want.x = Mathf.Clamp(want.x, -12.3f, 12.3f);
                    want.z = Mathf.Clamp(want.z, -24.8f, 24.8f);
                    if (!view.hasSmooth) { view.smooth = want; view.hasSmooth = true; }
                    view.smooth += (want - view.smooth) * k;
                    cam.Position = view.smooth;
                    cam.Fov = 66;
                    cam.LookAt(new Vector3(b.pos3.x + Mathf.Sin(yaw) * 1.2f, b.pos3.y - 0.1f, b.pos3.z - Mathf.Cos(yaw) * 1.2f));
                }
                else
                {
                    // onboard cameras ride with the vehicle (body -> three local: (y, -z, -x))
                    bool front = mode == "front";
                    Vector3 mount = front ? Space.Body(d.dims.x / 2 + 0.03, 0, 0) : Space.Body(0, 0, d.dims.z / 2 + 0.03);
                    cam.Position = b.rot3 * mount + b.pos3;
                    var q = b.rot3;
                    if (!front) q = q * Space.AxisAngle(Vector3.right, -Mathf.PI / 2);
                    cam.Quaternion = q;
                    cam.Fov = front ? 78 : 72;
                    view.hasSmooth = false;
                    // hide your own vehicle in the onboard views
                    own = b.m;
                }
            }
            Views.HideFor(cam, own);
            Views.DrawView(cam, scenery, extras, post, g.env);
        }

        // ------------------------------------------------------------------ teardown
        public void Dispose()
        {
            // Boot.OnDestroy runs while Unity tears the scene down (leaving Play mode): the UI and other
            // scene objects may already be gone, so every step runs on its own and a missing object is
            // not an error
            if (ui != null) ui.Resized -= OnUiResized;
            Quiet(DisposeScene);
            Quiet(overlays.Close);
            Quiet(post.Dispose);
            Quiet(sound.Dispose);
            Quiet(input.Dispose);
            Quiet(BotThumbs.Clear);
            if (world != null) UnityEngine.Object.Destroy(world.gameObject);
            Quiet(GraphicsQuality.Restore);
        }

        static void Quiet(Action step)
        {
            try { step(); }
            catch (MissingReferenceException) { /* destroyed with the scene */ }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
