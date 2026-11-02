// src/render/course3d.js: meshes for the competition course elements (gate, path markers, slalom,
// bin, torpedo board, octagon with table / baskets, pingers, dock).
// World mapping: NED (x north, y east, z down) -> three (east, up, south) (Space.Ned), under the
// mirrored World root.

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;

namespace Duburi.Game
{
    public sealed class Course3D
    {
        public GameObject group;
        readonly List<(GameObject led, double period)> pingerLights = new List<(GameObject, double)>();
        readonly Owned owned = new Owned();

        const float PI = Mathf.PI;
        static readonly Dictionary<string, Mat3> mats = new Dictionary<string, Mat3>();
        static Mat3 Mat(string key, Func<Mat3> make) { if (!mats.TryGetValue(key, out var m)) { m = make(); mats[key] = m; } return m; }
        static Mat3 Std(int color, float roughness = 0.6f, float metalness = 0.05f) => Mat3.Std(color, roughness, metalness);

        static Node Box(float w, float h, float d, Mat3 m) => Node.Mesh(Geo.Box(w, h, d), m);
        static Node Cyl(float r, float h, Mat3 m, int seg = 12) => Node.Mesh(Geo.Cylinder(r, r, h, seg), m);
        static Vector3 Ned(double x, double y, double z) => Space.Ned(x, y, z);

        // a vertical line from y0 to y1 (mooring rope)
        static Node Rope(float x, float z, float y0, float y1)
        {
            var g = new Geo { lines = true };
            g.pos.Add(new Vector3(x, y0, z)); g.pos.Add(new Vector3(x, y1, z));
            return Node.Line(g, Mat("rope", () => new Mat3 { kind = MatKind.Line, color = Colors.Rgb(0x2b2b2b).linear, transparent = true, opacity = 0.7f }));
        }

        static Node ImagePlane(string animal, float size) =>
            Node.Mesh(Geo.Plane(size, size), new Mat3 { map = Art.AnimalTexture(animal), roughness = 0.8f, side = Side.Double });

        // the board print (shark-top or sawfish-top version from the handbook) with
        // the two 4 in openings cut out and their 0.5 in red borders drawn
        Texture2D BoardTexture(Board board)
        {
            const int N = 512;
            var g = new Canvas2D(N, N);
            float S = (float)board.size;
            Vector2 ToPx(double u, double v) => new Vector2(((float)u / S + 0.5f) * N, (0.5f - (float)v / S) * N);
            void Shape(BoardOpening o, float grow)
            {
                var c = ToPx(o.u, o.v);
                float r = ((float)o.r + grow) / S * N;
                g.BeginPath();
                if (o.shape == "square") g.Rect(c.x - r, c.y - r, 2 * r, 2 * r); else g.Arc(c.x, c.y, r, 0, PI * 2);
            }
            var img = Art.Image(board.sharkTop ? "boardSharkTop" : "boardSawfishTop");
            g.composite = Canvas2D.Op.SourceOver;
            if (img != null) g.DrawImage(img, 0, 0, N, N); else g.FillRect(0, 0, N, N, Colors.Hex("#2f8fc0"));
            var red = Colors.Hex("#e31b1b");
            foreach (var o in board.openings) { Shape(o, 0.0127f); g.Fill(red); }
            g.composite = Canvas2D.Op.DestinationOut;
            foreach (var o in board.openings) { Shape(o, 0); g.Fill(Color.black); }
            g.composite = Canvas2D.Op.SourceOver;
            g.StrokeRect(3, 3, N - 6, N - 6, 6, Colors.Hex("#1b2a3a"));
            return owned.Add(g.ToTexture("board", true, TextureWrapMode.Clamp, 4));
        }

        /// textSprite(text, { color, bg, w, h, font }): label texture (font = bold sans of fontPx)
        public static Texture2D TextSprite(string text, string color = "#fff", string bg = "rgba(0,0,0,0.55)", int w = 256, int h = 64, float fontPx = 34)
        {
            var g = new Canvas2D(w, h);
            g.FillRect(0, 0, w, h, Canvas2D.Css(bg));
            g.FillText(text, w / 2f, h / 2f + 2, fontPx, Canvas2D.Css(color));
            return g.ToTexture("label:" + text, true, TextureWrapMode.Clamp, 1);
        }

        /// createCourse(course, { logoTexture }): the course under 'parent' (the mirrored World root)
        public static Course3D Create(Course course, Texture2D logoTexture, Transform parent)
        {
            var c3 = new Course3D();
            c3.Build(course, logoTexture, parent);
            return c3;
        }

        void Build(Course course, Texture2D logoTexture, Transform parent)
        {
            var root = Node.Group("course");
            float floorY = (float)-course.floor;
            var leds = new List<(Node, double)>();

            // ---------------- gate ----------------
            {
                var G = course.gate;
                var g = Node.Group();
                g.position = Ned(G.pos.x, G.pos.y, 0);
                g.rotY = Space.NedYaw(G.heading);
                var black = Mat("black", () => Std(0x151515, 0.7f));
                var red = Mat("red", () => Std(0xd8262c, 0.55f));
                float W = (float)G.width, bar = 0.076f;
                float yTop = (float)-(G.topDepth - 0.04);
                // top bar: black on the left, red on the right ("red, right, above")
                var left = Box(W / 2 + bar, bar, bar, black); left.position = new Vector3(-W / 4, yTop, 0); g.Add(left);
                var right = Box(W / 2 + bar, bar, bar, red); right.position = new Vector3(W / 4, yTop, 0); g.Add(right);
                float legH = (float)(G.legDepth.y - G.legDepth.x);
                foreach (float s in new[] { -1f, 1f })
                {
                    var legU = Box(bar, legH / 2, bar, s > 0 ? red : black); // figure: left black over red, right red over black
                    legU.position = new Vector3(s * W / 2, (float)-G.legDepth.x - legH / 4, 0); g.Add(legU);
                    var legL = Box(bar, legH / 2, bar, s > 0 ? black : red); legL.position = new Vector3(s * W / 2, (float)-G.legDepth.x - 3 * legH / 4, 0); g.Add(legL);
                    g.Add(Rope(s * W / 2, 0, (float)-G.legDepth.y, floorY));
                    // floats that hold the gate up
                    var fl = Node.Mesh(Geo.Sphere(0.09f, 12, 8), Mat("float", () => Std(0xffe14d)));
                    fl.position = new Vector3(s * W / 2, -0.12f, 0); g.Add(fl);
                    g.Add(Rope(s * W / 2, 0, -0.12f, yTop));
                }
                var div = Box(0.051f, (float)(G.dividerDepth.y - G.dividerDepth.x), 0.012f, red);
                div.position = new Vector3(0, (float)-(G.dividerDepth.x + G.dividerDepth.y) / 2, 0); g.Add(div);
                // reef shark on one side, sawfish on the other
                foreach (float s in new[] { -1f, 1f })
                {
                    var im = ImagePlane(s == G.sharkSide ? "shark" : "sawfish", 0.305f);
                    im.position = new Vector3(s * 0.9f, yTop - bar / 2 - 0.17f, 0.0f);
                    g.Add(im);
                }
                root.Add(g);
            }

            // ---------------- path markers ----------------
            foreach (var p in course.paths)
            {
                var orange = Mat("orange", () => Std(0xff7a00, 0.5f).Emissive(0x401a00));
                var m = Box(0.152f, 0.02f, 1.22f, orange);
                m.position = Ned(p.pos.x, p.pos.y, course.floor - 0.012);
                m.rotY = Space.NedYaw(p.heading);
                root.Add(m);
                // arrow tip so the direction reads on camera
                var tip = Node.Mesh(Geo.Cone(0.12f, 0.25f, 3), orange);
                tip.rotX = -PI / 2;
                var tg = Node.Group(); tg.Add(tip); tip.position = new Vector3(0, 0, -0.72f);
                tg.position = m.position; tg.rotY = m.rotY; tg.scale = new Vector3(1, 0.15f, 1);
                root.Add(tg);
            }

            // ---------------- slalom ----------------
            {
                var S = course.slalom;
                var white = Mat("pvcWhite", () => Std(0xf2f2f2, 0.4f));
                var red = Mat("pvcRed", () => Std(0xd8262c, 0.45f));
                float h = (float)(S.band.y - S.band.x);
                var geo = Geo.Cylinder(0.0335f, 0.0335f, h, 12);
                foreach (var set in S.sets)
                    foreach (var pp in set.pipes)
                    {
                        var m = Node.Mesh(geo, pp.color == "red" ? red : white);
                        var c = Ned(pp.p.x, pp.p.y, (S.band.x + S.band.y) / 2);
                        m.position = c;
                        root.Add(m);
                        root.Add(Rope(c.x, c.z, (float)-S.band.y, floorY));
                        var cap = Node.Mesh(Geo.Sphere(0.05f, 10, 8), Mat("float", () => Std(0xffe14d)));
                        cap.position = new Vector3(c.x, (float)-S.band.x + 0.05f, c.z); root.Add(cap);
                    }
            }

            // ---------------- bin ----------------
            {
                var Bn = course.bin;
                var g = Node.Group();
                g.position = Ned(Bn.pos.x, Bn.pos.y, course.floor);
                g.rotY = Space.NedYaw(Bn.heading);
                float L = (float)Bn.size.x, W = (float)Bn.size.y, H = (float)Bn.height, PL = (float)Bn.plate.x, PW = (float)Bn.plate.y, t = 0.012f;
                var plate = Mat("binPlate", () => Std(0xeeeeee, 0.75f));
                var dark = Mat("binDark", () => Std(0x161616, 0.9f));
                // top plate around the opening (four strips)
                float endL = (PL - L) / 2, sideW = (PW - W) / 2;
                foreach (float s in new[] { -1f, 1f })
                {
                    var e = Box(PW, t, endL, plate); e.position = new Vector3(0, H, s * (L / 2 + endL / 2)); g.Add(e);
                    var sd = Box(sideW, t, L, plate); sd.position = new Vector3(s * (W / 2 + sideW / 2), H, 0); g.Add(sd);
                    // the black box under the opening
                    var a = Box(W, H, t, dark); a.position = new Vector3(0, H / 2, s * L / 2); g.Add(a);
                    var b = Box(t, H, L, dark); b.position = new Vector3(s * W / 2, H / 2, 0); g.Add(b);
                }
                // legs under the plate corners
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        var l = Box(0.03f, H, 0.03f, plate); l.position = new Vector3(sx * (PW / 2 - 0.03f), H / 2, sz * (PL / 2 - 0.03f)); g.Add(l);
                    }
                // halves: halves[0] is the near half (toward the approach = +Z local)
                for (int i = 0; i < Bn.halves.Length; i++)
                {
                    var m = new Mat3 { map = Art.AnimalTexture(Bn.halves[i], "bin"), roughness = 0.85f };
                    var pl = Node.Mesh(Geo.Plane(W - 0.01f, L / 2 - 0.01f), m);
                    pl.rotX = -PI / 2;
                    pl.position = new Vector3(0, 0.006f, i == 0 ? L / 4 : -L / 4);
                    g.Add(pl);
                }
                var mid = Box(W, 0.004f, 0.012f, dark); mid.position = new Vector3(0, 0.008f, 0); g.Add(mid);
                root.Add(g);
            }

            // ---------------- torpedo board ----------------
            {
                var Bd = course.board;
                var g = Node.Group();
                g.position = Ned(Bd.pos.x, Bd.pos.y, Bd.depth);
                g.rotY = Space.NedYaw(Bd.heading);
                var tex = BoardTexture(Bd);
                float size = (float)Bd.size;
                var front = Node.Mesh(Geo.Plane(size, size), new Mat3 { map = tex, alphaTest = 0.5f, side = Side.Double, roughness = 0.8f });
                front.rotY = PI; // face along the board normal (local -Z)
                g.Add(front);
                var frame = Mat("frame", () => Std(0xdddddd, 0.5f));
                float e = size / 2 + 0.02f;
                foreach (float s in new[] { -1f, 1f })
                {
                    var a = Box(size + 0.08f, 0.04f, 0.04f, frame); a.position = new Vector3(0, s * e, 0.02f); g.Add(a);
                    var b = Box(0.04f, size + 0.08f, 0.04f, frame); b.position = new Vector3(s * e, 0, 0.02f); g.Add(b);
                }
                root.Add(g);
                // stand
                var pole = Cyl(0.03f, (float)(course.floor - Bd.depth - Bd.size / 2), frame);
                var pp = Ned(Bd.pos.x - Bd.dir.x * 0.08, Bd.pos.y - Bd.dir.y * 0.08, (course.floor + Bd.depth + Bd.size / 2) / 2);
                pole.position = pp; root.Add(pole);
                // "far" bars on the floor
                var bars = Node.Group();
                bars.position = Ned(Bd.pos.x + Bd.dir.x * Bd.farDistance, Bd.pos.y + Bd.dir.y * Bd.farDistance, course.floor - 0.01);
                bars.rotY = Space.NedYaw(Bd.heading);
                foreach (float off in new[] { -0.08f, 0.08f })
                {
                    var b = Box(0.9f, 0.02f, 0.05f, Mat("barWhite", () => Std(0xffffff).Emissive(0x333333)));
                    b.position = new Vector3(0, 0, off); bars.Add(b);
                }
                var farTex = owned.Add(TextSprite("FAR", bg: "rgba(0,0,0,0)", fontPx: 48));
                var lbl = Node.Mesh(Geo.Plane(0.6f, 0.15f), new Mat3 { kind = MatKind.Basic, map = farTex, transparent = true });
                lbl.rotX = -PI / 2; lbl.position = new Vector3(0, 0.012f, 0.25f); bars.Add(lbl);
                root.Add(bars);
            }

            // ---------------- octagon ----------------
            {
                var O = course.octagon;
                var g = Node.Group();
                g.position = Ned(O.pos.x, O.pos.y, 0);
                var pvc = Mat("pvcWhite", () => Std(0xf2f2f2, 0.4f));
                var noodle = Mat("noodle", () => Std(0xff8a1f, 0.8f));
                float R = (float)O.radius;
                for (int i = 0; i < 8; i++)
                {
                    float a0 = i / 8f * PI * 2 + PI / 8, a1 = (i + 1) / 8f * PI * 2 + PI / 8;
                    var p0 = new Vector3(Mathf.Cos(a0) * R, 0.03f, Mathf.Sin(a0) * R);
                    var p1 = new Vector3(Mathf.Cos(a1) * R, 0.03f, Mathf.Sin(a1) * R);
                    float len = Vector3.Distance(p0, p1);
                    foreach (var (r, m) in new[] { (0.024f, pvc), (0.06f, noodle) })
                    {
                        var s = Cyl(r, len * (m == noodle ? 0.7f : 1f), m, 10);
                        s.position = (p0 + p1) * 0.5f;
                        s.quaternion = Space.FromUnitVectors(Vector3.up, (p1 - p0).normalized);
                        g.Add(s);
                    }
                }
                // images hanging inside, facing the centre
                foreach (var im in O.images)
                {
                    var pl = ImagePlane(im.animal, 0.305f);
                    float dx = (float)Math.Cos(im.angle), dy = (float)Math.Sin(im.angle); // NED direction from the centre
                    pl.position = new Vector3(dy * (R - 0.08f), -0.19f, -dx * (R - 0.08f));
                    g.Add(pl);
                    pl.LookAt(new Vector3(0, -0.19f, 0) + g.position - g.position);
                    g.Add(Rope(dy * (R - 0.08f), -dx * (R - 0.08f), 0.02f, -0.03f));
                }
                root.Add(g);
                // table, baskets
                var tg = Node.Group();
                tg.position = Ned(O.pos.x, O.pos.y, O.tableTop);
                var top = Box((float)O.tableHalf * 2, 0.03f, (float)O.tableHalf * 2, Mat("table", () => Std(0xbfc6cc, 0.5f)));
                top.position = new Vector3(0, -0.015f, 0); tg.Add(top);
                float legLen = (float)(course.floor - O.tableTop);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sy in new[] { -1f, 1f })
                    {
                        var l = Cyl(0.024f, legLen, pvc, 8); l.position = new Vector3(sx * 0.28f, -legLen / 2, sy * 0.28f); tg.Add(l);
                    }
                root.Add(tg);
                var net = Mat("basket", () => new Mat3 { color = Colors.Rgb(0x3fa34d).linear, roughness = 0.7f, transparent = true, opacity = 0.75f, side = Side.Double });
                foreach (var bk in O.baskets)
                {
                    var bg = Node.Group();
                    bg.position = Ned(bk.c.x, bk.c.y, bk.depthTop + 0.1);
                    float hx = (float)bk.h.y, hz = (float)bk.h.x; // three X = east (NED y), Z = south (NED x)
                    bg.Add(Box(hx * 2, 0.01f, hz * 2, net));
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var a = Box(hx * 2, 0.1f, 0.01f, net); a.position = new Vector3(0, 0.05f, s * hz); bg.Add(a);
                        var b = Box(0.01f, 0.1f, hz * 2, net); b.position = new Vector3(s * hx, 0.05f, 0); bg.Add(b);
                    }
                    var iconTex = owned.Add(TextSprite(bk.item.ToUpperInvariant(), bg: "#1d4f2a", fontPx: 40));
                    var icon = Node.Mesh(Geo.Plane(0.22f, 0.11f), new Mat3 { kind = MatKind.Basic, map = iconTex, side = Side.Double });
                    icon.position = new Vector3(0, 0.16f, 0); icon.rotY = PI / 2;
                    bg.Add(icon);
                    root.Add(bg);
                }
            }

            // ---------------- pingers ----------------
            foreach (var pg in course.pingers)
            {
                var body = Cyl(0.04f, 0.22f, Mat("pinger", () => Std(0x333a40, 0.3f, 0.6f)), 10);
                body.position = Ned(pg.pos.x, pg.pos.y, course.floor - 0.11);
                root.Add(body);
                var led = Node.Mesh(Geo.Sphere(0.03f, 8, 6), new Mat3 { kind = MatKind.Basic, color = Colors.Rgb(0x40ff90).linear });
                led.position = body.position + new Vector3(0, 0.13f, 0);
                root.Add(led);
                leds.Add((led, pg.period));
            }

            // ---------------- start dock ----------------
            {
                var D = course.dock;
                var g = Node.Group();
                g.position = Ned(D.pos.x - 2.2, D.pos.y, 0);
                var deck = Box(4.5f, 0.18f, 1.2f, Mat("dock", () => Std(0x8a6a48, 0.9f)));
                deck.position = new Vector3(0, 0.05f, 0); g.Add(deck);
                foreach (float s in new[] { -1f, 1f })
                {
                    var fl = Cyl(0.18f, 4.4f, Mat("dockFloat", () => Std(0x2a3a4a)), 10);
                    fl.rotZ = PI / 2; fl.position = new Vector3(0, -0.06f, s * 0.4f); g.Add(fl);
                }
                if (logoTexture != null)
                {
                    float asp = logoTexture.width > 0 ? logoTexture.width / (float)logoTexture.height : 2.6f;
                    var sign = Node.Mesh(Geo.Plane(1.2f * asp * 0.6f, 0.72f), new Mat3 { kind = MatKind.Basic, map = logoTexture, side = Side.Double });
                    sign.position = new Vector3(0, 1.05f, 0); sign.rotY = PI; // faces north (toward the course)
                    g.Add(sign);
                    var post = Cyl(0.04f, 0.8f, Mat("frame", () => Std(0xdddddd)), 8); post.position = new Vector3(0, 0.5f, 0); g.Add(post);
                }
                var startTex = owned.Add(TextSprite("START", bg: "#0d3b66", fontPx: 44));
                var lbl = Node.Mesh(Geo.Plane(1.2f, 0.3f), new Mat3 { kind = MatKind.Basic, map = startTex, side = Side.Double });
                lbl.position = new Vector3(0, 0.16f, -0.61f); lbl.rotY = PI; g.Add(lbl);
                root.Add(g);
                // start zone ring on the surface
                var ring = Node.Mesh(Geo.Ring((float)D.radius - 0.04f, (float)D.radius + 0.04f, 64),
                    new Mat3 { kind = MatKind.Basic, color = Color.white, transparent = true, opacity = 0.35f, side = Side.Double });
                ring.rotX = -PI / 2;
                ring.position = Ned(D.pos.x, D.pos.y, -0.01);
                root.Add(ring);
            }

            var dyn = new List<Node>();
            foreach (var (n, _) in leds) dyn.Add(n);
            Bots3D.MergeStatic(root, dyn);
            group = root.Realize(parent, owned);
            foreach (var (n, period) in leds) pingerLights.Add((n.transform.gameObject, period));
        }

        /// update(time): pinger LEDs flash at the start of each ping period
        public void Update(double time)
        {
            for (int i = 0; i < pingerLights.Count; i++)
            {
                var (led, period) = pingerLights[i];
                double ph = (time % period) / period;
                bool v = ph < 0.08;
                if (led.activeSelf != v) led.SetActive(v);
            }
        }

        public void Dispose()
        {
            if (group != null) UnityEngine.Object.Destroy(group);
            owned.DestroyAll();
            group = null;
            pingerLights.Clear();
        }
    }
}
