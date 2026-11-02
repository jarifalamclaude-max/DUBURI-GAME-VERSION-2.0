// Port of src/alloc/frames.js.
// ArduSub built-in frame motor-factor tables, copied from
// libraries/AP_Motors/AP_Motors6DOF.cpp (ArduSub 4.5.7), plus representative
// thruster geometries consistent with those tables (for presets).
//
// Factor order (ArduSub add_motor_raw_6dof): roll, pitch, yaw, throttle, forward, lateral.

using System.Collections.Generic;

namespace Duburi.Core
{
    /// JS FRAMES entry
    public sealed class FrameInfo
    {
        public string key, name, mixer, warn;
        public double[][] table;
    }

    public static class Frames
    {
        const double S = JsMath.SQRT1_2;

        public static readonly string[] FACTOR_AXES = { "roll", "pitch", "yaw", "throttle", "forward", "lateral" };

        static readonly double[][] T_BLUEROV1 = {
            new[] { 0, 0, -1.0, 0, 1.0, 0 },
            new[] { 0, 0, 1.0, 0, 1.0, 0 },
            new[] { -0.5, 0.5, 0, 0.45, 0, 0 },
            new[] { 0.5, 0.5, 0, 0.45, 0, 0 },
            new[] { 0, -1.0, 0, 1.0, 0, 0 },
            new[] { -0.25, 0, 0, 0, 0, 1.0 },
        };
        static readonly double[][] T_V6_90 = {
            new[] { 1.0, 1.0, 0, 1.0, 0, 0 },
            new[] { 0, 0, 1.0, 0, 1.0, 0 },
            new[] { 1.0, -1.0, 0, 1.0, 0, 0 },
            new[] { 0, 0, 0, 0, 0, 1.0 },
            new[] { 0, 0, 0, 0, 0, 1.0 },
            new[] { -1.0, 1.0, 0, 1.0, 0, 0 },
            new[] { 0, 0, -1.0, 0, 1.0, 0 },
            new[] { -1.0, -1.0, 0, 1.0, 0, 0 },
        };
        static readonly double[][] T_V6 = {
            new[] { 0, 0, 1.0, 0, -1.0, 1.0 },
            new[] { 0, 0, -1.0, 0, -1.0, -1.0 },
            new[] { 0, 0, -1.0, 0, 1.0, 1.0 },
            new[] { 0, 0, 1.0, 0, 1.0, -1.0 },
            new[] { 1.0, -1.0, 0, -1.0, 0, 0 },
            new[] { -1.0, -1.0, 0, -1.0, 0, 0 },
            new[] { 1.0, 1.0, 0, -1.0, 0, 0 },
            new[] { -1.0, 1.0, 0, -1.0, 0, 0 },
        };
        static readonly double[][] T_V = {
            new[] { 0, 0, 1.0, 0, -1.0, 1.0 },
            new[] { 0, 0, -1.0, 0, -1.0, -1.0 },
            new[] { 0, 0, -1.0, 0, 1.0, 1.0 },
            new[] { 0, 0, 1.0, 0, 1.0, -1.0 },
            new[] { 1.0, 0, 0, -1.0, 0, 0 },
            new[] { -1.0, 0, 0, -1.0, 0, 0 },
        };
        static readonly double[][] T_SIMPLE3 = {
            new[] { 0, 0, -1.0, 0, 1.0, 0 },
            new[] { 0, 0, 1.0, 0, 1.0, 0 },
            new[] { 0, 0, 0, -1.0, 0, 0 },
        };
        static readonly double[][] T_DEFAULT = {
            new[] { 0, 0, -1.0, 0, 1.0, 0 },
            new[] { 0, 0, 1.0, 0, 1.0, 0 },
            new[] { 1.0, 0, 0, -1.0, 0, 0 },
            new[] { -1.0, 0, 0, -1.0, 0, 0 },
            new[] { 0, 0, 0, 0, 0, 1.0 },
        };

        // FRAME_CONFIG values (AP_Motors6DOF::sub_frame_t)
        public static readonly OrderedMap<int, FrameInfo> FRAMES = new OrderedMap<int, FrameInfo>
        {
            [0] = new FrameInfo { key = "BLUEROV1", name = "BlueROV1", table = T_BLUEROV1, mixer = "generic" },
            [1] = new FrameInfo { key = "VECTORED", name = "Vectored (BlueROV2, 6 thrusters)", table = T_V, mixer = "vectored" },
            [2] = new FrameInfo { key = "VECTORED_6DOF", name = "Vectored 6DOF (BlueROV2 Heavy, 8 thrusters)", table = T_V6, mixer = "vectored6dof" },
            [3] = new FrameInfo { key = "VECTORED_6DOF_90DEG", name = "Vectored 6DOF 90°", table = T_V6_90, mixer = "generic" },
            [4] = new FrameInfo { key = "SIMPLEROV_3", name = "SimpleROV-3", table = T_SIMPLE3, mixer = "generic" },
            [5] = new FrameInfo { key = "SIMPLEROV_4", name = "SimpleROV-4 (uses default 5-motor table)", table = T_DEFAULT, mixer = "generic" },
            [6] = new FrameInfo { key = "SIMPLEROV_5", name = "SimpleROV-5 (uses default 5-motor table)", table = T_DEFAULT, mixer = "generic" },
            // In ArduSub 4.5.7 the CUSTOM case has its `break` commented out and falls
            // through to SIMPLEROV_3. We reproduce that faithfully and warn in the UI.
            [7] = new FrameInfo { key = "CUSTOM", name = "Custom (falls through to SimpleROV-3 in firmware)", table = T_SIMPLE3, mixer = "generic", warn = "ArduSub 4.5 has no runtime custom frame: FRAME_CONFIG=7 behaves like SimpleROV-3." },
        };

        static FrameInfo Frame(int frameConfig) => FRAMES.TryGetValue(frameConfig, out var f) ? f : FRAMES[5];

        public static double[][] FrameTable(int frameConfig)
        {
            var t = Frame(frameConfig).table;
            var r = new double[t.Length][];
            for (int i = 0; i < t.Length; i++) r[i] = (double[])t[i].Clone();
            return r;
        }

        // add_motor_raw_6dof testing_order: every built-in Sub frame tests motors in board order 1..N
        public static int[] FrameTestOrder(int frameConfig)
        {
            var t = Frame(frameConfig).table;
            var r = new int[t.Length];
            for (int i = 0; i < t.Length; i++) r[i] = i + 1;
            return r;
        }

        // --- Representative geometries -------------------------------------------
        // Positions in the vehicle frame (FRD, metres, relative to the CG).
        // `axis` is the direction the thruster pushes the vehicle for a positive mixer
        // output (the frame convention). Values for the BlueROV2 family are close to
        // published dimensions; others are derived from the factor tables.

        static ThrusterDef T(string name, V3 pos, V3 axis) => new ThrusterDef { name = name, pos = pos, axis = axis };

        public static List<ThrusterDef> HeavyLayout()
        {
            double hx = 0.156, hy = 0.111, hz = 0.085; // horizontal thrusters
            double vx = 0.12, vy = 0.218, vz = -0.07; // vertical thrusters (outboard, upper)
            return new List<ThrusterDef>
            {
                T("T1 front-right", new V3(hx, hy, hz), new V3(-S, S, 0)),
                T("T2 front-left", new V3(hx, -hy, hz), new V3(-S, -S, 0)),
                T("T3 rear-right", new V3(-hx, hy, hz), new V3(S, S, 0)),
                T("T4 rear-left", new V3(-hx, -hy, hz), new V3(S, -S, 0)),
                T("T5 vert front-right", new V3(vx, vy, vz), new V3(0, 0, 1)),
                T("T6 vert front-left", new V3(vx, -vy, vz), new V3(0, 0, 1)),
                T("T7 vert rear-right", new V3(-vx, vy, vz), new V3(0, 0, 1)),
                T("T8 vert rear-left", new V3(-vx, -vy, vz), new V3(0, 0, 1)),
            };
        }

        public static List<ThrusterDef> VectoredLayout()
        {
            var L = HeavyLayout().GetRange(0, 4);
            L.Add(T("T5 vert right", new V3(0, 0.11, -0.06), new V3(0, 0, 1)));
            L.Add(T("T6 vert left", new V3(0, -0.11, -0.06), new V3(0, 0, 1)));
            return L;
        }

        // Heuristic geometry from a factor table (used for frames without a published layout).
        public static List<ThrusterDef> LayoutFromTable(double[][] table, double X = 0.18, double Y = 0.15, double Z = 0.06)
        {
            var outp = new List<ThrusterDef>(table.Length);
            for (int i = 0; i < table.Length; i++)
            {
                double[] f = table[i];
                double r = f[0], p = f[1], y = f[2], t = f[3], fw = f[4], lat = f[5];
                V3 axis, pos;
                if (Js.Truthy(fw) || Js.Truthy(lat))
                {
                    double n = JsMath.Hypot(fw, lat);
                    axis = new V3(fw / n, lat / n, 0);
                    if (Js.Truthy(fw) && Js.Truthy(lat))
                    {
                        // choose the corner that yields the right yaw sign
                        bool have = false;
                        double bestV = 0;
                        V3 bestPos = default;
                        foreach (double sx in new[] { 1.0, -1.0 })
                            foreach (double sy in new[] { 1.0, -1.0 })
                            {
                                double N = sx * X * axis.y - sy * Y * axis.x;
                                if (!have || N * JsMath.Sign(Js.Or(y, 1)) > bestV) { have = true; bestV = N * JsMath.Sign(Js.Or(y, 1)); bestPos = new V3(sx * X, sy * Y, 0); }
                            }
                        pos = bestPos;
                    }
                    else if (Js.Truthy(fw))
                    {
                        pos = new V3(0, Js.Truthy(y) ? (-JsMath.Sign(y) * JsMath.Sign(fw)) * Y : 0, 0);
                    }
                    else
                    {
                        pos = new V3(Js.Truthy(y) ? JsMath.Sign(y) * JsMath.Sign(lat) * X : 0, 0, 0);
                    }
                    if (Js.Truthy(r)) pos.z = -JsMath.Sign(r) * JsMath.Sign(Js.Or(lat, 1)) * Z;
                }
                else
                {
                    double dz = Js.Truthy(t) ? -JsMath.Sign(t) : 1;
                    axis = new V3(0, 0, dz);
                    pos = new V3(
                        Js.Truthy(p) ? -JsMath.Sign(p) * dz * X * JsMath.Min(1, System.Math.Abs(p) * 2) : 0,
                        Js.Truthy(r) ? JsMath.Sign(r) * dz * Y * JsMath.Min(1, System.Math.Abs(r) * 2) : 0,
                        -Z);
                }
                outp.Add(T("T" + (i + 1), pos, axis));
            }
            return outp;
        }

        public static List<ThrusterDef> FrameLayout(int frameConfig)
        {
            if (frameConfig == 2) return HeavyLayout();
            if (frameConfig == 1) return VectoredLayout();
            return LayoutFromTable(FrameTable(frameConfig));
        }
    }
}
