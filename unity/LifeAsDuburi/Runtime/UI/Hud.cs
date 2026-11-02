// src/ui/hud.js: per-player heads-up display laid over that player's
// split-screen viewport, plus the shared competition clock (with the
// always-visible Pause / Quit buttons) and the live leaderboard for the TV-camera
// quarter. Refreshed ~20 times a second; texts are only rebuilt when the value
// they show changes (few allocations on WebGL).

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.UI;
using CoreGame = Duburi.Core.Game;

namespace Duburi.Game
{
    public sealed class Hud
    {
        static string FmtTime(double s) => ((long)Math.Floor(s / 60)).ToString() + ":" + Js.PadStart(((int)Math.Floor(s % 60)).ToString(), 2, '0');
        static double Deg(double r) => ((r * 180) / Math.PI + 360) % 360;
        static readonly string[] BoardNames = { "gate", "Gate", "slalom", "Slalom", "bin", "Bin", "torpedo", "Torpedoes", "octagon", "Octagon", "home", "Return home" };
        static string TaskName(string t) { for (int i = 0; i < BoardNames.Length; i += 2) if (BoardNames[i] == t) return BoardNames[i + 1]; return t; }

        sealed class TaskRow { public GameObject go; public Text label, val; public Image dot, ring; public double last = double.NaN; public bool done; }

        sealed class Panel
        {
            public RectTransform el, tl, tr, bl, br, bc, toasts, banner, mmRoot, att, attG, attH, hydro, hydroL, pg0, pg1, card;
            public Player p;
            public Text score, best, depth, hdg, spd, buoy, hold, cam, gp, l0, l1, bannerB, bannerSpan, bannerEm, bannerSmall, chosenText;
            public Image holdBg, holdRule, bannerBorder;
            public RawImage chosenIcon;
            public GameObject[] trRows = new GameObject[4];
            public Image[] mk = new Image[2], tp = new Image[2];
            public LayoutElement trMin, blMin, scoreLE;
            public VerticalLayoutGroup blGroup;
            public readonly Dictionary<string, TaskRow> tasks = new Dictionary<string, TaskRow>();
            public RectTransform[] markers;
            public Image[] markerStroke;
            public bool hidden, small, tiny;
            public float cardUntil;
            public PadDrawing cardPad;
            // last shown values
            public double lastTotal = double.NaN, lastBestKey = double.NaN, lastDepth = double.NaN, lastSpd = double.NaN, lastBuoy = double.NaN;
            public int lastRuns = -1, lastHdg = -1, lastMk = -1, lastTp = -1, lastSlOk = -1, lastL0 = -1, lastL1 = -1;
            public string lastCam, lastGp = "", lastChosen = "?", lastReason, lastState;
            public bool lastHold = true, lastBannerSmall;
            public double lastBannerTotal = double.NaN;
        }

        readonly UiRoot ui;
        readonly CoreGame game;
        readonly string[] cams;
        readonly InputHub input;
        readonly IReadOnlyList<string> devices;
        readonly RectTransform root, top, board, boardIn, boardTable;
        readonly Text clock;
        readonly List<Panel> panels = new List<Panel>();
        readonly Texture2D minimapBg;
        float acc = 1;
        Rect? boardRect;
        int lastClock = -1;
        bool lastLow;
        readonly Player[] sortBuf;
        readonly RectTransform[] boardRows;
        readonly Text[][] boardCells;
        readonly Image[] boardDots;
        readonly int[] boardWho;
        readonly string[] boardState;
        readonly double[] boardScore;

        // ARENA: north to the right: x (north) -> canvas x, y (east) -> canvas y (down)
        static Vector2 MapP(double x, double y)
        {
            var A = Course.ARENA;
            return new Vector2((float)((x + A.length / 2) * (400 / A.length)), (float)((y + A.width / 2) * (220 / A.width)));
        }

        /// new HUD(root, game, { logoUrl, cams, onPause, onQuit }). `cams` is the App's array of
        /// camera modes per player ("chase" | "front" | "down"), read on every refresh.
        /// With `input` + `devices` the start-of-match controller cards light up the pressed buttons.
        public Hud(UiRoot ui, CoreGame game, string[] cams, Action onPause, Action onQuit, InputHub input = null, IReadOnlyList<string> devices = null)
        {
            this.ui = ui; this.game = game; this.cams = cams; this.input = input; this.devices = devices;
            root = Ui.Node("hud", ui.HudLayer);
            Ui.Stretch(root);

            // leaderboard for the TV-camera quarter (3 players)
            board = Ui.Node("lboard", root);
            Ui.PlaceTopLeft(board, 0, 0, 0, 0);
            boardIn = Ui.Box(board, "lb-in", Css.Rgba(3, 16, 24, 0.78f), 12, Css.line);
            Ui.Anchor(boardIn, new Vector2(0, 0), new Vector2(14, 14));
            Ui.VBox(boardIn, Ui.Pad(10, 14), 0);
            Ui.Fit(boardIn);
            Ui.LE(boardIn, -1, -1, -1, -1, 260);
            Ui.H4(boardIn, "LIVE STANDINGS", Css.acc, 0, 6, 15);
            boardTable = Ui.Node("table", boardIn);
            var tbl = boardTable.gameObject.AddComponent<Table>();
            tbl.cellPadX = 6; tbl.flexCol = 1;
            int n = game.players.Count;
            sortBuf = new Player[n];
            boardRows = new RectTransform[n];
            boardCells = new Text[n][];
            boardDots = new Image[n];
            boardWho = new int[n]; boardState = new string[n]; boardScore = new double[n];
            for (int i = 0; i < n; i++) boardWho[i] = -1;
            for (int i = 0; i < n; i++)
            {
                var tr = Ui.Node("tr", boardTable);
                tr.gameObject.AddComponent<TableRow>().padding = Ui.Pad(4, 0, 4, 0);
                Ui.Rule(tr, Css.White(0.06f), 1, 0).rectTransform.pivot = new Vector2(0.5f, 0);
                var cells = new Text[4];
                cells[0] = Ui.Label(tr, "", 13, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
                var who = Ui.Node("who", tr);
                Ui.HBox(who, null, 6, TextAnchor.MiddleLeft);
                var dot = Ui.Node("dot", who);
                boardDots[i] = Ui.Img(dot, Ui.Circle(9), Color.white);
                Ui.LE(dot, 9, 9, 0, 0, 9, 9);
                cells[1] = Ui.Label(who, "", 13, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
                cells[2] = Ui.Label(tr, "", 13, Css.text, UiFont.Regular, TextAnchor.MiddleLeft, false);
                cells[3] = Ui.Label(tr, "", 13, Css.text, UiFont.Mono, TextAnchor.MiddleRight, false);
                boardRows[i] = tr;
                boardCells[i] = cells;
            }
            var lbp = Ui.Label(boardIn, "TV camera follows the leader", 11, Css.mut);
            lbp.transform.SetAsLastSibling();
            board.gameObject.SetActive(false);

            minimapBg = DrawMinimapBg();
            var panelLayer = Ui.Node("panels", root);
            Ui.Stretch(panelLayer);
            for (int i = 0; i < game.players.Count; i++) panels.Add(MakePanel(panelLayer, game.players[i]));

            // the shared clock (z-index 5: above the panels) with the always-visible match buttons
            top = Ui.Box(root, "hud-clock", Css.Rgba(3, 16, 24, 0.7f), 10, Css.line, 1, true);
            Ui.HBox(top, Ui.Pad(4, 14, 4, 6), 10, TextAnchor.MiddleLeft);
            Ui.Fit(top);
            if (Art.Logo != null) Ui.Logo(top, 30, 4);
            var cbox = Ui.Node("c", top);
            Ui.VBox(cbox, null, 0);
            clock = Ui.Label(cbox, "15:00", 24, Css.text, UiFont.Mono, TextAnchor.MiddleLeft, false, "clock");
            Ui.LE(clock, -1, 25);
            var sc = Scenarios.Get(game.scenario);
            Ui.Label(cbox, sc.name + " · " + Js.Str(game.depth) + " m deep", 11, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false);
            // always-visible match buttons (mouse); pads and keys use the pause menu
            var bar = Ui.Node("hud-btns", top);
            Ui.VBox(bar, Ui.Pad(0, 0, 0, 6), 4, TextAnchor.MiddleLeft);
            Ui.HudButton(bar, "⏸ Pause", false, () => onPause?.Invoke());
            Ui.HudButton(bar, "✕ Quit match", true, () => onQuit?.Invoke());
            Ui.Anchor(top, new Vector2(0.5f, 1), new Vector2(0, -8));
        }

        // ---------------------------------------------------------------- build
        static RectTransform HudBox(Transform parent, string name, Vector2 corner, Vector2 offset)
        {
            var rt = Ui.Box(parent, name, Css.hud, 8, Color.clear);
            Ui.Anchor(rt, corner, offset);
            return rt;
        }

        static Text Row(Transform parent, string label, out GameObject row)
        {
            var r = Ui.Node("row", parent);
            Ui.HBox(r, null, 10, TextAnchor.MiddleLeft);
            Ui.Label(r, label, 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false);
            Ui.Spacer(r);
            row = r.gameObject;
            return Ui.Label(r, "", 12, Css.text, UiFont.MonoMedium, TextAnchor.MiddleRight, false);
        }

        static Text Tag(Transform parent, Color bg, Color fg, string s, out Image bgImg)
        {
            var rt = Ui.Node("em", parent);
            bgImg = Ui.Img(rt, Ui.Rounded(4), bg);
            Ui.HBox(rt, Ui.Pad(1, 5), 0, TextAnchor.MiddleCenter);
            return Ui.Label(rt, s, 10, fg, UiFont.Regular, TextAnchor.MiddleCenter, false);
        }

        Panel MakePanel(Transform parent, Player p)
        {
            var P = new Panel { p = p };
            var pc = Colors.Hex(p.color);
            var el = Ui.Node("pp", parent);
            Ui.PlaceTopLeft(el, 0, 0, 0, 0);
            el.gameObject.AddComponent<RectMask2D>(); // .pp { overflow: hidden }
            P.el = el;

            // top-left: name, vehicle, score
            var tl = HudBox(el, "pp-tl", new Vector2(0, 1), new Vector2(10, -10));
            Ui.Backing(tl, "pc", Ui.Rounded(8, 0, 2, 4), pc).transform.SetSiblingIndex(1); // border-left: 4px solid var(--pc)
            Ui.HBox(tl, Ui.Pad(6, 12, 6, 12), 8, TextAnchor.MiddleLeft);
            Ui.Fit(tl);
            var dot = Ui.Node("dot", tl);
            Ui.Img(dot, Ui.Circle(12), pc);
            Ui.LE(dot, 12, 12, 0, 0, 12, 12);
            var nmBox = Ui.Node("n", tl);
            Ui.VBox(nmBox);
            Ui.Label(nmBox, HelpText.Esc(p.name), 16, Css.text, UiFont.Bold, TextAnchor.MiddleLeft, false, "nm");
            Ui.Label(nmBox, HelpText.Esc(p.design.name), 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false, "bot");
            Ui.Gap(tl, 0).GetComponent<LayoutElement>().preferredWidth = 6; // .sc { margin-left: 14px }
            var scBox = Ui.Node("sc", tl);
            Ui.VBox(scBox, null, 0, TextAnchor.MiddleRight);
            P.score = Ui.Label(scBox, "0", 26, pc, UiFont.Mono, TextAnchor.MiddleRight, false, "score");
            P.scoreLE = Ui.LE(P.score, -1, 27);
            P.best = Ui.Label(scBox, "", 11, Css.mut, UiFont.Regular, TextAnchor.MiddleRight, false, "best");
            P.tl = tl;

            // top-right: depth, heading, speed, buoyancy, tags, attitude
            var tr = HudBox(el, "pp-tr", new Vector2(1, 1), new Vector2(-10, -10));
            Ui.VBox(tr, Ui.Pad(6, 10), 0);
            Ui.Fit(tr);
            P.trMin = Ui.LE(tr, -1, -1, -1, -1, 150);
            P.depth = Row(tr, "Depth", out P.trRows[0]);
            P.hdg = Row(tr, "Heading", out P.trRows[1]);
            P.spd = Row(tr, "Speed", out P.trRows[2]);
            P.buoy = Row(tr, "Net buoy.", out P.trRows[3]);
            var tags = Ui.Node("tags", tr);
            Ui.HBox(tags, Ui.Pad(4, 0, 0, 0), 4, TextAnchor.MiddleLeft);
            P.hold = Tag(tags, Css.Rgba(89, 217, 142, 0.25f), Css.good, "DEPTH HOLD", out P.holdBg);
            P.holdRule = Ui.Rule(P.hold.transform, Css.mut, 1, 0.5f);
            P.holdRule.enabled = false;
            P.cam = Tag(tags, Css.Rgba(58, 160, 224, 0.25f), Colors.Hex("#9fd6ff"), "CHASE", out _);
            // .att: artificial horizon (svg viewBox -30 -30 60 60 drawn at 64 px)
            var attHolder = Ui.Node("att-holder", tr);
            Ui.VBox(attHolder, Ui.Pad(6, 0, 0, 0), 0, TextAnchor.UpperCenter, false);
            var attBox = Ui.Node("att-box", attHolder);
            Ui.LE(attBox, 64, 64, 0, 0, 64, 64);
            P.att = Ui.Node("att", attBox);
            P.att.anchorMin = P.att.anchorMax = new Vector2(0.5f, 0.5f);
            P.att.sizeDelta = new Vector2(60, 60);
            P.att.localScale = Vector3.one * (64f / 60f);
            var sky = Ui.Node("sky", P.att);
            sky.sizeDelta = new Vector2(57.5f, 57.5f);
            Ui.Img(sky, Ui.Circle(57.5f), Colors.Hex("#2f7fb8"));
            sky.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            P.attG = Ui.Node("att-g", sky);
            P.attG.sizeDelta = Vector2.zero;
            P.attH = Ui.Node("att-h", P.attG);
            P.attH.sizeDelta = Vector2.zero;
            var ground = Ui.Node("ground", P.attH);
            ground.anchoredPosition = new Vector2(0, -20); ground.sizeDelta = new Vector2(80, 40);
            Ui.Img(ground, null, Colors.Hex("#7a5530"));
            var horizon = Ui.Node("line", P.attH);
            horizon.sizeDelta = new Vector2(54, 1.5f);
            Ui.Img(horizon, null, Color.white);
            var ring = Ui.Node("ring", P.att);
            ring.sizeDelta = new Vector2(55.5f, 55.5f);
            Ui.Img(ring, Ui.Ring(55.5f, 1.5f), Css.White(0.6f));
            var ac = Ui.Node("att-ac", P.att);
            ac.pivot = new Vector2(0.5f, 1 - 4f / 12f);
            ac.sizeDelta = new Vector2(32, 12);
            ac.anchoredPosition = Vector2.zero;
            Ui.Img(ac, Ui.PolySprite("att-ac", 128, 48, 4, new Vector2(16, 4), new float[] { -14, 0, -5, 0, 0, 5, 5, 0, 14, 0 }, false, false, 2.5f, Color.white), Css.acc);
            P.tr = tr;

            // bottom-left: chosen animal + task list
            var bl = HudBox(el, "pp-bl", new Vector2(0, 0), new Vector2(10, 10));
            P.blGroup = Ui.VBox(bl, Ui.Pad(6, 10), 0);
            Ui.Fit(bl);
            P.blMin = Ui.LE(bl, -1, -1, -1, -1, 190);
            var chosen = Ui.Node("chosen", bl);
            Ui.HBox(chosen, Ui.Pad(0, 0, 4, 0), 6, TextAnchor.MiddleLeft);
            P.chosenText = Ui.Label(chosen, "Pass the gate first", 12, Css.warm, UiFont.Regular, TextAnchor.MiddleLeft, false);
            var icon = Ui.Node("img", chosen);
            P.chosenIcon = Ui.Raw(icon, null);
            P.chosenIcon.gameObject.SetActive(false);
            foreach (var k in CoreGame.TASK_KEYS)
            {
                var r = Ui.Node("task", bl);
                Ui.HBox(r, null, 6, TextAnchor.MiddleLeft);
                Ui.LE(r, -1, 18.6f); // line-height 1.55 at 12px
                var dcell = Ui.Node("i", r);
                Ui.LE(dcell, 12, 8, 0, 0, 12, 8);
                var d = Ui.Node("dot", dcell);
                d.anchorMin = d.anchorMax = new Vector2(0, 0.5f); d.pivot = new Vector2(0, 0.5f); d.sizeDelta = new Vector2(8, 8);
                var dotFill = Ui.Img(d, Ui.Circle(8), Css.good);
                dotFill.enabled = false;
                var ringRt = Ui.Node("ring", d); Ui.Stretch(ringRt);
                var dotRing = Ui.Img(ringRt, Ui.Ring(8, 1), Css.mut);
                var label = Ui.Label(r, CoreGame.TASK_LABEL[k], 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false);
                Ui.LE(label, -1, -1, 1);
                var val = Ui.Label(r, "0", 12, Css.mut, UiFont.Mono, TextAnchor.MiddleRight, false);
                P.tasks[k] = new TaskRow { go = r.gameObject, label = label, val = val, dot = dotFill, ring = dotRing };
            }
            P.bl = bl;

            // bottom-right: minimap + payload
            var br = Ui.Node("pp-br", el);
            Ui.Anchor(br, new Vector2(1, 0), new Vector2(-10, 10));
            Ui.VBox(br, null, 6, TextAnchor.LowerRight, false);
            Ui.Fit(br);
            var mmBox = Ui.Node("mm-box", br);
            Ui.LE(mmBox, 200, 110, 0, 0, 200, 110);
            P.mmRoot = Ui.Node("mm", mmBox);
            P.mmRoot.anchorMin = P.mmRoot.anchorMax = P.mmRoot.pivot = new Vector2(1, 0);
            P.mmRoot.sizeDelta = new Vector2(200, 110);
            Ui.Raw(P.mmRoot, minimapBg);
            var north = Ui.Label(P.mmRoot, "N →", 8, Css.White(0.6f), UiFont.Regular, TextAnchor.LowerLeft, false);
            Ui.PlaceTopLeft(north.rectTransform, 178, 2, 30, 10);
            int n = game.players.Count;
            P.markers = new RectTransform[n];
            P.markerStroke = new Image[n];
            for (int i = 0; i < n; i++)
            {
                var other = game.players[i];
                bool me = other == p;
                var m = Ui.Node("vehicle", P.mmRoot);
                m.anchorMin = m.anchorMax = new Vector2(0, 1);
                m.sizeDelta = new Vector2(64f / 3, 64f / 3);
                var tri = new float[] { 8, 0, -5, 5, -5, -5 };
                var o = new Vector2(32f / 3, 32f / 3);
                Ui.Img(m, Ui.PolySprite("mm-fill", 64, 64, 3, o, tri, true, true, 0, Color.white), Colors.Hex(other.color));
                var s = Ui.Node("stroke", m); Ui.Stretch(s);
                P.markerStroke[i] = Ui.Img(s, me ? Ui.PolySprite("mm-stroke2", 64, 64, 3, o, tri, false, true, 2, Color.white) : Ui.PolySprite("mm-stroke1", 64, 64, 3, o, tri, false, true, 1, Color.white),
                    me ? Color.white : new Color(0, 0, 0, 0.6f));
                if (me) m.SetAsLastSibling();
                P.markers[i] = m;
            }
            var pay = Ui.Box(br, "pay", Css.hud, 8, Color.clear);
            Ui.VBox(pay, Ui.Pad(6, 10), 0);
            Ui.LE(pay, -1, -1, -1, -1, 170);
            Text PayRow(string label, Image[] pips)
            {
                var r = Ui.Node("span", pay);
                Ui.HBox(r, null, 10, TextAnchor.MiddleLeft);
                Ui.Label(r, label, 12, Css.mut, UiFont.Regular, TextAnchor.MiddleLeft, false);
                Ui.Spacer(r);
                if (pips != null)
                {
                    var b = Ui.Node("b", r);
                    Ui.HBox(b, null, 1, TextAnchor.MiddleRight);
                    for (int k = 0; k < 2; k++) pips[k] = Ui.IconImage(b, "pipOn", 12, pc);
                    return null;
                }
                return Ui.Label(r, "open", 12, pc, UiFont.Mono, TextAnchor.MiddleRight, false);
            }
            PayRow("Markers", P.mk);
            PayRow("Torpedoes", P.tp);
            P.gp = PayRow("Gripper", null);
            P.br = br;

            // bottom-centre: hydrophone ring
            var bc = Ui.Node("pp-bc", el);
            Ui.Anchor(bc, new Vector2(0.5f, 0), new Vector2(0, 8));
            Ui.VBox(bc, null, 2, TextAnchor.LowerCenter, false);
            Ui.Fit(bc);
            var hBox = Ui.Node("hydro-box", bc);
            Ui.LE(hBox, 100, 100, 0, 0, 100, 100);
            P.hydro = Ui.Node("hydro", hBox); // viewBox -50 -50 100 100
            P.hydro.anchorMin = P.hydro.anchorMax = P.hydro.pivot = new Vector2(0.5f, 0);
            P.hydro.sizeDelta = new Vector2(100, 100);
            var hr = Ui.Node("ring", P.hydro); hr.sizeDelta = new Vector2(88, 88);
            Ui.Img(hr, Ui.Circle(88), Css.Rgba(3, 16, 24, 0.55f));
            var hs = Ui.Node("stroke", P.hydro); hs.sizeDelta = new Vector2(89, 89);
            Ui.Img(hs, Ui.Ring(89, 1), Css.White(0.35f));
            var fwd = Ui.Label(P.hydro, "FWD", 9, Css.mut, UiFont.Regular, TextAnchor.MiddleCenter, false);
            fwd.rectTransform.sizeDelta = new Vector2(40, 12);
            fwd.rectTransform.anchoredPosition = new Vector2(0, 37);
            RectTransform Arrow(string name, Color c)
            {
                var g = Ui.Node(name, P.hydro); g.sizeDelta = Vector2.zero;
                var a = Ui.Node("path", g); a.sizeDelta = new Vector2(12, 12); a.anchoredPosition = new Vector2(0, 34);
                Ui.Img(a, Ui.PolySprite("hydro-arrow", 48, 48, 4, new Vector2(6, 40), new float[] { 0, -40, 6, -28, -6, -28 }, true, true, 0, Color.white), c);
                return g;
            }
            P.pg0 = Arrow("pg0", Colors.Hex("#ff8a1f"));
            P.pg1 = Arrow("pg1", Colors.Hex("#4fd1c5"));
            var me2 = Ui.Node("me", P.hydro); me2.sizeDelta = new Vector2(6, 6);
            Ui.Img(me2, Ui.Circle(6), pc);
            P.hydroL = Ui.Box(bc, "hydro-l", Css.Rgba(3, 16, 24, 0.55f), 6, Color.clear);
            Ui.VBox(P.hydroL, Ui.Pad(2, 8), 0, TextAnchor.MiddleCenter, false);
            P.l0 = Ui.Label(P.hydroL, "", 11, Colors.Hex("#ff9c45"), UiFont.Regular, TextAnchor.MiddleCenter, false);
            P.l1 = Ui.Label(P.hydroL, "", 11, Colors.Hex("#6fe0d5"), UiFont.Regular, TextAnchor.MiddleCenter, false);
            P.bc = bc;

            // toasts (newest on top)
            P.toasts = Ui.Node("pp-toasts", el);
            P.toasts.anchorMin = new Vector2(0.05f, 1); P.toasts.anchorMax = new Vector2(0.95f, 1); P.toasts.pivot = new Vector2(0.5f, 1);
            P.toasts.anchoredPosition = new Vector2(0, -70);
            Ui.VBox(P.toasts, null, 4, TextAnchor.UpperCenter, false);
            Ui.Fit(P.toasts, false, true);

            // run-ended banner
            var banner = Ui.Box(el, "pp-banner", Css.Rgba(3, 16, 24, 0.85f), 12, Color.clear);
            P.bannerBorder = Ui.Backing(banner, "border", Ui.Rounded(12, 2), pc);
            P.bannerBorder.transform.SetSiblingIndex(0);
            Ui.Anchor(banner, new Vector2(0.5f, 0.5f), Vector2.zero);
            Ui.VBox(banner, Ui.Pad(14, 24), 4, TextAnchor.MiddleCenter, false);
            Ui.Fit(banner);
            P.bannerB = Ui.Label(banner, "", 26, Css.text, UiFont.Bold, TextAnchor.MiddleCenter, false);
            P.bannerSpan = Ui.Label(banner, "", 14, Css.text, UiFont.Regular, TextAnchor.MiddleCenter, false);
            P.bannerEm = Ui.Label(banner, "", 22, pc, UiFont.Mono, TextAnchor.MiddleCenter, false);
            P.bannerSmall = Ui.Label(banner, "", 12, Css.mut, UiFont.Regular, TextAnchor.MiddleCenter, false);
            banner.gameObject.SetActive(false);
            P.banner = banner;
            return P;
        }

        // ---------------------------------------------------------------- layout
        /// layout(rects): each player's view rect in SCREEN PIXELS with a top-left origin
        /// (w = 0 for a player without a view)
        public void Layout(IList<Rect> rects)
        {
            int views = 0;
            for (int i = 0; i < panels.Count; i++)
            {
                var P = panels[i];
                var r = i < rects.Count ? ui.ToCanvas(rects[i]) : new Rect(0, 0, 0, 0);
                Ui.PlaceTopLeft(P.el, r.x, r.y, r.width, r.height);
                P.small = r.width < 700 || r.height < 420;
                P.tiny = r.width < 480 || r.height < 300;
                if (r.width > 0) views++;
                ApplyClasses(P);
            }
            // split screen: the shared clock sits where the viewports meet
            Ui.Anchor(top, views > 1 ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 1), views > 1 ? Vector2.zero : new Vector2(0, -8));
            if (boardRect.HasValue) SetBoardPx(boardRect.Value);
        }

        /// screen resized without new rects: re-apply the @media rules
        public void Refresh() { foreach (var P in panels) ApplyClasses(P); }

        void ApplyClasses(Panel P)
        {
            bool narrow = UiRoot.ViewportWidth <= 640; // @media (max-width: 640px)
            float mm = P.small ? 0.75f : 1f;            // .pp.small .mm { 150 x 82 }
            P.mmRoot.localScale = new Vector3(mm, mm, 1);
            Ui.LE(P.mmRoot.parent.GetComponent<RectTransform>(), 200 * mm, 110 * mm, 0, 0, 200 * mm, 110 * mm);
            P.mmRoot.parent.gameObject.SetActive(!P.tiny);
            P.score.fontSize = P.small ? 20 : 26;
            P.scoreLE.preferredHeight = P.small ? 21 : 27;
            float hy = P.small ? 0.76f : 1f;          // .pp.small .hydro { 76 x 76 }
            P.hydro.localScale = new Vector3(hy, hy, 1);
            Ui.LE(P.hydro.parent.GetComponent<RectTransform>(), 100 * hy, 100 * hy, 0, 0, 100 * hy, 100 * hy);
            P.hydroL.gameObject.SetActive(!P.tiny);
            P.trMin.minWidth = P.small ? 120 : 150;
            P.trRows[2].SetActive(!(P.small || narrow));
            P.trRows[3].SetActive(!(P.small || narrow));
            float att = P.small ? 48 : 64;
            P.att.localScale = Vector3.one * (att / 60f);
            Ui.LE(P.att.parent.GetComponent<RectTransform>(), att, att, 0, 0, att, att);
            P.att.parent.parent.gameObject.SetActive(!P.tiny);
            P.blGroup.padding = P.small ? Ui.Pad(4, 8) : Ui.Pad(6, 10);
            P.blMin.minWidth = P.small ? 0 : 190;
            P.bl.gameObject.SetActive(!P.tiny && !narrow);
            foreach (var t in P.tasks.Values) t.go.SetActive(!P.small || t.done);
            P.bannerSpan.fontSize = P.small ? 12 : 14;
            if (P.card != null)
            {
                P.card.gameObject.SetActive(!P.tiny);
                SizeCard(P);
            }
        }

        // ---------------------------------------------------------------- toasts
        public void Toast(int i, string text, double pts, string kind)
        {
            if (i < 0 || i >= panels.Count) return;
            var P = panels[i];
            var t = Ui.Box(P.toasts, "toast", Css.Rgba(3, 16, 24, 0.8f), 8, Color.clear);
            Ui.HBox(t, Ui.Pad(5, 12), 8, TextAnchor.MiddleCenter);
            if (pts != 0) Ui.Label(t, "+" + Js.Str(pts), 18, Css.good, UiFont.Mono, TextAnchor.MiddleLeft, false);
            Color c = kind == "warn" ? Css.warm : kind == "bad" ? Css.bad : Css.text;
            var span = Ui.Label(t, text, 14, c, kind == "bad" ? UiFont.Bold : UiFont.Regular, TextAnchor.MiddleLeft, true);
            span.supportRichText = false;
            t.SetAsFirstSibling();
            while (P.toasts.childCount > 4)
            {
                var last = P.toasts.GetChild(P.toasts.childCount - 1).gameObject;
                last.SetActive(false);
                UnityEngine.Object.Destroy(last);
            }
            var a = Anim.On(t, false);
            a.popTime = 0.25f;
            a.fadeAt = 3.2f; a.fadeDur = 0.5f;
            a.destroyAt = 3.8f;
        }

        // ---------------------------------------------------------------- per frame
        public void Update(float dt)
        {
            var g = game;
            double rem = g.Remaining();
            int secs = (int)Math.Floor(rem);
            if (secs != lastClock) { lastClock = secs; clock.text = FmtTime(rem); }
            bool low = rem < 60;
            if (low != lastLow) { lastLow = low; clock.color = low ? Css.bad : Css.text; }
            acc += dt;
            bool full = acc > 1f / 20;
            if (full) acc = 0;
            if (full && boardRect.HasValue) DrawBoard();
            for (int i = 0; i < panels.Count; i++) { var P = panels[i]; if (P.card != null && g.time > P.cardUntil) HideCard(i); }
            // the start-of-match controller cards light up the pressed buttons (main.js frame())
            if (input != null && devices != null)
                for (int i = 0; i < panels.Count && i < devices.Count; i++)
                {
                    var P = panels[i];
                    if (P.cardPad != null && devices[i] != null && devices[i].StartsWith("pad", StringComparison.Ordinal)) P.cardPad.Highlight(input.Pad(devices[i]));
                }
            if (!full) return;
            for (int i = 0; i < panels.Count; i++)
            {
                var P = panels[i];
                if (P.hidden) continue;
                UpdatePanel(P, i);
            }
        }

        void UpdatePanel(Panel P, int i)
        {
            var g = game;
            var p = P.p; var v = p.veh; var r = p.run;
            var e = v.Euler();
            double roll = e.x, pitch = e.y, yaw = e.z;
            P.attG.localRotation = Quaternion.Euler(0, 0, (float)(roll * 180 / Math.PI));
            P.attH.anchoredPosition = new Vector2(0, (float)(-pitch * 40));
            for (int k = 0; k < 2; k++)
            {
                var pg = g.course.pingers[k];
                var b = g.GetPingerBearing(p, pg.id);
                (k == 0 ? P.pg0 : P.pg1).localRotation = Quaternion.Euler(0, 0, (float)(-b.rel * 180 / Math.PI));
                int key = (int)Math.Round(b.dist * 10);
                if (key != (k == 0 ? P.lastL0 : P.lastL1))
                {
                    if (k == 0) P.lastL0 = key; else P.lastL1 = key;
                    (k == 0 ? P.l0 : P.l1).text = Js.Str(pg.freq) + " kHz " + (pg.id == "octagon" ? "Octagon" : "Torpedo") + " · " + Js.ToFixed(b.dist, 1) + " m";
                }
            }
            DrawMinimap(P);
            double total = g.Total(p);
            if (total != P.lastTotal) { P.lastTotal = total; P.score.text = Js.ToLocaleString(total); }
            double bestKey = p.runs.Count > 0 ? Math.Max(p.best, total) : -1;
            if (bestKey != P.lastBestKey || p.runs.Count != P.lastRuns)
            {
                P.lastBestKey = bestKey; P.lastRuns = p.runs.Count;
                P.best.text = p.runs.Count > 0 ? "best " + Js.ToLocaleString(Math.Max(p.best, total)) : "run " + (p.runs.Count + 1);
            }
            double depth = Math.Max(0, v.depth);
            double dk = Math.Round(depth * 100);
            if (dk != P.lastDepth) { P.lastDepth = dk; P.depth.text = Js.ToFixed(depth, 2) + " m"; }
            int hdg = (int)Math.Floor(Deg(yaw) + 0.5) % 360; // Math.round
            if (hdg != P.lastHdg) { P.lastHdg = hdg; P.hdg.text = Js.PadStart(hdg.ToString(), 3, '0') + "°"; }
            double spd = v.Speed();
            double sk = Math.Round(spd * 100);
            if (sk != P.lastSpd) { P.lastSpd = sk; P.spd.text = Js.ToFixed(spd, 2) + " m/s"; }
            double net = v.diag.net;
            double shown = v.diag.submerged > 0.98 ? net : (v.volume * v.diag.rho * g.env.g - v.mass * g.env.g);
            double bk = Math.Round(shown * 10) * 2 + (net >= 0 ? 1 : 0);
            if (bk != P.lastBuoy) { P.lastBuoy = bk; P.buoy.text = (net >= 0 ? "+" : "") + Js.ToFixed(shown, 1) + " N"; }
            if (v.depthHold != P.lastHold)
            {
                // .pp-tr em.off: grey, struck through
                P.lastHold = v.depthHold;
                P.holdBg.color = v.depthHold ? Css.Rgba(89, 217, 142, 0.25f) : Css.White(0.08f);
                P.hold.color = v.depthHold ? Css.good : Css.mut;
                P.holdRule.enabled = !v.depthHold;
            }
            string cam = i < cams.Length && cams[i] != null ? cams[i] : "chase";
            if (!ReferenceEquals(cam, P.lastCam)) { P.lastCam = cam; P.cam.text = cam.ToUpperInvariant(); }
            if (r.markers != P.lastMk) { P.lastMk = r.markers; Pips(P.mk, r.markers); }
            if (r.torpedoes != P.lastTp) { P.lastTp = r.torpedoes; Pips(P.tp, r.torpedoes); }
            string gp = r.held != null ? r.held.kind : null;
            if (gp != P.lastGp) { P.lastGp = gp; P.gp.text = gp != null ? "holding " + gp : "open"; }
            foreach (var k in CoreGame.TASK_KEYS)
            {
                double val = r.scores[k];
                var row = P.tasks[k];
                if (row.last != val)
                {
                    row.val.text = Js.Str(val);
                    row.done = val > 0;
                    // .task.done: text colour, green dot and value
                    row.label.color = row.done ? Css.text : Css.mut;
                    row.val.color = row.done ? Css.good : Css.mut;
                    row.dot.enabled = row.done;
                    row.ring.color = row.done ? Css.good : Css.mut;
                    row.go.SetActive(!P.small || row.done);
                    row.last = val;
                }
            }
            int slOk = 0;
            for (int k = 0; k < r.slalomBest.Length; k++) if (r.slalomBest[k] > 0) slOk++;
            if (slOk != P.lastSlOk) { P.lastSlOk = slOk; P.tasks["slalom"].label.text = "Slalom " + slOk + "/3"; }
            if (r.chosen != P.lastChosen)
            {
                P.lastChosen = r.chosen;
                if (r.chosen != null)
                {
                    P.chosenText.text = "Chosen:";
                    P.chosenIcon.gameObject.SetActive(true);
                    var tex = Art.AnimalIcon(r.chosen);
                    P.chosenIcon.texture = tex;
                    float aspect = tex != null && tex.height > 0 ? tex.width / (float)tex.height : 1;
                    Ui.LE(P.chosenIcon, 20 * aspect, 20, 0, 0, 20 * aspect, 20);
                    var name = P.chosenIcon.transform.parent.Find("name") as RectTransform;
                    if (name == null) name = Ui.Label(P.chosenIcon.transform.parent, "", 12, Css.warm, UiFont.Regular, TextAnchor.MiddleLeft, false, "name").rectTransform;
                    name.gameObject.SetActive(true);
                    name.GetComponent<Text>().text = r.chosen == "shark" ? "Reef shark" : "Sawfish";
                }
                else
                {
                    P.chosenText.text = "Pass the gate first";
                    P.chosenIcon.gameObject.SetActive(false);
                    var name = P.chosenIcon.transform.parent.Find("name");
                    if (name != null) name.gameObject.SetActive(false);
                }
            }
            if (r.state != "running")
            {
                bool small = g.over || p.left;
                if (!P.banner.gameObject.activeSelf || r.state != P.lastState || r.reason != P.lastReason || total != P.lastBannerTotal || small != P.lastBannerSmall)
                {
                    P.banner.gameObject.SetActive(true);
                    P.lastState = r.state; P.lastReason = r.reason; P.lastBannerTotal = total; P.lastBannerSmall = small;
                    bool bad = r.reason.StartsWith("Breach", StringComparison.Ordinal);
                    P.bannerB.text = bad ? "BREACH" : r.finished ? "RUN COMPLETE" : "RUN ENDED";
                    P.bannerB.color = bad ? Css.bad : Css.text;
                    P.bannerSpan.text = HelpText.Esc(r.reason);
                    P.bannerEm.text = Js.ToLocaleString(total) + " pts";
                    P.bannerSmall.text = small ? "" : "Forfeit for a new run: 4 · 0 · R3 · or your pause menu";
                    P.bannerSmall.gameObject.SetActive(!small);
                }
            }
            else if (P.banner.gameObject.activeSelf) { P.banner.gameObject.SetActive(false); P.lastState = r.state; }
        }

        static void Pips(Image[] pips, int n)
        {
            // '▮'.repeat(n) + '▯'.repeat(2 - n)
            for (int k = 0; k < pips.Length; k++) pips[k].sprite = Ui.Icon(k < n ? "pipOn" : "pipOff");
        }

        // ---------------------------------------------------------------- minimap
        Texture2D DrawMinimapBg()
        {
            var co = game.course;
            var A = Course.ARENA;
            float sx = 400f / (float)A.length;
            var R = new Raster(400, 220, new Color(1, 1, 1, 0));
            var pts = new List<Vector2>();
            R.FillRoundRect(0, 0, 400, 220, 0, Css.Rgba(8, 30, 44, 0.75f));
            pts.Clear(); pts.Add(new Vector2(1, 1)); pts.Add(new Vector2(399, 1)); pts.Add(new Vector2(399, 219)); pts.Add(new Vector2(1, 219));
            R.Stroke(pts, true, 2, Css.White(0.35f));
            void Ring(Vector2 c, float rad, float w, Color col) { Raster.Circle(pts, c.x, c.y, rad); R.Stroke(pts, true, w, col); }
            void Seg(Vector2 a, Vector2 b, float w, Color col) { pts.Clear(); pts.Add(a); pts.Add(b); R.Stroke(pts, false, w, col); }
            Ring(MapP(co.dock.pos.x, co.dock.pos.y), (float)co.dock.radius * sx, 2, Color.white);
            // gate
            var G = co.gate;
            Seg(MapP(G.pos.x - G.right.x * 1.5, G.pos.y - G.right.y * 1.5), MapP(G.pos.x + G.right.x * 1.5, G.pos.y + G.right.y * 1.5), 4, Colors.Hex("#e33"));
            foreach (var pth in co.paths)
                Seg(MapP(pth.pos.x, pth.pos.y), MapP(pth.pos.x + Math.Cos(pth.heading) * 1.4, pth.pos.y + Math.Sin(pth.heading) * 1.4), 3, Colors.Hex("#ff7a00"));
            foreach (var s in co.slalom.sets)
                foreach (var pp in s.pipes) { var q = MapP(pp.p.x, pp.p.y); R.FillCircle(q.x, q.y, 3, pp.color == "red" ? Colors.Hex("#e33") : Color.white); }
            var bq = MapP(co.bin.pos.x, co.bin.pos.y); R.FillCircle(bq.x, bq.y, 6, Colors.Hex("#ddd"));
            var dq = MapP(co.board.pos.x, co.board.pos.y); R.FillCircle(dq.x, dq.y, 6, Colors.Hex("#4fd1c5"));
            Ring(MapP(co.octagon.pos.x, co.octagon.pos.y), (float)co.octagon.radius * sx, 3, Colors.Hex("#ff8a1f"));
            R.MaskRoundRect(12); // .mm { border-radius: 6px } at 2x
            return R.ToTexture("minimap");
        }

        void DrawMinimap(Panel P)
        {
            const float k = 200f / 400f;
            var players = game.players;
            for (int i = 0; i < players.Count; i++)
            {
                var o = players[i];
                var m = P.markers[i];
                var q = MapP(o.veh.p.x, o.veh.p.y);
                m.anchoredPosition = new Vector2(q.x * k, -q.y * k);
                m.localRotation = Quaternion.Euler(0, 0, (float)(-o.veh.Euler().z * 180 / Math.PI));
            }
        }

        // ---------------------------------------------------------------- start-of-match card
        /// showCard(i, quickCardHTML(device, pad), sec): the player's controls card, hidden after `sec` s of game time
        public void ShowCard(int i, string device, PadState pad, float sec = 12)
        {
            if (i < 0 || i >= panels.Count) return;
            var P = panels[i];
            HideCard(i);
            var pc = Colors.Hex(P.p.color);
            var c = Ui.Box(P.el, "qcard", Css.Rgba(3, 16, 24, 0.8f), 12, pc);
            c.anchorMin = c.anchorMax = new Vector2(0.5f, 0.5f);
            c.pivot = new Vector2(0.5f, 0.62f); // transform: translate(-50%, -38%)
            c.anchoredPosition = Vector2.zero;
            Ui.VBox(c, Ui.Pad(8, 12, 6, 12), 0);
            Ui.Fit(c, false, true);
            Ui.Group(c, 1, false); // pointer-events: none
            P.cardPad = ControlsUi.QuickCard(c, device, pad, pc);
            P.card = c;
            P.cardUntil = (float)game.time + sec; // game time, so a slow first load doesn't eat it
            SizeCard(P);
            c.gameObject.SetActive(!P.tiny);
        }

        void SizeCard(Panel P)
        {
            // width: min(560px, 86%); .pp.small: 92%, padding 4px 8px
            float w = P.el.rect.width;
            P.card.sizeDelta = new Vector2(Mathf.Min(560, w * (P.small ? 0.92f : 0.86f)), P.card.sizeDelta.y);
            P.card.GetComponent<VerticalLayoutGroup>().padding = P.small ? Ui.Pad(4, 8) : Ui.Pad(8, 12, 6, 12);
        }

        public void HideCard(int i)
        {
            if (i < 0 || i >= panels.Count) return;
            var P = panels[i];
            if (P.card == null) return;
            var c = P.card;
            P.card = null;
            P.cardPad = null;
            var a = Anim.On(c, false);
            a.fadeAt = 0; a.fadeDur = 0.5f; a.destroyAt = 0.5f;
        }

        /// highlightCard: light up the pressed buttons on player i's controller card
        public void HighlightCard(int i, PadState pad) { if (i >= 0 && i < panels.Count) panels[i].cardPad?.Highlight(pad); }

        // ---------------------------------------------------------------- panels shown / hidden (main.js buildViews)
        public void SetPanelHidden(int i, bool hidden)
        {
            if (i < 0 || i >= panels.Count) return;
            panels[i].hidden = hidden;
            panels[i].el.gameObject.SetActive(!hidden);
        }

        // ---------------------------------------------------------------- leaderboard
        /// setBoard(rect): place the leaderboard over a viewport rect in screen pixels (top-left origin), or hide it (null)
        public void SetBoard(Rect? rectPx)
        {
            boardRect = rectPx;
            board.gameObject.SetActive(rectPx.HasValue);
            if (rectPx.HasValue) SetBoardPx(rectPx.Value);
        }

        void SetBoardPx(Rect px)
        {
            var r = ui.ToCanvas(px);
            Ui.PlaceTopLeft(board, r.x, r.y, r.width, r.height);
        }

        void DrawBoard()
        {
            var g = game;
            int n = g.players.Count;
            for (int i = 0; i < n; i++) sortBuf[i] = g.players[i];
            // stable insertion sort by max(best, total), best first
            for (int i = 1; i < n; i++)
            {
                var x = sortBuf[i];
                double kx = Math.Max(x.best, g.Total(x));
                int j = i - 1;
                while (j >= 0 && Math.Max(sortBuf[j].best, g.Total(sortBuf[j])) < kx) { sortBuf[j + 1] = sortBuf[j]; j--; }
                sortBuf[j + 1] = x;
            }
            for (int i = 0; i < n; i++)
            {
                var p = sortBuf[i];
                var r = p.run;
                string st = p.left ? "left" : r.state != "running" ? (r.finished ? "done" : "run ended") : TaskName(g.CurrentTask(p));
                var cells = boardCells[i];
                double score = Math.Max(p.best, g.Total(p));
                if (boardWho[i] != p.index)
                {
                    boardWho[i] = p.index;
                    cells[0].text = (i + 1).ToString();
                    cells[1].text = HelpText.Esc(p.name) + "\n<size=11><color=#8fb3c4>" + HelpText.Esc(p.design.name) + "</color></size>";
                    var pc = Colors.Hex(p.color);
                    boardDots[i].color = pc; cells[3].color = pc;
                    boardScore[i] = double.NaN; boardState[i] = null;
                }
                if (!ReferenceEquals(boardState[i], st)) { boardState[i] = st; cells[2].text = st; }
                if (boardScore[i] != score) { boardScore[i] = score; cells[3].text = "<b>" + Js.ToLocaleString(score) + "</b>"; }
            }
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            if (minimapBg != null) UnityEngine.Object.Destroy(minimapBg); // one per match
        }
    }
}
