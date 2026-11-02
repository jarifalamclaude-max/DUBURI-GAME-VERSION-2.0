// The text blocks of src/ui/menu.js shared by several screens: rulesHTML(),
// controlsHTML() and resultsHTML(game), built as uGUI under a vertical parent.

using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.UI;
using CoreGame = Duburi.Core.Game;

namespace Duburi.Game
{
    public static class HelpText
    {
        static string P(double v) => Js.Str(v);
        static string Em(string s) => Ui.Col(s, Css.warm); // .rules em { color: #ffd38c; font-style: normal }

        static Text Para(Transform parent, string s, float size, Color c, float top, float bottom)
        {
            var holder = Ui.Node("p", parent);
            Ui.VBox(holder, Ui.Pad((int)top, 0, (int)bottom, 0));
            var t = Ui.Label(holder, s, size, c);
            t.lineSpacing = 1.05f;
            return t;
        }

        /// rulesHTML()
        public static void Rules(Transform parent)
        {
            var root = Ui.Node("rules", parent);
            Ui.VBox(root);
            Para(root, "You are an autonomous underwater vehicle on a real competition course: monitor ocean life, tag animals and clean up trash. Every vehicle must pass the <b>gate</b> before any other task scores. A run is one attempt: <b>end</b> it and keep the points, <b>forfeit</b> it to start again from the dock (the points are lost), or <b>skip</b> a task you can't do (no points for it, you are moved to the next one).", 16, Css.text, 16, 16);
            string[] items =
            {
                $"<b>Heading out (coin flip)</b> — you start on the surface at the dock, parallel to the gate (heads) or facing away (tails). {Em("Dive before you leave the start zone")} — surfacing anywhere except inside the octagon is a breach and ends the run. +{P(POINTS.coinFlip)}",
                $"<b>Collecting data (gate)</b> — 3 m × 1.5 m gate, red divider in the middle, a reef shark on one side and a sawfish on the other. The side you pass under is the animal you {Em("choose")} for the rest of the run. +{P(POINTS.gatePass)} pass, +{P(POINTS.gateClean)} clean (no leg touch), +{P(POINTS.gateAnimal)} side. <b>Style:</b> every 90° of roll or pitch inside the gate zone +{P(POINTS.styleRollPitch)}, yaw +{P(POINTS.styleYaw)} (rolling back doesn't count).",
                $"<b>Navigate the channel (slalom)</b> — 3 sets of white / red / white pipes near the floor. +{P(POINTS.slalomSet)} per set, +{P(POINTS.slalomSide)} for keeping the red pipe on the same side as the gate divider, +{P(POINTS.slalomInside)} for staying between the pipe tops and bottoms. Orange path markers point the way.",
                $"<b>Drop a BRUVS (bin)</b> — drop your 2 markers into the 12 × 24 in bin on the floor. +{P(POINTS.binAny)} each, +{P(POINTS.binCorrect)} in the half with your chosen animal. Use the down camera.",
                $"<b>Tagging (torpedoes)</b> — 2 torpedoes at the 0.6 m board with two 4 in openings. +{P(POINTS.torpedoThrough)} through, +{P(POINTS.torpedoTouch)} touching the board, +{P(POINTS.torpedoAnimal)} for the opening by your animal, +{P(POINTS.torpedoOrder)} for chosen-animal first then the other, +{P(POINTS.torpedoFar)} each from behind the “far” bars (2 m).",
                $"<b>Ocean cleanup (octagon)</b> — follow the 30 kHz pinger down to the table. Grab the cup or the spoon and drop it in its basket (+{P(POINTS.basketAny)}, +{P(POINTS.basketCorrect)} right basket), spin once per item in the baskets (+{P(POINTS.octRotation)} each), surface inside the octagon (+{P(POINTS.octSurface)}), surface holding an item (+{P(POINTS.octWithItem)}), face an animal image (+{P(POINTS.octFaceAny)}, your animal +{P(POINTS.octFaceChosen)}).",
                $"<b>Return home</b> — back through the gate under water: +{P(POINTS.home)}, run complete.",
                $"<b>Time bonus</b> — gate + (a marker in the bin or a torpedo through) + surfaced in the octagon: {P(POINTS.timePerMinute)} points per minute left when the run ends.",
            };
            // <ol> with padding-left 22px; li { margin-bottom: 8px; line-height: 1.45 }
            for (int i = 0; i < items.Length; i++)
            {
                var li = Ui.Node("li", root);
                var h = Ui.HBox(li, Ui.Pad(0, 0, 8, 0), 6, TextAnchor.UpperLeft);
                var num = Ui.Label(li, (i + 1) + ".", 16, Css.text, UiFont.Regular, TextAnchor.UpperRight, false);
                Ui.LE(num, 16, -1, 0, -1, 16);
                var t = Ui.Label(li, items[i], 16, Css.text);
                t.lineSpacing = 1.05f;
                Ui.LE(t, 0, -1, 1);
            }
            Para(root, "Hydrophone ring at the bottom of your screen: the arrows point at the 30 kHz (octagon) and 25 kHz (torpedo) pingers. Physics: the DUBURI simulator's 6-DOF model with T200/T500 thrusters and EOS-80 sea water — salt and cold water make you more buoyant, current and waves push you around.", 13, Css.mut, 16, 16);
        }

        /// one table of [key, what] rows (.controls table)
        static void KeyTable(Transform parent, List<string[]> rows)
        {
            var tbl = Ui.Node("table", parent);
            Ui.VBox(tbl);
            foreach (var r in rows)
            {
                var tr = Ui.Node("tr", tbl);
                Ui.HBox(tr, null, 0, TextAnchor.MiddleLeft).childForceExpandHeight = true;
                Ui.Rule(tr, Css.White(0.06f), 1, 0).rectTransform.pivot = new Vector2(0.5f, 0);
                var k = Ui.Node("td", tr);
                Ui.HBox(k, Ui.Pad(3, 6), 0, TextAnchor.MiddleLeft);
                Ui.LE(k, 0, -1, 0.42f);
                Ui.Kbd(k, r[0]);
                var v = Ui.Node("td", tr);
                Ui.VBox(v, Ui.Pad(3, 6), 0, TextAnchor.MiddleLeft);
                Ui.LE(v, 0, -1, 0.58f);
                Ui.Label(v, r[1], 13, Css.text);
            }
        }

        /// controlsHTML()
        public static void Controls(Transform parent)
        {
            var grid = Ui.Node("controls", parent);
            var g = grid.gameObject.AddComponent<AutoGrid>();
            g.minCell = 260; g.gap = 16;
            void Col(string title, string device)
            {
                var c = Ui.Node("col", grid);
                Ui.VBox(c);
                Ui.H4(c, title);
                KeyTable(c, Controls_HelpRows(device));
            }
            Col("Keyboard — left", "kbA");
            Col("Keyboard — right", "kbB");
            Col("Gamepad 1 (PS / Xbox)", "pad0");
            Para(parent, "Every button and key can be changed under <b>Controls</b> (title screen, lobby or pause menu).", 13, Css.mut, 13, 0);
            Para(parent, "Roll / pitch keys spin the vehicle; let go and the stabiliser levels it again, keeping your heading. Depth hold keeps you at the depth where you let go of up / down.", 13, Css.mut, 13, 13);
        }

        // HELP.kbA / kbB / pad (input.js): helpRows with the PS names for the pad
        static List<string[]> Controls_HelpRows(string device) => Duburi.Game.Controls.HelpRows(device, "ps");

        /// resultsHTML(game): every player's best run, best first
        public static void Results(Transform parent, CoreGame game)
        {
            var ranked = new List<Player>(game.players);
            Js.StableSort(ranked, (a, b) => b.best - a.best);
            bool narrow = UiRoot.ViewportWidth <= 760; // @media (max-width: 760px): smaller, no task columns
            float fs = narrow ? 11 : 14;
            var tblRt = Ui.Node("results", parent);
            var tbl = tblRt.gameObject.AddComponent<Table>();
            tbl.cellPadX = 6;
            tbl.flexCol = 1;
            tbl.padding = Ui.Pad(12, 0, 0, 0);
            var keys = CoreGame.TASK_KEYS;
            int nCols = 3 + keys.Length + 2;
            bool TaskCol(int c) => c >= 3 && c < 3 + keys.Length;

            // header
            var head = Row(tblRt, Css.line, 6);
            for (int c = 0; c < nCols; c++)
            {
                string s = c == 0 ? "" : c == 1 ? "Player" : c == 2 ? "Vehicle" : TaskCol(c) ? CoreGame.TASK_LABEL[keys[c - 3]] : c == nCols - 2 ? "Best run" : "Runs";
                var t = Ui.Label(head, s, 12, Css.mut, UiFont.Medium, c == 1 || c == 2 ? TextAnchor.LowerLeft : TextAnchor.LowerRight);
                if (narrow && TaskCol(c)) t.gameObject.SetActive(false);
            }
            for (int i = 0; i < ranked.Count; i++)
            {
                var p = ranked[i];
                RunResult best = null;
                foreach (var r in p.runs) if (best == null || r.total > best.total) best = r;
                var pc = Colors.Hex(p.color);
                var tr = Row(tblRt, Css.White(0.06f), 8);
                if (i == 0) Ui.Backing(tr, "win", null, new Color(1, 181 / 255f, 71 / 255f, 0.12f)); // tr.win td
                // rank (🏆 for the winner)
                var rank = Ui.Node("td", tr);
                Ui.HBox(rank, null, 0, TextAnchor.MiddleRight);
                if (i == 0) Ui.IconImage(rank, "trophy", fs + 2, Color.white);
                else Ui.Label(rank, (i + 1).ToString(), fs, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
                // player: dot + name (+ "(left)")
                var pl = Ui.Node("td", tr);
                Ui.HBox(pl, null, 6, TextAnchor.MiddleLeft);
                var dot = Ui.Node("dot", pl);
                Ui.Img(dot, Ui.Circle(10), pc);
                Ui.LE(dot, 10, 10, 0, 0, 10, 10);
                Ui.Label(pl, Esc(p.name) + (p.left ? " <size=" + Mathf.RoundToInt(fs * 0.83f) + ">(left)</size>" : ""), fs, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
                Ui.Label(tr, Esc(p.design.name), fs, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
                for (int k = 0; k < keys.Length; k++)
                {
                    var t = Ui.Label(tr, Js.Str(best != null ? best.scores[keys[k]] : 0), fs, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
                    if (narrow) t.gameObject.SetActive(false);
                }
                Ui.Label(tr, Ui.B(Js.ToLocaleString(best != null ? best.total : 0)), fs, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
                Ui.Label(tr, p.runs.Count.ToString(), fs, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
            }
            Para(parent, "Each vehicle's best run counts.", 13, Css.mut, 13, 0);
        }

        static RectTransform Row(Transform table, Color rule, int padY)
        {
            var tr = Ui.Node("tr", table);
            var row = tr.gameObject.AddComponent<TableRow>();
            row.padding = Ui.Pad(padY, 0, padY, 0);
            var r = Ui.Rule(tr, rule, 1, 0);
            r.rectTransform.pivot = new Vector2(0.5f, 0);
            return tr;
        }

        /// names typed by players go into rich text: keep their < > as text
        public static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<​");
    }
}
