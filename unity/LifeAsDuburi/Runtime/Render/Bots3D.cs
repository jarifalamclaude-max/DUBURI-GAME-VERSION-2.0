// src/render/bots3d.js: 3D models of the player vehicles and of the game items (markers, torpedoes,
// cup, spoon), built from three.js primitives at real scale (metres).
//
// Model frame: body FRD point b = [x fwd, y right, z down] -> (b[1], -b[2], -b[0]),
// i.e. the model faces -Z, +Y is up, +X is right, origin at the CG (three.js space, under the
// mirrored World root).

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;

namespace Duburi.Game
{
    /// state = { gripClosed, holding, markers: 0..2, torpedoes: 0..2 }
    public sealed class BotState
    {
        public bool gripClosed, holding;
        public int markers = 2, torpedoes = 2;
    }

    /// createBotMesh() result: { group, update(dt, veh, state), parts }
    public sealed class BotMesh
    {
        public GameObject group;
        public Transform[] props;
        public Transform gripper, gripPoint;
        public Transform[] fingers;
        public GameObject[] markers, torpedoes;
        internal Owned owned = new Owned();
        float open = 1;   // smoothed claw opening 0..1
        float[] spin;
        static readonly BotState Idle = new BotState();

        /// group.position / quaternion in three.js space (the World root's coordinates)
        public void SetPose(Vector3 threePos, Quaternion threeQuat)
        {
            var t = group.transform;
            t.localPosition = threePos;
            t.localRotation = threeQuat;
        }

        Renderer[] renderers;
        /// every renderer of the vehicle (RenderHooks.CamState.hide / Views.HideFor)
        public Renderer[] Renderers => renderers ?? (renderers = group != null ? group.GetComponentsInChildren<Renderer>(true) : new Renderer[0]);

        public bool Visible
        {
            get => group.activeSelf;
            set { if (group.activeSelf != value) group.SetActive(value); }
        }

        /// update(dt, veh, state): props spin with the rotor speed, claw and payloads follow the state
        public void Update(float dt, Vehicle veh, BotState state)
        {
            if (veh != null && veh.th != null)
                for (int i = 0; i < props.Length; i++) if (i < veh.th.Length && veh.th[i] != null) Spin(i, (float)veh.th[i].n, dt);
            Payloads(dt, state);
        }

        /// the same with rotor speeds given directly (the menu backdrop's { n: 0.6 } thrusters)
        public void Update(float dt, IList<double> n, BotState state)
        {
            if (n != null) for (int i = 0; i < props.Length && i < n.Count; i++) Spin(i, (float)n[i], dt);
            Payloads(dt, state);
        }

        void Spin(int i, float n, float dt)
        {
            spin[i] += n * 60 * dt;
            props[i].localRotation = Space.Euler(0, 0, spin[i]);
        }

        void Payloads(float dt, BotState state)
        {
            if (state == null) return;
            bool holding = state.holding;
            float target = holding || state.gripClosed ? 0 : 1;
            open += (target - open) * Mathf.Min(1, dt * 10);
            float a = (holding ? 0.22f : 0.02f) + 0.5f * open;
            fingers[0].localRotation = Space.Euler(0, a, 0);     // left finger swings to -X when opening
            fingers[1].localRotation = Space.Euler(0, -a, 0);
            int nm = state.markers, nt = state.torpedoes;
            for (int i = 0; i < markers.Length; i++) { bool v = i < nm; if (markers[i].activeSelf != v) markers[i].SetActive(v); }
            for (int i = 0; i < torpedoes.Length; i++) { bool v = i < nt; if (torpedoes[i].activeSelf != v) torpedoes[i].SetActive(v); }
        }

        internal void Init()
        {
            spin = new float[props.Length];
            Payloads(0, Idle);
        }

        /// disposeObject(group)
        public void Dispose()
        {
            if (group != null) UnityEngine.Object.Destroy(group);
            owned.DestroyAll();
            group = null;
            renderers = null;
        }
    }

    public static partial class Bots3D
    {
        const float PI = Mathf.PI;
        static readonly Vector3 Z_AXIS = new Vector3(0, 0, 1);
        static readonly Vector3 Y_AXIS = new Vector3(0, 1, 0);

        // body FRD -> model coords
        static Vector3 P(V3 b) => Space.Body(b);
        static Vector3 P(double x, double y, double z) => Space.Body(x, y, z);
        static Node Place(Node o, V3 b) { o.position = P(b); return o; }
        static Node Add(Node parent, Node o, V3? b = null) { if (b.HasValue) Place(o, b.Value); parent.Add(o); return o; }
        static Node Add(Node parent, Node o, double x, double y, double z) { o.position = P(x, y, z); parent.Add(o); return o; }

        // ---------------------------------------------------------------- materials

        static Mat3 Std(Color linear, float metalness, float roughness) => new Mat3 { color = linear, metalness = metalness, roughness = roughness };
        static Mat3 Std(int hex, float metalness, float roughness) => Std(Colors.Rgb(hex).linear, metalness, roughness);

        /// Per-bot build context: materials + a geometry cache so parts are reused.
        sealed class Ctx
        {
            public readonly Dictionary<string, Mat3> M = new Dictionary<string, Mat3>();
            readonly Dictionary<string, Geo> geos = new Dictionary<string, Geo>();
            public Color color;   // linear
            public string hex;    // the player colour as given ("#rrggbb"), for payload items
            public Geo Geo(string key, Func<Geo> make)
            {
                if (!geos.TryGetValue(key, out var g)) { g = make(); geos[key] = g; }
                return g;
            }
        }

        static Ctx MakeCtx(string color)
        {
            Color c = Colors.Hex(color).linear;
            var ctx = new Ctx { color = c, hex = color };
            var M = ctx.M;
            M["team"] = Std(c, 0.1f, 0.45f);
            M["teamDark"] = Std(c * 0.42f, 0.1f, 0.6f);
            M["foam"] = Std(c, 0, 0.85f);
            M["black"] = Std(0x16181b, 0, 0.75f);            // HDPE / plastic
            M["plastic"] = Std(0x2a2e34, 0.1f, 0.5f);
            M["alu"] = Std(0xb9c0c8, 0.6f, 0.35f);
            M["anod"] = Std(0x3b4048, 0.5f, 0.4f);
            M["prop"] = Std(0x2b323b, 0.1f, 0.45f).Double();
            M["tunnel"] = Std(0x060708, 0, 0.95f).Double();
            M["acrylic"] = Std(0xd8ecff, 0, 0.05f).Transparent(0.22f, false).Double();
            M["pcb"] = Std(0x1d7a3c, 0.1f, 0.55f);
            M["chip"] = Std(0x101114, 0.2f, 0.4f);
            M["blue"] = Std(0x2b6cb0, 0.2f, 0.5f);
            M["lens"] = Std(0x04060a, 0.6f, 0.06f);
            M["white"] = Std(0xf2f2f2, 0, 0.5f);
            M["led"] = new Mat3 { color = Color.white, roughness = 0.3f }.Emissive(0xfff2d8, 3);
            M["halo"] = new Mat3 { kind = MatKind.Basic, color = Colors.Rgb(0xfff0c8).linear, additive = true, transparent = true, opacity = 0.3f, depthWrite = false };
            return ctx;
        }

        static Node Mesh(Geo g, Mat3 m, string name = "") => Node.Mesh(g, m, name);

        // Cylinder along model Z (body x), centred.
        static Geo CylZ(float rt, float rb, float h, int seg, bool open = false) => Geo.Cylinder(rt, rb, h, seg, 1, open).RotateX(PI / 2);

        // Point a child's +Z along a body-frame direction.
        static void Orient(Node o, V3 axisBody) => o.quaternion = Space.FromUnitVectors(Z_AXIS, P(axisBody).normalized);

        // Rod between two body points (unit cylinder scaled along its length).
        static Node Rod(Ctx ctx, Node parent, V3 a, V3 b, float r, Mat3 mat)
        {
            var g = ctx.Geo("rod" + r.ToString("R"), () => Geo.Cylinder(r, r, 1, 8));
            var m = Mesh(g, mat);
            Vector3 v = P(a), w = P(b);
            m.position = (v + w) * 0.5f;
            w -= v;
            m.scale = new Vector3(1, w.magnitude, 1);
            m.quaternion = Space.FromUnitVectors(Y_AXIS, w.normalized);
            parent.Add(m);
            return m;
        }

        static V3 B(double x, double y, double z) => new V3(x, y, z);

        // ---------------------------------------------------------------- thrusters

        // Twisted 3-blade propeller in the XY plane, spinning about Z.
        static Geo PropGeometry(float R, float hand = 1, int blades = 3)
        {
            var g = new Geo();
            double rh = R * 0.3, pitch = 0.55 * (hand != 0 ? hand : 1);
            const int seg = 4;
            Vector3 Pt(double a, double rr, double side)
            {
                double k = (rr - rh) / (R - rh);
                double c = R * 0.5 * (1 - 0.45 * k);                 // chord
                double ph = pitch * (1 - 0.4 * k);                   // twist
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double t = side * c * 0.5;
                return new Vector3((float)(ca * rr - sa * t * Math.Cos(ph)), (float)(sa * rr + ca * t * Math.Cos(ph)), (float)(t * Math.Sin(ph)));
            }
            for (int b = 0; b < blades; b++)
            {
                double a = b * 2 * Math.PI / blades;
                for (int s = 0; s < seg; s++)
                {
                    double r0 = rh + (R - rh) * s / seg, r1 = rh + (R - rh) * (s + 1) / seg;
                    Vector3 p00 = Pt(a, r0, -1), p01 = Pt(a, r0, 1), p10 = Pt(a, r1, -1), p11 = Pt(a, r1, 1);
                    g.pos.Add(p00); g.pos.Add(p10); g.pos.Add(p11); g.pos.Add(p00); g.pos.Add(p11); g.pos.Add(p01);
                }
            }
            g.ComputeVertexNormals();
            g.EnsureUV();
            return g;
        }

        // Thruster sizes per preset: duct outer radius, duct length.
        static readonly Dictionary<string, float[]> TH_SIZE = new Dictionary<string, float[]>
        {
            ["t200-16v"] = new[] { 0.05f, 0.07f }, ["t500-20v"] = new[] { 0.075f, 0.1f }, ["generic-small"] = new[] { 0.03f, 0.042f },
        };

        sealed class ThOpt
        {
            public float[] size;
            public float[] tunnel;    // [dn, dp]
        }

        static Vector2 V(double x, double y) => new Vector2((float)x, (float)y);

        // Ducted thruster with local +Z = thrust axis. Returns { group, prop }.
        static Node MakeThruster(Ctx ctx, ThrusterDef t, int i, ThOpt opt, out Node prop)
        {
            float[] size = opt?.size ?? (t.preset != null && TH_SIZE.TryGetValue(t.preset, out var s0) ? s0 : TH_SIZE["t200-16v"]);
            float R = size[0], L = size[1];
            float hand = (float)(t.hand ?? 1);
            var M = ctx.M;
            var g = Node.Group("thruster_" + i);
            float th = R * 0.13f, rh = R * 0.26f;
            string rk = R.ToString("R");

            prop = Mesh(ctx.Geo("prop" + rk + hand, () => PropGeometry(R - th - 0.002f, hand)), M["prop"], "propeller_" + i);
            var hub = Mesh(ctx.Geo("hub" + rk, () => Geo.Lathe(new[] {
                V(0, -L * 0.42), V(rh * 0.7, -L * 0.42), V(rh, -L * 0.3),
                V(rh, L * 0.25), V(rh * 0.55, L * 0.42), V(0, L * 0.46),
            }, 8).RotateX(PI / 2)), M["black"]);

            if (opt?.tunnel != null)
            {
                // Tunnel through a hull: dark openings with rims; prop sits at the -axis opening.
                float dn = opt.tunnel[0], dp = opt.tunnel[1];
                var ring = ctx.Geo("trim" + rk, () => Geo.Ring(R * 0.92f, R * 1.18f, 20));
                var disc = ctx.Geo("tdisc" + rk, () => Geo.Circle(R * 0.93f, 20));
                foreach (var (d, s) in new[] { (-dn, -1f), (dp, 1f) })
                {
                    var r = Mesh(ring, M["plastic"]); r.position = new Vector3(0, 0, d + s * 0.0015f); if (s < 0) r.rotY = PI; g.Add(r);
                    var k = Mesh(disc, M["tunnel"]); k.position = new Vector3(0, 0, d); if (s < 0) k.rotY = PI; g.Add(k);
                }
                prop.position = new Vector3(0, 0, -dn - 0.003f);
                hub.scale = Vector3.one * 0.35f; hub.position = new Vector3(0, 0, -dn - 0.002f);
                g.Add(prop, hub);
                return g;
            }

            var duct = Mesh(ctx.Geo("duct" + rk, () => Geo.Lathe(new[] {
                V(R - th, -L / 2), V(R - th * 0.3, -L / 2), V(R, -L * 0.25),
                V(R, L / 2), V(R - th, L / 2), V(R - th, -L / 2),
            }, 16).RotateX(PI / 2)), M["black"]);
            // three stator struts hub -> duct
            var strut = ctx.Geo("strut" + rk, () => Geo.Box(0.004f, R - th - rh + 0.004f, L * 0.14f));
            for (int k = 0; k < 3; k++)
            {
                var s = Mesh(strut, M["black"]);
                float a = k * 2 * PI / 3 + PI / 6;
                float rm = (rh + R - th) / 2;
                s.position = new Vector3(Mathf.Cos(a) * rm, Mathf.Sin(a) * rm, L * 0.3f);
                s.rotZ = a - PI / 2;
                g.Add(s);
            }
            prop.position = new Vector3(0, 0, -L * 0.08f);
            g.Add(duct, hub, prop);
            return g;
        }

        static List<Node> AddThrusters(Ctx ctx, Node root, BotDesign design, Func<ThrusterDef, int, ThOpt> opts = null)
        {
            var props = new List<Node>();
            for (int i = 0; i < design.thrusters.Length; i++)
            {
                var t = design.thrusters[i];
                var group = MakeThruster(ctx, t, i, opts?.Invoke(t, i), out var prop);
                Place(group, t.pos);
                Orient(group, t.axis);
                root.Add(group);
                props.Add(prop);
            }
            return props;
        }

        // ---------------------------------------------------------------- sensors

        sealed class SensorSpec
        {
            public V3 cam; public float camR = 0.014f;
            public V3[] leds = new V3[0]; public float ledR = 0.011f;
            public V3? down;
        }

        // Front camera lens, LED lights and a downward camera.
        static void AddSensors(Ctx ctx, Node root, SensorSpec sp)
        {
            var M = ctx.M;
            float camR = sp.camR, ledR = sp.ledR;
            var camG = Node.Group("camera_front");
            camG.Add(Mesh(ctx.Geo("bezel" + camR.ToString("R"), () => CylZ(camR * 1.3f, camR * 1.3f, camR * 0.9f, 16)), M["anod"]));
            var glass = Mesh(ctx.Geo("lensG" + camR.ToString("R"), () => Geo.Sphere(camR, 16, 8, 0, 2 * PI, 0, PI / 2).RotateX(-PI / 2)), M["lens"]);
            glass.position = new Vector3(0, 0, -camR * 0.45f); glass.scale = new Vector3(1, 1, 0.6f);
            camG.Add(glass);
            Add(root, camG, sp.cam);

            for (int i = 0; i < sp.leds.Length; i++)
            {
                var l = Node.Group("led_" + i);
                l.Add(Mesh(ctx.Geo("ledH" + ledR.ToString("R"), () => CylZ(ledR, ledR, ledR * 1.8f, 12)), M["alu"]));
                var face = Mesh(ctx.Geo("ledF" + ledR.ToString("R"), () => Geo.Circle(ledR * 0.8f, 12)), M["led"]);
                face.position = new Vector3(0, 0, -ledR * 0.91f); face.rotY = PI;
                var halo = Mesh(ctx.Geo("ledHalo" + ledR.ToString("R"), () => Geo.Sphere(ledR * 2, 10, 6)), M["halo"]);
                halo.position = new Vector3(0, 0, -ledR * 1.2f); halo.renderOrder = 2;
                l.Add(face, halo);
                Add(root, l, sp.leds[i]);
            }

            if (sp.down.HasValue)
            {
                var d = Node.Group("camera_down");
                d.Add(Mesh(ctx.Geo("dcamH", () => Geo.Cylinder(0.016f, 0.016f, 0.014f, 14)), M["anod"]));
                var lens = Mesh(ctx.Geo("dcamL", () => Geo.Sphere(0.011f, 12, 6, 0, 2 * PI, PI / 2, PI / 2)), M["lens"]);
                lens.position = new Vector3(0, -0.007f, 0); lens.scale = new Vector3(1, 0.6f, 1);
                d.Add(lens);
                Add(root, d, sp.down.Value);
            }
        }

        // ---------------------------------------------------------------- payloads

        sealed class GripSpec { public float s = 1, arm = 0.04f, post = 0.03f; }

        // Gripper claw at body point; jaws open sideways, pointing forward (-Z).
        static Node AddGripper(Ctx ctx, Node root, V3 b, GripSpec gs, out Node[] fingers, out Node tipPt)
        {
            var M = ctx.M;
            float s = gs.s, arm = gs.arm, post = gs.post;
            string sk = s.ToString("R");
            var g = Node.Group("gripper");
            g.Add(Mesh(ctx.Geo("gBase" + sk, () => Geo.Box(0.042f * s, 0.026f * s, 0.03f * s)), M["anod"]));
            if (arm > 0)
            {
                var a = Mesh(ctx.Geo("gArm" + sk, () => Geo.Box(0.014f * s, 0.014f * s, 1)), M["alu"]);
                a.scale = new Vector3(1, 1, arm); a.position = new Vector3(0, 0, arm / 2); g.Add(a);
            }
            if (post > 0)
            {
                var p = Mesh(ctx.Geo("gPost" + sk, () => Geo.Box(0.014f * s, 1, 0.014f * s)), M["alu"]);
                p.scale = new Vector3(1, post, 1); p.position = new Vector3(0, post / 2, arm); g.Add(p);
            }
            var link = ctx.Geo("gLink" + sk, () => Geo.Box(0.007f * s, 0.02f * s, 0.05f * s).Translate(0, 0, -0.025f * s));
            var tip = ctx.Geo("gTip" + sk, () => Geo.Box(0.007f * s, 0.02f * s, 0.024f * s).Translate(0, 0, -0.012f * s));
            fingers = new Node[2];
            for (int k = 0; k < 2; k++)
            {
                float side = k == 0 ? -1 : 1;
                var f = Node.Group(side < 0 ? "finger_left" : "finger_right");
                f.position = new Vector3(side * 0.013f * s, 0, -0.015f * s);
                f.Add(Mesh(link, M["alu"]));
                var t = Mesh(tip, M["black"]);
                t.position = new Vector3(0, 0, -0.048f * s); t.rotY = side * 0.7f;    // tips bend inward
                f.Add(t);
                g.Add(f);
                fingers[k] = f;
            }
            tipPt = Node.Object3D("grip_point");
            tipPt.position = new Vector3(0, 0, -0.065f * s);
            g.Add(tipPt);
            Add(root, g, b);
            return g;
        }

        // Marker dropper: two horizontal markers side by side under a holder plate.
        static Node[] AddMarkers(Ctx ctx, Node root, V3 b, string color)
        {
            var g = Node.Group("marker_dropper");
            var plate = Mesh(ctx.Geo("mPlate", () => Geo.Box(0.12f, 0.006f, 0.17f)), ctx.M["black"]);
            plate.position = new Vector3(0, 0.03f, 0); g.Add(plate);
            var rail = ctx.Geo("mRail", () => Geo.Box(0.004f, 0.05f, 0.17f));
            foreach (float x in new[] { -0.06f, 0f, 0.06f }) { var r = Mesh(rail, ctx.M["black"]); r.position = new Vector3(x, 0.005f, 0); g.Add(r); }
            var loaded = new Node[2];
            float[] xs = { -0.03f, 0.03f };
            for (int i = 0; i < 2; i++)
            {
                var m = ItemNode("marker", color);
                m.name = "marker_" + i; m.rotX = PI / 2; m.position = new Vector3(xs[i], 0, 0);
                g.Add(m);
                loaded[i] = m;
            }
            Add(root, g, b);
            return loaded;
        }

        sealed class TorpSpec { public char layout = 'h'; public V3 off; }

        // Two torpedo tubes ('v' = stacked, 'h' = side by side), pointing forward.
        static Node[] AddTorpedoes(Ctx ctx, Node root, V3 b, string color, TorpSpec ts)
        {
            var g = Node.Group("torpedo_launcher");
            var tube = ctx.Geo("tTube", () => CylZ(0.029f, 0.029f, 0.16f, 16, true));
            var lip = ctx.Geo("tLip", () => Geo.Ring(0.026f, 0.033f, 16));
            var loaded = new Node[2];
            for (int i = 0; i < 2; i++)
            {
                float d = (i == 0 ? -1 : 1) * 0.031f;
                var t = Mesh(tube, ctx.M["anod"]);
                var l = Mesh(lip, ctx.M["black"]); l.position = new Vector3(0, 0, -0.0801f); l.rotY = PI;
                var tg = Node.Group(); tg.Add(t, l);
                if (ts.layout == 'v') tg.position = new Vector3(0, d, 0); else tg.position = new Vector3(d, 0, 0);
                var torp = ItemNode("torpedo", color);
                torp.name = "torpedo_" + i; torp.position = new Vector3(0, 0, -0.012f);
                tg.Add(torp);
                g.Add(tg);
                loaded[i] = torp;
            }
            Add(root, g, B(b.x + ts.off.x, b.y + ts.off.y, b.z + ts.off.z));
            return loaded;
        }

        // Logo decal plane (width w, aspect ~2.6). rotY turns the +Z face outward.
        static Node LogoPlane(Ctx ctx, Texture2D tex, float w, float rotY)
        {
            float aspect = tex != null && tex.width > 0 && tex.height > 0 ? tex.width / (float)tex.height : 2.6f;
            if (!ctx.M.ContainsKey("logo"))
            {
                // polygonOffset of the JS: the plane already sits 0.7-0.8 mm off the surface
                ctx.M["logo"] = new Mat3 { map = tex, transparent = true, roughness = 0.55f, metalness = 0 };
            }
            var m = Mesh(Geo.Plane(w, w / aspect), ctx.M["logo"], "logo");
            m.rotY = rotY;
            return m;
        }

        // ---------------------------------------------------------------- vehicles

        sealed class Spec
        {
            public List<Node> props;
            public SensorSpec sensors;     // null: the model builds its own cameras
            public GripSpec grip;
            public TorpSpec torp;
            public Node[] torpedoes;       // non-null: the model built its own launcher (two loaded torpedoes)
        }

        static Spec BuildBarracuda(Ctx ctx, Node root, BotDesign d, Texture2D tex)
        {
            var M = ctx.M;
            const float R = 0.085f;
            Add(root, Mesh(CylZ(R, R, 0.78f, 28), M["team"], "hull"), 0.01, 0, 0);
            var nose = Mesh(Geo.Sphere(1, 28, 10, 0, 2 * PI, 0, PI / 2).RotateX(-PI / 2), M["team"], "nose");
            nose.scale = new Vector3(R, R, 0.15f);
            Add(root, nose, 0.4, 0, 0);
            Add(root, Mesh(CylZ(R, 0.03f, 0.14f, 28), M["anod"], "tail_cone"), -0.45, 0, 0);

            // tail fins in a + pattern
            var finGeo = ctx.Geo("fin", () =>
            {
                var s = new Shape2();
                s.MoveTo(-0.38, 0.06); s.LineTo(-0.5, 0.03); s.LineTo(-0.535, 0.13); s.LineTo(-0.475, 0.13); s.ClosePath();
                return Geo.Extrude(s, new ExtrudeOptions { depth = 0.005, bevelEnabled = false }).Translate(0, 0, -0.0025f).RotateY(PI / 2);
            });
            for (int k = 0; k < 4; k++) { var f = Mesh(finGeo, M["teamDark"]); f.rotZ = k * PI / 2; root.Add(f); }

            // logo stripe: white band with the logo on each flank
            Add(root, Mesh(CylZ(R + 0.0008f, R + 0.0008f, 0.2f, 28, true), M["white"], "stripe"), 0.05, 0, 0);
            if (tex != null)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    var l = LogoPlane(ctx, tex, 0.17f, side * PI / 2);
                    l.position = new Vector3(side * (R + 0.0015f), 0, -0.05f);
                    root.Add(l);
                }
            }

            // main prop in a shroud at the stern; others are tunnel thrusters through the hull
            var props = AddThrusters(ctx, root, d, (t, i) => i == 0
                ? new ThOpt { size = new[] { 0.055f, 0.075f } }
                : new ThOpt { size = new[] { 0.028f, 0.05f }, tunnel = new[] { R + 0.0005f, R + 0.0005f } });
            return new Spec
            {
                props = props,
                sensors = new SensorSpec { cam = B(0.548, 0, 0), camR = 0.016f, leds = new[] { B(0.525, 0.035, 0.02), B(0.525, -0.035, 0.02) }, down = B(0.25, 0, 0.087) },
                grip = new GripSpec { s = 1, arm = 0.03f, post = 0.05f },
                torp = new TorpSpec { layout = 'h', off = B(-0.05, 0, -0.03) },
            };
        }

        static Spec BuildKraken(Ctx ctx, Node root, BotDesign d)
        {
            var M = ctx.M;
            double X = 0.37, Y = 0.3, Z = 0.22;
            const float r = 0.012f;
            var alu = M["alu"];
            // 12 frame edges
            foreach (int sy in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Rod(ctx, root, B(-X, sy * Y, sz * Z), B(X, sy * Y, sz * Z), r, alu);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Rod(ctx, root, B(sx * X, -Y, sz * Z), B(sx * X, Y, sz * Z), r, alu);
            foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) Rod(ctx, root, B(sx * X, sy * Y, -Z), B(sx * X, sy * Y, Z), r, alu);
            // cross members
            foreach (double x in new[] { -0.12, 0.12 }) Rod(ctx, root, B(x, -Y, -Z), B(x, Y, -Z), r, alu);
            foreach (double x in new[] { -0.2, 0.2 }) Rod(ctx, root, B(x, -Y, Z), B(x, Y, Z), r, alu);
            foreach (int sy in new[] { -1, 1 })
            {
                Rod(ctx, root, B(0, sy * Y, -Z), B(0, sy * Y, Z), r, alu);
                Rod(ctx, root, B(0, sy * 0.187, -0.102), B(0, sy * Y, -0.102), r * 0.8f, alu);   // vertical thruster mounts
                foreach (int sx in new[] { -1, 1 }) Rod(ctx, root, B(sx * 0.265, sy * 0.189, 0), B(sx * X, sy * Y, 0), r * 0.8f, alu);
            }
            // two big hull tubes with end caps, clamped to the bottom cross members
            var hull = ctx.Geo("hull", () => CylZ(0.07f, 0.07f, 0.44f, 24));
            var cap = ctx.Geo("hcap", () => CylZ(0.075f, 0.075f, 0.025f, 24));
            foreach (double y in new[] { -0.14, 0.14 })
            {
                Add(root, Mesh(hull, M["anod"], "hull_tube"), 0, y, 0.13);
                Add(root, Mesh(cap, M["black"]), 0.225, y, 0.13);
                Add(root, Mesh(cap, M["black"]), -0.225, y, 0.13);
            }
            // foam on top in two blocks (gap over the vertical thrusters)
            var foam = ctx.Geo("foam", () => Geo.Box(0.58f, 0.07f, 0.24f));
            foreach (double x in new[] { -0.21, 0.21 }) Add(root, Mesh(foam, M["foam"], "foam"), x, 0, -0.195);
            // front camera housing
            Add(root, Mesh(CylZ(0.03f, 0.03f, 0.08f, 16), M["acrylic"]), 0.33, 0, 0);
            return new Spec
            {
                props = AddThrusters(ctx, root, d),
                sensors = new SensorSpec { cam = B(0.375, 0, 0), camR = 0.02f, ledR = 0.015f, leds = new[] { B(0.38, 0.22, -0.06), B(0.38, -0.22, -0.06) }, down = B(0.25, 0, 0.235) },
                grip = new GripSpec { s = 1.5f, arm = 0.05f, post = 0.0f },
                torp = new TorpSpec { layout = 'v', off = B(0.03, 0, 0.02) },
            };
        }

        static Spec BuildManta(Ctx ctx, Node root, BotDesign d)
        {
            var M = ctx.M;
            float sx = 0.42f, sy = 0.09f, sz = 0.31f;
            var top = Mesh(Geo.Sphere(1, 32, 8, 0, 2 * PI, 0, PI / 2), M["team"], "body_top");
            var bot = Mesh(Geo.Sphere(1, 32, 5, 0, 2 * PI, PI / 2, PI / 2), M["teamDark"], "body_bottom");
            top.scale = new Vector3(sx, sy, sz); bot.scale = new Vector3(sx, sy * 0.8f, sz);
            root.Add(top, bot);
            // smoked canopy over the electronics bay
            var can = Mesh(Geo.Sphere(1, 20, 8, 0, 2 * PI, 0, PI / 2), M["acrylic"], "canopy");
            can.scale = new Vector3(0.11f, 0.05f, 0.17f); can.position = new Vector3(0, 0.06f, -0.03f); root.Add(can);
            Add(root, Mesh(Geo.Box(0.08f, 0.03f, 0.14f), M["chip"]), 0.03, 0, -0.07);
            // tail fin
            var fin = new Shape2();
            fin.MoveTo(0, 0); fin.LineTo(0.12, 0); fin.LineTo(0.16, 0.09); fin.LineTo(0.12, 0.09); fin.ClosePath();
            var fm = Mesh(Geo.Extrude(fin, new ExtrudeOptions { depth = 0.006, bevelEnabled = false }).Translate(0, 0, -0.003f).RotateY(-PI / 2), M["teamDark"], "tail_fin");
            fm.position = new Vector3(0, 0.035f, 0.14f); root.Add(fm);
            // wingtip fairings around the vertical thrusters
            var fair = ctx.Geo("fair", () => Geo.Torus(0.056f, 0.008f, 4, 16).RotateX(PI / 2));
            foreach (var t in d.thrusters) if (t.axis.z != 0) Add(root, Mesh(fair, M["teamDark"]), t.pos);
            return new Spec
            {
                props = AddThrusters(ctx, root, d),
                sensors = new SensorSpec { cam = B(0.305, 0, 0.005), camR = 0.013f, leds = new[] { B(0.285, 0.09, 0.005), B(0.285, -0.09, 0.005) }, down = B(0.18, 0, 0.065) },
                grip = new GripSpec { s = 1.2f, arm = 0.03f, post = 0.04f },
                torp = new TorpSpec { layout = 'h', off = B(-0.05, 0, 0.04) },
            };
        }

        static Spec BuildDuboMini(Ctx ctx, Node root, BotDesign d)
        {
            var M = ctx.M;
            var body = Mesh(Geo.Capsule(0.07f, 0.2f, 6, 18).RotateX(PI / 2), M["team"], "body");
            root.Add(body);
            Add(root, Mesh(CylZ(0.0705f, 0.0705f, 0.05f, 20, true), M["teamDark"], "band"), -0.03, 0, 0);
            // big cute camera eye
            var eye = Node.Group("eye");
            eye.Add(Mesh(CylZ(0.046f, 0.046f, 0.014f, 24), M["white"]));
            var dome = Mesh(Geo.Sphere(0.04f, 20, 8, 0, 2 * PI, 0, PI / 2).RotateX(-PI / 2), M["acrylic"]);
            dome.position = new Vector3(0, 0, -0.007f); eye.Add(dome);
            var glint = Mesh(Geo.Sphere(0.005f, 8, 6), M["led"]);
            glint.position = new Vector3(0.009f, 0.01f, -0.03f); eye.Add(glint);
            Add(root, eye, 0.15, 0, 0.02);
            // little dorsal fin
            var fin = new Shape2();
            fin.MoveTo(0, 0); fin.LineTo(0.08, 0); fin.LineTo(0.1, 0.045); fin.LineTo(0.07, 0.045); fin.ClosePath();
            var fm = Mesh(Geo.Extrude(fin, new ExtrudeOptions { depth = 0.004, bevelEnabled = false }).Translate(0, 0, -0.002f).RotateY(-PI / 2), M["teamDark"], "tail_fin");
            fm.position = new Vector3(0, 0.055f, 0.08f); root.Add(fm);
            var props = AddThrusters(ctx, root, d, (t, i) =>
            {
                if (t.name == "Lateral") return new ThOpt { size = new[] { 0.022f, 0.03f }, tunnel = new[] { 0.0705f, 0.0705f } };
                if (t.name == "Vert front") return new ThOpt { size = new[] { 0.022f, 0.03f }, tunnel = new[] { 0.057f, 0.077f } };
                if (t.name == "Vert rear") return new ThOpt { size = new[] { 0.022f, 0.03f }, tunnel = new[] { 0.0605f, 0.0805f } };
                return null;
            });
            return new Spec
            {
                props = props,
                sensors = new SensorSpec { cam = B(0.172, 0, 0.02), camR = 0.022f, ledR = 0.007f, leds = new[] { B(0.13, 0.055, -0.03), B(0.13, -0.055, -0.03) }, down = B(0.06, 0, 0.072) },
                grip = new GripSpec { s = 0.8f, arm = 0.02f, post = 0.02f },
                torp = new TorpSpec { layout = 'h', off = B(-0.05, 0, -0.03) },
            };
        }

        static Spec Build(string id, Ctx ctx, Node root, BotDesign d, Texture2D tex)
        {
            switch (id)
            {
                case "barracuda": return BuildBarracuda(ctx, root, d, tex);
                case "kraken": return BuildKraken(ctx, root, d);
                case "manta": return BuildManta(ctx, root, d);
                case "dubomini": return BuildDuboMini(ctx, root, d);
                default: return BuildDuburi(ctx, root, d, tex);
            }
        }

        // Merge every mesh that never moves relative to the hull into one mesh per
        // material: ~100 draw calls per bot become ~10, which matters with four bots
        // in four split-screen views.
        public static void MergeStatic(Node group, IList<Node> dynamic)
        {
            var dyn = new HashSet<Node>(dynamic);
            Matrix4x4 inv = group.WorldMatrix.inverse;
            var keys = new List<(Mat3 mat, int order)>();
            var buckets = new Dictionary<(Mat3, int), List<Geo>>();
            var victims = new List<Node>();
            group.Traverse(o =>
            {
                if (o == group || o.geo == null || o.geo.lines || o.mat == null) return;
                for (var p = o; p != null && p != group; p = p.parent) if (dyn.Contains(p)) return;
                var g = o.geo.idx != null ? o.geo.ToNonIndexed() : o.geo.Clone();
                g.col = null;
                if (g.nrm.Count < g.pos.Count) g.ComputeVertexNormals();
                g.EnsureUV();
                g.ApplyMatrix(inv * o.WorldMatrix);
                var key = (o.mat, o.renderOrder);
                if (!buckets.TryGetValue(key, out var list)) { list = new List<Geo>(); buckets[key] = list; keys.Add(key); }
                list.Add(g);
                victims.Add(o);
            });
            foreach (var o in victims)
            {
                // keep children that are dynamic: re-parent them to the group with their world transform
                foreach (var c in o.children.ToArray()) group.Attach(c);
                o.parent?.Remove(o);
            }
            // drop empty static containers
            var empties = new List<Node>();
            group.Traverse(o => { if (o != group && o.geo == null && o.children.Count == 0 && !dyn.Contains(o) && o.isGroup) empties.Add(o); });
            foreach (var e in empties) e.parent?.Remove(e);
            foreach (var key in keys)
            {
                var merged = Geo.Merge(buckets[key]);
                if (merged == null) continue;
                var m = Node.Mesh(merged, key.mat, "static");
                m.renderOrder = key.order;
                group.Add(m);
            }
        }

        /// The finished scene-graph tree of a vehicle (static parts merged per material) before it
        /// becomes GameObjects.
        internal sealed class BotTree
        {
            public Node group, grip, tip;
            public List<Node> props;
            public Node[] fingers, markers, torpedoes;
        }

        internal static BotTree BuildTree(BotDesign design, string color, Texture2D logoTexture)
        {
            color = string.IsNullOrEmpty(color) ? design.color : color;
            var ctx = MakeCtx(color);
            var group = Node.Group("bot_" + design.id);
            var spec = Build(design.id, ctx, group, design, logoTexture);

            if (spec.sensors != null) AddSensors(ctx, group, spec.sensors);
            var grip = AddGripper(ctx, group, design.gripper, spec.grip, out var fingers, out var tip);
            var markers = AddMarkers(ctx, group, design.marker, design.id == "duburi" ? "#f4f4f4" : color); // DUBURI: white, as on the real robot
            var torpedoes = spec.torpedoes ?? AddTorpedoes(ctx, group, design.torpedo, color, spec.torp);
            var props = spec.props;

            var dynamic = new List<Node>();
            dynamic.AddRange(props); dynamic.AddRange(fingers); dynamic.AddRange(markers); dynamic.AddRange(torpedoes);
            MergeStatic(group, dynamic);
            foreach (var d in dynamic) MergeStatic(d, new Node[0]);
            group.Traverse(o => { o.castShadow = false; o.receiveShadow = false; });
            return new BotTree { group = group, grip = grip, tip = tip, props = props, fingers = fingers, markers = markers, torpedoes = torpedoes };
        }

        /// createBotMesh(design, color, { logoTexture }): the vehicle under 'parent' (a mirrored World
        /// root or one of its children), positioned in three.js space with BotMesh.SetPose.
        public static BotMesh CreateBotMesh(BotDesign design, string color, Texture2D logoTexture, Transform parent, int layer = -1)
        {
            var t = BuildTree(design, color, logoTexture);
            var bm = new BotMesh();
            bm.group = t.group.Realize(parent, bm.owned, layer);
            bm.props = new Transform[t.props.Count];
            for (int i = 0; i < t.props.Count; i++) bm.props[i] = t.props[i].transform;
            bm.gripper = t.grip.transform;
            bm.gripPoint = t.tip.transform;
            bm.fingers = new[] { t.fingers[0].transform, t.fingers[1].transform };
            bm.markers = new[] { t.markers[0].transform.gameObject, t.markers[1].transform.gameObject };
            bm.torpedoes = new[] { t.torpedoes[0].transform.gameObject, t.torpedoes[1].transform.gameObject };
            bm.Init();
            return bm;
        }

        public static BotMesh CreateBotMesh(BotDesign design, Color color, Texture2D logoTexture, Transform parent, int layer = -1) =>
            CreateBotMesh(design, Colors.ToHex(color), logoTexture, parent, layer);

        // ---------------------------------------------------------------- items

        static Dictionary<string, Geo> IG;                                       // shared item geometries
        static readonly Dictionary<string, Mat3> IM = new Dictionary<string, Mat3>(); // shared item materials by key

        static Dictionary<string, Geo> ItemGeos()
        {
            if (IG != null) return IG;
            IG = new Dictionary<string, Geo>
            {
                ["marker"] = Geo.Box(0.051f, 0.152f, 0.051f),
                ["band"] = Geo.Box(0.0535f, 0.03f, 0.0535f),
                ["torp"] = Geo.Lathe(new[] {
                    V(0, -0.076), V(0.011, -0.076), V(0.019, -0.062), V(0.025, -0.04), V(0.025, 0.04),
                    V(0.023, 0.056), V(0.017, 0.068), V(0.008, 0.075), V(0, 0.076),
                }, 16).RotateX(-PI / 2),                                  // nose -> -Z
                ["torpBand"] = CylZ(0.0255f, 0.0255f, 0.012f, 16, true),
                ["finA"] = Geo.Box(0.075f, 0.002f, 0.026f),
                ["finB"] = Geo.Box(0.002f, 0.075f, 0.026f),
                ["cup"] = Geo.Lathe(new[] {
                    V(0, -0.045), V(0.032, -0.045), V(0.0455, 0.044), V(0.044, 0.046), V(0.0425, 0.044),
                    V(0.0295, -0.0425), V(0, -0.0425),
                }, 28),
                ["bowl"] = Geo.Sphere(1, 20, 6, 0, 2 * PI, PI / 2, PI / 2),
                ["handle"] = Geo.Box(0.018f, 0.005f, 0.22f),
                ["neck"] = Geo.Box(0.01f, 0.005f, 0.04f),
            };
            foreach (var g in IG.Values) g.shared = true;
            return IG;
        }

        static Mat3 ItemMat(string key, Func<Mat3> make)
        {
            if (!IM.TryGetValue(key, out var m)) { m = make(); IM[key] = m; }
            return m;
        }

        /// createItemMesh(kind, color) as a scene-graph node (origin at the item's centre)
        public static Node ItemNode(string kind, string color = "#ffb547")
        {
            var G = ItemGeos();
            var g = Node.Group("item_" + kind);
            Mat3 Team() => ItemMat("team" + color, () => Std(Colors.Hex(color).linear, 0.1f, 0.45f));
            var white = ItemMat("white", () => Std(0xf4f4f4, 0, 0.5f));
            if (kind == "marker")
            {
                g.Add(Node.Mesh(G["marker"], Team()), Node.Mesh(G["band"], white));
            }
            else if (kind == "torpedo")
            {
                var band = Node.Mesh(G["torpBand"], white); band.position = new Vector3(0, 0, -0.02f);
                var fa = Node.Mesh(G["finA"], white); var fb = Node.Mesh(G["finB"], white);
                fa.position = fb.position = new Vector3(0, 0, 0.058f);
                g.Add(Node.Mesh(G["torp"], Team()), band, fa, fb);
            }
            else if (kind == "cup")
            {
                g.Add(Node.Mesh(G["cup"], ItemMat("cup", () => Std(0xd62828, 0, 0.4f).Double())));
            }
            else if (kind == "spoon")
            {
                var silver = ItemMat("silver", () => Std(0xd5d9de, 0.85f, 0.25f).Double());
                var bowl = Node.Mesh(G["bowl"], silver);
                bowl.scale = new Vector3(0.035f, 0.014f, 0.05f); bowl.position = new Vector3(0, 0, -0.1f);
                var handle = Node.Mesh(G["handle"], silver); handle.position = new Vector3(0, 0.004f, 0.04f);
                var neck = Node.Mesh(G["neck"], silver); neck.position = new Vector3(0, 0.002f, -0.06f); neck.rotX = -0.1f;
                g.Add(bowl, handle, neck);
            }
            g.Traverse(o => o.castShadow = false);
            return g;
        }

        /// createItemMesh(kind, color): 'marker' | 'torpedo' | 'cup' | 'spoon' as a GameObject under
        /// parent (three.js space). Shares geometry and materials with every other item.
        public static GameObject CreateItemMesh(string kind, string color, Transform parent, int layer = -1)
        {
            var n = ItemNode(kind, string.IsNullOrEmpty(color) ? "#ffb547" : color);
            return n.Realize(parent, new Owned(), layer);
        }

        /// disposeObject(obj): destroys meshes and materials that are not shared, then the object
        /// (never textures).
        public static void DisposeObject(GameObject obj)
        {
            if (obj == null) return;
            foreach (var mf in obj.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m != null && !Geo.SharedMeshes.Contains(m)) UnityEngine.Object.Destroy(m);
            }
            foreach (var r in obj.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials) if (m != null && !Mats.IsShared(m)) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(obj);
        }
    }
}
