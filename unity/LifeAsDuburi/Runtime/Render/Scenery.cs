// src/render/scenery.js: environment scenery for the water scenarios (pool / pacific / arctic):
// lights, wave-following water surface, caustic floor, walls or seabed, life, particles, god rays,
// arena buoy line and the DUBURI logo banners / sign.
// three.js world = (east, up, south); water surface at Y = 0, arena floor at Y = -depth.
// Everything random is seeded. The JS animates on the GPU (shared uniforms + patched shaders); this
// port has no custom shaders, so the same motions run on the CPU once per frame: wave-following
// vertices and floats, kelp sway, fish (with the tail wiggle), jellyfish, god rays, marine snow, and
// the floor caustics as an animated cookie on the sun. Per-camera state (fog / background above or
// below the surface, rays, the water's look from below or above) is applied in
// RenderHooks.BeginCamera from what ApplyForCamera() decided.

using System;
using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Env = Duburi.Core.Environment;

namespace Duburi.Game
{
    /// the wave uniforms (uWaveA, uWaveK, uWaveW, uWaveT, uWaveD)
    public struct WaveState
    {
        public float A, K, W, T;
        public Vector2 D; // [cos, sin] of the wave heading (north, east)
        /// scWave: elevation at three.js (x, z); NED: xN = -z, yE = x
        public float Eta(float x, float z) => A == 0 ? 0 : A * Mathf.Cos(K * (-z * D.x + x * D.y) - W * T);
    }

    /// keep rocks, kelp and ice away from the course (three.js x, z, radius)
    public struct ClearZone
    {
        public float x, z, r;
        public ClearZone(float x, float z, float r) { this.x = x; this.z = z; this.r = r; }
    }

    public sealed class Scenery
    {
        const float TAU = Mathf.PI * 2;
        const int ThumbLayer = 29;

        public GameObject root;
        public Light Sun;
        public ScenarioLook look;
        public WaveState wave;
        /// sunDir (three.js): the scenery's sun comes from here
        public Vector3 SunDir { get; private set; }
        public float Caustic { get; private set; }
        public bool IsPool { get; private set; }

        readonly Owned owned = new Owned();
        readonly List<Action<float>> animators = new List<Action<float>>();
        float time;

        // fog / background objects (swapped per viewport)
        Color fogUnderColor, fogAboveColor, bgUnder, bgAbove;
        float fogUnderDensity, fogAboveDensity;

        // ---------------------------------------------------------------- utilities

        static Func<double> Mulberry32(uint seed)
        {
            uint a = seed;
            return () =>
            {
                unchecked
                {
                    a += 0x6d2b79f5;
                    uint t = (a ^ (a >> 15)) * (1 | a);
                    t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Smooth(double e0, double e1, double x) { double t = Math.Min(1, Math.Max(0, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t); }

        // three Color(hex) is linear; helpers keep colours linear like the JS
        static Color C(int hex) => Colors.Rgb(hex).linear;

        // three Color.offsetHSL in the linear working space
        static Color OffsetHSL(Color c, double dh, double ds, double dl)
        {
            RgbToHsl(c.r, c.g, c.b, out double h, out double s, out double l);
            h += dh; s += ds; l += dl;
            h = ((h % 1) + 1) % 1;
            s = Math.Min(1, Math.Max(0, s));
            l = Math.Min(1, Math.Max(0, l));
            if (s == 0) return new Color((float)l, (float)l, (float)l, 1);
            double p = l <= 0.5 ? l * (1 + s) : l + s - l * s, q = 2 * l - p;
            return new Color((float)Hue2Rgb(q, p, h + 1.0 / 3), (float)Hue2Rgb(q, p, h), (float)Hue2Rgb(q, p, h - 1.0 / 3), 1);
        }

        static double Hue2Rgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * 6 * (2.0 / 3 - t);
            return p;
        }

        static void RgbToHsl(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double hue = 0, sat;
            double light = (min + max) / 2.0;
            if (min == max) { hue = 0; sat = 0; }
            else
            {
                double delta = max - min;
                sat = light <= 0.5 ? delta / (max + min) : delta / (2 - max - min);
                if (max == r) hue = (g - b) / delta + (g < b ? 6 : 0);
                else if (max == g) hue = (b - r) / delta + 2;
                else hue = (r - g) / delta + 4;
                hue /= 6;
            }
            h = hue; s = sat; l = light;
        }

        // ---------------------------------------------------------------- canvas textures

        static Texture2D ToTexture(Raster cv, string name, bool srgb = true) =>
            cv.ToTexture(name, true, TextureWrapMode.Repeat, 8, srgb);

        // tileable value noise on a G x G lattice, domain [0,1)^2
        static Func<double, double, double> ValueNoise(int G, Func<double> rnd)
        {
            var L = new float[G * G];
            for (int i = 0; i < L.Length; i++) L[i] = (float)rnd();
            return (u, v) =>
            {
                double x = u * G, y = v * G, xi = Math.Floor(x), yi = Math.Floor(y);
                double fx = x - xi, fy = y - yi;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                int x0 = (int)((((long)xi % G) + G) % G), y0 = (int)((((long)yi % G) + G) % G), x1 = (x0 + 1) % G, y1 = (y0 + 1) % G;
                double a = L[y0 * G + x0], b = L[y0 * G + x1], c = L[y1 * G + x0], d = L[y1 * G + x1];
                return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
            };
        }

        // tileable Worley noise: writes [d1, d2, cellId] into out
        static Action<double, double, double[]> Worley(int G, Func<double> rnd)
        {
            var jx = new float[G * G];
            for (int i = 0; i < jx.Length; i++) jx[i] = (float)rnd();
            var jy = new float[G * G];
            for (int i = 0; i < jy.Length; i++) jy[i] = (float)rnd();
            return (u, v, o) =>
            {
                double x = u * G, y = v * G, xi = Math.Floor(x), yi = Math.Floor(y);
                double d1 = 9, d2 = 9;
                int id = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long cx = (long)xi + dx, cy = (long)yi + dy;
                        int k = (int)((((cy % G) + G) % G) * G + (((cx % G) + G) % G));
                        double px = cx + jx[k] - x, py = cy + jy[k] - y, d = px * px + py * py;
                        if (d < d1) { d2 = d1; d1 = d; id = k; } else if (d < d2) d2 = d;
                    }
                o[0] = Math.Sqrt(d1); o[1] = Math.Sqrt(d2); o[2] = id;
            };
        }

        static Raster PaintPixels(int S, Action<double, double, float[]> fn)
        {
            var cv = new Raster(S, S);
            cv.Paint((u, v, rgb) => fn(u, v, rgb));
            return cv;
        }

        // 1 m period: 4 x 4 light-blue pool tiles with grout
        static Raster TilesCanvas()
        {
            const int S = 256, n = 4, t = S / n, gw = 4;
            var g = new Raster(S, S);
            var rnd = Mulberry32(7);
            g.FillRect(0, 0, S, S, Colors.Hex("#86b9c9"));
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double l = 0.94 + rnd() * 0.06;
                    var c = new Color(JsRound(186 * l) / 255f, JsRound(226 * l) / 255f, JsRound(240 * l) / 255f, 1);
                    g.FillRect(i * t + gw / 2, j * t + gw / 2, t - gw, t - gw, c);
                    g.FillRect(i * t + gw / 2 + 4, j * t + gw / 2 + 4, t - gw - 8, 5, new Color(1, 1, 1, 0.16f));
                }
            return g;
        }

        static float JsRound(double x) => (float)Math.Floor(x + 0.5);

        // 4 m period
        static Raster SandCanvas(bool pale)
        {
            var rnd = Mulberry32(21);
            var n1 = ValueNoise(6, rnd);
            var n2 = ValueNoise(48, rnd);
            double[] bse = pale ? new double[] { 240, 232, 208 } : new double[] { 214, 200, 168 };
            return PaintPixels(512, (u, v, o) =>
            {
                double r = Math.Sin(TAU * (v * 9 + u + n1(u, v) * 1.6));
                double s = 0.93 + 0.06 * r * Math.Abs(r) + (n2(u, v) - 0.5) * 0.1 - (rnd() < 0.02 ? 0.12 : 0);
                o[0] = (float)(bse[0] * s); o[1] = (float)(bse[1] * s); o[2] = (float)(bse[2] * s);
            });
        }

        static Raster GravelCanvas()
        {
            var rnd = Mulberry32(33);
            var w = Worley(14, rnd);
            var n = ValueNoise(64, rnd);
            var f = new double[3];
            double[][] pal = { new double[] { 128, 126, 120 }, new double[] { 104, 100, 94 }, new double[] { 140, 132, 118 }, new double[] { 92, 96, 100 }, new double[] { 118, 110, 98 }, new double[] { 150, 148, 142 } };
            return PaintPixels(512, (u, v, o) =>
            {
                w(u, v, f);
                var c = pal[(int)f[2] % pal.Length];
                double gap = Smooth(0.0, 0.14, f[1] - f[0]);
                double s = (0.45 + 0.55 * gap) * (1.08 - f[0] * 0.45) * (0.9 + n(u, v) * 0.2);
                o[0] = (float)(c[0] * s); o[1] = (float)(c[1] * s); o[2] = (float)(c[2] * s);
            });
        }

        static Raster RockCanvas()
        {
            var rnd = Mulberry32(45);
            var a = ValueNoise(4, rnd);
            var b = ValueNoise(12, rnd);
            var c = ValueNoise(40, rnd);
            var w = Worley(5, rnd);
            var f = new double[3];
            return PaintPixels(512, (u, v, o) =>
            {
                w(u, v, f);
                double fbm = a(u, v) * 0.5 + b(u, v) * 0.3 + c(u, v) * 0.2;
                double crack = Smooth(0.0, 0.06, f[1] - f[0]);
                double s = (0.45 + 0.8 * fbm) * (0.55 + 0.45 * crack);
                o[0] = (float)(78 * s); o[1] = (float)(82 * s); o[2] = (float)(70 * s);
            });
        }

        static Raster SpriteCanvas()
        {
            var g = new Raster(64, 64);
            g.RadialGradient(32, 32, 32, new[] { 0f, 0.35f, 1f }, new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0) });
            return g;
        }

        // ---------------------------------------------------------------- shape builders

        static Geo RockGeometry(uint seed)
        {
            var rnd = Mulberry32(seed);
            var g = Geo.Icosahedron(1, 2);
            double a = rnd() * 10, b = rnd() * 10, c = rnd() * 10;
            for (int i = 0; i < g.pos.Count; i++)
            {
                double x = g.pos[i].x, y = g.pos[i].y, z = g.pos[i].z;
                double k = 1 + 0.18 * Math.Sin(x * 3.1 + a) * Math.Cos(z * 2.7 + b) + 0.1 * Math.Sin(y * 4.3 + c) + 0.05 * Math.Sin((x + z) * 7.0);
                g.pos[i] = new Vector3((float)(x * k), (float)(y * k * 0.62), (float)(z * k * (0.85 + 0.15 * Math.Sin(a))));
            }
            g.ComputeVertexNormals();
            g.EnsureUV();
            return g;
        }

        // vertical strip, local y in [0,1]
        static Geo Ribbon(double width, int segs, double twist, double bend, double phase)
        {
            var g = new Geo { idx = new List<int>() };
            for (int i = 0; i <= segs; i++)
            {
                double v = i / (double)segs, a = phase + twist * v, ox = Math.Sin(v * Math.PI * 1.3 + phase) * bend * v;
                double w = width * (0.5 + 0.5 * Math.Sin(Math.PI * Math.Min(1, v * 0.9 + 0.12)));
                double cx = Math.Cos(a) * w * 0.5, cz = Math.Sin(a) * w * 0.5, nx = -Math.Sin(a), nz = Math.Cos(a);
                g.pos.Add(new Vector3((float)(ox - cx), (float)v, (float)-cz)); g.pos.Add(new Vector3((float)(ox + cx), (float)v, (float)cz));
                g.nrm.Add(new Vector3((float)nx, 0, (float)nz)); g.nrm.Add(new Vector3((float)nx, 0, (float)nz));
                g.uv.Add(new Vector2(0, (float)v)); g.uv.Add(new Vector2(1, (float)v));
                if (i < segs) { int b = i * 2; g.idx.Add(b); g.idx.Add(b + 1); g.idx.Add(b + 2); g.idx.Add(b + 1); g.idx.Add(b + 3); g.idx.Add(b + 2); }
            }
            return g;
        }

        static Geo PlantGeometry(int blades, double width, int segs, double twist, double bend, uint seed)
        {
            var rnd = Mulberry32(seed);
            var parts = new List<Geo>();
            for (int i = 0; i < blades; i++)
            {
                double tw = twist * (0.6 + rnd() * 0.8), be = bend * (0.5 + rnd()), ph = rnd() * TAU;
                var g = Ribbon(width, segs, tw, be, ph);
                float s = (float)(0.75 + rnd() * 0.25);
                g.Scale(1, s, 1);
                g.RotateY((float)(rnd() * TAU));
                float tx = (float)((rnd() - 0.5) * 0.12), tz = (float)((rnd() - 0.5) * 0.12);
                g.Translate(tx, 0, tz);
                parts.Add(g);
            }
            return Geo.Merge(parts);
        }

        static Geo BranchCoralGeometry(uint seed)
        {
            var rnd = Mulberry32(seed);
            var parts = new List<Geo>();
            var up = new Vector3(0, 1, 0);
            void Branch(Vector3 start, Vector3 dir, float len, float rad, int depth)
            {
                var g = Geo.Cylinder(rad * 0.65f, rad, len, 5, 1);
                g.Translate(0, len / 2, 0);
                var q = Space.FromUnitVectors(up, dir);
                g.ApplyMatrix(Matrix4x4.TRS(start, q, Vector3.one));
                parts.Add(g);
                Vector3 end = start + dir * len;
                if (depth > 0)
                {
                    int n = 2 + (rnd() < 0.4 ? 1 : 0);
                    for (int k = 0; k < n; k++)
                    {
                        float rx = (float)(rnd() - 0.5), ry = (float)(rnd() * 0.3), rz = (float)(rnd() - 0.5);
                        Vector3 d = (dir + new Vector3(rx, ry, rz) * 1.3f).normalized;
                        d.y = Mathf.Max(d.y, 0.3f);
                        d.Normalize();
                        Branch(end, d, len * 0.75f, rad * 0.7f, depth - 1);
                    }
                }
                else
                {
                    var tip = Geo.Sphere(rad * 0.75f, 5, 4);
                    tip.Translate(end.x, end.y, end.z);
                    parts.Add(tip);
                }
            }
            for (int t = 0; t < 3; t++)
            {
                float a = (float)(t / 3.0 * TAU + rnd());
                Branch(new Vector3(Mathf.Cos(a) * 0.06f, 0, Mathf.Sin(a) * 0.06f), new Vector3(Mathf.Cos(a) * 0.35f, 1, Mathf.Sin(a) * 0.35f).normalized, 0.32f, 0.05f, 2);
            }
            return Geo.Merge(parts);
        }

        static Geo BrainCoralGeometry()
        {
            var g = Geo.Sphere(0.5f, 22, 10, 0, TAU, 0, Mathf.PI / 2);
            for (int i = 0; i < g.pos.Count; i++)
            {
                Vector3 v = g.pos[i].normalized;
                double k = 0.5 * (1 + 0.05 * Math.Sin(22 * v.x + 7 * Math.Sin(15 * v.z)) * Math.Sin(18 * v.z + 5 * v.y));
                g.pos[i] = new Vector3((float)(v.x * k), (float)(v.y * k * 0.72), (float)(v.z * k));
            }
            g.ComputeVertexNormals();
            return g;
        }

        static Geo TubeSpongeGeometry(uint seed)
        {
            var rnd = Mulberry32(seed);
            var parts = new List<Geo>();
            for (int i = 0; i < 5; i++)
            {
                float h = (float)(0.3 + rnd() * 0.5), r = (float)(0.05 + rnd() * 0.05);
                var g = Geo.Cylinder(r * 1.15f, r * 0.8f, h, 9, 1, true);
                float tx = (float)((rnd() - 0.5) * 0.3), tz = (float)((rnd() - 0.5) * 0.3);
                g.Translate(tx, h / 2, tz);
                parts.Add(g);
            }
            return Geo.Merge(parts);
        }

        static Geo StarGeometry()
        {
            var s = new Shape2();
            for (int i = 0; i < 10; i++)
            {
                double r = i % 2 == 1 ? 0.38 : 1, a = i / 10.0 * TAU + Math.PI / 2;
                if (i == 0) s.MoveTo(Math.Cos(a) * r, Math.Sin(a) * r); else s.LineTo(Math.Cos(a) * r, Math.Sin(a) * r);
            }
            s.ClosePath();
            var g = Geo.Extrude(s, new ExtrudeOptions { depth = 0.12, bevelEnabled = true, bevelThickness = 0.06, bevelSize = 0.08, bevelSegments = 1 });
            g.RotateX(-Mathf.PI / 2);
            return g;
        }

        // fish body (closed, front faces) + tail fin (both sides)
        static Geo FishGeometry()
        {
            var body = Geo.Sphere(0.5f, 8, 6);
            body.Scale(0.32f, 0.55f, 1);
            var tail = new Geo { idx = new List<int> { 0, 1, 2 } };
            tail.pos.Add(new Vector3(0, 0, -0.42f)); tail.pos.Add(new Vector3(0, 0.3f, -0.82f)); tail.pos.Add(new Vector3(0, -0.3f, -0.82f));
            for (int i = 0; i < 3; i++) { tail.nrm.Add(new Vector3(1, 0, 0)); tail.uv.Add(Vector2.zero); }
            return Geo.Merge(new List<Geo> { body, tail.WithBackFaces() });
        }

        static Geo JellyGeometry()
        {
            var bell = Geo.Sphere(0.5f, 14, 6, 0, TAU, 0, Mathf.PI * 0.5f);
            bell.Scale(1, 0.8f, 1);
            var parts = new List<Geo> { bell };
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * TAU;
                var t = Geo.Cylinder(0.012f, 0.004f, 0.9f, 3, 1, true);
                t.Translate(Mathf.Cos(a) * 0.3f, -0.45f, Mathf.Sin(a) * 0.3f);
                parts.Add(t);
            }
            return Geo.Merge(parts);
        }

        static Geo IceGeometry(uint seed)
        {
            var rnd = Mulberry32(seed);
            var s = new Shape2();
            const int n = 11;
            for (int i = 0; i < n; i++)
            {
                double a = i / (double)n * TAU, r = 0.75 + rnd() * 0.3;
                if (i == 0) s.MoveTo(Math.Cos(a) * r, Math.Sin(a) * r); else s.LineTo(Math.Cos(a) * r, Math.Sin(a) * r);
            }
            s.ClosePath();
            var g = Geo.Extrude(s, new ExtrudeOptions { depth = 1, bevelEnabled = true, bevelThickness = 0.08, bevelSize = 0.06, bevelSegments = 1 });
            g.RotateX(-Mathf.PI / 2); // extrusion along +Y, y in [-0.08, 1.08]
            g.Translate(0, -1, 0);     // top ~ +0.08
            g.ComputeVertexNormals();
            return g;
        }

        // ---------------------------------------------------------------- per-object dynamic parts

        sealed class Dummy
        {
            public Vector3 position, rotation, scale = Vector3.one;
            public Matrix4x4 Matrix => Matrix4x4.TRS(position, Space.Euler(rotation.x, rotation.y, rotation.z), scale);
            public void Reset() { position = Vector3.zero; rotation = Vector3.zero; scale = Vector3.one; }
        }

        sealed class Follower { public Node node; public Transform t; public Vector3 basePos; }
        readonly List<Follower> followers = new List<Follower>();

        sealed class DynMesh
        {
            public Mesh mesh;
            public Vector3[] basePos, pos, baseNrm, nrm;
            public Node node;
        }

        // water surface
        DynMesh water;
        Material waterMat;
        Texture2D texUnder, texAbove;
        float scaleUnder, scaleAbove;
        const float RUnder = 3f, RAbove = 12f;

        // rope lines following the waves
        DynMesh ropeLines;

        // kelp sway
        sealed class Sway { public DynMesh dm; public float[] w; public int[] inst; public Vector2[] instXZ; public float amount; }
        readonly List<Sway> sways = new List<Sway>();

        // god rays
        sealed class Ray { public GameObject go; public Material mat; public Mesh mesh; public Vector3[] basePos, pos; public float[] d; public float seed; public Vector3 top, bottom; }
        readonly List<Ray> rays = new List<Ray>();
        Color rayColor;
        float rayI, rayFade;

        // marine snow
        ParticleSystem snow;
        ParticleSystemRenderer snowRenderer;
        ParticleSystem.Particle[] snowParticles;
        Vector3[] snowPos;
        float[] snowSeed;
        float snowSize, snowFall, snowDepth;

        // caustic cookie
        CausticCookie cookie;
        float sunBase;

        // ---------------------------------------------------------------- main

        /// createScenery(scene, scenario, { logoTexture, arena, clearZones }) under the mirrored root 'world'.
        public static Scenery Create(Transform world, Scenario scenario, Texture2D logoTexture, Arena arena, IList<ClearZone> clearZones)
        {
            var s = new Scenery();
            s.Build(world, scenario, logoTexture, arena ?? new Arena { length = 50, width = 25, depth = 4.8 }, clearZones ?? new ClearZone[0]);
            RenderHooks.Ensure();
            RenderHooks.BeginCamera += s.OnBeginCamera;
            return s;
        }

        void Build(Transform world, Scenario scenario, Texture2D logoTexture, Arena arena, IList<ClearZone> clearZones)
        {
            look = scenario?.look ?? new ScenarioLook { water = 0x2f9fc4, fog = 0x3aa6c9, fogDensity = 0.03, sky = 0x9fd8ec, floor = "sand", light = 1, sun = 0xffffff, surface = 0x7fd6ee, particles = 0.5, life = "none", ice = false };
            float L = (float)arena.length, W = (float)arena.width, D = (float)arena.depth;
            bool isPool = look.floor == "tiles";
            IsPool = isPool;
            string id = scenario?.id ?? look.floor;
            uint seed = 17;
            foreach (char ch in id) seed = unchecked((uint)(((ulong)seed * 31 + ch) & 0xffffffffUL));
            var rnd = Mulberry32(seed);

            var rootN = Node.Group("scenery");
            float light = (float)look.light;

            // caustic strength: strong in bright clear water, faint in dim / murky water
            float caustic = Mathf.Clamp((light - 0.6f) * 1.6f, 0.08f, 1.0f) * Mathf.Clamp(1.4f - (float)look.fogDensity * 12, 0.3f, 1.0f);
            if (look.ice) caustic *= 0.7f;
            Caustic = caustic;

            SunDir = new Vector3(0.28f, 1, 0.38f).normalized;
            bool InArena(double x, double z) => Math.Abs(x) < W / 2 && Math.Abs(z) < L / 2;
            bool NearZone(double x, double z, double r)
            {
                foreach (var c in clearZones) if (Math.Sqrt((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z)) < c.r + r) return true;
                return false;
            }
            var dummy = new Dummy();

            // ---- lights
            Ambient.SetHemisphere(Color.Lerp(C(look.surface), Color.white, 0.5f), C(look.water) * 0.5f, 1.5f * light);
            var sunGo = new GameObject("Sun");
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.color = Colors.Rgb(look.sun);
            sunBase = 2.2f * light / Mathf.PI;
            Sun.intensity = sunBase;
            Sun.shadows = LightShadows.None;
            Sun.cullingMask = ~(1 << ThumbLayer);
            SetSunDirection(-SunDir);

            // ---- fog / background objects (swapped per viewport)
            fogUnderColor = Colors.Rgb(look.fog); fogUnderDensity = (float)look.fogDensity;
            fogAboveColor = Colors.Rgb(look.sky); fogAboveDensity = isPool ? 0.006f : 0.011f;
            bgUnder = Colors.Rgb(look.fog); bgAbove = Colors.Rgb(look.sky);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogUnderColor;
            RenderSettings.fogDensity = fogUnderDensity;

            // ---- seabed height (flat arena, gentle relief outside, never shallower than 1.5 m)
            double SeabedY(double x, double z)
            {
                if (isPool) return -D;
                double dx = Math.Max(0, Math.Abs(x) - (W / 2 + 1)), dz = Math.Max(0, Math.Abs(z) - (L / 2 + 1)), d = Math.Sqrt(dx * dx + dz * dz);
                if (d <= 0) return -D;
                double n = Math.Sin(x * 0.11 + 1.3) * Math.Cos(z * 0.09 - 0.7) * 1.1 + Math.Sin(x * 0.27 + z * 0.21) * 0.45 + Math.Sin(z * 0.37 - x * 0.13) * 0.25;
                return Math.Min(-1.5, -D + Smooth(0, 10, d) * n * 0.9 - Smooth(10, 80, d) * 1.2);
            }

            // ---- placement: outside the arena (biased close) or in its edge band, never in clear zones
            bool Allowed(double x, double z, double r, double band = 1.6)
            {
                if (InArena(x, z) && Math.Min(W / 2 - Math.Abs(x), L / 2 - Math.Abs(z)) > band) return false;
                return !NearZone(x, z, r);
            }
            List<double[]> Scatter(int n, double r = 0.5, double inside = 0.12, double spread = 40, double falloff = 18)
            {
                var outL = new List<double[]>();
                for (int tries = 0; outL.Count < n && tries < n * 60; tries++)
                {
                    double x, z;
                    if (rnd() < inside)
                    {
                        double t = rnd(), e = rnd() * 1.5;
                        int side = (int)Math.Floor(rnd() * 4);
                        if (side < 2) { x = (side == 1 ? 1 : -1) * (W / 2 - e); z = (t - 0.5) * L; } else { z = (side == 3 ? 1 : -1) * (L / 2 - e); x = (t - 0.5) * W; }
                    }
                    else
                    {
                        x = (rnd() * 2 - 1) * (W / 2 + spread); z = (rnd() * 2 - 1) * (L / 2 + spread);
                        if (InArena(x, z)) continue;
                        double d = Math.Sqrt(Math.Pow(Math.Max(0, Math.Abs(x) - W / 2), 2) + Math.Pow(Math.Max(0, Math.Abs(z) - L / 2), 2));
                        if (rnd() > Math.Exp(-d / falloff)) continue;
                    }
                    if (Allowed(x, z, r)) outL.Add(new[] { x, z });
                }
                return outL;
            }

            // instanced(geo, mat, list, place, opts): static instances are baked into one mesh
            Node Instanced(Geo geo, Mat3 mat, List<double[]> list, Action<double[], int, Dummy> place, Func<int, Color> color = null)
            {
                var mats = new List<Matrix4x4>(list.Count);
                var cols = color != null ? new List<Color>(list.Count) : null;
                for (int i = 0; i < list.Count; i++)
                {
                    dummy.Reset();
                    place(list[i], i, dummy);
                    mats.Add(dummy.Matrix);
                    if (color != null) cols.Add(color(i));
                }
                if (list.Count == 0) return null;
                var merged = Geo.MergeInstances(geo, mats, cols);
                var m = mat;
                if (cols != null || merged.col != null) { m = mat.Clone(); m.vertexColors = true; }
                var node = Node.Mesh(merged, m);
                rootN.Add(node);
                return node;
            }
            // instances that follow the waves: rigid chunks of nearby instances, moved by the wave at their centre
            void WaveInstanced(Geo geo, Mat3 mat, List<double[]> list, Action<double[], int, Dummy> place, Func<int, Color> color, float chunk)
            {
                var groups = new Dictionary<long, List<int>>();
                var keys = new List<long>();
                var mats = new Matrix4x4[list.Count];
                var cols = new Color[list.Count];
                var centres = new Vector3[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    dummy.Reset();
                    place(list[i], i, dummy);
                    mats[i] = dummy.Matrix;
                    centres[i] = dummy.position;
                    cols[i] = color != null ? color(i) : Color.white;
                    long key = ((long)Mathf.FloorToInt(dummy.position.x / chunk) << 32) ^ (uint)Mathf.FloorToInt(dummy.position.z / chunk);
                    if (!groups.TryGetValue(key, out var g)) { g = new List<int>(); groups[key] = g; keys.Add(key); }
                    g.Add(i);
                }
                var m = mat.Clone();
                if (color != null) m.vertexColors = true;
                foreach (var key in keys)
                {
                    var idxs = groups[key];
                    Vector3 c = Vector3.zero;
                    foreach (int i in idxs) c += centres[i];
                    c /= idxs.Count;
                    var ms = new List<Matrix4x4>(idxs.Count);
                    var cs = new List<Color>(idxs.Count);
                    foreach (int i in idxs) { ms.Add(Matrix4x4.Translate(-c) * mats[i]); cs.Add(cols[i]); }
                    var node = Node.Mesh(Geo.MergeInstances(geo, ms, color != null ? cs : null), m, "wave");
                    node.position = c;
                    rootN.Add(node);
                    followers.Add(new Follower { node = node, basePos = c });
                }
            }
            Func<int, Color> Pick(int[] arr) => _ => C(arr[(int)Math.Floor(rnd() * arr.Length)]);
            int PickHex(int[] arr) => arr[(int)Math.Floor(rnd() * arr.Length)];
            Color Tint(int hex, double v) { var c = C(hex); double dh = (rnd() - 0.5) * 0.04, dl = (rnd() - 0.5) * v; return OffsetHSL(c, dh, 0, dl); }

            // ---- water surface
            float wsx = isPool ? W : 400, wsz = isPool ? L : 400;
            var waterGeo = Geo.Plane(wsx, wsz, isPool ? 50 : 128, isPool ? 100 : 128);
            waterGeo.RotateX(-Mathf.PI / 2);
            // UVs = world x, z: the look from below / above is a camera-centred texture (OnBeginCamera)
            for (int i = 0; i < waterGeo.uv.Count; i++) waterGeo.uv[i] = new Vector2(waterGeo.pos[i].x, waterGeo.pos[i].z);
            BuildWaterTextures(isPool);
            waterMat = owned.Add(Mats.Create(new Mat3 { kind = MatKind.Basic, map = texUnder, transparent = true, depthWrite = false, side = Side.Double, name = "water" }, 2));
            water = MakeDynMesh(waterGeo, "water", false);

            // ---- floor
            Raster floorCv = isPool ? TilesCanvas() : look.floor == "gravel" ? GravelCanvas() : look.floor == "rock" ? RockCanvas() : SandCanvas(look.life == "reef");
            var floorTex = owned.Add(ToTexture(floorCv, "floor"));
            var floorMat = new Mat3 { map = floorTex, color = Color.white, roughness = 0.95f, metalness = 0 };
            Geo floorGeo;
            if (isPool)
            {
                floorGeo = Geo.Plane(W, L).ScaleUV(W, L); // tile texture has a 1 m period
                floorGeo.RotateX(-Mathf.PI / 2);
                floorGeo.Translate(0, -D, 0);
            }
            else
            {
                const float S = 300, per = 4; // texture period 4 m
                const int N = 120;
                floorGeo = Geo.Plane(S, S, N, N).ScaleUV(S / per, S / per);
                floorGeo.RotateX(-Mathf.PI / 2);
                for (int i = 0; i < floorGeo.pos.Count; i++)
                {
                    var p = floorGeo.pos[i];
                    floorGeo.pos[i] = new Vector3(p.x, (float)SeabedY(p.x, p.z), p.z);
                }
                floorGeo.ComputeVertexNormals();
            }
            rootN.Add(Node.Mesh(floorGeo, floorMat, "floor"));

            if (isPool) BuildPool(); else BuildSea();

            // ---- suspended particles / marine snow
            {
                int n = (int)Math.Floor(4000 * look.particles + 0.5);
                float ex = isPool ? W / 2 : W / 2 + 10, ez = isPool ? L / 2 : L / 2 + 12;
                snowPos = new Vector3[n];
                snowSeed = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float px = (float)((rnd() * 2 - 1) * ex), py = (float)(-D + rnd() * D), pz = (float)((rnd() * 2 - 1) * ez);
                    snowPos[i] = new Vector3(px, py, pz);
                    snowSeed[i] = (float)rnd();
                }
                var sprite = owned.Add(SpriteCanvas().ToTexture("sprite", true, TextureWrapMode.Clamp, 1, false));
                bool snowy = look.life == "arctic";
                snowSize = snowy ? 0.05f : isPool ? 0.022f : 0.032f;
                snowFall = snowy ? 0.05f : 0.008f;
                snowDepth = D;
                Color col = snowy ? Color.white : Color.Lerp(C(look.surface), Color.white, 0.6f);
                col.a = isPool ? 0.35f : snowy ? 0.8f : 0.55f;   // uOpacity
                if (n > 0) MakeSnow(n, sprite, col);
            }

            // ---- god rays (vertical additive quads from the surface)
            {
                double[] cfg = look.life == "reef" ? new double[] { 16, 0.22, 7 } : look.life == "kelp" ? new double[] { 7, 0.1, 6 } : look.life == "arctic" ? new double[] { 6, 0.12, 5 } : null;
                if (cfg != null)
                {
                    int n = (int)cfg[0];
                    double inten = cfg[1], len = cfg[2];
                    var rayTex = owned.Add(RayTexture());
                    rayColor = Color.Lerp(C(look.surface), Color.white, 0.5f);
                    rayI = (float)(inten * look.light);
                    rayFade = (float)(look.fogDensity * 1.2);
                    for (int i = 0; i < n; i++)
                    {
                        double x, z;
                        if (look.ice) { x = (rnd() - 0.5) * 4; z = (rnd() - 0.5) * (L - 6); } else { x = (rnd() - 0.5) * (W + 14); z = (rnd() - 0.5) * (L + 14); }
                        double w = 1.0 + rnd() * 2.0, l = len * (0.8 + rnd() * 0.4), s = rnd(), yaw = rnd() * Math.PI;
                        double bx = -SunDir.x / SunDir.y * l * 0.7, bz = -SunDir.z / SunDir.y * l * 0.7;
                        var pos = new List<Vector3>();
                        var uvs = new List<Vector2>();
                        var idx = new List<int>();
                        for (int q = 0; q < 2; q++)
                        {
                            double a = yaw + q * Math.PI / 2, cx = Math.Cos(a), cz = Math.Sin(a), wt = w * 0.5, wb = w * 0.8;
                            int b = pos.Count;
                            pos.Add(V3(x - cx * wt, 0.02, z - cz * wt)); pos.Add(V3(x + cx * wt, 0.02, z + cz * wt));
                            pos.Add(V3(x + bx - cx * wb, -l, z + bz - cz * wb)); pos.Add(V3(x + bx + cx * wb, -l, z + bz + cz * wb));
                            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                            idx.Add(b); idx.Add(b + 2); idx.Add(b + 1); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b + 3);
                        }
                        var ray = new Ray { seed = (float)s, top = V3(x, 0, z), bottom = V3(x + bx, -l, z + bz) };
                        var mesh = owned.Add(new Mesh { name = "ray" });
                        mesh.MarkDynamic();
                        mesh.SetVertices(pos);
                        mesh.SetUVs(0, uvs);
                        mesh.SetTriangles(idx, 0);
                        mesh.RecalculateBounds();
                        ray.mesh = mesh;
                        ray.basePos = pos.ToArray();
                        ray.pos = pos.ToArray();
                        ray.d = new float[uvs.Count];
                        for (int k = 0; k < uvs.Count; k++) ray.d[k] = uvs[k].y;
                        ray.mat = owned.Add(Mats.Create(new Mat3 { kind = MatKind.Basic, map = rayTex, additive = true, transparent = true, depthWrite = false, side = Side.Double, color = rayColor, name = "ray" }, 3));
                        rays.Add(ray);
                    }
                }
            }

            // ================================================================ pool
            void BuildPool()
            {
                float H = D + 0.4f; // wall height, floor to 0.4 m above water
                var parts = new List<Geo>();
                void Wall(float w, float rotY, float x, float z)
                {
                    var g = Geo.Plane(w, H).ScaleUV(w, H);
                    g.RotateY(rotY); g.Translate(x, (0.4f - D) / 2, z);
                    parts.Add(g);
                }
                Wall(W, 0, 0, -L / 2); // north wall, faces +Z
                Wall(W, Mathf.PI, 0, L / 2);
                Wall(L, Mathf.PI / 2, -W / 2, 0);
                Wall(L, -Mathf.PI / 2, W / 2, 0);
                rootN.Add(Node.Mesh(Geo.MergeNonIndexed(parts), new Mat3 { map = floorTex, color = Color.white, roughness = 0.6f }, "walls"));

                // competition lane lines with T ends + wall targets
                const int lanes = 10;
                const float lw = 0.25f;
                var marks = new List<Geo>();
                for (int i = 0; i < lanes; i++)
                {
                    float x = -W / 2 + W / lanes * (i + 0.5f);
                    var g = Geo.Box(lw, 0.004f, L - 4); g.Translate(x, -D + 0.003f, 0); marks.Add(g);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        g = Geo.Box(1.0f, 0.004f, lw); g.Translate(x, -D + 0.003f, s * (L / 2 - 2)); marks.Add(g);
                        g = Geo.Box(lw, D - 0.3f, 0.004f); g.Translate(x, -D + (D - 0.3f) / 2, s * (L / 2 - 0.003f)); marks.Add(g);
                        g = Geo.Box(0.5f, lw, 0.004f); g.Translate(x, -0.3f - lw / 2, s * (L / 2 - 0.003f)); marks.Add(g);
                    }
                }
                rootN.Add(Node.Mesh(Geo.MergeNonIndexed(marks), Mat3.Std(0x0d2a6b, 0.6f), "lanes"));

                // deck slabs + white coping
                var dk = new List<Geo>();
                const float dx = 6, dz = 8;
                void Slab(float w, float d, float x, float z, int hex, float h = 0.4f, float y = 0.2f)
                {
                    var g = Geo.Box(w, h, d); g.Translate(x, y, z); dk.Add(g.Colored(hex));
                }
                Slab(W + 2 * dx, dz, 0, -L / 2 - dz / 2, 0xd6d1c4);
                Slab(W + 2 * dx, dz, 0, L / 2 + dz / 2, 0xd6d1c4);
                Slab(dx, L, -W / 2 - dx / 2, 0, 0xd6d1c4);
                Slab(dx, L, W / 2 + dx / 2, 0, 0xd6d1c4);
                Slab(W + 0.7f, 0.35f, 0, -L / 2 - 0.175f, 0xf4f2ec, 0.1f, 0.45f);
                Slab(W + 0.7f, 0.35f, 0, L / 2 + 0.175f, 0xf4f2ec, 0.1f, 0.45f);
                Slab(0.35f, L, -W / 2 - 0.175f, 0, 0xf4f2ec, 0.1f, 0.45f);
                Slab(0.35f, L, W / 2 + 0.175f, 0, 0xf4f2ec, 0.1f, 0.45f);
                rootN.Add(Node.Mesh(Geo.MergeNonIndexed(dk), new Mat3 { vertexColors = true, roughness = 0.85f }, "deck"));

                // hall (inside of a box) and ceiling light panels
                const float hallH = 14;
                var hall = Geo.Box(W + 2 * dx, hallH, L + 2 * dz);
                hall.Translate(0, 0.35f + hallH / 2, 0);
                rootN.Add(Node.Mesh(hall, new Mat3 { color = C(0xdfe9ee), roughness = 0.9f, side = Side.Back }, "hall"));
                var lights = new List<double[]>();
                foreach (int x in new[] { -8, 0, 8 }) foreach (int z in new[] { -21, -7, 7, 21 }) lights.Add(new double[] { x, z });
                Instanced(Geo.Box(3, 0.1f, 1), Mat3.Basic(0xffffff), lights, (p, i, o) => { o.position = new Vector3((float)p[0], 0.35f + hallH - 0.08f, (float)p[1]); });

                // DUBURI banners: underwater on both end walls, and high on the hall end walls
                if (logoTexture != null)
                {
                    float asp = logoTexture.width > 0 && logoTexture.height > 0 ? logoTexture.width / (float)logoTexture.height : 2.6f;
                    var bp = new List<Geo>();
                    void Banner(float w, float y, float z, float rotY) { var g = Geo.Plane(w, w / asp); g.RotateY(rotY); g.Translate(0, y, z); bp.Add(g); }
                    Banner(9, -2.2f, -L / 2 + 0.02f, 0);
                    Banner(9, -2.2f, L / 2 - 0.02f, Mathf.PI);
                    Banner(14, 7, -L / 2 - dz + 0.05f, 0);
                    Banner(14, 7, L / 2 + dz - 0.05f, Mathf.PI);
                    rootN.Add(Node.Mesh(Geo.MergeNonIndexed(bp), new Mat3 { map = logoTexture, transparent = true, roughness = 0.8f }, "banners"));
                }

                // lane ropes along the side walls (discs on the surface)
                var fl = new List<double[]>();
                const double spacing = 0.1;
                int nPer = (int)Math.Floor((L - 0.2) / spacing) + 1;
                foreach (int sx in new[] { -1, 1 }) for (int i = 0; i < nPer; i++) fl.Add(new double[] { sx * (W / 2 - 0.25), -L / 2 + 0.1 + i * spacing, i });
                var disc = Geo.Cylinder(0.055f, 0.055f, 0.085f, 10);
                disc.RotateX(Mathf.PI / 2);
                Color red = C(0xd62828), blue = C(0x1d4fbf), white = C(0xf2f2f2);
                WaveInstanced(disc, new Mat3 { roughness = 0.4f }, fl, (p, i, o) => { o.position = new Vector3((float)p[0], 0.0f, (float)p[1]); },
                    i => { double k = fl[i][2], zz = k * spacing; return zz < 5 || zz > L - 5.2 ? red : Math.Floor(k / 5) % 2 == 1 ? white : blue; }, 1.0f);

                // starting blocks at the south end
                var blk = new List<Geo>();
                var ped = Geo.Box(0.5f, 0.7f, 0.5f); ped.Translate(0, 0.75f, 0); blk.Add(ped.Colored(0xeeeeee));
                var top = Geo.Box(0.55f, 0.05f, 0.72f); top.RotateX(-0.17f); top.Translate(0, 1.12f, -0.02f); blk.Add(top.Colored(0x1b2f6b));
                var lanesX = new List<double[]>();
                for (int i = 0; i < lanes; i++) lanesX.Add(new double[] { -W / 2 + W / lanes * (i + 0.5), 0 });
                Instanced(Geo.MergeNonIndexed(blk), new Mat3 { vertexColors = true, roughness = 0.6f }, lanesX, (p, i, o) => { o.position = new Vector3((float)p[0], 0, L / 2 + 0.55f); });
            }

            // ================================================================ sea
            void BuildSea()
            {
                string life = look.life;

                // rocks (two shapes)
                int nRocks = life == "kelp" ? 90 : life == "arctic" ? 40 : 30;
                var rockMat = new Mat3 { color = Color.white, roughness = 0.95f, flatShading = true };
                int rockBase = life == "kelp" ? 0x5d6158 : life == "arctic" ? 0x6a6f72 : 0x9a9182;
                for (int v = 0; v < 2; v++)
                {
                    var pts = Scatter(nRocks, r: 1.2, inside: 0.1);
                    Instanced(RockGeometry(seed + (uint)(v * 101)), rockMat, pts, (p, i, o) =>
                    {
                        double x = p[0], z = p[1];
                        double s = InArena(x, z) ? 0.2 + rnd() * 0.35 : 0.4 + rnd() * rnd() * (life == "kelp" ? 2.4 : 1.4);
                        o.position = new Vector3((float)x, (float)(SeabedY(x, z) + s * 0.15), (float)z);
                        float rx = (float)((rnd() - 0.5) * 0.3), ry = (float)(rnd() * TAU), rz = (float)((rnd() - 0.5) * 0.3);
                        o.rotation = new Vector3(rx, ry, rz);
                        float sx = (float)(s * (0.8 + rnd() * 0.5)), sz = (float)(s * (0.8 + rnd() * 0.5));
                        o.scale = new Vector3(sx, (float)s, sz);
                    }, _ => Tint(rockBase, 0.12));
                }

                if (life == "reef")
                {
                    int[] pal = { 0xff6f61, 0xff9f1c, 0xf15bb5, 0x9b5de5, 0xfee440, 0x00bbf9, 0xe76f51, 0xffb4c8 };
                    var centres = Scatter(28, r: 1.5, inside: 0.15, falloff: 14);
                    List<double[]> Cluster(int n, double r)
                    {
                        var outL = new List<double[]>();
                        for (int t = 0; outL.Count < n && t < n * 20; t++)
                        {
                            var c = centres[(int)Math.Floor(rnd() * centres.Count)];
                            double a = rnd() * TAU, d = rnd() * 1.8, x = c[0] + Math.Cos(a) * d, z = c[1] + Math.Sin(a) * d;
                            if (Allowed(x, z, r)) outL.Add(new[] { x, z });
                        }
                        return outL;
                    }
                    var coralMat = new Mat3 { color = Color.white, roughness = 0.75f };
                    Action<double[], int, Dummy> Place(double lo, double hi) => (p, i, o) =>
                    {
                        double s = lo + rnd() * (hi - lo);
                        o.position = new Vector3((float)p[0], (float)(SeabedY(p[0], p[1]) - 0.03), (float)p[1]);
                        o.rotation = new Vector3(0, (float)(rnd() * TAU), 0);
                        o.scale = Vector3.one * (float)s;
                    };
                    var g1 = BranchCoralGeometry(seed + 5);
                    Instanced(g1, coralMat, Cluster(70, 0.5), Place(0.6, 1.5), _ => C(PickHex(pal)));
                    var g2 = BrainCoralGeometry();
                    Instanced(g2, coralMat, Cluster(55, 0.5), Place(0.5, 1.4), _ => Tint(PickHex(new[] { 0xc9a227, 0x8fbf5a, 0xd77a61, 0xb06ab3 }), 0.1));
                    var g3 = TubeSpongeGeometry(seed + 9);
                    Instanced(g3, new Mat3 { color = Color.white, roughness = 0.8f, side = Side.Double }, Cluster(40, 0.5), Place(0.7, 1.6), _ => C(PickHex(new[] { 0x9b5de5, 0xff9f1c, 0xf15bb5, 0xffd23f })));
                    AddStars(new[] { 0xff7b29, 0xe63946, 0x9b5de5, 0x3a86ff }, 30);
                    AddFish(new[]
                    {
                        new School { c = new Vector3(-17, -2.4f, -8), r = new Vector2(4, 7), w = 0.22f, n = 22, col = 0xffd23f, s = 0.2f },
                        new School { c = new Vector3(18, -2.0f, 10), r = new Vector2(3.5f, 6), w = -0.28f, n = 22, col = 0x3fa7ff, s = 0.18f },
                        new School { c = new Vector3(0, -3.0f, -31), r = new Vector2(8, 3), w = 0.18f, n = 20, col = 0xff7b29, s = 0.22f },
                    });
                }
                else if (life == "kelp")
                {
                    var kelpPts = new List<double[]>();
                    for (int t = 0; kelpPts.Count < 280 && t < 20000; t++)
                    {
                        // dense band 1..14 m outside the arena edges, a few inside the edge band
                        double x, z;
                        if (rnd() < 0.06)
                        {
                            var pl = Scatter(1, r: 0.6, inside: 1);
                            if (pl.Count == 0) continue;
                            x = pl[0][0]; z = pl[0][1];
                        }
                        else
                        {
                            int side = (int)Math.Floor(rnd() * 4);
                            double off = 1 + rnd() * rnd() * 14, t2 = rnd();
                            if (side < 2) { x = (side == 1 ? 1 : -1) * (W / 2 + off); z = (t2 - 0.5) * (L + 30); } else { z = (side == 3 ? 1 : -1) * (L / 2 + off); x = (t2 - 0.5) * (W + 30); }
                            if (!Allowed(x, z, 0.6)) continue;
                        }
                        kelpPts.Add(new[] { x, z });
                    }
                    SwayInstanced(PlantGeometry(3, 0.22, 10, 2.2, 0.25, seed + 3), new Mat3 { color = Color.white, roughness = 0.7f, side = Side.Double }, kelpPts, (p, i, o) =>
                    {
                        double x = p[0], z = p[1], y = SeabedY(x, z), h = (-y - 0.2) * (0.55 + rnd() * 0.42);
                        o.position = new Vector3((float)x, (float)y, (float)z);
                        o.rotation = new Vector3(0, (float)(rnd() * TAU), 0);
                        float sx = (float)(1 + rnd() * 0.4), sz = (float)(1 + rnd() * 0.4);
                        o.scale = new Vector3(sx, (float)h, sz);
                    }, _ => Tint(PickHex(new[] { 0x6b6a24, 0x7a6a28, 0x55601f }), 0.12), 1.0f);
                    var weed = Scatter(260, r: 0.3, inside: 0.25, falloff: 12);
                    SwayInstanced(PlantGeometry(5, 0.07, 5, 1.0, 0.12, seed + 4), new Mat3 { color = Color.white, roughness = 0.8f, side = Side.Double }, weed, (p, i, o) =>
                    {
                        o.position = new Vector3((float)p[0], (float)SeabedY(p[0], p[1]), (float)p[1]);
                        o.rotation = new Vector3(0, (float)(rnd() * TAU), 0);
                        o.scale = new Vector3(1, (float)(0.25 + rnd() * 0.5), 1);
                    }, _ => Tint(PickHex(new[] { 0x4f5d1e, 0x7a3b2e, 0x6b5a2a }), 0.12), 0.15f);
                    AddFish(new[]
                    {
                        new School { c = new Vector3(-17, -2.2f, 0), r = new Vector2(4, 12), w = 0.12f, n = 14, col = 0x9aa5a8, s = 0.34f },
                        new School { c = new Vector3(16, -3.0f, -15), r = new Vector2(3, 5), w = -0.2f, n = 12, col = 0x7d8a7a, s = 0.3f },
                    });
                }
                else if (life == "arctic")
                {
                    AddStars(new[] { 0xe85d2a, 0xd6336c, 0xf08c2e, 0x9c36b5 }, 40);
                    AddJellies(12);
                }

                if (look.ice) AddIce();

                // arena boundary: red / white floats on a rope, corner anchor lines
                var fl = new List<double[]>();
                const double step = 0.75;
                double per = 2 * (W + L);
                for (double s = 0; s < per; s += step) { var e = EdgePoint(s); fl.Add(new[] { e[0], e[1], s }); }
                Color red = C(0xd62828), white = C(0xf4f4f4);
                var fg = Geo.Sphere(1, 10, 6);
                fg.Scale(1, 0.8f, 1);
                WaveInstanced(fg, new Mat3 { roughness = 0.45f }, fl, (p, i, o) => { o.position = new Vector3((float)p[0], 0.02f, (float)p[1]); o.scale = Vector3.one * 0.15f; },
                    i => Math.Floor(fl[i][2] / 3) % 2 == 1 ? white : red, 1.5f);
                var rp = new Geo { lines = true };
                const double seg = 0.5;
                for (double s = 0; s < per; s += seg)
                {
                    var a = EdgePoint(s); var b = EdgePoint(Math.Min(per, s + seg));
                    rp.pos.Add(V3(a[0], 0.0, a[1])); rp.pos.Add(V3(b[0], 0.0, b[1]));
                }
                foreach (var cxz in new[] { new[] { -W / 2.0, -L / 2.0 }, new[] { W / 2.0, -L / 2.0 }, new[] { W / 2.0, L / 2.0 }, new[] { -W / 2.0, L / 2.0 } })
                {
                    double x = cxz[0], z = cxz[1];
                    rp.pos.Add(V3(x, 0, z)); rp.pos.Add(V3(x * 1.08, SeabedY(x * 1.08, z * 1.04), z * 1.04));
                }
                ropeLines = MakeDynMesh(rp, "rope", true);
                ropeLines.node.mat = new Mat3 { kind = MatKind.Line, color = C(0xe8dfc4) };

                // floating DUBURI marker buoy with sign near the south end
                const float bx = 7, bz = 22; // east of the start dock, clear of the course
                var bp = new List<Geo>();
                var body = Geo.Cylinder(0.42f, 0.55f, 0.9f, 16); body.Translate(0, -0.25f, 0); bp.Add(body.Colored(0xf2a20c));
                var ring = Geo.Torus(0.5f, 0.05f, 6, 20); ring.RotateX(Mathf.PI / 2); ring.Translate(0, 0.12f, 0); bp.Add(ring.Colored(0xffffff));
                var mast = Geo.Cylinder(0.035f, 0.035f, 1.5f, 6); mast.Translate(0, 0.9f, 0); bp.Add(mast.Colored(0x9aa0a6));
                var board = Geo.Box(1.9f, 1.9f / 2.6f + 0.1f, 0.04f); board.Translate(0, 1.25f, 0); bp.Add(board.Colored(0xffffff));
                var buoy = Node.Mesh(Geo.MergeNonIndexed(bp), new Mat3 { vertexColors = true, roughness = 0.5f }, "buoy");
                buoy.position = new Vector3(bx, 0, bz);
                rootN.Add(buoy);
                followers.Add(new Follower { node = buoy, basePos = buoy.position });
                if (logoTexture != null)
                {
                    float asp = logoTexture.width > 0 && logoTexture.height > 0 ? logoTexture.width / (float)logoTexture.height : 2.6f;
                    const float w = 1.8f;
                    var front = Geo.Plane(w, w / asp); var back = Geo.Plane(w, w / asp);
                    front.Translate(0, 1.25f, 0.025f);
                    back.RotateY(Mathf.PI); back.Translate(0, 1.25f, -0.025f);
                    var sign = Node.Mesh(Geo.MergeNonIndexed(new List<Geo> { front, back }), new Mat3 { map = logoTexture, transparent = true, roughness = 0.7f }, "sign");
                    sign.position = new Vector3(bx, 0, bz);
                    rootN.Add(sign);
                    followers.Add(new Follower { node = sign, basePos = sign.position });
                }

                double[] EdgePoint(double s)
                {
                    if (s < W) return new[] { -W / 2 + s, -L / 2.0 };
                    if (s < W + L) return new[] { W / 2.0, -L / 2 + (s - W) };
                    if (s < 2 * W + L) return new[] { W / 2 - (s - W - L), L / 2.0 };
                    return new[] { -W / 2.0, L / 2 - Math.Min(L, s - 2 * W - L) };
                }
            }

            void AddStars(int[] pal, int n)
            {
                var pts = Scatter(n, r: 0.2, inside: 0.3, falloff: 10);
                Instanced(StarGeometry(), new Mat3 { color = Color.white, roughness = 0.8f }, pts, (p, i, o) =>
                {
                    o.position = new Vector3((float)p[0], (float)(SeabedY(p[0], p[1]) + 0.005), (float)p[1]);
                    o.rotation = new Vector3(0, (float)(rnd() * TAU), 0);
                    o.scale = Vector3.one * (float)(0.1 + rnd() * 0.1);
                }, _ => C(PickHex(pal)));
            }

            // schools of fish swimming elliptical loops (CPU-animated instance matrices)
            void AddFish(School[] schools)
            {
                int total = 0;
                foreach (var s in schools) total += s.n;
                var P = new float[total * 7];
                var cols = new Color[total];
                int k = 0;
                for (int si = 0; si < schools.Length; si++)
                {
                    var s = schools[si];
                    for (int j = 0; j < s.n; j++, k++)
                    {
                        P[k * 7] = si;
                        P[k * 7 + 1] = (float)((rnd() - 0.5) * 0.7);
                        P[k * 7 + 2] = (float)((rnd() - 0.5) * 2.2);
                        P[k * 7 + 3] = (float)((rnd() - 0.5) * 0.9);
                        P[k * 7 + 4] = (float)((rnd() - 0.5) * 2.2);
                        P[k * 7 + 5] = (float)(0.9 + rnd() * 0.2);
                        P[k * 7 + 6] = (float)(s.s * (0.8 + rnd() * 0.4));
                        cols[k] = Tint(s.col, 0.15);
                    }
                }
                var fish = FishGeometry();
                int nv = fish.Count;
                // one mesh for every fish: per-vertex fish index, palette colour per fish
                var all = new List<Geo>(total);
                for (int i = 0; i < total; i++) { var g = fish.Clone(); g.Colored(cols[i]); all.Add(g); }
                var merged = Geo.Merge(all);
                var dm = MakeDynMesh(merged, "fish", true);
                dm.node.mat = new Mat3 { color = Color.white, roughness = 0.45f, metalness = 0.25f, vertexColors = true };
                var basePos = fish.pos.ToArray();
                var baseNrm = fish.nrm.ToArray();
                animators.Add(t =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        int o = i * 7;
                        var s = schools[(int)P[o]];
                        float a = t * s.w * P[o + 5] + P[o + 1], dir = s.w < 0 ? -1 : 1;
                        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        var pos = new Vector3(s.c.x + s.r.x * ca + P[o + 2], s.c.y + Mathf.Sin(a * 2 + P[o + 1] * 5) * 0.3f + P[o + 3], s.c.z + s.r.y * sa + P[o + 4]);
                        float yaw = Mathf.Atan2(-s.r.x * sa * dir, s.r.y * ca * dir);
                        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw), sc = P[o + 6];
                        // tail wiggle: transformed.x += sin(uTime * 11 + instance * 1.37) * 0.22 * clamp(0.1 - position.z, 0, 1)
                        float wig = Mathf.Sin(t * 11f + i * 1.37f) * 0.22f;
                        int vb = i * nv;
                        for (int v = 0; v < nv; v++)
                        {
                            Vector3 lp = basePos[v];
                            lp.x += wig * Mathf.Clamp01(0.1f - lp.z);
                            // rotation about Y then uniform scale + translation
                            float x = (cy * lp.x + sy * lp.z) * sc, z = (-sy * lp.x + cy * lp.z) * sc;
                            dm.pos[vb + v] = new Vector3(pos.x + x, pos.y + lp.y * sc, pos.z + z);
                            Vector3 n = baseNrm[v];
                            dm.nrm[vb + v] = new Vector3(cy * n.x + sy * n.z, n.y, -sy * n.x + cy * n.z);
                        }
                    }
                    dm.mesh.SetVertices(dm.pos);
                    dm.mesh.SetNormals(dm.nrm);
                });
            }

            void AddJellies(int n)
            {
                var J = new float[n * 5];
                int made = 0;
                for (int i = 0; made < n && i < 400; i++)
                {
                    double x = (rnd() * 2 - 1) * (W / 2 + 10), z = (rnd() * 2 - 1) * (L / 2 + 10);
                    if (InArena(x, z) && Math.Min(W / 2 - Math.Abs(x), L / 2 - Math.Abs(z)) > 2) continue;
                    double y = -1.6 - rnd() * 2.2, ph = rnd() * TAU, sc = 0.25 + rnd() * 0.25;
                    J[made * 5] = (float)x; J[made * 5 + 1] = (float)y; J[made * 5 + 2] = (float)z; J[made * 5 + 3] = (float)ph; J[made * 5 + 4] = (float)sc;
                    made++;
                }
                // translucent glowing bells: emissive + lit colour folded into one unlit colour
                // (there is no transparent + emissive lit template)
                Color albedo = C(0xf2c6e8), em = C(0x8a4f9e) * 0.6f;
                float k = AmbientEstimate();
                Color col = em + albedo * k;
                col.a = 0.55f;
                var mat = new Mat3 { kind = MatKind.Basic, color = col, transparent = true, opacity = 0.55f, depthWrite = false, side = Side.Double };
                var geo = JellyGeometry();
                var nodes = new Node[n];
                for (int i = 0; i < n; i++) { nodes[i] = Node.Mesh(geo, mat, "jelly"); rootN.Add(nodes[i]); }
                animators.Add(t =>
                {
                    for (int i = 0; i < n; i++)
                    {
                        var tr = nodes[i].transform;
                        if (tr == null) continue;
                        int o = i * 5;
                        float ph = J[o + 3], s = J[o + 4], p = Mathf.Sin(t * 1.6f + ph);
                        tr.localPosition = new Vector3(J[o] + Mathf.Sin(t * 0.07f + ph) * 0.8f, J[o + 1] + Mathf.Sin(t * 0.25f + ph) * 0.4f, J[o + 2] + Mathf.Cos(t * 0.06f + ph) * 0.8f);
                        tr.localRotation = Space.Euler(Mathf.Sin(t * 0.3f + ph) * 0.15f, ph, 0);
                        tr.localScale = new Vector3(s * (1 + 0.1f * p), s * (1 - 0.12f * p), s * (1 + 0.1f * p));
                    }
                });
            }

            // ice floes; open water over clear zones, the centre-line, the boundary and the sign buoy
            void AddIce()
            {
                bool Open(double x, double z, double r) =>
                    NearZone(x, z, r + 1.0) ||
                    (Math.Abs(x) < 3 + r && Math.Abs(z) < L / 2 + 3) ||
                    Math.Sqrt((x - 7) * (x - 7) + (z - 22) * (z - 22)) < 2.5 + r ||
                    (Math.Abs(Math.Abs(x) - W / 2) < r + 0.8 && Math.Abs(z) < L / 2 + r) ||
                    (Math.Abs(Math.Abs(z) - L / 2) < r + 0.8 && Math.Abs(x) < W / 2 + r);
                var lists = new[] { new List<double[]>(), new List<double[]>() };
                const double step = 4.2;
                for (double gz = -L / 2.0 - 40; gz <= L / 2.0 + 40; gz += step)
                    for (double gx = -W / 2.0 - 40; gx <= W / 2.0 + 40; gx += step)
                    {
                        if (rnd() < 0.12) continue;
                        double x = gx + (rnd() - 0.5) * 2.2, z = gz + (rnd() - 0.5) * 2.2, r = 1.7 + rnd() * 1.4;
                        if (Open(x, z, r)) continue;
                        lists[rnd() < 0.5 ? 0 : 1].Add(new[] { x, z, r });
                    }
                var mat = new Mat3 { color = Color.white, roughness = 0.55f }.Emissive(0x6fa8bf, 0.45f);
                for (int v = 0; v < 2; v++)
                {
                    Instanced(IceGeometry(seed + 200 + (uint)v), mat, lists[v], (p, i, o) =>
                    {
                        double x = p[0], z = p[1], r = p[2];
                        double thick = InArena(x, z) ? 0.3 + rnd() * 0.25 : 0.4 + rnd() * 1.1;
                        o.position = new Vector3((float)x, 0.1f, (float)z);
                        o.rotation = new Vector3(0, (float)(rnd() * TAU), 0);
                        float sx = (float)(r * (0.85 + rnd() * 0.3)), sz = (float)(r * (0.85 + rnd() * 0.3));
                        o.scale = new Vector3(sx, (float)thick, sz);
                    }, _ => Tint(0xe6f4fa, 0.06));
                }
            }

            // kelp / weed: instances baked into one mesh; the sway runs per vertex on the CPU
            void SwayInstanced(Geo geo, Mat3 mat, List<double[]> list, Action<double[], int, Dummy> place, Func<int, Color> color, float sway)
            {
                var parts = new List<Geo>(list.Count);
                var instXZ = new Vector2[list.Count];
                var g2 = mat.side == Side.Double ? geo.TwoSided() : geo;
                for (int i = 0; i < list.Count; i++)
                {
                    dummy.Reset();
                    place(list[i], i, dummy);
                    var c = g2.Clone().ApplyMatrix(dummy.Matrix);
                    c.Colored(color(i));
                    parts.Add(c);
                    instXZ[i] = new Vector2(dummy.position.x, dummy.position.z);
                }
                if (parts.Count == 0) return;
                var merged = Geo.Merge(parts);
                var w = new float[merged.Count];
                var inst = new int[merged.Count];
                int nv = g2.Count;
                for (int i = 0; i < list.Count; i++)
                    for (int v = 0; v < nv; v++) { float y = g2.pos[v].y; w[i * nv + v] = y * y * sway; inst[i * nv + v] = i; }
                var m = mat.Clone();
                m.vertexColors = true;
                m.side = Side.Front; // back faces are in the geometry already
                var dm = MakeDynMesh(merged, "sway", true);
                dm.node.mat = m;
                sways.Add(new Sway { dm = dm, w = w, inst = inst, instXZ = instXZ, amount = sway });
            }

            Vector3 V3(double x, double y, double z) => new Vector3((float)x, (float)y, (float)z);

            // ---- realize
            rootN.Add(water.node);
            water.node.mat = null; // the water's material is per camera (waterMat)
            root = rootN.Realize(world, owned);
            var wr = water.node.transform.gameObject;
            wr.AddComponent<MeshFilter>().sharedMesh = water.mesh;
            var wmr = wr.AddComponent<MeshRenderer>();
            wmr.sharedMaterial = waterMat;
            wmr.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var f in followers) f.t = f.node.transform;
            foreach (var r in rays)
            {
                r.go = new GameObject("ray");
                r.go.transform.SetParent(root.transform, false);
                r.go.AddComponent<MeshFilter>().sharedMesh = r.mesh;
                var mr = r.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = r.mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // ---- caustics on the sun (the JS adds them to the floor / walls in the fragment shader)
            cookie = CausticCookie.Shared;
            ApplyCookie();
        }

        sealed class School { public Vector3 c; public Vector2 r; public float w; public int n; public int col; public float s; }

        // ---------------------------------------------------------------- helpers for the CPU-animated parts

        DynMesh MakeDynMesh(Geo g, string name, bool asNode)
        {
            var dm = new DynMesh();
            if (g.col != null && !g.lines)
            {
                // palette colours are applied by Node realize; keep the mesh dynamic here
            }
            dm.basePos = g.pos.ToArray();
            dm.pos = g.pos.ToArray();
            dm.baseNrm = g.nrm.Count == g.Count ? g.nrm.ToArray() : null;
            dm.nrm = dm.baseNrm != null ? (Vector3[])dm.baseNrm.Clone() : null;
            dm.node = Node.Mesh(g, null, name);
            if (asNode)
            {
                // realized with the scenery: the mesh comes from Geo.ToMesh (palette UVs included)
                dmPending.Add(dm);
            }
            else
            {
                dm.mesh = owned.Add(g.ToMesh(name));
                dm.mesh.MarkDynamic();
            }
            return dm;
        }
        readonly List<DynMesh> dmPending = new List<DynMesh>();

        static Texture2D RayTexture()
        {
            // e = sin(PI u)^2, alpha *= (1 - v)^1.6
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true) { name = "ray", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float u = (i + 0.5f) / N, v = (j + 0.5f) / N;
                    float e = Mathf.Sin(Mathf.PI * u); e *= e;
                    float a = e * Mathf.Pow(1 - v, 1.6f);
                    px[j * N + i] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255), 0, 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        void MakeSnow(int n, Texture2D sprite, Color colLinear)
        {
            var go = new GameObject("particles");
            snow = go.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = snow.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = n;
            main.startLifetime = 1e6f;
            main.startSpeed = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = snow.emission;
            emission.enabled = false;
            var shape = snow.shape;
            shape.enabled = false;
            snowRenderer = go.GetComponent<ParticleSystemRenderer>();
            snowRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            snowRenderer.sortMode = ParticleSystemSortMode.None;
            snowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            snowRenderer.receiveShadows = false;
            var m = new Mat3 { kind = MatKind.Points, map = sprite, color = colLinear, opacity = colLinear.a, name = "particles" };
            snowRenderer.sharedMaterial = owned.Add(Mats.Create(m));
            snowParticles = new ParticleSystem.Particle[n];
            for (int i = 0; i < n; i++)
            {
                float s = snowSeed[i];
                var p = new ParticleSystem.Particle
                {
                    startLifetime = 1e6f,
                    remainingLifetime = 1e6f,
                    startSize = snowSize * (0.6f + 0.8f * s),
                    startColor = new Color(1, 1, 1, 0.35f + 0.65f * Frac(s * 13.7f)),
                    velocity = Vector3.zero,
                };
                snowParticles[i] = p;
            }
            snow.Play();
            UpdateSnow(0);
            go.transform.SetParent(null, false);
            snowGo = go;
        }
        GameObject snowGo;

        static float Frac(float x) => x - Mathf.Floor(x);

        void UpdateSnow(float t)
        {
            if (snow == null) return;
            float uDepth = snowDepth;
            for (int i = 0; i < snowParticles.Length; i++)
            {
                Vector3 p = snowPos[i];
                float s = snowSeed[i];
                float x = p.x + Mathf.Sin(t * 0.21f + s * 6.28f) * 0.35f;
                float z = p.z + Mathf.Cos(t * 0.17f + s * 9.1f) * 0.35f;
                float m = p.y + uDepth - t * snowFall * (0.5f + s) + Mathf.Sin(t * 0.3f + s * 20.0f) * 0.15f;
                float mod = uDepth - 0.05f;
                float y = -uDepth + (m - mod * Mathf.Floor(m / mod));
                snowParticles[i].position = new Vector3(x, y, -z); // three -> Unity world
            }
            snow.SetParticles(snowParticles, snowParticles.Length);
        }

        // ---------------------------------------------------------------- water looks (camera-centred textures)

        void BuildWaterTextures(bool isPool)
        {
            Color surf = C(look.surface), deep = C(look.fog) * 0.7f, waterC = C(look.water), sky = C(look.sky), sunCol = C(look.sun);
            float lightK = (float)look.light, aboveAlpha = isPool ? 0.35f : 0.72f;
            const float shim = 0.2f; // mean of the animated ripple shimmer
            const int N = 256;
            Vector3 sd = SunDir;

            // from below: Snell's window toward straight up, total internal reflection outside it
            var under = new Color[N * N];
            float maxU = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float rx = ((i + 0.5f) / N - 0.5f) * 2 * RUnder, rz = ((j + 0.5f) / N - 0.5f) * 2 * RUnder;
                    var up = new Vector3(rx, 1, rz).normalized;   // camera -> surface point, depth 1
                    float c = up.y;                                 // dot(up, n), flat n
                    float win = (float)Smooth(0.60, 0.70, c);
                    float rim = (float)(Smooth(0.55, 0.65, c) * (1.0 - Smooth(0.66, 0.76, c)));
                    Color skyC = Color.Lerp(surf, Color.white, 0.5f) * 1.3f;
                    Color refl = Color.Lerp(deep, surf, 0.35f + 0.35f * shim);
                    Color col = Color.Lerp(refl, skyC, win) + surf * (rim * 0.5f + shim * 0.35f);
                    col += sunCol * Mathf.Pow(Mathf.Max(Vector3.Dot(up, sd), 0), 90) * 2.5f * win;
                    col *= lightK;
                    col.a = Mathf.Lerp(0.94f, 0.82f, win);
                    under[j * N + i] = col;
                    maxU = Mathf.Max(maxU, col.maxColorComponent);
                }
            // from above: Fresnel mix of water and sky, sun glint
            var above = new Color[N * N];
            float maxA = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float rx = ((i + 0.5f) / N - 0.5f) * 2 * RAbove, rz = ((j + 0.5f) / N - 0.5f) * 2 * RAbove;
                    var V = new Vector3(-rx, 1, -rz).normalized;   // surface point -> camera at height 1
                    float fres = 0.02f + 0.98f * Mathf.Pow(1 - Mathf.Clamp01(V.y), 5);
                    var h = (V + sd).normalized;
                    float spec = Mathf.Pow(Mathf.Max(h.y, 0), 220);
                    Color col = Color.Lerp(waterC, sky, fres) * Mathf.Min(lightK, 1.1f) + sunCol * spec * 1.6f;
                    col.a = Mathf.Clamp01(Mathf.Lerp(aboveAlpha, 1, fres) + spec);
                    above[j * N + i] = col;
                    maxA = Mathf.Max(maxA, col.maxColorComponent);
                }
            scaleUnder = Mathf.Max(1, maxU);
            scaleAbove = Mathf.Max(1, maxA);
            texUnder = owned.Add(EncodeHdr(under, N, scaleUnder, "waterUnder"));
            texAbove = owned.Add(EncodeHdr(above, N, scaleAbove, "waterAbove"));
        }

        static Texture2D EncodeHdr(Color[] lin, int N, float scale, string name)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                Color c = lin[i] / scale;
                Color g = new Color(c.r, c.g, c.b).gamma;
                px[i] = new Color32(To8(g.r), To8(g.g), To8(g.b), To8(lin[i].a));
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);

        // an estimate of the diffuse light a surface gets (ambient + half the sun), for unlit stand-ins
        float AmbientEstimate()
        {
            Color amb = (Color.Lerp(C(look.surface), Color.white, 0.5f) + C(look.water) * 0.5f) * 0.5f * (1.5f * (float)look.light / Mathf.PI);
            float a = (amb.r + amb.g + amb.b) / 3f;
            return a + 0.55f * 0.3f + sunBase * 0.5f;
        }

        // ---------------------------------------------------------------- sun

        /// the sun shines along this three.js direction
        public void SetSunDirection(Vector3 threeDir)
        {
            if (Sun == null) return;
            Sun.transform.rotation = Quaternion.LookRotation(Space.DirToUnity(threeDir), Vector3.up);
        }

        void ApplyCookie()
        {
            if (Sun == null) return;
            if (cookie == null || Caustic <= 0)
            {
                Sun.cookie = null;
                Sun.intensity = sunBase;
                return;
            }
            // light * cookie = base * (1 + cc * caustic * k): k turns the JS's added diffuse * cc * uCaustic
            // into a fraction of the sun's diffuse on the floor (n.l ~ 0.92)
            float k = 1f / Mathf.Max(0.05f, sunBase * 0.92f);
            cookieGain = Caustic * k;
            float m = 1 + 2 * cookieGain;
            Sun.intensity = sunBase * m;
            Sun.cookie = cookie.Texture;
            var ad = Sun.GetUniversalAdditionalLightData();
            ad.lightCookieSize = new Vector2(3.5f, 3.5f);   // the JS caustic tiles every 3.5 m
            ad.lightCookieOffset = Vector2.zero;
            cookieScale = m;
        }
        float cookieGain, cookieScale = 1;

        // ---------------------------------------------------------------- per frame

        /// update(time, env): wave uniforms from the environment and the animations
        public void Update(float t, Env env)
        {
            var w = wave;
            if (env != null)
            {
                var wc = env.cfg != null ? env.cfg.waves : null;
                w.A = wc != null && wc.amplitude > 0 && env.waveOmega != 0 ? (float)wc.amplitude : 0;
                w.K = (float)env.waveK;
                w.W = (float)env.waveOmega;
                w.T = (float)env.time;
                w.D = new Vector2((float)env.waveDir.x, (float)env.waveDir.y);
            }
            Update(t, w);
        }

        /// update(time, fakeEnv) with the wave uniforms given directly (the menu backdrop)
        public void Update(float t, WaveState w)
        {
            if (root == null) return;
            time = t;
            wave = w;
            if (!realizedDyn) RealizeDyn();
            for (int i = 0; i < animators.Count; i++) animators[i](t);
            // water surface follows the waves
            if (water.mesh != null && (w.A != 0 || waterWasWavy))
            {
                for (int i = 0; i < water.basePos.Length; i++)
                {
                    var b = water.basePos[i];
                    water.pos[i] = new Vector3(b.x, b.y + w.Eta(b.x, b.z), b.z);
                }
                water.mesh.SetVertices(water.pos);
                waterWasWavy = w.A != 0;
            }
            for (int i = 0; i < followers.Count; i++)
            {
                var f = followers[i];
                if (f.t == null) continue;
                var b = f.basePos;
                f.t.localPosition = new Vector3(b.x, b.y + w.Eta(b.x, b.z), b.z);
            }
            if (ropeLines != null && ropeLines.mesh != null && (w.A != 0 || ropeWasWavy))
            {
                for (int i = 0; i < ropeLines.basePos.Length; i++)
                {
                    var b = ropeLines.basePos[i];
                    ropeLines.pos[i] = new Vector3(b.x, b.y + w.Eta(b.x, b.z), b.z);
                }
                ropeLines.mesh.SetVertices(ropeLines.pos);
                ropeWasWavy = w.A != 0;
            }
            foreach (var s in sways)
            {
                var dm = s.dm;
                if (dm.mesh == null) continue;
                int ni = s.instXZ.Length;
                if (swayX == null || swayX.Length < ni) { swayX = new float[ni]; swayZ = new float[ni]; }
                for (int i = 0; i < ni; i++)
                {
                    float ph = t * 0.7f + s.instXZ[i].x * 0.31f + s.instXZ[i].y * 0.23f;
                    swayX[i] = Mathf.Sin(ph) * 0.6f + Mathf.Sin(ph * 2.3f + 1.7f) * 0.2f;
                    swayZ[i] = Mathf.Cos(ph * 0.8f + 0.5f) * 0.5f;
                }
                for (int v = 0; v < dm.basePos.Length; v++)
                {
                    int i = s.inst[v];
                    float k = s.w[v];
                    var b = dm.basePos[v];
                    dm.pos[v] = new Vector3(b.x + swayX[i] * k, b.y, b.z + swayZ[i] * k);
                }
                dm.mesh.SetVertices(dm.pos);
            }
            foreach (var r in rays)
            {
                float ox = Mathf.Sin(t * 0.23f + r.seed * 6.28f) * 0.6f, oz = Mathf.Cos(t * 0.19f + r.seed * 4.1f) * 0.6f;
                for (int i = 0; i < r.basePos.Length; i++) r.pos[i] = r.basePos[i] + new Vector3(ox * r.d[i], 0, oz * r.d[i]);
                r.mesh.SetVertices(r.pos);
            }
            UpdateSnow(t);
            if (cookie != null && Sun != null && Sun.cookie != null)
            {
                cookie.Update(t * 0.45f + 23f, cookieGain, cookieScale);
            }
        }
        bool waterWasWavy = true, ropeWasWavy = true, realizedDyn;
        float[] swayX, swayZ;

        void RealizeDyn()
        {
            realizedDyn = true;
            foreach (var dm in dmPending)
            {
                if (dm.node.transform == null) continue;
                var mf = dm.node.transform.GetComponent<MeshFilter>();
                if (mf != null) { dm.mesh = mf.sharedMesh; dm.mesh.MarkDynamic(); }
            }
        }

        /// scWave at three.js (x, z) with the current wave uniforms
        public float WaveY(float x, float z) => wave.Eta(x, z);

        /// applyForCamera(camera): under / above the surface for this camera (fog, background, god
        /// rays, the water's look), applied when the camera renders. Call it every frame per view.
        public void ApplyForCamera(Camera cam)
        {
            if (cam == null) return;
            var st = RenderHooks.State(cam);
            Vector3 c = Space.FromUnity(cam.transform.position);
            float eta = wave.Eta(c.x, c.z);
            st.under = c.y < eta;
            st.depth = eta - c.y;
            st.threePos = c;
            st.fov = cam.fieldOfView;
            st.pixelHeight = cam.pixelHeight;
            st.scenery = this;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = st.under ? bgUnder : bgAbove;
        }

        void OnBeginCamera(Camera cam, RenderHooks.CamState st)
        {
            if (st.scenery != this || root == null) return;
            bool under = st.under;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = under ? fogUnderColor : fogAboveColor;
            RenderSettings.fogDensity = under ? fogUnderDensity : fogAboveDensity;
            // god rays only under water, faded with the distance like the JS fragment shader
            foreach (var r in rays)
            {
                if (r.go.activeSelf != under) r.go.SetActive(under);
                if (!under) continue;
                float dist = DistToSegment(st.threePos, r.top, r.bottom);
                float flick = 0.55f + 0.45f * Mathf.Sin(time * 0.6f + r.seed * 30f);
                float a = rayI * flick * Mathf.Exp(-dist * rayFade) * (float)Smooth(0.5, 3.0, dist);
                Color c = rayColor; c.a = a;
                Mats.SetBaseColorLinear(r.mat, c);
            }
            // the water from below (Snell's window) or from above (Fresnel + glint), centred on the camera
            if (waterMat != null)
            {
                float h = Mathf.Max(0.05f, Mathf.Abs(st.depth));
                float R = under ? RUnder : RAbove;
                float s = 1f / (2 * R * h);
                Mats.SetMap(waterMat, under ? texUnder : texAbove);
                Mats.SetMapTiling(waterMat, new Vector2(s, s), new Vector2(0.5f - st.threePos.x * s, 0.5f - st.threePos.z * s));
                float k = under ? scaleUnder : scaleAbove;
                Mats.SetBaseColorLinear(waterMat, new Color(k, k, k, 1));
            }
            // marine snow: 1..48 px points (gl_PointSize clamp)
            if (snowRenderer != null && st.pixelHeight > 0)
            {
                snowRenderer.minParticleSize = 1f / st.pixelHeight;
                snowRenderer.maxParticleSize = 48f / st.pixelHeight;
            }
        }

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        /// fog colour of the water (for bubbles and post)
        public Color FogColor(bool under) => under ? fogUnderColor : fogAboveColor;

        public void Dispose()
        {
            RenderHooks.BeginCamera -= OnBeginCamera;
            RenderHooks.Forget(this);
            if (root != null) UnityEngine.Object.Destroy(root);
            if (Sun != null) UnityEngine.Object.Destroy(Sun.gameObject);
            if (snowGo != null) UnityEngine.Object.Destroy(snowGo);
            owned.DestroyAll();
            root = null;
            Sun = null;
        }
    }

    /// The floor caustics as a light cookie: the JS caustic pattern (after Dave Hoskins, uv period 1)
    /// baked into key frames once, cross-faded every frame into one R8 texture.
    public sealed class CausticCookie
    {
        const int N = 128, Frames = 32;
        const float Step = 0.12f;   // pattern time between key frames
        readonly byte[][] keys = new byte[Frames][];
        int made;
        readonly byte[] lut = new byte[256];
        readonly byte[] buf = new byte[N * N];
        float lutGain = -1, lutScale = -1;
        public Texture2D Texture { get; }

        static CausticCookie shared;
        public static CausticCookie Shared => shared ?? (shared = new CausticCookie());

        CausticCookie()
        {
            var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.R8) ? TextureFormat.R8 : TextureFormat.Alpha8;
            Texture = new Texture2D(N, N, fmt, true, true) { name = "caustics", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            Texture.hideFlags = HideFlags.DontSave;
            keys[0] = Bake(23f);
            made = 1;
            Fill(keys[0]);
        }

        // tileable water caustic, min(c, 2) / 2 as a byte
        static byte[] Bake(float time)
        {
            var px = new byte[N * N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float u = (i + 0.5f) / N, v = (j + 0.5f) / N;
                    float c = Mathf.Min(Caustic(u, v, time), 2f);
                    px[j * N + i] = (byte)Mathf.Clamp(Mathf.RoundToInt(c / 2f * 255f), 0, 255);
                }
            return px;
        }

        static float Mod(float x, float y) => x - y * Mathf.Floor(x / y);

        // scCaustic(uv, time) of the JS
        static float Caustic(float u, float v, float time)
        {
            const float TAU = 6.2831853f;
            float px = Mod(u * TAU, TAU) - 250f, py = Mod(v * TAU, TAU) - 250f;
            float ix = px, iy = py, c = 1f;
            for (int n = 0; n < 4; n++)
            {
                float t = time * (1f - 3.5f / (n + 1));
                float nx = px + Mathf.Cos(t - ix) + Mathf.Sin(t + iy);
                float ny = py + Mathf.Sin(t - iy) + Mathf.Cos(t + ix);
                ix = nx; iy = ny;
                float a = px / (Mathf.Sin(ix + t) / 0.005f), b = py / (Mathf.Cos(iy + t) / 0.005f);
                c += 1f / Mathf.Sqrt(a * a + b * b);
            }
            c /= 4f;
            c = 1.17f - Mathf.Pow(c, 1.4f);
            return Mathf.Pow(Mathf.Abs(c), 8f);
        }

        /// pattern time t; cookie = (1 + cc * gain) / scale
        public void Update(float t, float gain, float scale)
        {
            if (made < Frames) { keys[made] = Bake(23f + made * Step); made++; } // one more key frame per frame
            if (gain != lutGain || scale != lutScale)
            {
                for (int i = 0; i < 256; i++) lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((1 + 2 * i / 255f * gain) / scale * 255f), 0, 255);
                lutGain = gain; lutScale = scale;
            }
            float f = Mod((t - 23f) / Step, made);
            int k0 = (int)f, k1 = (k0 + 1) % made;
            int w = Mathf.RoundToInt((f - k0) * 256);
            var a = keys[k0]; var b = keys[k1];
            for (int i = 0; i < buf.Length; i++) buf[i] = lut[(a[i] * (256 - w) + b[i] * w) >> 8];
            Texture.LoadRawTextureData(buf);
            Texture.Apply(true, false);
        }

        void Fill(byte[] key)
        {
            for (int i = 0; i < buf.Length; i++) buf[i] = key[i];
            Texture.LoadRawTextureData(buf);
            Texture.Apply(true, false);
        }
    }
}
