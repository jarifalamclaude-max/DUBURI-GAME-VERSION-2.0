// Port of src/sim/bots.js.
// The vehicles players can pick. Each is a physical description in the body
// FRD frame (x forward, y right, z down, origin at the CG): mass, inertia,
// added mass, linear + quadratic damping (Fossen form) and a thruster layout
// that runs through the simulator's own thruster model (T200 / T500 /
// generic, Physics/Thruster.cs) and allocation (Alloc/Allocation.cs).
// DUBURI uses the BlueROV2 Heavy geometry (ArduSub FRAME_CONFIG 2) and the
// hydrodynamic coefficients identified for it by Wu (2018); the others are
// scaled from it by size and shape.

using System.Collections.Generic;

namespace Duburi.Core
{
    /// collision sphere [x, y, z, r] in the body frame (m)
    public struct Sphere
    {
        public V3 c;
        public double r;
        public Sphere(double x, double y, double z, double r) { c = new V3(x, y, z); this.r = r; }
        /// JS index access s[0..3]
        public double this[int i] => i == 3 ? r : c[i];
    }

    /// JS BOTS entry
    public sealed class BotDesign
    {
        public string id, name, kind, blurb;
        public string color;          // "#rrggbb"
        public double mass;           // kg
        public double Bnet;           // N, net buoyancy in fresh water (design ballast)
        public V3 dims;               // m, length (x) / width (y) / height (z)
        public double zB;             // m, centre of buoyancy above the CG
        public V3 inertia;            // kg m^2 about x, y, z
        public double[] addedMass;    // [X, Y, Z (kg), K, M, N (kg m^2)]
        public double[] dampLin;      // [surge, sway, heave (N s/m), roll, pitch, yaw (N m s)]
        public double[] dampQuad;     // same axes, quadratic
        public ThrusterDef[] thrusters;
        public Sphere[] spheres;      // collision spheres (body)
        public V3 gripper, marker, torpedo; // payload mount points (body, m)
    }

    public static class Bots
    {
        const double S = JsMath.SQRT1_2;

        static ThrusterDef[] Map(List<ThrusterDef> L, System.Func<ThrusterDef, int, ThrusterDef> f)
        {
            var r = new ThrusterDef[L.Count];
            for (int i = 0; i < L.Count; i++) r[i] = f(L[i], i);
            return r;
        }

        static ThrusterDef With(ThrusterDef t, string preset, double? hand)
        {
            var c = t.Clone();
            c.preset = preset;
            c.hand = hand;
            return c;
        }

        static ThrusterDef T(string name, V3 pos, V3 axis, string preset, double? hand) => new ThrusterDef { name = name, pos = pos, axis = axis, preset = preset, hand = hand };

        static ThrusterDef[] KrakenThrusters()
        {
            // scale(vectoredLayout(), 1.7)
            var L = Frames.VectoredLayout();
            for (int i = 0; i < L.Count; i++) L[i].pos = new V3(L[i].pos.x * 1.7, L[i].pos.y * 1.7, L[i].pos.z * 1.7);
            // horizontal thrusters raised to the CG plane so full surge does not pitch it over
            return Map(L, (t, i) =>
            {
                var c = With(t, "t500-20v", i % 2 != 0 ? -1 : 1);
                c.pos = new V3(t.pos.x, t.pos.y, i < 4 ? 0 : t.pos.z);
                return c;
            });
        }

        static ThrusterDef[] MantaThrusters()
        {
            var L = new List<ThrusterDef>
            {
                T("H front-right", new V3(0.2, 0.26, 0.02), new V3(-S, S, 0), null, null),
                T("H front-left", new V3(0.2, -0.26, 0.02), new V3(-S, -S, 0), null, null),
                T("H rear-right", new V3(-0.2, 0.26, 0.02), new V3(S, S, 0), null, null),
                T("H rear-left", new V3(-0.2, -0.26, 0.02), new V3(S, -S, 0), null, null),
                T("V front-right", new V3(0.16, 0.36, -0.02), new V3(0, 0, 1), null, null),
                T("V front-left", new V3(0.16, -0.36, -0.02), new V3(0, 0, 1), null, null),
                T("V rear-right", new V3(-0.16, 0.36, -0.02), new V3(0, 0, 1), null, null),
                T("V rear-left", new V3(-0.16, -0.36, -0.02), new V3(0, 0, 1), null, null),
            };
            return Map(L, (t, i) => With(t, "t200-16v", i % 2 != 0 ? -1 : 1));
        }

        static ThrusterDef[] DuboMiniThrusters()
        {
            // surge + yaw (two drive thrusters), sway (tunnel), heave + pitch (two vertical tunnels); roll is passive
            var L = new List<ThrusterDef>
            {
                T("Fwd right", new V3(-0.12, 0.1, 0), new V3(1, 0, 0), null, 1),
                T("Fwd left", new V3(-0.12, -0.1, 0), new V3(1, 0, 0), null, -1),
                T("Lateral", new V3(0.02, 0, 0), new V3(0, 1, 0), null, 1),
                T("Vert front", new V3(0.12, 0, -0.01), new V3(0, 0, 1), null, 1),
                T("Vert rear", new V3(-0.08, 0, -0.01), new V3(0, 0, 1), null, -1),
            };
            return Map(L, (t, i) => With(t, "generic-small", t.hand));
        }

        public static readonly OrderedMap<string, BotDesign> BOTS = new OrderedMap<string, BotDesign>
        {
            ["duburi"] = new BotDesign
            {
                id = "duburi",
                name = "Duburi",
                kind = "Open frame · 8 × T200 vectored 6-DOF",
                blurb = "Team DUBURI's workhorse. Full 6-DOF control, good at everything. Rolls and flips cleanly for gate style.",
                color = "#ffb547",
                mass = 11.5, Bnet = 2.0, dims = new V3(0.46, 0.58, 0.26), zB = 0.02,
                inertia = new V3(0.26, 0.23, 0.37),
                addedMass = new[] { 6.36, 7.12, 18.68, 0.189, 0.135, 0.222 },
                dampLin = new double[] { 13.7, 8, 33, 0.4, 0.8, 0.5 },
                dampQuad = new[] { 141, 217, 190, 1.19, 0.47, 1.5 },
                thrusters = Map(Frames.HeavyLayout(), (t, i) => With(t, "t200-16v", i % 2 != 0 ? -1 : 1)),
                spheres = new[] { new Sphere(0.11, 0, 0, 0.21), new Sphere(-0.11, 0, 0, 0.21) },
                gripper = new V3(0.27, 0, 0.14), marker = new V3(0, 0, 0.15), torpedo = new V3(0.25, 0, 0.05),
            },
            ["barracuda"] = new BotDesign
            {
                id = "barracuda",
                name = "Barracuda",
                kind = "Torpedo AUV · 5 thrusters",
                blurb = "Long and slim: fastest in a straight line, but slow to turn and it cannot roll. Pitch flips only.",
                color = "#4fd1c5",
                mass = 16, Bnet = 1.5, dims = new V3(1.1, 0.17, 0.17), zB = 0.03,
                inertia = new V3(0.06, 1.65, 1.65),
                addedMass = new[] { 1.0, 24, 24, 0.01, 1.7, 1.7 },
                dampLin = new double[] { 3, 25, 25, 0.3, 4, 4 },
                dampQuad = new[] { 10, 160, 160, 0.1, 8, 8 },
                thrusters = new[]
                {
                    T("Main", new V3(-0.56, 0, 0), new V3(1, 0, 0), "t200-16v", 1),
                    T("Vert fwd", new V3(0.36, 0, 0), new V3(0, 0, 1), "t200-16v", 1),
                    T("Vert aft", new V3(-0.34, 0, 0), new V3(0, 0, 1), "t200-16v", -1),
                    T("Lat fwd", new V3(0.32, 0, 0), new V3(0, 1, 0), "t200-16v", 1),
                    T("Lat aft", new V3(-0.3, 0, 0), new V3(0, 1, 0), "t200-16v", -1),
                },
                spheres = new[] { new Sphere(0.4, 0, 0, 0.1), new Sphere(0.13, 0, 0, 0.1), new Sphere(-0.14, 0, 0, 0.1), new Sphere(-0.42, 0, 0, 0.1) },
                gripper = new V3(0.52, 0, 0.1), marker = new V3(0, 0, 0.1), torpedo = new V3(0.5, 0, -0.06),
            },
            ["kraken"] = new BotDesign
            {
                id = "kraken",
                name = "Kraken",
                kind = "Box frame · 6 × T500 vectored",
                blurb = "Heavy and very stable — shrugs off current. Huge thrust, but it is big and has no pitch control.",
                color = "#f56565",
                mass = 30, Bnet = 3.0, dims = new V3(0.76, 0.62, 0.46), zB = 0.06,
                inertia = new V3(0.9, 1.0, 1.3),
                addedMass = new[] { 14, 18, 40, 0.6, 0.6, 0.7 },
                dampLin = new[] { 25, 30, 60, 2, 2, 1.5 },
                dampQuad = new double[] { 260, 320, 350, 4, 4, 4 },
                thrusters = KrakenThrusters(),
                spheres = new[] { new Sphere(0.17, 0, 0, 0.32), new Sphere(-0.17, 0, 0, 0.32) },
                gripper = new V3(0.42, 0, 0.24), marker = new V3(0, 0, 0.25), torpedo = new V3(0.4, 0.2, 0.08),
            },
            ["manta"] = new BotDesign
            {
                id = "manta",
                name = "Manta",
                kind = "Flat wing · 8 × T200, low stability",
                blurb = "Wide and flat with small righting moment: the best stunt bot. Rolls fast, a bit twitchy in waves.",
                color = "#9f7aea",
                mass = 13, Bnet = 1.5, dims = new V3(0.62, 0.84, 0.18), zB = 0.012,
                inertia = new V3(0.42, 0.3, 0.65),
                addedMass = new[] { 5, 6, 30, 0.4, 0.25, 0.35 },
                dampLin = new[] { 12, 14, 45, 0.6, 0.6, 0.6 },
                dampQuad = new[] { 110, 140, 320, 2.2, 1.2, 1.4 },
                thrusters = MantaThrusters(),
                spheres = new[] { new Sphere(0.12, 0.22, 0, 0.14), new Sphere(0.12, -0.22, 0, 0.14), new Sphere(-0.12, 0.22, 0, 0.14), new Sphere(-0.12, -0.22, 0, 0.14) },
                gripper = new V3(0.32, 0, 0.1), marker = new V3(0, 0, 0.1), torpedo = new V3(0.3, 0.1, 0.02),
            },
            ["dubomini"] = new BotDesign
            {
                id = "dubomini",
                name = "Dubo Mini",
                kind = "Mini AUV · 5 small thrusters",
                blurb = "Team DUBURI's little one: two drive thrusters, a lateral tunnel and two vertical tunnels. Light, quick to strafe, does pitch flips — but the current pushes it around.",
                color = "#68d391",
                mass = 4.2, Bnet = 0.6, dims = new V3(0.34, 0.26, 0.16), zB = 0.014,
                inertia = new V3(0.03, 0.04, 0.05),
                addedMass = new[] { 1.4, 2.2, 3.5, 0.02, 0.025, 0.02 },
                dampLin = new[] { 5, 7, 10, 0.1, 0.1, 0.08 },
                dampQuad = new[] { 30, 45, 55, 0.2, 0.2, 0.15 },
                thrusters = DuboMiniThrusters(),
                spheres = new[] { new Sphere(0, 0, 0, 0.17) },
                gripper = new V3(0.19, 0, 0.08), marker = new V3(0, 0, 0.08), torpedo = new V3(0.17, 0, -0.04),
            },
        };

        public static readonly string[] BOT_ORDER = { "duburi", "dubomini", "barracuda", "kraken", "manta" };
        public static readonly string[] PLAYER_COLORS = { "#ffb547", "#4fd1c5", "#f56565", "#9f7aea", "#68d391", "#63b3ed", "#f6e05e", "#ed64a6" };

        /// BOTS[id] ?? fallback
        public static BotDesign Get(string id, BotDesign fallback = null) => id != null && BOTS.TryGetValue(id, out var d) ? d : fallback;
    }
}
