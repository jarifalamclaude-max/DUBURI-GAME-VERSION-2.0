// The DUBURI vehicle model, rebuilt from the team's CAD renders and the material palette exported
// with the CAD model (names in the comments below), scaled to the design's 0.46 x 0.58 x 0.26 m.
//
//   * a light-grey OCTAGONAL pressure hull with a flat lid, a polished-brass hatch ring with a clear
//     window on top (electronics deck visible through it) and a brass-ringed camera porthole in front;
//   * eight red metallic toggle latches with satin-steel hinges around the top edge;
//   * four grey bracket wings on the diagonals: a vertical T200 at each wing tip (duct up) and a
//     horizontal / vectored T200 on a web plate below the wing, nearer the hull (8 x T200: black
//     ducts and nose cones, blue propellers);
//   * a black powder-coated open frame below: side plates with triangular truss cut-outs and skids,
//     a slotted bottom plate, a top cross plate, end bars; a grey battery box;
//   * at the front bottom a torpedo launcher (two torpedoes in white holders with red rods and
//     chrome rails); the marker dropper and the gripper come from the shared payload builders.
//
// Every thruster sits exactly at the Core design's position / axis (Bots.BOTS["duburi"]), and the
// gripper, marker and torpedo parts at the design's mount points, so bubbles, cameras, collisions
// and physics match. Coordinates in the comments are body FRD (x fwd, y right, z down, metres);
// geometry is built in three space (Space.Body) like the rest of Bots3D. Static parts are merged
// per material by MergeStatic (about 14 draw calls + 8 propellers + payloads), ~19k triangles.

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;

namespace Duburi.Game
{
    public static partial class Bots3D
    {
        const float S2 = 0.70710678f;

        // hull (body z: lid top -0.110, hull body -0.100 .. 0.032)
        const float HULL_A = 0.125f;             // octagon apothem (centre to face)
        const float HULL_TOP = -0.100f, HULL_BOT = 0.032f;
        const float LID_A = 0.130f, LID_T = 0.010f;
        const float HATCH_R = 0.060f;            // window opening in the lid

        // the T200 model (TH_SIZE "t200-16v": outer duct radius 0.05)
        const float T_RO = 0.050f, T_RI = 0.0435f, T_LD = 0.050f;

        // ---------------------------------------------------------------- helpers

        static void DuburiMats(Ctx ctx)
        {
            var M = ctx.M;
            M["dHull"] = Std(0x8c8c8c, 0.45f, 0.5f);                    // the CAD hull: Aluminum - Anodized (Grey)
            M["dAnod"] = Std(0x898989, 0.55f, 0.55f);                   // Aluminum - Anodized Rough (Grey)
            M["dPowder"] = Std(0x191919, 0.05f, 0.82f);                 // Powder Coat (Black)
            M["dT200"] = Std(0x141414, 0.05f, 0.38f);                   // Opaque(25,25,25): moulded thruster plastic
            M["dBrass"] = Std(0xf3cb7c, 0.85f, 0.16f);                  // Brass - Polished
            M["dChrome"] = Std(0xc4c5c4, 0.9f, 0.08f);                  // Chrome
            M["dSteel"] = Std(0xb4b0b0, 0.8f, 0.36f);                   // Steel - Satin / Stainless Steel - Satin
            M["dRed"] = Std(0xc43527, 0.55f, 0.32f);                    // Paint - Metallic (Red)
            M["dBlue"] = Std(0x2699d0, 0.05f, 0.4f).Double();           // Opaque(38,153,208): propellers
            M["dWhite"] = Std(0xf4f4f4, 0f, 0.45f);                     // Opaque(244,244,244)
            M["dGlass"] = Std(0xf6f6f3, 0f, 0.03f).Transparent(0.16f, false); // Acrylic (Clear) / Glass (Clear)
            // Few materials = few draw calls after MergeStatic (it merges per material object): the
            // battery (Opaque(191,189,187)) shares the hull grey, dark recesses the thruster black, and
            // the shared payload builders (gripper, marker dropper) use DUBURI's own metals.
            M["dBatt"] = Std(0xbfbdbb, 0.1f, 0.5f);                     // Opaque(191,189,187): battery box
            M["dStainless"] = M["dSteel"];
            M["dWell"] = M["dAnod"];
            M["dDeck"] = M["dT200"];
            M["anod"] = M["dAnod"];
            M["alu"] = M["dSteel"];
            M["black"] = M["dPowder"];
            M["chip"] = M["dT200"];
        }

        static Node Part(Node parent, Geo g, Mat3 m, Vector3 pos, string name = "")
        {
            var n = Mesh(g, m, name);
            n.position = pos;
            parent.Add(n);
            return n;
        }

        static Node Part(Node parent, Geo g, Mat3 m, double x, double y, double z, string name = "") => Part(parent, g, m, P(x, y, z), name);

        /// Rod between two points in the parent's own (three) coordinates.
        static Node LRod(Ctx ctx, Node parent, Vector3 a, Vector3 b, float r, Mat3 mat)
        {
            var m = Mesh(ctx.Geo("rod" + r.ToString("R"), () => Geo.Cylinder(r, r, 1, 8)), mat);
            Vector3 w = b - a;
            m.position = (a + b) * 0.5f;
            m.scale = new Vector3(1, w.magnitude, 1);
            m.quaternion = Space.FromUnitVectors(Y_AXIS, w.normalized);
            parent.Add(m);
            return m;
        }

        /// Closed polygon from x, y pairs, wound counter-clockwise so Extrude orients the holes itself.
        static Shape2 Poly(params float[] xy)
        {
            int n = xy.Length / 2;
            double area = 0;
            for (int i = 0, j = n - 1; i < n; j = i++) area += xy[2 * j] * (double)xy[2 * i + 1] - xy[2 * i] * (double)xy[2 * j + 1];
            var s = new Shape2();
            for (int k = 0; k < n; k++)
            {
                int i = area >= 0 ? k : n - 1 - k;
                if (k == 0) s.MoveTo(xy[2 * i], xy[2 * i + 1]); else s.LineTo(xy[2 * i], xy[2 * i + 1]);
            }
            s.ClosePath();
            return s;
        }

        static Path2 HolePoly(params float[] xy)
        {
            var p = new Path2();
            p.MoveTo(xy[0], xy[1]);
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(xy[i], xy[i + 1]);
            p.ClosePath();
            return p;
        }

        static Path2 HoleCircle(double x, double y, double r)
        {
            var p = new Path2();
            p.Absarc(x, y, r, 0, 2 * Math.PI, true);
            return p;
        }

        /// Rounded slot (stadium) between the centres (x0, y0) and (x1, y1).
        static Path2 HoleSlot(double x0, double y0, double x1, double y1, double r)
        {
            double a = Math.Atan2(y1 - y0, x1 - x0);
            var p = new Path2();
            p.Absarc(x1, y1, r, a - Math.PI / 2, a + Math.PI / 2, false);
            p.Absarc(x0, y0, r, a + Math.PI / 2, a + Math.PI * 1.5, false);
            p.ClosePath();
            return p;
        }

        /// Regular octagon (faces on the axes and the diagonals) as x, y pairs.
        static float[] Octagon(float apothem)
        {
            float r = apothem / Mathf.Cos(PI / 8);
            var xy = new float[16];
            for (int k = 0; k < 8; k++)
            {
                float a = PI / 8 + k * PI / 4;
                xy[2 * k] = r * Mathf.Cos(a);
                xy[2 * k + 1] = r * Mathf.Sin(a);
            }
            return xy;
        }

        /// Extruded plate (thickness t) whose shape x / y run along the three-space directions ax / ay,
        /// centred on the plane through o (three space).
        static Geo PlateGeo(Shape2 s, float t, Vector3 o, Vector3 ax, Vector3 ay, int curveSegments = 6)
        {
            var g = Geo.Extrude(s, new ExtrudeOptions { depth = t, bevelEnabled = false, curveSegments = curveSegments }).Translate(0, 0, -t / 2);
            ax.Normalize(); ay.Normalize();
            Vector3 az = Vector3.Cross(ax, ay);
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(ax.x, ax.y, ax.z, 0));
            m.SetColumn(1, new Vector4(ay.x, ay.y, ay.z, 0));
            m.SetColumn(2, new Vector4(az.x, az.y, az.z, 0));
            m.SetColumn(3, new Vector4(o.x, o.y, o.z, 1));
            return g.ApplyMatrix(m);
        }

        /// Horizontal plate: shape x = body y (right), shape y = body x (forward), centred at body depth z.
        static Geo HPlate(Shape2 s, float t, float z, int curveSegments = 6) =>
            PlateGeo(s, t, P(0, 0, z), P(0, 1, 0), P(1, 0, 0), curveSegments);

        /// A group whose local +X / +Y / +Z are the given three-space directions (+Y up, +Z out).
        static Node Frame3(string name, Vector3 pos, Vector3 x, Vector3 y)
        {
            var g = Node.Group(name);
            x.Normalize(); y.Normalize();
            g.position = pos;
            g.quaternion = Space.FromBasis(x, y, Vector3.Cross(x, y));
            return g;
        }

        // ---------------------------------------------------------------- the vehicle

        static Spec BuildDuburi(Ctx ctx, Node root, BotDesign d, Texture2D tex)
        {
            DuburiMats(ctx);
            DuburiHull(ctx, root, tex);
            DuburiLatches(ctx, root);
            DuburiWings(ctx, root, d);
            DuburiFrame(ctx, root);

            var props = new List<Node>();
            for (int i = 0; i < d.thrusters.Length; i++)
            {
                var t = d.thrusters[i];
                var g = T200(ctx, t, i, out var prop);
                Place(g, t.pos);
                Orient(g, t.axis);
                root.Add(g);
                props.Add(prop);
            }
            return new Spec
            {
                props = props,
                sensors = null,                                       // camera behind the porthole (DuburiHull)
                grip = new GripSpec { s = 1f, arm = 0.03f, post = 0.022f },  // compact, under the front of the frame
                torpedoes = DuburiLauncher(ctx, root, d.torpedo),
            };
        }

        // Pressure hull: octagonal body, lid with the brass hatch, porthole, penetrators, team band, logos.
        static void DuburiHull(Ctx ctx, Node root, Texture2D tex)
        {
            var M = ctx.M;
            float h = HULL_BOT - HULL_TOP;
            Part(root, Geo.Prism(8, HULL_A, h, true), M["dHull"], 0, 0, (HULL_TOP + HULL_BOT) / 2, "hull");
            Part(root, Geo.Prism(8, 0.128f, 0.006f), M["dAnod"], 0, 0, HULL_BOT + 0.003f, "hull_bottom");

            // lid: octagon plate with the hatch opening
            var lid = Poly(Octagon(LID_A));
            lid.holes.Add(HoleCircle(0, 0, HATCH_R));
            Part(root, HPlate(lid, LID_T, HULL_TOP - LID_T / 2, 12), M["dHull"], Vector3.zero, "lid");

            // hatch: polished brass ring, clear window, the electronics deck seen through it
            Part(root, Geo.Tube(HATCH_R, 0.071f, 0.016f, 48), M["dBrass"], 0, 0, HULL_TOP - LID_T - 0.008f, "hatch_ring");
            Part(root, Geo.Tube(0.0545f, HATCH_R + 0.0005f, 0.004f, 48), M["dBrass"], 0, 0, HULL_TOP - 0.004f);
            Part(root, Geo.Circle(HATCH_R + 0.0005f, 48).RotateX(-PI / 2), M["dGlass"], 0, 0, HULL_TOP - LID_T - 0.011f, "hatch_window");
            const float deckZ = -0.050f;
            Part(root, Geo.Cylinder(HATCH_R, HATCH_R, deckZ - (HULL_TOP - LID_T), 32, 1, true).Flipped(), M["dWell"], 0, 0, (deckZ + HULL_TOP - LID_T) / 2);
            Part(root, Geo.Circle(HATCH_R, 32).RotateX(-PI / 2), M["dDeck"], 0, 0, deckZ);
            Part(root, Geo.Box(0.08f, 0.003f, 0.055f), M["pcb"], -0.005, 0, deckZ - 0.0015);
            Part(root, Geo.Box(0.045f, 0.012f, 0.04f), M["dAnod"], 0.005, -0.015, deckZ - 0.009);   // computer heatsink
            Part(root, Geo.Box(0.022f, 0.008f, 0.02f), M["chip"], -0.02, 0.022, deckZ - 0.007);
            Part(root, Geo.Box(0.018f, 0.012f, 0.03f), M["dWhite"], 0.025, 0.028, deckZ - 0.009);
            Part(root, Geo.Box(0.03f, 0.006f, 0.012f), M["dRed"], -0.035, -0.02, deckZ - 0.006);

            // front porthole: brass ring and clear port over the camera
            const float pz = -0.040f;
            Part(root, Geo.Tube(0.030f, 0.046f, 0.003f, 40).RotateX(PI / 2), M["dBrass"], HULL_A + 0.0015, 0, pz);
            Part(root, Geo.Tube(0.034f, 0.044f, 0.014f, 40).RotateX(PI / 2), M["dBrass"], HULL_A + 0.007, 0, pz, "porthole");
            Part(root, Geo.Circle(0.0345f, 32), M["dDeck"], HULL_A + 0.0005, 0, pz).rotY = PI;
            Part(root, CylZ(0.013f, 0.013f, 0.010f, 20), M["dAnod"], HULL_A + 0.0045, 0, pz, "camera_front");
            var lens = Part(root, Geo.Sphere(0.0095f, 16, 8, 0, 2 * PI, 0, PI / 2).RotateX(-PI / 2), M["lens"], HULL_A + 0.0085, 0, pz);
            lens.scale = new Vector3(1, 1, 0.45f);
            Part(root, Geo.Circle(0.0345f, 40), M["dGlass"], HULL_A + 0.0135, 0, pz, "porthole_glass").rotY = PI;

            // rear face: cable penetrators with hex nuts and a vent plug
            var gland = ctx.Geo("dGland", () => CylZ(0.0055f, 0.0055f, 0.014f, 10));
            var nut = ctx.Geo("dNut", () => Geo.Prism(6, 0.0072f, 0.004f).RotateX(PI / 2));
            foreach (double y in new[] { -0.03, 0, 0.03 })
                foreach (double z in new[] { -0.066, -0.04 })
                {
                    Part(root, gland, M["dT200"], -HULL_A - 0.007, y, z);
                    Part(root, nut, M["dSteel"], -HULL_A - 0.002, y, z);
                }
            Part(root, CylZ(0.0065f, 0.0065f, 0.008f, 12), M["dStainless"], -HULL_A - 0.004, 0, -0.012);

            // status light at the back of the lid in the player's colour, so players can tell the robots
            // apart (the CAD has no team markings)
            Part(root, CylZ(0.011f, 0.011f, 0.004f, 16).RotateX(PI / 2), M["dT200"], -0.100, 0, HULL_TOP - LID_T - 0.002, "status_base");
            Part(root, Geo.Sphere(0.008f, 16, 8, 0, 2 * PI, 0, PI / 2), M["team"], -0.100, 0, HULL_TOP - LID_T - 0.004, "status_light");
        }

        // Eight toggle latches round the lid: two on each side face, one on each diagonal face.
        static void DuburiLatches(Ctx ctx, Node root)
        {
            foreach (float side in new[] { -1f, 1f })
                foreach (float x in new[] { -0.03f, 0.03f })
                    Latch(ctx, root, new Vector2(0, side), new Vector2(x, side * HULL_A), true);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                    Latch(ctx, root, new Vector2(sx * S2, sy * S2), new Vector2(sx * S2 * HULL_A, sy * S2 * HULL_A), false);
        }

        // One latch on the hull face with outward normal n (body x, y) at point p, hinged just under the lid.
        // Local frame: +X along the face, +Y up, +Z out of the face; origin on the face at the lid's underside.
        static void Latch(Ctx ctx, Node root, Vector2 n, Vector2 p, bool link)
        {
            var M = ctx.M;
            Vector3 outward = P(n.x, n.y, 0), up = Vector3.up;
            Vector3 along = Vector3.Cross(up, outward);           // so that along x up = outward
            var g = Frame3("latch", P(p.x, p.y, HULL_TOP), along, up);

            g.Add(At(Mesh(ctx.Geo("lBase", () => Geo.Box(0.024f, 0.024f, 0.003f)), M["dSteel"]), 0, -0.014f, 0.0015f));
            g.Add(At(Mesh(ctx.Geo("lHinge", () => Geo.Cylinder(0.0032f, 0.0032f, 0.022f, 10).RotateZ(PI / 2)), M["dSteel"]), 0, -0.006f, 0.006f));
            g.Add(At(Mesh(ctx.Geo("lCatch", () => Geo.Box(0.016f, 0.007f, 0.004f)), M["dStainless"]), 0, 0.005f, 0.0065f));
            g.Add(At(Mesh(ctx.Geo("lStrike", () => Geo.Box(0.02f, 0.007f, 0.012f)), M["dAnod"]), 0, 0.0135f, -0.004f));

            // red lever: a U-loop rising above the lid, leaning slightly outward
            var lever = Node.Group("latch_lever");
            lever.position = new Vector3(0, -0.006f, 0.006f);
            lever.rotX = 0.22f;
            var arm = ctx.Geo("lArm", () => Geo.Box(0.0035f, 0.036f, 0.004f));
            lever.Add(At(Mesh(arm, M["dRed"]), -0.0085f, 0.018f, 0.003f));
            lever.Add(At(Mesh(arm, M["dRed"]), 0.0085f, 0.018f, 0.003f));
            lever.Add(At(Mesh(ctx.Geo("lTop", () => Geo.Box(0.0205f, 0.0035f, 0.0045f)), M["dRed"]), 0, 0.0345f, 0.003f));
            lever.Add(At(Mesh(ctx.Geo("lMid", () => Geo.Box(0.017f, 0.003f, 0.003f)), M["dRed"]), 0, 0.012f, 0.004f));
            g.Add(lever);

            if (link)
            {
                // stainless draw link down the face to the keeper
                var st = M["dStainless"];
                LRod(ctx, g, new Vector3(-0.008f, -0.008f, 0.006f), new Vector3(0, -0.048f, 0.0045f), 0.0016f, st);
                LRod(ctx, g, new Vector3(0.008f, -0.008f, 0.006f), new Vector3(0, -0.048f, 0.0045f), 0.0016f, st);
                g.Add(At(Mesh(ctx.Geo("lKeep", () => Geo.Box(0.014f, 0.010f, 0.003f)), M["dSteel"]), 0, -0.05f, 0.0015f));
            }
            root.Add(g);
        }

        static Node At(Node n, float x, float y, float z) { n.position = new Vector3(x, y, z); return n; }

        // Bracket wings on the diagonals: a top plate from the hull out to the vertical thruster, and a
        // web plate below the hull corner that carries the horizontal thruster.
        static void DuburiWings(Ctx ctx, Node root, BotDesign d)
        {
            var M = ctx.M;
            const float wt = 0.006f, wz = HULL_TOP + wt / 2, webR = 0.1315f, webT = 0.005f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    // radial (out along the diagonal) and tangential (towards the side face) directions
                    Vector3 rad = P(sx * S2, sy * S2, 0), tan = P(-sx * S2, sy * S2, 0);

                    // top plate in (tangential, radial); its tip ends in the vertical thruster's duct wall
                    var top = Poly(-0.050f, 0.118f, 0.050f, 0.118f, 0.058f, 0.170f, 0.066f, 0.192f, 0.022f, 0.205f, -0.030f, 0.200f);
                    top.holes.Add(HolePoly(-0.015f, 0.135f, 0.040f, 0.135f, 0.040f, 0.168f));
                    Part(root, PlateGeo(top, wt, P(0, 0, wz), tan, rad), M["dAnod"], Vector3.zero, "wing");

                    // web in (tangential, up), against the hull corner and down below it
                    // from the wing down beside the hull to the horizontal thruster (design z -0.04)
                    var web = Poly(-0.045f, 0.094f, 0.035f, 0.094f, 0.035f, 0.030f, 0.012f, -0.012f, -0.072f, -0.012f, -0.072f, 0.030f);
                    Part(root, PlateGeo(web, webT, rad * webR, tan, Vector3.up), M["dAnod"], Vector3.zero, "wing_web");
                }

            // thruster mounts: a block from the wing tip into each vertical duct, a clamp from the web to
            // each horizontal duct (at the design's thruster positions)
            var block = ctx.Geo("wBlock", () => Geo.Box(0.012f, 0.012f, 0.012f));
            foreach (var t in d.thrusters)
            {
                float px = (float)t.pos.x, py = (float)t.pos.y, pz = (float)t.pos.z;
                float sx = px >= 0 ? 1 : -1, sy = py >= 0 ? 1 : -1;
                Vector2 r2 = new Vector2(sx * S2, sy * S2), t2 = new Vector2(-sx * S2, sy * S2);
                float rho = px * r2.x + py * r2.y, tau = px * t2.x + py * t2.y;
                if (Math.Abs(t.axis.z) > 0.5)
                {
                    // on the duct wall, on the hull side, just under the wing
                    Vector2 c = r2 * (rho - (T_RI + T_RO) / 2);
                    float zb = HULL_TOP + wt + 0.006f;
                    if (pz - T_LD / 2 < zb + 0.006f && pz + T_LD / 2 > zb - 0.006f)
                        Part(root, block, M["dAnod"], P(c.x + t2.x * tau, c.y + t2.y * tau, zb));
                }
                else
                {
                    float inner = rho - T_RO, len = Mathf.Max(0.006f, inner - (webR + webT / 2) + 0.003f);
                    float mid = inner + 0.0015f - len / 2;
                    Vector3 pos = P(r2.x * mid + t2.x * tau, r2.y * mid + t2.y * tau, pz);
                    var clamp = Frame3("thruster_clamp", pos, P(t2.x, t2.y, 0), Vector3.up);
                    clamp.Add(Mesh(Geo.Box(0.034f, 0.03f, len), M["dT200"]));
                    root.Add(clamp);
                }
            }
        }

        // Black powder-coated frame: side plates with truss cut-outs, cross plates, end bars, battery.
        static void DuburiFrame(Ctx ctx, Node root)
        {
            var M = ctx.M;
            var black = M["dPowder"];
            const float py = 0.18f, pt = 0.006f;

            // side plates in (body x, up): skid along the bottom, sloped ends, triangular truss
            var side = Poly(-0.150f, 0f, 0.150f, 0f, 0.214f, -0.112f, 0.214f, -0.130f, -0.214f, -0.130f, -0.214f, -0.112f);
            foreach (float s in new[] { -1f, 1f })
            {
                side.holes.Add(HolePoly(s * 0.012f, -0.022f, s * 0.085f, -0.022f, s * 0.085f, -0.108f, s * 0.012f, -0.108f));
                side.holes.Add(HolePoly(s * 0.121f, -0.018f, s * 0.139f, -0.018f, s * 0.183f, -0.098f));
                side.holes.Add(HolePoly(s * 0.103f, -0.034f, s * 0.103f, -0.108f, s * 0.168f, -0.108f));
            }
            foreach (float s in new[] { -1f, 1f })
                Part(root, PlateGeo(side, pt, P(0, s * py, 0), P(1, 0, 0), Vector3.up), black, Vector3.zero, s > 0 ? "plate_right" : "plate_left");

            // top cross plate under the hull (stays inside the horizontal thrusters)
            var crossTop = Poly(-0.177f, -0.075f, 0.177f, -0.075f, 0.177f, 0.075f, -0.177f, 0.075f);
            crossTop.holes.Add(HoleSlot(-0.152, -0.03, -0.152, 0.03, 0.007));
            crossTop.holes.Add(HoleSlot(0.152, -0.03, 0.152, 0.03, 0.007));
            Part(root, HPlate(crossTop, 0.006f, HULL_BOT + 0.009f), black, Vector3.zero, "frame_top");
            var bracket = ctx.Geo("fBracket", () => Geo.Box(0.05f, 0.016f, 0.026f));
            foreach (float bx in new[] { -0.055f, 0.055f })
                foreach (float by in new[] { -0.152f, 0.152f })
                    Part(root, bracket, M["dAnod"], bx, by, HULL_BOT - 0.002f);

            // bottom plate: wide middle, narrow spine fore and aft (clear of the horizontal thrusters), slots
            var bottom = Poly(-0.177f, -0.088f, -0.045f, -0.088f, -0.045f, -0.198f, 0.045f, -0.198f, 0.045f, -0.088f, 0.177f, -0.088f,
                              0.177f, 0.088f, 0.045f, 0.088f, 0.045f, 0.198f, -0.045f, 0.198f, -0.045f, 0.088f, -0.177f, 0.088f);
            foreach (float s in new[] { -1f, 1f })
            {
                bottom.holes.Add(HoleSlot(s * 0.075, 0.05, s * 0.15, 0.05, 0.007));
                bottom.holes.Add(HoleSlot(s * 0.075, -0.05, s * 0.15, -0.05, 0.007));
            }
            bottom.holes.Add(HoleSlot(0, -0.175, 0, -0.12, 0.008));
            Part(root, HPlate(bottom, 0.006f, 0.114f), black, Vector3.zero, "frame_bottom");

            // end bars at skid level
            var bar = ctx.Geo("fBar", () => Geo.Box(2 * py, 0.013f, 0.012f));
            Part(root, bar, black, 0.205, 0, 0.1235);
            Part(root, bar, black, -0.205, 0, 0.1235);

            // battery box with straps and a cable gland
            Part(root, Geo.Box(0.075f, 0.068f, 0.09f), M["dBatt"], -0.06, -0.04, 0.076, "battery");
            var strap = ctx.Geo("fStrap", () => Geo.Box(0.0775f, 0.0705f, 0.008f));
            Part(root, strap, black, -0.04, -0.04, 0.076);
            Part(root, strap, black, -0.08, -0.04, 0.076);
            Part(root, CylZ(0.005f, 0.005f, 0.012f, 10), M["dT200"], -0.009, -0.04, 0.06);

            // downward camera under the bottom plate
            Part(root, Geo.Cylinder(0.012f, 0.012f, 0.012f, 16), M["dAnod"], 0.13, 0.025, 0.123, "camera_down");
            var dl = Part(root, Geo.Sphere(0.0085f, 12, 6, 0, 2 * PI, PI / 2, PI / 2), M["lens"], 0.13, 0.025, 0.129);
            dl.scale = new Vector3(1, 0.6f, 1);
        }

        // ---------------------------------------------------------------- T200 thruster

        // Ducted T200 with local +Z = thrust axis: rounded duct, nose cone (-Z) and motor pod (+Z),
        // four stator struts, a blue three-blade propeller (the spinning part).
        static Node T200(Ctx ctx, ThrusterDef t, int i, out Node prop)
        {
            var M = ctx.M;
            float hand = (float)(t.hand ?? 1);
            var g = Node.Group("thruster_" + i);
            g.Add(Mesh(ctx.Geo("t2Duct", () =>
            {
                var pts = new List<Vector2>();
                float rm = (T_RI + T_RO) / 2, rl = (T_RO - T_RI) / 2;
                for (int k = 0; k <= 6; k++)
                {
                    float a = PI + PI * k / 6f;                                    // rounded inlet lip, inside -> outside
                    pts.Add(new Vector2(rm + rl * Mathf.Cos(a), -T_LD / 2 + rl * Mathf.Sin(a)));
                }
                pts.Add(new Vector2(T_RO - 0.0015f, T_LD / 2 - 0.0008f));
                pts.Add(new Vector2(T_RO - 0.0025f, T_LD / 2));
                pts.Add(new Vector2(T_RI + 0.001f, T_LD / 2));
                pts.Add(new Vector2(T_RI, T_LD / 2 - 0.001f));
                pts.Add(new Vector2(T_RI, -T_LD / 2));
                return Geo.Lathe(pts, 36).RotateX(PI / 2);
            }), M["dT200"]));
            g.Add(Mesh(ctx.Geo("t2Hub", () => Geo.Lathe(new[] {
                V(0, -0.060), V(0.004, -0.058), V(0.009, -0.051), V(0.013, -0.040), V(0.0155, -0.028), V(0.0165, -0.016),
                V(0.0165, -0.006), V(0.0185, -0.004), V(0.0185, 0.034), V(0.016, 0.044), V(0.011, 0.051), V(0.005, 0.055), V(0, 0.056),
            }, 20).RotateX(PI / 2)), M["dT200"]));
            g.Add(At(Mesh(ctx.Geo("t2Band", () => CylZ(0.0188f, 0.0188f, 0.003f, 20, true)), M["dSteel"]), 0, 0, -0.0055f));
            var strut = ctx.Geo("t2Strut", () => Geo.Box(0.003f, T_RI - 0.0185f + 0.002f, 0.009f));
            for (int k = 0; k < 4; k++)
            {
                float a = k * PI / 2 + PI / 4, rm = (0.0185f + T_RI) / 2;
                var s = Mesh(strut, M["dT200"]);
                s.position = new Vector3(Mathf.Cos(a) * rm, Mathf.Sin(a) * rm, 0.018f);
                s.rotZ = a - PI / 2;
                g.Add(s);
            }
            prop = Mesh(ctx.Geo("t2Prop" + hand, () => T200Prop(T_RI - 0.0015f, 0.015f, hand)), M["dBlue"], "propeller_" + i);
            prop.position = new Vector3(0, 0, -0.008f);
            g.Add(prop);
            return g;
        }

        // Three broad, skewed, twisted blades in the XY plane (spinning about Z), from hub radius rh to R.
        static Geo T200Prop(float R, float rh, float hand)
        {
            var g = new Geo();
            const int blades = 3, seg = 6;
            float h = hand < 0 ? -1 : 1;
            Vector3 Pt(float a0, float k, float side)
            {
                float rr = rh + (R - rh) * k;
                float chord = R * (0.5f + 0.3f * Mathf.Sin(PI * k)) * (1 - 0.25f * k) * (1 - 0.65f * Mathf.Pow(k, 6));
                float ph = h * (0.75f - 0.4f * k);                                   // twist
                float a = a0 + h * 0.35f * k;                                        // skew
                float tt = side * chord * 0.5f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a), cp = Mathf.Cos(ph);
                return new Vector3(ca * rr - sa * tt * cp, sa * rr + ca * tt * cp, tt * Mathf.Sin(ph));
            }
            for (int b = 0; b < blades; b++)
            {
                float a0 = b * 2 * PI / blades;
                for (int s = 0; s < seg; s++)
                {
                    float k0 = s / (float)seg, k1 = (s + 1) / (float)seg;
                    Vector3 p00 = Pt(a0, k0, -1), p01 = Pt(a0, k0, 1), p10 = Pt(a0, k1, -1), p11 = Pt(a0, k1, 1);
                    g.pos.Add(p00); g.pos.Add(p10); g.pos.Add(p11); g.pos.Add(p00); g.pos.Add(p11); g.pos.Add(p01);
                }
            }
            g.ComputeVertexNormals();
            g.EnsureUV();
            return g;
        }

        // ---------------------------------------------------------------- torpedo launcher

        // Two torpedoes side by side in white holders with chrome rails and red rods, a release servo
        // on top, bracketed down to the front bar. b = the design's torpedo mount point.
        static Node[] DuburiLauncher(Ctx ctx, Node root, V3 b)
        {
            var M = ctx.M;
            var g = Node.Group("torpedo_launcher");
            var holder = Poly(-0.068f, -0.032f, 0.068f, -0.032f, 0.068f, 0.032f, -0.068f, 0.032f);
            holder.holes.Add(HoleCircle(-0.031, 0, 0.0265));
            holder.holes.Add(HoleCircle(0.031, 0, 0.0265));
            foreach (float z in new[] { -0.05f, 0.02f })
            {
                g.Add(Mesh(PlateGeo(holder, 0.014f, new Vector3(0, 0, z), Vector3.right, Vector3.up, 12), M["dWhite"], "torpedo_holder"));
                foreach (float x in new[] { -0.074f, 0.074f }) g.Add(At(Mesh(ctx.Geo("tlBlock", () => Geo.Box(0.012f, 0.05f, 0.02f)), M["dAnod"]), x, 0, z));
            }
            g.Add(At(Mesh(Geo.Box(0.15f, 0.005f, 0.11f), M["dAnod"]), 0, -0.035f, -0.015f));
            var rail = ctx.Geo("tlRail", () => CylZ(0.0032f, 0.0032f, 0.12f, 10));
            foreach (float x in new[] { -0.058f, 0.058f })
                foreach (float y in new[] { -0.024f, 0.024f })
                    g.Add(At(Mesh(rail, M["dChrome"]), x, y, -0.015f));
            var rod = ctx.Geo("tlRod", () => CylZ(0.003f, 0.003f, 0.11f, 10));
            foreach (float x in new[] { -0.031f, 0.031f }) g.Add(At(Mesh(rod, M["dRed"]), x, 0.029f, -0.015f));
            g.Add(At(Mesh(Geo.Box(0.03f, 0.016f, 0.04f), M["dAnod"]), 0, 0.04f, -0.015f));        // release servo
            g.Add(At(Mesh(Geo.Box(0.008f, 0.004f, 0.03f), M["dT200"]), 0, 0.05f, -0.015f));       // servo horn

            var loaded = new Node[2];
            for (int i = 0; i < 2; i++)
            {
                var torp = ItemNode("torpedo", "#f4f4f4"); // white, as on the real launcher
                torp.name = "torpedo_" + i;
                torp.position = new Vector3((i == 0 ? -1 : 1) * 0.031f, 0, -0.012f);
                g.Add(torp);
                loaded[i] = torp;
            }
            // tucked under the front porthole, hanging from the frame's top plate, noses at the launch point
            g.scale = Vector3.one * 0.85f;
            Add(root, g, B(b.x - 0.06, b.y, b.z));
            return loaded;
        }
    }
}
