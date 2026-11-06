// The overlay screens of src/main.js: overlay() with keyboard / gamepad
// navigation (navFocus / navMove / navActivate / navBack / navPoll and the owner
// rules), the pause menu, the controls screen with re-binding (endCapture swap
// rules, 8 s pad timeout, Escape cancels), the quit confirmation and the results.
// Game logic stays in the App: these methods take callbacks.

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CoreGame = Duburi.Core.Game;

namespace Duburi.Game
{
    public sealed class Overlays
    {
        static readonly Dictionary<string, string> TASK_NAME = new Dictionary<string, string>
        {
            ["gate"] = "Gate", ["slalom"] = "Slalom", ["bin"] = "Bin", ["torpedo"] = "Torpedoes", ["octagon"] = "Octagon", ["home"] = "Return home",
        };

        sealed class NavItem { public RectTransform rt; public Button button; public Skin skin; }
        sealed class Nav { public readonly List<NavItem> items = new List<NavItem>(); public int i; public int owner; public Action onBack; }
        sealed class Capture { public string device, id; public int i; public float t0; }
        sealed class Ctrl { public string device; public int owner; public Action back; }

        readonly UiRoot ui;
        readonly InputHub input;
        readonly Sound sound;
        readonly KeyRepeat keys = new KeyRepeat();
        readonly List<string> pressed = new List<string>(8);
        RectTransform ov;
        ScrollRect ovScroll;
        Nav nav, building;
        Ctrl ctrl;
        Capture capture;
        PadDrawing ctrlPad;

        /// the device of every player (main.js this.devices), for the owner rule of navPoll
        public IReadOnlyList<string> Devices { get; set; }
        /// an overlay with navigation is open (main.js this.nav)
        public bool IsOpen => nav != null;
        /// waiting for the new button / key of a re-binding
        public bool Capturing => capture != null;
        /// the controls screen is open (main.js this.ctrl)
        public bool ControlsOpen => ctrl != null;
        public RectTransform Current => ov;

        public Overlays(UiRoot ui, InputHub input, Sound sound)
        {
            this.ui = ui; this.input = input; this.sound = sound;
        }

        void Sfx(string name) => sound?.Sfx(name);

        // ---------------------------------------------------------------- overlay + navigation
        /// overlay(html, { owner, onBack }): a .panel over everything; the builder fills it and
        /// registers its [data-nav] buttons with AddNav in document order
        public PanelParts Overlay(Action<PanelParts> build, int owner = -1, Action onBack = null, float maxWidth = 1000, bool header = true)
        {
            Close();
            var o = Ui.Node("overlay", ui.OverlayLayer);
            Ui.Stretch(o);
            Ui.Img(o, null, Css.Rgba(2, 10, 16, 0.6f), true);
            var P = Ui.Panel(o, maxWidth, header);
            ov = o;
            ovScroll = P.scrollRect;
            building = new Nav { owner = owner, onBack = onBack };
            build?.Invoke(P);
            nav = building;
            building = null;
            for (int i = 0; i < nav.items.Count; i++)
            {
                // only a real mouse movement moves the focus (no focus change when the overlay
                // appears under a resting cursor)
                var h = nav.items[i].rt.gameObject.AddComponent<HoverFocus>();
                h.index = i;
                h.onMove = k => { if (nav != null && nav.i != k) NavFocus(k); };
            }
            NavFocus(0);
            input?.PadPresses(); // swallow the press that opened it
            return P;
        }

        /// register a button as the next [data-nav] item of the overlay being built
        public void AddNav(Button b)
        {
            if (building == null || b == null) return;
            building.items.Add(new NavItem { rt = (RectTransform)b.transform, button = b, skin = b.GetComponent<Skin>() });
        }

        /// closeOverlay()
        public void Close()
        {
            if (ov != null) { ov.gameObject.SetActive(false); UnityEngine.Object.Destroy(ov.gameObject); ov = null; }
            ovScroll = null;
            nav = null; ctrl = null; capture = null; ctrlPad = null;
        }

        public void NavFocus(int i) => NavFocus(i, false);

        void NavFocus(int i, bool reveal)
        {
            var n = nav;
            if (n == null || n.items.Count == 0) return;
            n.i = ((i % n.items.Count) + n.items.Count) % n.items.Count;
            for (int k = 0; k < n.items.Count; k++) if (n.items[k].skin != null) n.items[k].skin.Focus = k == n.i;
            if (reveal) Reveal(n.items[n.i].rt);
        }

        public void NavMove(int d) { if (nav != null) { NavFocus(nav.i + d, true); Sfx("uiMove"); } }

        public void NavActivate()
        {
            var n = nav;
            if (n != null && n.i < n.items.Count) { Sfx("uiSelect"); n.items[n.i].button.onClick.Invoke(); }
        }

        public void NavBack()
        {
            var n = nav;
            if (n != null && n.onBack != null) { Sfx("uiBack"); n.onBack(); }
        }

        // keep the item chosen with keys / pad inside the scrolling body
        void Reveal(RectTransform item)
        {
            var sr = ovScroll;
            if (sr == null || item == null || !item.IsChildOf(sr.content)) return;
            var vp = sr.viewport;
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(vp, item);
            var r = vp.rect;
            float dy = 0;
            if (b.max.y > r.yMax) dy = b.max.y - r.yMax + 8;
            else if (b.min.y < r.yMin) dy = b.min.y - r.yMin - 8;
            if (dy == 0) return;
            var c = sr.content;
            float max = Mathf.Max(0, c.rect.height - r.height);
            c.anchoredPosition = new Vector2(c.anchoredPosition.x, Mathf.Clamp(c.anchoredPosition.y - dy, 0, max));
            sr.StopMovement();
        }

        /// once per frame, after input.Poll() and before the game reads input: the keyboard part of
        /// main.js onKey (re-binding capture and menu navigation), navPoll() and the live controller
        /// drawing of the controls screen. While an overlay is open every key belongs to it.
        public void Tick()
        {
            if (input == null) return;
            if (capture != null && !capture.device.StartsWith("pad", StringComparison.Ordinal))
            {
                string code = input.FirstKeyPressed();
                if (code != null)
                {
                    input.Consume(code);
                    if (code == "Escape") EndCapture(null, -1); else EndCapture(code, -1);
                }
            }
            else if (nav != null)
            {
                pressed.Clear();
                foreach (var k in input.Pressed) pressed.Add(k);
                foreach (var k in pressed) input.Consume(k); // a key used in a menu does nothing in the game
                var start = nav;
                if (keys.Hit(pressed, input.Down, "ArrowUp") | keys.Hit(pressed, input.Down, "KeyW")) NavMove(-1);
                else if (keys.Hit(pressed, input.Down, "ArrowDown") | keys.Hit(pressed, input.Down, "KeyS")) NavMove(1);
                if (nav == start && nav != null)
                {
                    if (pressed.Contains("Enter") || pressed.Contains("Space") || pressed.Contains("NumpadEnter")) NavActivate();
                    else if (pressed.Contains("Escape") || pressed.Contains("Tab") || pressed.Contains("Backspace")) NavBack();
                }
            }
            NavPoll();
            if (ctrl != null && ctrl.device.StartsWith("pad", StringComparison.Ordinal) && ov != null && ctrlPad != null) ctrlPad.Highlight(input.Pad(ctrl.device));
        }

        /// navPoll(): gamepad navigation of the open overlay (and the pad re-binding capture)
        void NavPoll()
        {
            if (nav == null) return;
            var presses = input.PadPresses();
            if (capture != null && capture.device.StartsWith("pad", StringComparison.Ordinal))
            {
                int want = int.Parse(capture.device.Substring(3));
                int hit = -1;
                foreach (var p in presses) if (p.pad == want && p.button != 16) { hit = p.button; break; }
                if (hit >= 0) EndCapture(null, hit);
                else if (Time.realtimeSinceStartup - capture.t0 > 8) EndCapture(null, -1);
                return;
            }
            foreach (var pr in presses)
            {
                if (nav == null) break;
                int owner = nav.owner;
                var devs = Devices;
                string od = owner >= 0 && devs != null && owner < devs.Count ? devs[owner] : null;
                int ownerPad = od != null && od.StartsWith("pad", StringComparison.Ordinal) ? int.Parse(od.Substring(3)) : -1;
                if (ownerPad >= 0 && pr.pad != ownerPad && pr.button != 9) continue; // the owner drives their menu
                if (pr.button == 12) NavMove(-1);
                else if (pr.button == 13) NavMove(1);
                else if (pr.button == 0) NavActivate();
                else if (pr.button == 1 || pr.button == 9) NavBack();
            }
        }

        // ---------------------------------------------------------------- shared bits
        static void NavHint(Transform parent, string a, string b, string c)
        {
            // .note.nav-hint { margin: 4px 0 12px }
            var holder = Ui.Node("nav-hint", parent);
            Ui.VBox(holder, Ui.Pad(4, 0, 12, 0));
            var r = new Rich(holder, 13, Css.mut);
            r.T(a).T(b).T(c);
        }

        // <details><summary>title</summary>…</details>
        static void Details(Transform parent, string title, Action<Transform> body)
        {
            var d = Ui.Node("details", parent);
            Ui.VBox(d, Ui.Pad(8, 0), 6);
            var sum = Ui.Node("summary", d);
            Ui.Img(sum, null, new Color(0, 0, 0, 0), true);
            Ui.HBox(sum, null, 6, TextAnchor.MiddleLeft);
            var marker = Ui.IconImage(sum, "right", 11, Css.acc);
            Ui.Label(sum, title, 16, Css.acc, UiFont.SemiBold, TextAnchor.MiddleLeft, false);
            Ui.Spacer(sum);
            var content = Ui.Node("content", d);
            Ui.VBox(content);
            body(content);
            content.gameObject.SetActive(false);
            var b = sum.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() =>
            {
                bool open = !content.gameObject.activeSelf;
                content.gameObject.SetActive(open);
                marker.sprite = Ui.Icon(open ? "down" : "right");
            });
        }

        static RectTransform PauseMenu(Transform parent)
        {
            // .pause-menu { flex column; gap 6px; margin 14px 0 6px; max-width 520px }
            var holder = Ui.Node("pause-menu-holder", parent);
            Ui.HBox(holder, Ui.Pad(14, 0, 6, 0), 0, TextAnchor.UpperLeft);
            var m = Ui.Node("pause-menu", holder);
            Ui.VBox(m, null, 6);
            Ui.LE(m, 520, -1, 0, -1, 0);
            return m;
        }

        static RectTransform PauseBtns(Transform parent)
        {
            // .pause-btns { flex; gap 10px; flex-wrap; margin 16px 0 }
            var holder = Ui.Node("pause-btns-holder", parent);
            Ui.VBox(holder, Ui.Pad(16, 0), 0);
            var f = Ui.Node("pause-btns", holder);
            var fl = f.gameObject.AddComponent<Flow>();
            fl.hgap = 10; fl.vgap = 10; fl.childAlignment = TextAnchor.MiddleLeft;
            return f;
        }

        // ---------------------------------------------------------------- pause menu
        /// openPause(pi): the pause menu of player pi (the owner drives it with their pad).
        /// The App first checks state == "game" and !game.over, sets paused, hides the player's card
        /// and calls sound.Ensure(). onAction gets: "resume" (also Esc / ○ / B), "quit", "skip", "end",
        /// "forfeit", "leave", "restart" — the App does main.js's work for them —, and after the UI did
        /// its part: "quality" (GraphicsQuality.Set to the next level, then the menu reopens) and
        /// "mute" (Sound.SetMuted toggled, then the menu reopens). "Controls & customise" opens the
        /// controls screen, which comes back here.
        public void OpenPause(CoreGame g, int pi, string device, int humans, Action<string> onAction)
        {
            var p = pi >= 0 && pi < g.players.Count ? g.players[pi] : g.players[0];
            bool running = p.run.state == "running";
            string task = running && TASK_NAME.TryGetValue(g.CurrentTask(p), out var tn) ? tn : null;
            var pc = Colors.Hex(p.color);
            Action reopen = () => OpenPause(g, pi, device, humans, onAction);
            Overlay(P =>
            {
                // header in the player's colour
                P.headerRule.color = pc;
                var dot = Ui.Node("pdot", P.header);
                Ui.Img(dot, Ui.Circle(14), pc);
                Ui.DropShadow(dot, 7, 24, 0, pc);
                Ui.LE(dot, 14, 14, 0, 0, 14, 14);
                Ui.H2(P.header, "Paused — " + HelpText.Esc(p.name));
                Ui.Label(P.header, HelpText.Esc(p.design.name) + " · " + Js.ToLocaleString(g.Total(p)) + " pts" + (task != null ? " · on: " + task : ""), 14, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false, "sub");
                var m = PauseMenu(P.content);
                void B(string a, string label, string sub, string cls, Action fn) => AddNav(Ui.MenuItem(m, label, sub, cls, fn));
                B("resume", "Resume", "", "primary", () => onAction?.Invoke("resume"));
                B("quit", "Quit match", "leave the match and go back to the lobby", "bad", () => onAction?.Invoke("quit"));
                if (running) B("skip", "Skip task" + (task != null ? ": " + task : ""), "no points for it — you are moved to the next task", "", () => onAction?.Invoke("skip"));
                if (running) B("end", "End run", "keep the points of this run", "", () => onAction?.Invoke("end"));
                B("forfeit", "Forfeit run", "lose this run's points and start again from the dock", "warn", () => onAction?.Invoke("forfeit"));
                if (humans > 1) B("leave", "Leave match", "the others keep playing", "warn", () => onAction?.Invoke("leave"));
                B("restart", "Restart match", "same players, same water", "", () => onAction?.Invoke("restart"));
                B("quality", "Graphics: " + GraphicsQuality.Label(GraphicsQuality.Current), "Ultra (4K, SSAO, live reflections) · High · Medium · Low", "", () =>
                {
                    var order = GraphicsQuality.Order;
                    int k = Array.IndexOf(order, GraphicsQuality.Current);
                    GraphicsQuality.Set(order[((k + 1) % order.Length + order.Length) % order.Length]);
                    onAction?.Invoke("quality");
                    reopen();
                });
                B("controls", "Controls & customise", device == "cpu" ? "" : "see your layout, re-bind buttons and keys", "", () =>
                    OpenControls(device == "cpu" || device == null ? "kbA" : device, pi, reopen));
                B("mute", sound != null && sound.Muted ? "Sound: off" : "Sound: on", "", "", () =>
                {
                    sound?.SetMuted(!sound.Muted);
                    onAction?.Invoke("mute");
                    reopen();
                });
                NavHint(P.content, "↑ ↓ / D-pad to choose ·", "Enter / ✕ / A to select ·", "Esc / ○ / B to resume");
                Details(P.content, "Rules", HelpText.Rules);
                Details(P.content, "Controls", HelpText.Controls);
            }, pi, () => onAction?.Invoke("resume"));
        }

        // ---------------------------------------------------------------- controls screen
        /// openControlsFromMenu(dev): the controls screen over the (hidden) menu; Done / back return to it
        public void OpenControlsFromMenu(Menu menu, string dev)
        {
            string back = menu.Screen ?? "title";
            menu.Hide();
            OpenControls(dev, -1, () => { Close(); menu.Show(back); });
        }

        /// openControls(device, owner, back, focus): the controller / keyboard drawing, the stick
        /// presets and toggles and every re-bindable action of one device
        public void OpenControls(string device, int owner, Action back, int focus = 0)
        {
            var pads = input != null ? input.Pads() : null;
            bool isPad = device.StartsWith("pad", StringComparison.Ordinal);
            var pad = isPad && input != null ? input.Pad(device) : null;
            string style = isPad ? Controls.PadStyle(pad) : "ps";
            var b = Controls.GetBindings(device);
            void Reopen(int i) { input?.ReloadBindings(); OpenControls(device, owner, back, i); }
            int Idx(Button el) { for (int k = 0; k < nav.items.Count; k++) if (nav.items[k].button == el) return k; return 0; }
            PadDrawing drawing = null;
            var P = Overlay(P =>
            {
                Ui.H2(P.header, "Controls");
                var tabs = Ui.Node("ctabs", P.header);
                var tf = tabs.gameObject.AddComponent<Flow>();
                tf.hgap = 6; tf.vgap = 6; tf.childAlignment = TextAnchor.MiddleLeft;
                foreach (var d in Controls.Devices)
                {
                    if (d.id == "cpu") continue;
                    bool connected = true;
                    if (d.id.StartsWith("pad", StringComparison.Ordinal))
                    {
                        connected = false;
                        int want = int.Parse(d.id.Substring(3));
                        if (pads != null) foreach (var p in pads) if (p.index == want) connected = true;
                    }
                    string dev = d.id;
                    Button tab = null;
                    tab = Ui.CTab(tabs, d.label, connected ? null : "(not connected)", d.id == device, () => OpenControls(dev, owner, back, Idx(tab)));
                    AddNav(tab);
                }
                var cols = Ui.Node("ctrl-cols", P.content);
                var g = cols.gameObject.AddComponent<AutoGrid>();
                g.weights = new[] { 1.4f, 1f }; g.minCol = new[] { 0f, 260f }; g.collapseBelow = 860; g.gap = 18;
                g.padding = Ui.Pad(12, 0, 0, 0);
                var draw = Ui.Node("ctrl-draw", cols);
                Ui.VBox(draw, null, 8);
                var list = Ui.Node("ctrl-list", cols);
                Ui.VBox(list, null, 5);
                if (isPad)
                {
                    drawing = PadDrawing.Build(draw, device, style, false);
                    Ui.Label(draw, pad != null ? "Connected: " + HelpText.Esc(pad.id) + " — press buttons to see them light up." : "Connect this controller and press any button.", 13, Css.mut);
                    Ui.H4(list, "Sticks", Css.acc, 10, 4);
                    foreach (var k in Controls.StickPresetOrder)
                    {
                        if (!Controls.StickPresets.TryGetValue(k, out var pr)) continue;
                        bool on = b.sticks == k;
                        string key = k;
                        Button el = null;
                        el = Ui.Bind(list, pr.label, pr.desc, on ? "●" : "○", on, () =>
                        {
                            var bb = Controls.GetBindings(device); bb.sticks = key; Controls.SaveBindings(device, bb); Reopen(Idx(el));
                        }, out _);
                        AddNav(el);
                    }
                    Button inv = null, trg = null;
                    inv = Ui.Bind(list, "Invert stick Y (up / down and forward / back)", null, b.invertY ? "ON" : "off", false, () =>
                    {
                        var bb = Controls.GetBindings(device); bb.invertY = !bb.invertY; Controls.SaveBindings(device, bb); Reopen(Idx(inv));
                    }, out _);
                    AddNav(inv);
                    trg = Ui.Bind(list, "Triggers move up / down", null, b.triggersHeave ? "ON" : "off", false, () =>
                    {
                        var bb = Controls.GetBindings(device); bb.triggersHeave = !bb.triggersHeave; Controls.SaveBindings(device, bb); Reopen(Idx(trg));
                    }, out _);
                    AddNav(trg);
                    H4Small(list, "Buttons", "select one, then press the new button");
                    foreach (var a in Concat(Controls.PadHolds, Controls.Actions))
                    {
                        int bi = b.buttons.TryGetValue(a.id, out var v) ? v : -1;
                        AddBind(list, device, "pad", a.id, ActionLabel(a.id), Controls.ButtonName(bi, style));
                    }
                }
                else
                {
                    ControlsUi.KeyboardGrid(draw, device);
                    H4Small(list, "Keys", "select one, then press the new key");
                    foreach (var a in Controls.KeyAxes) AddBind(list, device, "key", "ax:" + a.id, a.label, Controls.KeyName(b.axes.TryGetValue(a.id, out var c) ? c : null));
                    foreach (var a in Controls.Actions) AddBind(list, device, "key", "ac:" + a.id, a.label, Controls.KeyName(b.actions.TryGetValue(a.id, out var c) ? c : null));
                    AddBind(list, device, "key", "fine", "Fine control (hold)", Controls.KeyName(b.fine));
                }
                var btns = PauseBtns(P.content);
                Button reset = null;
                reset = Ui.Btn(btns, "Reset " + (isPad ? "this controller" : "these keys") + " to default", () => { Controls.ResetBindings(device); Reopen(Idx(reset)); });
                AddNav(reset);
                AddNav(Ui.Btn(btns, "Done", () => back?.Invoke(), true));
                var hint = Ui.Node("nav-hint", P.content);
                Ui.VBox(hint, Ui.Pad(4, 0, 12, 0));
                new Rich(hint, 13, Css.mut).T("↑ ↓ / D-pad to choose ·").T("Enter / ✕ / A to change ·").T("Esc / ○ / B to go back. A button already in use is swapped.");
            }, owner, back, 1180);
            ctrl = new Ctrl { device = device, owner = owner, back = back };
            ctrlPad = drawing;
            NavFocus(focus);
            if (focus > 0)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(P.panel);
                if (nav != null) Reveal(nav.items[nav.i].rt);
            }
        }

        static IEnumerable<ActionDef> Concat(ActionDef[] a, ActionDef[] b) { foreach (var x in a) yield return x; foreach (var x in b) yield return x; }

        static string ActionLabel(string id)
        {
            foreach (var a in Controls.Actions) if (a.id == id) return a.label;
            foreach (var a in Controls.PadHolds) if (a.id == id) return a.label;
            return id;
        }

        static void H4Small(Transform parent, string title, string small)
        {
            var holder = Ui.Node("h4", parent);
            Ui.VBox(holder, Ui.Pad(10, 0, 4, 0));
            Ui.Label(holder, Ui.B(title) + " <size=13>" + Ui.Col(small, Css.mut) + "</size>", 16, Css.acc, UiFont.Regular);
        }

        // a .bind row that starts a capture when chosen
        void AddBind(Transform list, string device, string kind, string id, string label, string key)
        {
            Button el = null;
            RectTransform kbd = null;
            el = Ui.Bind(list, label, null, key, false, () =>
            {
                // .bind.capturing: green border, blinking, "press a button…"
                var skin = el.GetComponent<Skin>();
                skin.borderN = skin.borderH = skin.borderF = Css.good;
                skin.Apply();
                Ui.Clear(kbd);
                Ui.KbdFixed(kbd, device.StartsWith("pad", StringComparison.Ordinal) ? "press a button…" : "press a key…", Css.good);
                Anim.On(el).blinkPeriod = 0.8f;
                int i = 0;
                for (int k = 0; k < nav.items.Count; k++) if (nav.items[k].button == el) i = k;
                capture = new Capture { device = device, id = id, i = i, t0 = Time.realtimeSinceStartup };
                input?.PadPresses(); // the ✕ that started this is not the new binding
            }, out kbd);
            AddNav(el);
        }

        /// endCapture(code): store a captured button / key (cancelled: key null and button -1);
        /// a binding already in use is swapped
        void EndCapture(string key, int button)
        {
            var c = capture;
            capture = null;
            if (c == null) return;
            string dev = c.device;
            bool got = key != null || button >= 0;
            if (got)
            {
                var b = Controls.GetBindings(dev);
                if (dev.StartsWith("pad", StringComparison.Ordinal))
                {
                    int old = b.buttons.TryGetValue(c.id, out var o) ? o : -1;
                    var ks = new List<string>(b.buttons.Keys);
                    foreach (var k in ks) if (k != c.id && b.buttons[k] == button) b.buttons[k] = old;
                    b.buttons[c.id] = button;
                }
                else
                {
                    // slots: every key axis, every action, the fine-control key
                    var slots = new List<(string g, string k)>();
                    foreach (var a in Controls.KeyAxes) slots.Add(("axes", a.id));
                    foreach (var a in Controls.Actions) slots.Add(("actions", a.id));
                    slots.Add(("fine", null));
                    string Get((string g, string k) s) => s.g == "fine" ? b.fine : s.g == "axes" ? (b.axes.TryGetValue(s.k, out var v) ? v : null) : (b.actions.TryGetValue(s.k, out var w) ? w : null);
                    void Set((string g, string k) s, string v) { if (s.g == "fine") b.fine = v; else if (s.g == "axes") b.axes[s.k] = v; else b.actions[s.k] = v; }
                    var mine = c.id == "fine" ? ("fine", (string)null) : (c.id.StartsWith("ax:", StringComparison.Ordinal) ? "axes" : "actions", c.id.Substring(3));
                    string old = Get(mine);
                    foreach (var sl in slots) if (!(sl.g == mine.Item1 && sl.k == mine.Item2) && Get(sl) == key) Set(sl, old);
                    Set(mine, key);
                }
                Controls.SaveBindings(dev, b);
                Sfx("uiSelect");
            }
            else Sfx("uiBack");
            input?.ReloadBindings();
            input?.Pressed.Clear();
            var cur = ctrl;
            int owner = cur != null ? cur.owner : -1;
            Action back = cur != null ? cur.back : (Action)Close;
            OpenControls(dev, owner, back, c.i);
        }

        // ---------------------------------------------------------------- quit + results
        /// confirmQuit(): the always-visible Quit button asks first. The App checks state == "game" and
        /// !game.over and sets paused before calling; onNo = resume (also Esc / ○ / B), onYes = toMenu
        public void ConfirmQuit(Action onNo, Action onYes)
        {
            Overlay(P =>
            {
                Ui.H2(P.header, "Quit the match?");
                var holder = Ui.Node("p", P.content);
                Ui.VBox(holder, Ui.Pad(16, 0, 0, 0));
                Ui.Label(holder, "The match ends for everyone and you go back to the lobby.", 16, Css.text);
                var m = PauseMenu(P.content);
                AddNav(Ui.MenuItem(m, "No, keep playing", null, "", () => onNo?.Invoke()));
                AddNav(Ui.MenuItem(m, "Yes, quit", null, "bad", () => onYes?.Invoke()));
            }, -1, () => onNo?.Invoke());
        }

        /// showResults(): the final standings. onAction: "again" (start(cfg) again), "lobby" (toMenu,
        /// also Esc / ○ / B), "menu" (toMenu, then menu.Show("title"))
        public void ShowResults(CoreGame g, Action<string> onAction)
        {
            Overlay(P =>
            {
                Ui.Logo(P.header, 34, 4);
                Ui.H2(P.header, "Results — " + Scenarios.Get(g.scenario).name + ", " + Js.Str(g.depth) + " m");
                HelpText.Results(P.content, g);
                var btns = PauseBtns(P.content);
                AddNav(Ui.Btn(btns, "Play again", () => onAction?.Invoke("again"), true));
                AddNav(Ui.Btn(btns, "Lobby", () => onAction?.Invoke("lobby")));
                AddNav(Ui.Btn(btns, "Main menu", () => onAction?.Invoke("menu")));
            }, -1, () => onAction?.Invoke("lobby"));
        }
    }

    /// moves the overlay focus to the item under the mouse, only on real mouse movement
    public sealed class HoverFocus : MonoBehaviour, IPointerMoveHandler
    {
        public int index;
        public Action<int> onMove;
        public void OnPointerMove(PointerEventData e) { if (e.delta != Vector2.zero) onMove?.Invoke(index); }
    }
}
