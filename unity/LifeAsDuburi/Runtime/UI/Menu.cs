// src/ui/menu.js: title, lobby (players join with their own controller, pick a
// vehicle and a colour, PS5-couch style), water, options and how-to-play
// screens. The chosen setup is remembered (PlayerPrefs "life-as-duburi-setup-v2",
// the browser's localStorage JSON) and handed to the App with onStart.

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Duburi.Game
{
    public sealed class Menu
    {
        const string STORE = "life-as-duburi-setup-v2";
        public const string GAME_NAME = MenuSetup.GAME_NAME;

        public MatchSetup Setup;
        /// "title" | "setup" | "help", null while hidden
        public string Screen { get; private set; }
        public bool Visible => el.gameObject.activeSelf && Screen != null;

        readonly UiRoot ui;
        readonly InputHub input;
        readonly Sound sound;
        readonly Action<MatchSetup> onStart;
        readonly Action<string> onControls;
        readonly RectTransform el, screenRoot;
        RectTransform slotBox, scenBox;
        readonly List<Dropdown> selects = new List<Dropdown>();
        readonly KeyRepeat keys = new KeyRepeat();

        /// new Menu(root, { logoUrl, thumb, onStart, sound, input, onControls }); onStart gets a copy of
        /// the setup (all 4 slots; "off" slots are not playing), onControls the device id to show
        public Menu(UiRoot ui, InputHub input, Sound sound, Action<MatchSetup> onStart, Action<string> onControls)
        {
            this.ui = ui; this.input = input; this.sound = sound; this.onStart = onStart; this.onControls = onControls;
            Setup = LoadSetup();
            el = Ui.Node("menu", ui.MenuLayer);
            Ui.Stretch(el);
            // .menu { background: radial-gradient(ellipse at 50% 30%, rgba(4,19,28,.1), rgba(4,19,28,.65)) }
            var bg = Ui.Raw(el, Ui.Gradient("menu-bg", 64, 64, (u, v) =>
            {
                float dx = (u - 0.5f) / 0.5f, dy = (v - 0.3f) / 0.7f;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.41f);
                return Color.Lerp(Css.Rgba(4, 19, 28, 0.1f), Css.Rgba(4, 19, 28, 0.65f), t);
            }));
            screenRoot = Ui.Node("screen", el);
            Ui.Stretch(screenRoot);
            el.gameObject.SetActive(false);
            ui.Resized += OnResized;
            // a controller plugged in: tell the player how to join / see its layout (main.js gamepadconnected)
            if (input != null) input.PadConnected += i => { if (i >= 0 && Visible) Flash("Controller " + (i + 1) + " connected — press ✕ / A to join, Share / View for its layout"); };
        }

        // the title screen is laid out for the canvas width (logo, h1 size): build it again
        void OnResized()
        {
            if (Screen == "title") Show("title");
        }

        // ---------------------------------------------------------------- saved setup
        static MatchSetup LoadSetup()
        {
            var def = MenuSetup.DefaultSetup();
            try
            {
                var s = Json.Obj(Json.Parse(PlayerPrefs.GetString(STORE, "")));
                var slots = Json.Arr(Json.Get(s, "slots"));
                if (s != null && slots != null && slots.Count == 4)
                {
                    bool ok = true;
                    foreach (var x in slots) { var o = Json.Obj(x); string bot = Json.Str(Json.Get(o, "bot")); if (o == null || bot == null || !Bots.BOTS.ContainsKey(bot)) ok = false; }
                    if (ok)
                    {
                        // a water that no longer exists (e.g. an older save) falls back to the pool; the rest is kept
                        string sc = Json.Str(Json.Get(s, "scenario"));
                        var S = new MatchSetup
                        {
                            scenario = sc != null && Scenarios.SCENARIOS.ContainsKey(sc) ? sc : "pool",
                            minutes = Json.Num(Json.Get(s, "minutes"), def.minutes),
                            coinFlip = Json.Bool(Json.Get(s, "coinFlip"), def.coinFlip),
                            depth = Json.Num(Json.Get(s, "depth"), def.depth),
                            slots = new SlotSetup[4],
                        };
                        for (int i = 0; i < 4; i++)
                        {
                            var o = Json.Obj(slots[i]);
                            S.slots[i] = new SlotSetup
                            {
                                type = Json.Str(Json.Get(o, "type"), def.slots[i].type),
                                name = Json.Str(Json.Get(o, "name"), def.slots[i].name),
                                bot = Json.Str(Json.Get(o, "bot")),
                                color = Json.Str(Json.Get(o, "color"), def.slots[i].color),
                            };
                        }
                        return S;
                    }
                }
            }
            catch (Exception) { /* storage unavailable */ }
            return def;
        }

        static void SaveSetup(MatchSetup S)
        {
            try
            {
                var slots = new List<object>();
                foreach (var sl in S.slots) slots.Add(new Dictionary<string, object> { ["type"] = sl.type, ["name"] = sl.name, ["bot"] = sl.bot, ["color"] = sl.color });
                var o = new Dictionary<string, object> { ["scenario"] = S.scenario, ["minutes"] = S.minutes, ["coinFlip"] = S.coinFlip, ["depth"] = S.depth, ["slots"] = slots };
                PlayerPrefs.SetString(STORE, Json.Stringify(o));
                PlayerPrefs.Save();
            }
            catch (Exception) { /* ignore */ }
        }

        // ---------------------------------------------------------------- screens
        public void Show(string screen = "title")
        {
            el.gameObject.SetActive(true);
            Ui.Clear(screenRoot);
            selects.Clear();
            slotBox = scenBox = null;
            Screen = screen;
            if (screen == "title") Title();
            else if (screen == "setup") SetupScreen();
            else if (screen == "help") Help();
            input?.PadPresses(); // drop presses that opened this screen
        }

        public void Hide()
        {
            el.gameObject.SetActive(false);
            Ui.Clear(screenRoot);
            selects.Clear();
            Screen = null;
        }

        void Title()
        {
            float vw = ui.Size.x;
            var s = Ui.Node("title-screen", screenRoot);
            s.anchorMin = s.anchorMax = s.pivot = new Vector2(0.5f, 0.5f);
            float w = Mathf.Min(760, vw - 32);
            Ui.VBox(s, null, 0, TextAnchor.UpperCenter, false);
            var fit = Ui.Fit(s, false, true);
            s.sizeDelta = new Vector2(w, 0);
            // logo: width min(460px, 82vw), white, rounded, shadow
            var tex = Art.Logo;
            float lw = Mathf.Min(460, vw * 0.82f);
            float lh = tex != null && tex.width > 0 ? lw * tex.height / tex.width : lw * 0.42f;
            var logo = Ui.Node("title-logo", s);
            Ui.Img(logo, Ui.Rounded(10), Color.white);
            Ui.DropShadow(logo, 10, 40, 10, new Color(0, 0, 0, 0.45f));
            var li = Ui.Node("img", logo); Ui.Stretch(li, 2, 2, 2, 2);
            Ui.Raw(li, tex);
            Ui.LE(logo, lw, lh, 0, 0, lw, lh);
            // h1 with the subtitle span
            Ui.Gap(s, 22);
            float h1 = Mathf.Clamp(vw * 0.06f, 30, 56);
            var t1 = Ui.Label(s, GAME_NAME, h1, Css.text, UiFont.Bold, TextAnchor.UpperCenter, true, "h1");
            Ui.TextShadow(t1, new Color(0, 0, 0, 0.6f), 3);
            Ui.LE(t1, w);
            var t2 = Ui.Label(s, "AN UNDERWATER ROBOTICS GAME", h1 * 0.5f, Css.acc, UiFont.SemiBold, TextAnchor.UpperCenter, true, "span");
            Ui.TextShadow(t2, new Color(0, 0, 0, 0.6f), 3);
            Ui.LE(t2, w);
            Ui.Gap(s, 6 + 18);
            var tag = Ui.Label(s, "Pilot a real AUV through a competition course — in a pool or the open sea. 1 to 4 players on one screen.", 18, Css.pale, UiFont.Regular, TextAnchor.UpperCenter, true, "tag");
            Ui.TextShadow(tag, new Color(0, 0, 0, 0.7f), 2);
            Ui.LE(tag, w);
            Ui.Gap(s, 18 + 24);
            var btns = Ui.Node("title-btns", s);
            var bf = btns.gameObject.AddComponent<Flow>();
            bf.hgap = 12; bf.vgap = 12; bf.childAlignment = TextAnchor.UpperCenter;
            Ui.LE(btns, w);
            Ui.Btn(btns, "Play", () => Go("setup"), true, 20, Ui.Pad(12, 34));
            Ui.Btn(btns, "Controls", () => Controls(), false, 20, Ui.Pad(12, 34));
            Ui.Btn(btns, "How to play", () => { sound?.Ensure(); Show("help"); }, false, 20, Ui.Pad(12, 34));
            Ui.Gap(s, 24);
            var press = new Rich(s, 14, Css.pale, UiFont.Regular, TextAnchor.UpperCenter);
            press.T("Press").Kbd("Enter").T("or").Pad("✕ / A").T("to start");
            Ui.LE(press.rect, w);
            Anim.On(press.rect).blinkPeriod = 1.6f;
            Ui.Gap(s, 10 + 12);
            var small = Ui.Label(s, "Team BRACU DUBURI · BRAC University · physics from the DUBURI ROV simulator (ArduSub-style control, T200/T500 thrusters, EOS-80 sea water)", 12, Css.mut, UiFont.Regular, TextAnchor.UpperCenter, true, "small");
            Ui.TextShadow(small, Color.black, 1);
            Ui.LE(small, w);
        }

        /// controls screen for a device (default: the first connected controller)
        public void Controls(string dev = null)
        {
            Sfx("uiSelect");
            if (dev == null)
            {
                var pads = input?.Pads();
                dev = pads != null && pads.Count > 0 ? "pad" + pads[0].index : "kbA";
            }
            onControls?.Invoke(dev);
        }

        void Sfx(string name)
        {
            if (sound == null) return;
            sound.Ensure();
            sound.Sfx(name);
        }

        void Go(string screen) { Sfx("uiSelect"); Show(screen); }

        void Help()
        {
            var P = Ui.Panel(screenRoot, 1180, true);
            Ui.H2(P.header, "How to play");
            Ui.Btn(P.header, "Back", () => Show("title"));
            HelpText.Rules(P.content);
            Ui.H3(P.content, "Controls");
            HelpText.Controls(P.content);
        }

        void SetupScreen()
        {
            var P = Ui.Panel(screenRoot, 1180, true);
            Ui.Logo(P.header, 34, 4);
            Ui.H2(P.header, "Lobby");
            Ui.Spacer(P.header);
            Ui.Btn(P.header, "Back", () => Show("title"));
            Ui.Btn(P.header, "Controls", () => Controls());
            Ui.Btn(P.header, "Start match ▶", () => Start(), true);
            // join bar
            var jb = Ui.Box(P.content, "join-bar", Css.Rgba(58, 160, 224, 0.12f), 10, Css.Rgba(58, 160, 224, 0.35f));
            Ui.VBox(jb, Ui.Pad(10, 12), 0);
            var holder = Ui.Node("m", P.content); // margin: 12px 0
            jb.SetParent(holder, false);
            Ui.VBox(holder, Ui.Pad(12, 0), 0);
            var j = new Rich(jb, 13, Css.text, UiFont.Regular, TextAnchor.UpperLeft, 6);
            j.B("Join:").T("press").Pad("✕ / A").T("on a controller ·").Kbd("Space").T("left keyboard ·").Kbd("Enter").T("right keyboard  —")
             .Pad("◀ ▶").T("vehicle ·").Pad("▲ ▼").T("colour ·").Pad("○ / B").T("leave ·").Pad("△ / Y").T("add CPU ·").Pad("Share / View").T("your controls ·").Pad("Options / Start").T("start");
            slotBox = Ui.Node("slots", P.content);
            var sg = slotBox.gameObject.AddComponent<AutoGrid>();
            sg.minCell = 240; sg.gap = 12;
            DrawSlots();
            Ui.H3(P.content, "Water");
            scenBox = Ui.Node("scen", P.content);
            var cg = scenBox.gameObject.AddComponent<AutoGrid>();
            cg.minCell = 230; cg.gap = 12;
            DrawScen();
            Ui.H3(P.content, "Match");
            DrawOpts(P.content);
        }

        // ---------------------------------------------------------------- lobby slots
        static string DeviceLabel(string id)
        {
            foreach (var d in Duburi.Game.Controls.Devices) if (d.id == id) return d.label;
            return id;
        }

        void DrawSlots()
        {
            var box = slotBox;
            if (box == null) return;
            Ui.Clear(box);
            var S = Setup.slots;
            for (int i = 0; i < S.Length; i++) SlotCard(box, i, S[i]);
        }

        void SlotCard(Transform box, int i, SlotSetup sl)
        {
            bool off = sl.type == "off";
            var pc = Colors.Hex(sl.color);
            var design = Bots.Get(sl.bot, Bots.BOTS["duburi"]);
            var st = Stats.BotStats(design);
            var c = Ui.Box(box, "slot", Css.White(0.03f), 10, off ? Css.line : pc);
            Ui.Backing(c, "top", Ui.Rounded(10, 0, 1, 4), pc).transform.SetSiblingIndex(1); // border-top: 4px solid var(--pc)
            if (!off) Ui.DropShadow(c, 10, 24, 6, new Color(0, 0, 0, 0.25f));
            Ui.VBox(c, Ui.Pad(14, 10, 10, 10), 6);
            if (off) Ui.Group(c, 0.55f); // .slot.off { opacity: .55 }

            // P1 + device select
            var top = Ui.Node("slot-top", c);
            Ui.HBox(top, null, 8, TextAnchor.MiddleLeft);
            Ui.Label(top, "P" + (i + 1), 18, pc, UiFont.Bold, TextAnchor.MiddleLeft, false);
            var ids = new List<string> { "off" };
            var labels = new List<string> { "— press to join —" };
            foreach (var d in Duburi.Game.Controls.Devices) { ids.Add(d.id); labels.Add(d.label); }
            var dd = Ui.Select(top, labels, Mathf.Max(0, ids.IndexOf(sl.type)), k => Assign(i, ids[k]));
            Ui.LE(dd, 0, 32, 1, -1, 0, 32);
            selects.Add(dd);

            if (off)
            {
                var hint = Ui.Box(c, "join-hint", Color.clear, 8, Css.line);
                Ui.VBox(hint, Ui.Pad(6), 0, TextAnchor.MiddleCenter);
                new Rich(hint, 13, Css.mut, UiFont.Regular, TextAnchor.UpperCenter).T("Press").Pad("✕ / A").T("to join");
                Anim.On(hint).blinkPeriod = 1.6f;
            }
            else
            {
                var ready = Ui.Node("ready", c);
                Ui.HBox(ready, null, 0, TextAnchor.MiddleLeft);
                Ui.Line(ready, "● " + (sl.type == "cpu" ? "CPU" : "Ready"), 13, pc, UiFont.Bold, TextAnchor.MiddleLeft, 3);
                if (sl.type != "cpu") { string dev = sl.type; Ui.LinkBtn(ready, "controls", () => Controls(dev)); }
            }

            var name = Ui.TextField(c, sl.name, 16, !off, v => { sl.name = v.Length > 0 ? v : "Player " + (i + 1); });
            Ui.LE(name, -1, 32, -1, -1, 0, 32);

            // vehicle picker
            var pick = Ui.Node("bot-pick", c);
            Ui.HBox(pick, null, 4, TextAnchor.MiddleCenter);
            Ui.Arrow(pick, "‹", () => CycleBot(i, -1));
            var thumbBox = Ui.Node("thumb", pick);
            var aspect = thumbBox.gameObject.AddComponent<AspectBox>();
            aspect.designW = 240; aspect.designH = 150;
            Ui.LE(thumbBox, 0, -1, 1);
            var thumbBg = Ui.Node("bg", thumbBox); Ui.Stretch(thumbBg);
            Ui.Raw(thumbBg, Ui.Gradient("thumb-bg", 64, 40, (u, v) =>
            {
                // radial-gradient(circle, rgba(58,160,224,.25), transparent 70%)
                float dx = (u - 0.5f) * 240, dy = (v - 0.5f) * 150;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / (0.7f * Mathf.Sqrt(120 * 120 + 75 * 75)));
                return Css.Rgba(58, 160, 224, 0.25f * (1 - t));
            }));
            var thumb = Ui.Node("img", thumbBox);
            thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 1); thumb.pivot = new Vector2(0.5f, 1);
            thumb.sizeDelta = new Vector2(240, 150);
            aspect.drawing = thumb;
            Ui.Raw(thumb, BotThumbs.Get(sl.bot, pc));
            Ui.Arrow(pick, "›", () => CycleBot(i, 1));

            Ui.Label(c, HelpText.Esc(design.name), 20, Css.text, UiFont.Bold, TextAnchor.MiddleLeft, true, "bot-name");
            var kind = Ui.Label(c, HelpText.Esc(design.kind), 13, Css.mut, UiFont.Regular, TextAnchor.UpperLeft, true, "bot-kind");
            Ui.LE(kind, -1, 13 * 1.25f);
            var blurb = Ui.Label(c, HelpText.Esc(design.blurb), 13, Css.blurb, UiFont.Regular, TextAnchor.UpperLeft, true, "bot-blurb");
            Ui.LE(blurb, -1, -1, -1, -1, -1, 52);
            Bar(c, "Top speed", st.surge, 2.0, Js.ToFixed(st.surge, 2) + " m/s", pc);
            Bar(c, "Strafe", st.sway, 1.2, Js.ToFixed(st.sway, 2) + " m/s", pc);
            Bar(c, "Turn rate", st.yawRate, 400, Js.ToFixed(st.yawRate, 0) + "°/s", pc);
            Bar(c, "Stability", st.righting, 18, Js.ToFixed(st.righting, 1) + " N·m/rad", pc);
            // degrees of freedom
            var dof = Ui.Node("dof", c);
            var df = dof.gameObject.AddComponent<Flow>();
            df.hgap = 3; df.vgap = 3;
            foreach (var a in new[] { "Surge", "Sway", "Heave", "Roll", "Pitch", "Yaw" })
            {
                bool on = st.dof.Contains(a);
                var em = Ui.Box(dof, "em", Color.clear, 10, on ? pc : Css.line);
                Ui.HBox(em, Ui.Pad(1, 6), 0, TextAnchor.MiddleCenter);
                var t = Ui.Label(em, a, 11, on ? Css.text : Colors.Hex("#5d7a88"), UiFont.Regular, TextAnchor.MiddleCenter, false);
                if (!on) Ui.Rule(t.transform, Colors.Hex("#5d7a88"), 1, 0.5f); // text-decoration: line-through
            }
            Ui.Label(c, Js.Str(st.mass) + " kg · " + st.thrusters + " thrusters · " + Js.ToFixed(st.thrust, 0) + " N forward", 12, Css.mut, UiFont.Regular, TextAnchor.UpperLeft, true, "bot-meta");
            // colours
            var cols = Ui.Node("colors", c);
            var cf = cols.gameObject.AddComponent<Flow>();
            cf.hgap = 5; cf.vgap = 5;
            foreach (var col in Bots.PLAYER_COLORS)
            {
                string cc = col;
                Ui.Swatch(cols, Colors.Hex(col), col == sl.color, () => { sl.color = cc; DrawSlots(); });
            }
        }

        // .bar: grid 70px 1fr 92px
        static void Bar(Transform parent, string label, double v, double max, string unit, Color pc)
        {
            var r = Ui.Node("bar", parent);
            Ui.HBox(r, null, 6, TextAnchor.MiddleLeft);
            var l = Ui.Label(r, label, 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false);
            Ui.LE(l, 70, -1, 0, -1, 70);
            var track = Ui.Node("track", r);
            Ui.LE(track, 0, 6, 1, -1, 0, 6);
            var i = Ui.Node("i", track);
            float pct = (float)Math.Min(100, (v / max) * 100) / 100f;
            i.anchorMin = new Vector2(0, 0); i.anchorMax = new Vector2(Mathf.Max(0, pct), 1);
            i.offsetMin = i.offsetMax = Vector2.zero;
            Ui.Img(i, Ui.BarSprite(pc), Color.white);
            var b = Ui.Label(r, unit, 11, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
            Ui.LE(b, 92, -1, 0, -1, 92);
        }

        /// put a device in slot i (a device is only ever in one slot)
        public void Assign(int i, string type)
        {
            var S = Setup.slots;
            if (type != "off" && type != "cpu") for (int k = 0; k < S.Length; k++) if (k != i && S[k].type == type) S[k].type = "off";
            S[i].type = type;
            Sfx(type == "off" ? "uiBack" : "uiSelect");
            DrawSlots();
        }

        public int Join(string type)
        {
            var S = Setup.slots;
            for (int k = 0; k < S.Length; k++) if (S[k].type == type) return k;
            for (int k = 0; k < S.Length; k++) if (S[k].type == "off") { Assign(k, type); return k; }
            return -1;
        }

        void CycleBot(int i, int d)
        {
            var sl = Setup.slots[i];
            var O = Bots.BOT_ORDER;
            sl.bot = O[((Array.IndexOf(O, sl.bot) + d) % O.Length + O.Length) % O.Length];
            Sfx("uiMove");
            DrawSlots();
        }

        void CycleColor(int i, int d)
        {
            var sl = Setup.slots[i];
            var C = Bots.PLAYER_COLORS;
            sl.color = C[((Array.IndexOf(C, sl.color) + d) % C.Length + C.Length) % C.Length];
            Sfx("uiMove");
            DrawSlots();
        }

        int SlotOf(string t) { var S = Setup.slots; for (int k = 0; k < S.Length; k++) if (S[k].type == t) return k; return -1; }

        bool SelectOpen()
        {
            foreach (var d in selects) if (d != null && d.transform.Find("Dropdown List") != null) return true;
            return false;
        }

        // keyboard: join / pick in the lobby, Enter on the title (menu.js onKey)
        void OnKeys()
        {
            if (!Visible || input == null) return;
            if (UiRoot.TextFieldFocused || SelectOpen()) return; // keys typed into an INPUT / SELECT
            if (Screen == "title")
            {
                if (input.Pressed.Contains("Enter") || input.Pressed.Contains("Space")) Go("setup");
                return;
            }
            if (Screen != "setup") return;
            if (input.Pressed.Contains("Space")) { Join("kbA"); return; }
            if (input.Pressed.Contains("Enter") || input.Pressed.Contains("NumpadEnter"))
            {
                // Enter on a focused button clicks it (the browser's default action)
                var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var btn = sel != null && sel.activeInHierarchy ? sel.GetComponent<Button>() : null;
                if (btn != null) { btn.onClick.Invoke(); return; }
                bool shift = input.Down.Contains("ShiftLeft") || input.Down.Contains("ShiftRight");
                if (SlotOf("kbB") >= 0 && shift) { Start(); return; }
                Join("kbB");
                return;
            }
            int a = SlotOf("kbA"), b = SlotOf("kbB");
            if (a >= 0)
            {
                if (keys.Hit(input, "KeyA")) { CycleBot(a, -1); return; }
                if (keys.Hit(input, "KeyD")) { CycleBot(a, 1); return; }
                if (keys.Hit(input, "KeyW")) { CycleColor(a, -1); return; }
                if (keys.Hit(input, "KeyS")) { CycleColor(a, 1); return; }
            }
            if (b >= 0)
            {
                if (keys.Hit(input, "ArrowLeft")) { CycleBot(b, -1); return; }
                if (keys.Hit(input, "ArrowRight")) { CycleBot(b, 1); return; }
                if (keys.Hit(input, "ArrowUp")) { CycleColor(b, -1); return; }
                if (keys.Hit(input, "ArrowDown")) { CycleColor(b, 1); return; }
            }
        }

        /// called every frame by the app while a menu screen is up (keys + pads)
        public void Poll()
        {
            if (!Visible || input == null) return;
            OnKeys();
            if (!Visible) return;
            foreach (var pr in input.PadPresses())
            {
                int button = pr.button;
                string dev = "pad" + pr.pad;
                if (Screen == "title") { if (button == 0 || button == 9) Go("setup"); else if (button == 8) { Controls(dev); return; } continue; }
                if (Screen == "help") { if (button == 1 || button == 0) Show("title"); continue; }
                if (Screen != "setup") continue;
                int i = SlotOf(dev);
                if (button == 0) { if (i < 0) Join(dev); continue; }
                if (button == 8) { Controls(dev); return; }
                if (button == 9) { if (i >= 0) Start(); continue; }
                if (i < 0) { if (button == 1) Show("title"); continue; }
                if (button == 1) Assign(i, "off");
                else if (button == 14) CycleBot(i, -1);
                else if (button == 15) CycleBot(i, 1);
                else if (button == 12) CycleColor(i, -1);
                else if (button == 13) CycleColor(i, 1);
                else if (button == 3) { int free = SlotOf("off"); if (free >= 0) Assign(free, "cpu"); } // △ / Y adds a CPU
            }
        }

        // ---------------------------------------------------------------- water + options
        void DrawScen()
        {
            var box = scenBox;
            if (box == null) return;
            Ui.Clear(box);
            foreach (var id in Scenarios.SCENARIO_ORDER)
            {
                var sc = Scenarios.SCENARIOS[id];
                double rho = Water.WaterDensity(sc.env.temperature, sc.env.salinity);
                double cur = JsMath.Hypot(sc.env.current.x, sc.env.current.y);
                bool on = Setup.scenario == id;
                var card = Ui.Node("scen-card", box);
                var hit = Ui.Img(card, null, new Color(0, 0, 0, 0), true);
                // background: linear-gradient(180deg, sky 0%, surface 18%, water 45%, fog 100%), clipped to the radius
                var bgm = Ui.Node("bg", card); Ui.Ignore(bgm); Ui.Stretch(bgm);
                Ui.Img(bgm, Ui.Rounded(12), Color.white);
                bgm.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var grad = Ui.Node("gradient", bgm); Ui.Stretch(grad);
                var stops = new[] { Colors.Rgb(sc.look.sky), Colors.Rgb(sc.look.surface), Colors.Rgb(sc.look.water), Colors.Rgb(sc.look.fog) };
                Ui.Raw(grad, Ui.Gradient("scen-" + id, 4, 128, (u, v) => Ui.Stops(v, stops, new[] { 0f, 0.18f, 0.45f, 1f })));
                if (on)
                {
                    Ui.Backing(card, "glow", Ui.Rounded(15, 3), new Color(1, 181 / 255f, 71 / 255f, 0.35f), -3).transform.SetAsLastSibling();
                    Ui.Backing(card, "border", Ui.Rounded(12, 2), Css.acc).transform.SetAsLastSibling();
                }
                Ui.VBox(card, Ui.Pad(14), 4);
                Ui.LE(card, -1, -1, -1, -1, -1, 220);
                Shadowed(Ui.Label(card, sc.name, 21, Color.white, UiFont.Bold));
                Shadowed(Ui.Label(card, sc.place, 13, Css.White(0.9f)));
                var pHolder = Ui.Node("p", card); Ui.VBox(pHolder, Ui.Pad(4, 0), 0);
                Shadowed(Ui.Label(pHolder, sc.blurb, 13, Color.white));
                var spacer = Ui.Node("auto", card); Ui.LE(spacer, -1, 0, -1, 1); // dl { margin: auto 0 0 }
                var dl = Ui.Box(card, "dl", new Color(0, 0, 0, 0.35f), 8, Color.clear);
                var t = dl.gameObject.AddComponent<Table>();
                t.padding = Ui.Pad(8);
                t.cellPadX = 5; t.flexCol = 1;
                void Dt(string k, string v)
                {
                    var tr = Ui.Node("tr", dl);
                    tr.gameObject.AddComponent<TableRow>().padding = Ui.Pad(0, 0, 1, 0);
                    Shadowed(Ui.Label(tr, k, 12, Css.White(0.8f), UiFont.Regular, TextAnchor.MiddleLeft, false));
                    Shadowed(Ui.Label(tr, v, 12, Color.white, UiFont.Mono, TextAnchor.MiddleLeft, false));
                }
                Dt("Depth", Js.Str(Setup.depth > 0 ? Setup.depth : sc.depth) + " m");
                Dt("Water", Js.Str(sc.env.temperature) + " °C · " + Js.Str(sc.env.salinity) + " g/kg");
                Dt("Density", Js.ToFixed(rho, 1) + " kg/m³");
                Dt("Visibility", "~" + Js.Str(sc.env.visibility) + " m");
                Dt("Current", Js.ToFixed(cur, 2) + " m/s");
                Dt("Waves", Js.ToFixed(sc.env.waves.amplitude * 2, 2) + " m, " + Js.Str(sc.env.waves.period) + " s");
                string sid = id;
                var btn = card.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.targetGraphic = hit;
                btn.onClick.AddListener(() => { Setup.scenario = sid; Sfx("uiMove"); DrawScen(); });
            }
        }

        // text-shadow: 0 1px 4px rgba(0,0,0,.8)
        static void Shadowed(Text t) => Ui.TextShadow(t, new Color(0, 0, 0, 0.8f), 1);

        void DrawOpts(Transform parent)
        {
            var S = Setup;
            var box = Ui.Node("opts", parent);
            var f = box.gameObject.AddComponent<Flow>();
            f.hgap = 24; f.vgap = 10; f.childAlignment = TextAnchor.MiddleLeft;
            // Match clock
            var mins = new[] { 5, 10, 15, 20 };
            var l1 = Ui.Node("label", box);
            Ui.HBox(l1, null, 8, TextAnchor.MiddleLeft);
            Ui.Label(l1, "Match clock", 15, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
            var minLabels = new List<string>();
            int minIdx = 0;
            for (int k = 0; k < mins.Length; k++) { minLabels.Add(mins[k] + " min"); if (S.minutes == mins[k]) minIdx = k; }
            var d1 = Ui.Select(l1, minLabels, minIdx, k => { S.minutes = mins[k]; });
            Ui.LE(d1, 92, 32, 0, -1, 92, 32);
            selects.Add(d1);
            // Water depth
            var l2 = Ui.Node("label", box);
            Ui.HBox(l2, null, 8, TextAnchor.MiddleLeft);
            Ui.Label(l2, "Water depth", 15, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
            var depLabels = new List<string>();
            int depIdx = 0;
            var D = Scenarios.DEPTH_CHOICES;
            for (int k = 0; k < D.Length; k++) { depLabels.Add(D[k] != 0 ? Js.Str(D[k]) + " m" : "water default"); if (S.depth == D[k]) depIdx = k; }
            var d2 = Ui.Select(l2, depLabels, depIdx, k => { S.depth = D[k]; DrawScen(); });
            Ui.LE(d2, 140, 32, 0, -1, 140, 32);
            selects.Add(d2);
            Ui.Check(box, "Coin flip start (+" + Js.Str(POINTS.coinFlip) + ", random start heading)", S.coinFlip, v => { S.coinFlip = v; });
        }

        public void Start()
        {
            var S = Setup;
            int used = 0;
            foreach (var s in S.slots) if (s.type != "off") used++;
            if (used == 0) { Flash("Press ✕ / A, Space or Enter to join first"); return; }
            SaveSetup(S);
            Sfx("uiSelect");
            onStart?.Invoke(S.Clone());
        }

        /// .menu-flash: a message at the bottom of the menu for 2.2 s
        public void Flash(string text)
        {
            var t = Ui.Box(screenRoot, "menu-flash", Css.Rgba(3, 16, 24, 0.92f), 10, Css.acc);
            Ui.Anchor(t, new Vector2(0.5f, 0), new Vector2(0, 30));
            Ui.HBox(t, Ui.Pad(10, 18), 0, TextAnchor.MiddleCenter);
            Ui.Fit(t);
            Ui.Line(t, text, 16, Css.acc, UiFont.Regular, TextAnchor.MiddleCenter, 0);
            var a = Anim.On(t, false);
            a.destroyAt = 2.2f;
        }
    }

    /// keydown auto-repeat for held keys (the browser repeats keydown while a key is held)
    public sealed class KeyRepeat
    {
        public float delay = 0.5f, rate = 0.04f;
        readonly Dictionary<string, float> next = new Dictionary<string, float>();

        public bool Hit(InputHub input, string code) => Hit(input.Pressed, input.Down, code);

        /// pressed: keys pressed this frame; down: keys held now
        public bool Hit(ICollection<string> pressed, HashSet<string> down, string code)
        {
            float now = Time.unscaledTime;
            if (pressed.Contains(code)) { next[code] = now + delay; return true; }
            if (!down.Contains(code)) { next.Remove(code); return false; }
            if (next.TryGetValue(code, out float t) && now >= t) { next[code] = now + rate; return true; }
            return false;
        }
    }
}
