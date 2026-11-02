// src/ui/controls.js: a drawing of the player's controller with what every
// button does (live — pressed buttons light up), the stick layout, the keyboard
// layout, and the small "your controls" card shown at the start of a match.
// The SVG of the browser version is rebuilt here: the controller body is
// rasterised once into a texture, the buttons are images that change colour,
// the callouts are thin rotated images and uGUI text.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Duburi.Game
{
    /// controllerSVG(device, { style, compact }) + highlightPad(root, pad)
    public sealed class PadDrawing
    {
        // button positions on the drawing (standard mapping indices), viewBox 800 x 430
        struct Btn { public float x, y, r, w, h; public bool face, stick; }
        static readonly Btn[] BTN =
        {
            new Btn { x = 560, y = 232, r = 15, face = true }, new Btn { x = 593, y = 199, r = 15, face = true }, new Btn { x = 527, y = 199, r = 15, face = true }, new Btn { x = 560, y = 166, r = 15, face = true },
            new Btn { x = 262, y = 108, w = 76, h = 16 }, new Btn { x = 538, y = 108, w = 76, h = 16 }, new Btn { x = 262, y = 80, w = 70, h = 20 }, new Btn { x = 538, y = 80, w = 70, h = 20 },
            new Btn { x = 352, y = 150, w = 22, h = 12 }, new Btn { x = 448, y = 150, w = 22, h = 12 },
            new Btn { x = 345, y = 262, r = 26, stick = true }, new Btn { x = 455, y = 262, r = 26, stick = true },
            new Btn { x = 262, y = 172, w = 18, h = 22 }, new Btn { x = 262, y = 226, w = 18, h = 22 }, new Btn { x = 235, y = 199, w = 22, h = 18 }, new Btn { x = 289, y = 199, w = 22, h = 18 },
            new Btn { x = 400, y = 236, r = 10 },
        };
        // where each button's label goes (side, y), in the JS object's key order
        static readonly int[] CALLOUT_BTN = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 13, 14, 15 };
        static readonly Dictionary<int, (char side, float y)> CALLOUT = new Dictionary<int, (char, float)>
        {
            [6] = ('L', 60), [4] = ('L', 96), [12] = ('L', 150), [14] = ('L', 186), [13] = ('L', 222), [8] = ('L', 258),
            [7] = ('R', 60), [5] = ('R', 96), [3] = ('R', 140), [2] = ('R', 172), [1] = ('R', 204), [0] = ('R', 236), [9] = ('R', 268), [15] = ('R', 300),
        };
        static readonly string[] PS_SYMBOL = { "✕", "○", "□", "△" };
        static readonly string[] PS_COLOR = { "#7aa7ff", "#ff6b6b", "#f08ad8", "#3fd6a6" };
        static readonly string[] XB_SYMBOL = { "A", "B", "X", "Y" };
        static readonly string[] XB_COLOR = { "#4fd36b", "#ff5d5d", "#4f9bff", "#ffd24a" };

        public static readonly Dictionary<string, string> SHORT = new Dictionary<string, string>
        {
            ["marker"] = "Marker", ["torpedo"] = "Torpedo", ["grip"] = "Gripper", ["camera"] = "Camera", ["depthHold"] = "Depth hold", ["skip"] = "Skip task", ["endRun"] = "End run",
            ["restart"] = "Forfeit", ["pause"] = "Pause menu", ["rollLeft"] = "Roll left", ["rollRight"] = "Roll right", ["pitchUp"] = "Pitch up", ["pitchDown"] = "Pitch down",
        };

        static readonly Color padBtn = Colors.Hex("#2a3a47"), stickFill = Colors.Hex("#0e171e");
        static Texture2D bodyTex;

        public RectTransform Root;                 // the box in the layout (width: 100%)
        readonly Image[] fill = new Image[17], stroke = new Image[17];
        readonly Color[] fillN = new Color[17], strokeN = new Color[17];
        readonly RectTransform[] knob = new RectTransform[2];
        readonly Vector2[] knobBase = new Vector2[2];
        readonly bool[] lit = new bool[17];
        Vector2 lastStick0, lastStick1;

        // stickText(b, side)
        public static string StickText(Bindings b, char side)
        {
            var names = new Dictionary<string, string> { ["surge"] = "fwd / back", ["sway"] = "strafe", ["heave"] = "up / down", ["yaw"] = "turn" };
            var parts = new List<string>();
            if (!Controls.StickPresets.TryGetValue(b.sticks ?? "", out var preset) && !Controls.StickPresets.TryGetValue("ardusub", out preset)) return "";
            foreach (var kv in preset.map)
            {
                int i = kv.Value[0];
                if ((side == 'L' && (i == 0 || i == 1)) || (side == 'R' && (i == 2 || i == 3)))
                    parts.Add((i % 2 != 0 ? "↕" : "↔") + " " + (names.TryGetValue(kv.Key, out var n) ? n : kv.Key));
            }
            parts.Sort(string.CompareOrdinal); // JS sort(): UTF-16 order
            parts.Reverse();
            return string.Join(" · ", parts);
        }

        static Texture2D Body()
        {
            if (bodyTex != null) return bodyTex;
            var r = new Raster(800, 430, Css.White(0));
            var path = new Raster.Path().M(250, 120).C(300, 112, 500, 112, 550, 120).C(640, 130, 680, 200, 700, 290).C(712, 345, 660, 372, 620, 340)
                .C(585, 312, 560, 290, 500, 288).L(300, 288).C(240, 290, 215, 312, 180, 340).C(140, 372, 88, 345, 100, 290).C(120, 200, 160, 130, 250, 120);
            r.FillPoly(path.pts, Colors.Hex("#1b2a36"));                       // .pad-body
            r.Stroke(path.pts, true, 2, Css.White(0.35f));
            r.FillRoundRect(330, 128, 140, 80, 16, Colors.Hex("#142029"));     // .pad-pad
            r.StrokeRoundRect(330, 128, 140, 80, 16, 1, Css.White(0.15f));
            bodyTex = r.ToTexture("pad-body");
            return bodyTex;
        }

        static RectTransform At(Transform parent, string name, float cx, float cy, float w, float h)
        {
            var rt = Ui.Node(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // .pad-line: a polyline as thin images
        static void Polyline(Transform parent, float[] xy)
        {
            for (int i = 0; i + 3 < xy.Length; i += 2)
            {
                var a = new Vector2(xy[i], -xy[i + 1]);
                var b = new Vector2(xy[i + 2], -xy[i + 3]);
                var d = b - a;
                if (d.sqrMagnitude < 1e-4f) continue;
                var rt = Ui.Node("line", parent);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = a;
                rt.sizeDelta = new Vector2(d.magnitude + 0.6f, 1.2f);
                rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                Ui.Img(rt, null, Css.White(0.35f));
            }
        }

        // .pad-label at (x, baseline y) with text-anchor start / end: <tspan.pad-key>key</tspan>text
        static void Label(Transform parent, float x, float y, bool end, string key, string text, float size, Color color)
        {
            var rt = Ui.Node("label", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(end ? 1 : 0, 0.5f);
            rt.anchoredPosition = new Vector2(x, -(y - size * 0.32f));
            Ui.HBox(rt, null, 0, end ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            Ui.Fit(rt);
            if (!string.IsNullOrEmpty(key)) Ui.Pieces(rt, key, size, Css.acc, UiFont.Bold, false);
            Ui.Pieces(rt, text, size, color, UiFont.Medium, false);
        }

        /// controllerSVG(device, { style, compact })
        public static PadDrawing Build(Transform parent, string device, string style, bool compact)
        {
            var D = new PadDrawing();
            var b = Controls.GetBindings(device);
            var what = new Dictionary<int, List<string>>();
            void Add(int i, string s) { if (!what.TryGetValue(i, out var l)) what[i] = l = new List<string>(); l.Add(s); }
            foreach (var kv in b.buttons) if (kv.Value >= 0) Add(kv.Value, SHORT.TryGetValue(kv.Key, out var sh) ? sh : kv.Key);
            if (b.triggersHeave) { Add(6, "Down"); Add(7, "Up"); }
            string L = StickText(b, 'L'), R = StickText(b, 'R');
            bool ps = style == "ps";
            float labelSize = compact ? 15 : 14; // .qcard .pad-svg.compact .pad-label { font-size: 15px }

            var box = Ui.Node("pad-svg", parent);
            var aspect = box.gameObject.AddComponent<AspectBox>();
            var draw = Ui.Node("drawing", box);
            draw.anchorMin = draw.anchorMax = new Vector2(0.5f, 1);
            draw.pivot = new Vector2(0.5f, 1);
            draw.anchoredPosition = Vector2.zero;
            draw.sizeDelta = new Vector2(800, 430);
            aspect.drawing = draw;
            D.Root = box;

            var body = Ui.Node("body", draw);
            Ui.Stretch(body);
            Ui.Raw(body, Body());

            for (int i = 0; i < BTN.Length; i++)
            {
                var p = BTN[i];
                bool round = p.r > 0;
                float w = round ? p.r * 2 : p.w, h = round ? p.r * 2 : p.h;
                var g = At(draw, "pb" + i, p.x, p.y, w, h);
                Color fc = p.face ? Colors.Hex((ps ? PS_COLOR : XB_COLOR)[i]) : Css.White(0.4f);
                float sw = p.stick ? 3 : 1.5f;
                D.fill[i] = Ui.Img(g, round ? Ui.Circle(w) : Ui.Rounded(5), p.stick ? stickFill : padBtn);
                var srt = Ui.Node("stroke", g);
                Ui.Stretch(srt, -sw / 2, -sw / 2, -sw / 2, -sw / 2);
                D.stroke[i] = Ui.Img(srt, round ? Ui.Ring(w + sw, sw) : Ui.Rounded(5 + sw / 2, sw), fc);
                D.fillN[i] = D.fill[i].color; D.strokeN[i] = fc;
                if (p.stick) { D.knob[i - 10] = g; D.knobBase[i - 10] = g.anchoredPosition; }
                if (p.face)
                {
                    string sym = (ps ? PS_SYMBOL : XB_SYMBOL)[i];
                    var s = At(draw, "sym" + i, p.x, p.y, 2 * p.r, 2 * p.r);
                    if (Ui.HasIcons(sym))
                    {
                        var ic = Ui.Node("icon", s);
                        Ui.Stretch(ic, 7, 7, 7, 7);
                        Ui.Img(ic, Ui.Icon(Ui.IconKey(sym, 0, out _)), fc).preserveAspect = true;
                    }
                    else { var t = Ui.Label(s, sym, 16, fc, UiFont.Bold, TextAnchor.MiddleCenter, false); Ui.Stretch(t.rectTransform); }
                }
            }
            // callouts
            foreach (int i in CALLOUT_BTN)
            {
                if (!CALLOUT.TryGetValue(i, out var co)) continue;
                if (!what.TryGetValue(i, out var list) || list.Count == 0) continue;
                var p = BTN[i];
                bool left = co.side == 'L';
                float lx = left ? 150 : 650;
                string name = Controls.ButtonName(i, style) + " ";
                Polyline(draw, new[] { p.x, p.y, left ? Mathf.Min(p.x - 30, 200) : Mathf.Max(p.x + 30, 600), co.y, lx, co.y });
                Label(draw, left ? lx - 6 : lx + 6, co.y + 4, left, name, string.Join(" + ", list), labelSize, Css.text);
            }
            // sticks: labels under the controller
            for (int k = 0; k < 2; k++)
            {
                int i = 10 + k;
                bool left = k == 0;
                var p = BTN[i];
                string text = left ? L : R;
                string click = what.TryGetValue(i, out var cl) ? string.Join(" + ", cl) : "";
                float lx = left ? 385 : 415, ly = 412;
                Polyline(draw, new[] { p.x, p.y + p.r, p.x, ly - 22, left ? lx - 10 : lx + 10, ly - 22 });
                Label(draw, lx, ly - 18, left, (left ? "L" : "R") + " stick ", text, labelSize, Css.text);
                if (click.Length > 0) Label(draw, lx, ly + 2, left, null, "press " + (left ? "L" : "R") + "3: " + click, compact ? 15 : 12, Css.mut);
            }
            return D;
        }

        /// highlightPad(root, pad): light up the pressed buttons, move the sticks (call every frame while visible)
        public void Highlight(PadState pad)
        {
            if (pad == null || Root == null) return;
            for (int i = 0; i <= 16; i++)
            {
                bool on = i < pad.pressed.Length && (pad.pressed[i] || pad.values[i] > 0.4f);
                if (on == lit[i]) continue;
                lit[i] = on;
                fill[i].color = on ? Css.acc : fillN[i];      // .pad-btn-g.on { fill: var(--acc); stroke: #fff }
                stroke[i].color = on ? Color.white : strokeN[i];
            }
            var s0 = new Vector2(pad.axes[0], pad.axes[1]);
            var s1 = new Vector2(pad.axes[2], pad.axes[3]);
            if (s0 != lastStick0) { lastStick0 = s0; knob[0].anchoredPosition = knobBase[0] + new Vector2(s0.x * 9, -s0.y * 9); }
            if (s1 != lastStick1) { lastStick1 = s1; knob[1].anchoredPosition = knobBase[1] + new Vector2(s1.x * 9, -s1.y * 9); }
        }
    }

    public static class ControlsUi
    {
        /// keyboardHTML(device): the keys in use, grouped
        public static void KeyboardGrid(Transform parent, string device)
        {
            var b = Controls.GetBindings(device);
            string K(string id) => Controls.KeyName(b.axes.TryGetValue(id, out var c) ? c : null);
            string A(string id) => Controls.KeyName(b.actions.TryGetValue(id, out var c) ? c : null);
            string Pair(string a, string c) => K(a) + " / " + K(c);
            var grid = Ui.Node("kb-grid", parent);
            var g = grid.gameObject.AddComponent<AutoGrid>();
            g.weights = new[] { 1f, 1f }; g.gap = 14;
            void Row(Transform col, string k, string t)
            {
                var r = Ui.Node("kb-row", col);
                Ui.HBox(r, Ui.Pad(3, 0), 8, TextAnchor.MiddleLeft);
                Ui.Rule(r, Css.White(0.05f), 1, 0).rectTransform.pivot = new Vector2(0.5f, 0);
                var kc = Ui.Node("k", r);
                Ui.HBox(kc, null, 0, TextAnchor.MiddleLeft);
                Ui.LE(kc, 110, -1, 0, -1, 110);
                Ui.Kbd(kc, k);
                var tx = Ui.Label(r, t, 13, Css.text);
                Ui.LE(tx, 0, -1, 1);
            }
            var move = Ui.Node("col", grid); Ui.VBox(move);
            Ui.H4(move, "Move", Css.acc);
            Row(move, Pair("surge+", "surge-"), "forward / back");
            Row(move, Pair("yaw-", "yaw+"), "turn left / right");
            Row(move, Pair("sway-", "sway+"), "strafe left / right");
            Row(move, Pair("heave+", "heave-"), "up / down");
            Row(move, Pair("roll-", "roll+"), "roll");
            Row(move, Pair("pitch+", "pitch-"), "pitch");
            Row(move, Controls.KeyName(b.fine), "hold: fine control");
            var acts = Ui.Node("col", grid); Ui.VBox(acts);
            Ui.H4(acts, "Actions", Css.acc);
            foreach (var a in Controls.Actions) Row(acts, A(a.id), a.label);
        }

        /// quickCardHTML(device, pad): the small card in a player's view at the start of a match.
        /// Returns the controller drawing for pads (to light up its buttons), else null.
        public static PadDrawing QuickCard(Transform card, string device, PadState pad, Color pc)
        {
            var title = new Rich(card, 13, pc, UiFont.Bold, TextAnchor.UpperLeft);
            if (device.StartsWith("pad", StringComparison.Ordinal))
            {
                title.T("Your controller ·").T("Options/Start → Controls to change", Css.mut, UiFont.Regular);
                Ui.Gap(card, 4);
                return PadDrawing.Build(card, device, Controls.PadStyle(pad), true);
            }
            var b = Controls.GetBindings(device);
            string k(string id) => Controls.KeyName(b.axes.TryGetValue(id, out var c) ? c : null);
            string a(string id) => Controls.KeyName(b.actions.TryGetValue(id, out var c) ? c : null);
            title.T("Your keys ·").T(a("pause") + " → Controls to change", Css.mut, UiFont.Regular);
            Ui.Gap(card, 4);
            var keys = Ui.Node("qc-keys", card);
            var f = keys.gameObject.AddComponent<Flow>();
            f.hgap = 14; f.vgap = 6;
            void Span(string kbd, string what)
            {
                var s = Ui.Node("span", keys);
                Ui.HBox(s, null, 4, TextAnchor.MiddleLeft);
                Ui.Kbd(s, kbd);
                Ui.Label(s, what, 13, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
            }
            Span(k("surge+") + k("yaw-") + k("surge-") + k("yaw+"), "drive");
            Span(k("sway-") + " " + k("sway+"), "strafe");
            Span(k("heave+") + " " + k("heave-"), "up / down");
            Span(k("roll-") + " " + k("roll+"), "roll");
            Span(k("pitch+") + " " + k("pitch-"), "pitch");
            Span(a("marker"), "marker");
            Span(a("torpedo"), "torpedo");
            Span(a("grip"), "gripper");
            Span(a("camera"), "camera");
            Span(a("skip"), "skip task");
            Span(a("pause"), "menu");
            return null;
        }
    }
}
