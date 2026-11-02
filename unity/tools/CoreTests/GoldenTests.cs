// The C# side of every golden case in tools/golden/make-golden.mjs: same inputs,
// same calls, same structure.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Duburi.Core;
using static CoreTests.G;
using Env = Duburi.Core.Environment;

namespace CoreTests
{
    static class GoldenTests
    {
        const double DT = Vehicle.DT;

        // ------------------------------------------------------------ math
        public static void MathCases(Suite s)
        {
            var j = Load("math");
            int idx = 0;
            foreach (var c in j.GetProperty("linalg").EnumerateArray())
            {
                var A = Mat(c.GetProperty("A"));
                var svd = MathUtil.Svd(A);
                Compare(s, c, O("A", A, "S", svd.S, "U", svd.U, "V", svd.V, "P", MathUtil.Pinv(A), "rank", MathUtil.RankOf(A)), "linalg[" + idx++ + "]");
            }
            var so = j.GetProperty("solve");
            var SA = Mat(so.GetProperty("A"));
            var Sb = Vec(so.GetProperty("b"));
            Compare(s, so, O("A", SA, "b", Sb, "x", MathUtil.Solve(SA, Sb)), "solve");
            var m3j = j.GetProperty("m3");
            var m3 = Vec(m3j.GetProperty("m"));
            Compare(s, m3j, O("m", m3, "inv", MathUtil.M3Inv(m3), "mul", MathUtil.M3Mul(m3, MathUtil.M3Inv(m3)), "t", MathUtil.M3Transpose(m3)), "m3");
            idx = 0;
            foreach (var c in j.GetProperty("quats").EnumerateArray())
            {
                var e = Vec(c.GetProperty("e"));
                V3 v = V3Of(c.GetProperty("v"));
                var q2a = Vec(c.GetProperty("q2"));
                var q2 = new Quat(q2a[0], q2a[1], q2a[2], q2a[3]);
                Quat q = MathUtil.QFromEuler(e[0], e[1], e[2]);
                var ev = new V3(e[0], e[1], e[2]);
                Compare(s, c, O("e", e, "q", q, "euler", MathUtil.QToEuler(q), "aa", MathUtil.QToAxisAngle(q2), "q2", q2, "fromAA", MathUtil.QFromAxisAngleVec(v), "v", v,
                    "rot", MathUtil.QRotate(q, v), "rotInv", MathUtil.QRotateInv(q, v), "mul", MathUtil.QMul(q, q2), "mat", MathUtil.QToMat(q2),
                    "wrap", MathUtil.WrapPI(e[0] * 7), "wrap2", MathUtil.Wrap2PI(-e[2] * 5), "hyp", JsMath.Hypot(v.x, v.y, v.z), "norm", MathUtil.Vnorm(v), "cross", MathUtil.Vcross(v, ev)), "quats[" + idx++ + "]");
            }
            var r2 = new RNG(7);
            var gm = new GaussMarkov(new RNG(9), 0.3, 2);
            var seq = new List<object>();
            for (int i = 0; i < 50; i++) seq.Add(new[] { r2.Next(), r2.Gauss(), gm.Step(1.0 / 120) });
            Compare(s, j.GetProperty("rng"), O("seq", seq, "seed0", new RNG(0).Next(), "seedNeg", new RNG(-5.7).Next()), "rng");
        }

        // ------------------------------------------------------------ water
        public static void WaterCases(Suite s)
        {
            var j = Load("water");
            var dens = new List<object>();
            var visc = new List<object>();
            foreach (var row in j.GetProperty("dens").EnumerateArray())
            {
                var r = Vec(row);
                double T = r[0], S = r[1];
                dens.Add(new[] { T, S, Water.WaterDensity(T, S), Water.Eos80Density0(S, Water.ToT68(T)), Water.Eos80SecantBulkModulus(S, T, 50), Water.Eos80Density(S, T, 100) });
                visc.Add(new[] { T, S, Water.WaterViscosity(T, S), Water.KinematicViscosity(T, S) });
            }
            var air = new List<object>();
            foreach (double h in new double[] { 0, 100, 1500, 4000 }) air.Add(new[] { h, Water.AirPressureAtAltitude(h) });
            var am = Water.EllipsoidAddedMass(0.3, 0.2, 0.1, 1025);
            var drag = Water.DragFromProjections(new Water.Projections { area = new[] { 0.1, 0.2, 0.3 }, roll = 0.01, pitch = 0.02, yaw = 0.03 }, 1020);
            Compare(s, j, O("dens", dens, "visc", visc, "air", air, "am", O("coeff", am.coeff, "values", am.values), "drag", O("linear", drag.linear, "quadratic", drag.quadratic),
                "sphere", Water.EllipsoidAddedMass(0.2, 0.2, 0.2).values), "water");
        }

        // ------------------------------------------------------------ thruster
        public static void ThrusterCases(Suite s)
        {
            var j = Load("thruster");
            var curves = new OMap();
            foreach (var kv in Thrusters.THRUSTER_PRESETS) curves[kv.Key] = Thrusters.ParametricCurve(kv.Value.curve);
            curves["default"] = Thrusters.ParametricCurve();
            curves["odd"] = Thrusters.ParametricCurve(new CurveParams { fwdMax = 33.3, revMax = 21.7, deadband = 0, expo = 1.7, pwmMin = 1150, pwmMax = 1850 });
            curves["wide"] = Thrusters.ParametricCurve(new CurveParams { deadband = 500 });
            var pwms = new List<double>();
            for (double p = 1000; p <= 2000; p += 7.3) pwms.Add(p);
            var thrust = new OMap();
            foreach (var kv in curves)
            {
                var c = new ThrustCurve((double[][])kv.Value);
                thrust[kv.Key] = O("pwm", c.pwm, "T", c.T, "fwdMax", c.fwdMax, "revMax", c.revMax, "at", pwms.ConvertAll(p => c.Thrust(p)));
            }
            var odd = (double[][])curves["odd"];
            var variants = new OrderedMap<string, ThrusterDef>
            {
                ["t200"] = new ThrusterDef { preset = "t200-16v" },
                ["t500"] = new ThrusterDef { preset = "t500-20v", hand = -1, deduction = 0.1 },
                ["small"] = new ThrusterDef { preset = "generic-small", propDiameter = 0.06 },
                ["rev"] = new ThrusterDef { preset = "t200-16v", polarity = -1 },
                ["revTable"] = new ThrusterDef { preset = "t200-12v", polarity = -1, tableOnReversedUnit = true, deductionRev = 0.2 },
                ["off"] = new ThrusterDef { preset = "t200-16v", enabled = false },
                ["weak"] = new ThrusterDef { preset = "t200-16v", health = 0.5, tauUp = 0, reverseDwell = 0 },
                ["noElec"] = new ThrusterDef { preset = "t200-16v", elecSet = true, elec = null, inflowModel = false, J0 = 0.8 },
                ["custom"] = new ThrusterDef { table = odd, voltage = 14, escMinV = 9, idleCurrent = 0.2 },
            };
            var traces = new OMap();
            foreach (var kv in variants)
            {
                var def = kv.Value;
                if (def.table == null) def.table = Thrusters.ParametricCurve(Thrusters.THRUSTER_PRESETS[def.preset].curve);
                var st = new ThrusterState(def);
                var rows = new List<object>();
                for (int i = 0; i < 400; i++)
                {
                    double t = i * DT;
                    double pwm = i < 60 ? 1900 : i < 120 ? 1100 : i < 200 ? 1500 + 380 * JsMath.Sin(t * 9) : i < 260 ? 1520 : i < 330 ? 1300 : 1500 + 400 * JsMath.Cos(t * 4);
                    double vb = i < 300 ? st.Vnom : st.Vnom * (1 - 0.6 * ((i - 300) / 100.0));
                    st.Update(pwm, DT, vb);
                    double eff = st.Effective(JsMath.Sin(t * 3) * 1.5, -0.05 + i * 0.001);
                    rows.Add(new[] { st.n, st.T, st.Tcmd, st.current, st.dwell, eff, st.vent, st.ReactionTorque() });
                }
                var maxCurrent = new List<double>();
                foreach (double V in new double[] { 6, 12, 16, 22 }) maxCurrent.Add(st.MaxCurrent(V));
                var vscale = new List<object>();
                foreach (double V in new double[] { 5, 10, 12, 16, 21 }) vscale.Add(new[] { st.VoltageScale(V, 1), st.VoltageScale(V, -1) });
                var rec = O("Vnom", st.Vnom, "torqueRatio", st.torqueRatio, "reverseDwell", st.reverseDwell, "J0Rev", st.J0Rev, "escMinV", st.escMinV, "idleA", st.idleA, "maxCurrent", maxCurrent, "vscale", vscale, "rows", rows);
                st.T = 12.5;
                rec["setT"] = new[] { st.n, st.T };
                traces[kv.Key] = rec;
            }
            const string text = "# pwm, kgf\n1100, -2.9\r\n1300;-1.2\n 1500 0\n1700\t1.5,\n\n1900, 3.7, x\nfoo\n";
            Compare(s, j, O("curves", curves, "thrust", thrust, "pwms", pwms, "traces", traces, "parsed", Thrusters.ParseCurveText(text, "kgf"), "parsedN", Thrusters.ParseCurveText(text)), "thruster");
        }

        // ------------------------------------------------------------ environment
        public static void EnvCases(Suite s)
        {
            var j = Load("env");
            var cfgs = new OrderedMap<string, EnvConfig>
            {
                ["pool"] = Scenarios.EnvironmentConfig("pool"),
                ["pacific"] = Scenarios.EnvironmentConfig("pacific", 20),
                ["arctic"] = Scenarios.EnvironmentConfig("arctic"),
                ["sloped"] = new EnvConfig
                {
                    temperature = 12, salinity = 30, densityOverride = 0, airPressure = 990, current = new V3(0.1, -0.05, 0.01),
                    turbulence = new TurbulenceConfig { sigma = 0.08, tau = 0.5 }, waves = new WavesConfig { amplitude = 0.3, period = 3.5, direction = 200 },
                    pool = new PoolConfig { length = 30, width = 14, depth = 6, floor = "sloped", shallowDepth = 1.5, shallowEnd = "north", slopeStart = 12, slopeEnd = 13 },
                },
                ["override"] = new EnvConfig
                {
                    densityOverride = 1100, gravity = 9.7, waves = new WavesConfig { amplitude = 0.05, period = 0 },
                    pool = new PoolConfig { floor = "sloped", shallowDepth = 2, slopeStart = double.NaN, slopeEnd = 4 }, // slopeStart: 'x' in the JS
                },
            };
            var all = new OMap();
            double[] xs = { -14, -6.5, -1, 0, 0.5, 3, 7.25, 13 };
            int[] pIdx = { 0, 1, 7, 100, 255, 511, 512 };
            double[] gz = { -1, 0, 0.3, 1, 2.5, 4, 7.7, 12, 30 };
            foreach (var kv in cfgs)
            {
                var e = new Env(kv.Value, new RNG(77));
                var f = e.floor;
                var steps = new List<object>();
                var rec = O("rho", e.rho, "nu", e.nu, "g", e.g, "pAtm", e.pAtm, "waveOmega", e.waveOmega, "waveK", e.waveK, "waveDir", e.waveDir, "T68", e.T68, "salinityPSU", e.salinityPSU,
                    "floor", O("L", f.L, "W", f.W, "sloped", f.sloped, "minDepth", f.minDepth, "maxDepth", f.maxDepth, "a", f.a, "b", f.b, "north", f.north, "breaks", f.breaks,
                        "depth", Array.ConvertAll(xs, x => f.DepthAt(x)), "slope", Array.ConvertAll(xs, x => f.SlopeAt(x))),
                    "pTable", Array.ConvertAll(pIdx, i => e.pTable[i]), "pDz", e.pDz,
                    "gauge", Array.ConvertAll(gz, z => (object)new[] { e.PressureGauge(z), e.DensityAtDepth(z) }),
                    "steps", steps);
                for (int i = 0; i < 240; i++)
                {
                    e.Step(DT);
                    if (i % 8 == 7)
                    {
                        double x = -10 + i * 0.07, y = 3 - i * 0.02, z = 0.2 + i * 0.01;
                        var wp = e.WaveProfile(x, z);
                        steps.Add(O("t", e.time, "gust", e.gust, "gustAccel", e.gustAccel, "eta", e.SurfaceElevation(x, y), "vel", e.FluidVelocity(x, y, z), "acc", e.FluidAccel(x, y, z),
                            "p", e.PressureAt(x, y, z), "pa", e.AbsolutePressureAt(x, y, z * 3), "wp", O("k", wp.k, "fu", wp.fu, "fw", wp.fw, "fp", wp.fp)));
                    }
                }
                all[kv.Key] = rec;
            }
            Compare(s, j, all, "env");
        }

        // ------------------------------------------------------------ allocation + frames
        static object Analysis(LayoutAnalysis r) => O(
            "N", r.N, "controllable", r.controllable, "status", Map(r.status), "factors", r.factors,
            "authority", Map(r.authority, a => O("pos", a.pos, "neg", a.neg, "unit", a.unit)),
            "coupling", Map(r.coupling, c => Map(c)), "singular", r.singular, "rank", r.rank, "warnings", r.warnings,
            "conditioning", r.conditioning, "candidates", r.candidates == null ? new List<object>() : L(r.candidates, c => O("axes", c.axes, "rel", c.rel)),
            "lever", r.lever, "Fref", r.Fref, "summary", r.summary);

        public static void AllocCases(Suite s)
        {
            var j = Load("alloc");
            var bots = new OMap();
            foreach (var id in Bots.BOT_ORDER)
            {
                var d = Bots.BOTS[id];
                var env = new Env(Scenarios.EnvironmentConfig("pool"), new RNG(1));
                var v = new Vehicle(d, env);
                var defs = new List<ThrusterDef>();
                for (int i = 0; i < d.thrusters.Length; i++) defs.Add(new ThrusterDef { pos = d.thrusters[i].pos, axis = d.thrusters[i].axis, fwdMax = v.th[i].curve.fwdMax, revMax = v.th[i].curve.revMax });
                var bb = Allocation.BuildB(defs);
                bots[id] = O(
                    "thrusters", L(d.thrusters, t => O("name", t.name, "pos", t.pos, "axis", t.axis, "preset", t.preset, "hand", t.hand)),
                    "B", bb.B, "nom", bb.nom, "P", v.P, "wmax", v.wmax, "hands", L(v.th, t => t.def.hand), "volume", v.volume, "Mt", v.Mt, "It", v.It, "voltage", v.voltage,
                    "analysis", Analysis(Allocation.AnalyzeLayout(defs, new V3(0.01, 0, 0.02))),
                    "limited", Analysis(Allocation.AnalyzeLayout(defs, V3.Zero, new LayoutOptions { threshold = 0.2, enabledAxes = new Dictionary<string, bool> { ["roll"] = false, ["pitch"] = false } })));
            }
            var frames = new Dictionary<string, object>();
            for (int fc = 0; fc <= 8; fc++)
            {
                var Lay = Frames.FrameLayout(fc);
                var signs = new double[Lay.Count];
                for (int i = 0; i < signs.Length; i++) signs[i] = i % 3 != 0 ? 1 : -1;
                var tq = Allocation.TableQuality(Lay, Frames.FrameTable(fc), V3.Zero, signs);
                frames[fc.ToString()] = O("layout", L(Lay, t => O("name", t.name, "pos", t.pos, "axis", t.axis)), "table", Frames.FrameTable(fc), "order", Frames.FrameTestOrder(fc),
                    "analysis", Analysis(Allocation.AnalyzeLayout(Lay)), "W", tq.W,
                    "rows", L(tq.rows, r => O("axis", r.axis, "main", r.main, "off", L(r.off, o => O("axis", o.axis, "value", o.value)), "reversed", r.reversed)),
                    "ew", Allocation.EffectiveWrench(Lay, Frames.FrameTable(fc)));
            }
            Compare(s, j, O("bots", bots, "frames", frames, "empty", Analysis(Allocation.AnalyzeLayout(new List<ThrusterDef>()))), "alloc");
        }

        // ------------------------------------------------------------ vehicle traces
        public static void VehicleCases(Suite s)
        {
            var j = Load("vehicle");
            foreach (var sc in new[] { "pool", "pacific" })
                foreach (var id in Bots.BOT_ORDER)
                {
                    var env = new Env(Scenarios.EnvironmentConfig(sc), new RNG(5));
                    var v = new Vehicle(Bots.BOTS[id], env);
                    v.Reset(new V3(2, -1, 0.3), 0.4);
                    v.depthTarget = 0.8;
                    var rows = new List<object>();
                    for (int i = 0; i < 900; i++)
                    {
                        var I = v.input;
                        I.Clear();
                        if (i >= 150 && i < 300) { I.surge = 1; I.yaw = 0.5; }
                        if (i >= 300 && i < 400) I.roll = 1;
                        if (i >= 400 && i < 480) { I.pitch = -1; I.sway = 0.7; }
                        if (i >= 480 && i < 560) I.heave = 1; // up
                        if (i == 560) v.depthTarget = 0; // surface on depth hold (floats at the top)
                        if (i == 800) v.depthHold = false;
                        if (i >= 805 && i < 840) I.heave = -0.5; // down again
                        env.Step(DT);
                        v.Step(DT, !(i >= 860 && i < 875));
                        if (i % 4 == 3)
                        {
                            var dg = v.diag;
                            rows.Add(O("i", i, "p", v.p, "v", v.v, "w", v.w, "q", v.q, "tq", v.targetQ, "hdg", v.hdg, "dt", v.depthTarget, "dI", v.depthI, "acro", v.acro,
                                "u", (double[])v.u.Clone(), "depth", v.depth, "speed", v.Speed(),
                                "diag", O("submerged", dg.submerged, "buoyancy", dg.buoyancy, "weight", dg.weight, "rho", dg.rho, "thrust", dg.thrust, "current", dg.current, "net", dg.net, "flow", dg.flow),
                                "n", L(v.th, t => t.n), "Te", L(v.th, t => t.Teff), "cur", L(v.th, t => t.current)));
                        }
                    }
                    Compare(s, j.GetProperty(sc + "-" + id), rows, sc + "-" + id);
                }
        }

        // ------------------------------------------------------------ course
        static object Course2(Course c) => O(
            "floor", c.floor,
            "random", O("sharkSide", c.random.sharkSide, "boardSharkTop", c.random.boardSharkTop, "binSharkNear", c.random.binSharkNear),
            "dock", O("pos", c.dock.pos, "radius", c.dock.radius),
            "gate", O("pos", c.gate.pos, "heading", c.gate.heading, "width", c.gate.width, "legDepth", c.gate.legDepth, "topDepth", c.gate.topDepth, "barH", c.gate.barH,
                "dividerDepth", c.gate.dividerDepth, "sharkSide", c.gate.sharkSide, "dir", c.gate.dir, "right", c.gate.right),
            "paths", L(c.paths, p => O("pos", p.pos, "heading", p.heading)),
            "slalom", O("pos", c.slalom.pos, "sets", L(c.slalom.sets, st => O("pos", st.pos, "pipes", L(st.pipes, p => O("p", p.p, "color", p.color)))),
                "heading", c.slalom.heading, "dir", c.slalom.dir, "right", c.slalom.right, "band", c.slalom.band),
            "bin", O("pos", c.bin.pos, "size", c.bin.size, "plate", c.bin.plate, "height", c.bin.height, "sharkNear", c.bin.sharkNear, "heading", c.bin.heading, "dir", c.bin.dir, "right", c.bin.right, "halves", c.bin.halves),
            "board", O("pos", c.board.pos, "depth", c.board.depth, "size", c.board.size, "sharkTop", c.board.sharkTop, "heading", c.board.heading, "dir", c.board.dir, "right", c.board.right,
                "openings", L(c.board.openings, o => O("u", o.u, "v", o.v, "r", o.r, "shape", o.shape, "animal", o.animal, "label", o.label)), "farDistance", c.board.farDistance),
            "octagon", O("pos", c.octagon.pos, "radius", c.octagon.radius, "tableTop", c.octagon.tableTop, "tableHalf", c.octagon.tableHalf, "heading", c.octagon.heading,
                "images", L(c.octagon.images, im => O("animal", im.animal, "angle", im.angle)),
                "baskets", L(c.octagon.baskets, b => O("item", b.item, "c", b.c, "h", b.h, "depthTop", b.depthTop)),
                "trash", L(c.octagon.trash, t => O("kind", t.kind, "pos", t.pos))),
            "obstacles", L(c.obstacles, o =>
                o.type == "cyl" ? O("type", o.type, "tag", o.tag, "p", o.p, "r", o.r, "z0", o.z0, "z1", o.z1) :
                o.type == "box" ? O("type", o.type, "tag", o.tag, "c", o.c, "h", o.h, "yaw", o.yaw) :
                o.type == "ring" ? O("type", o.type, "tag", o.tag, "p", o.p, "R", o.R, "r", o.r, "z", o.z) :
                (object)O("type", o.type, "tag", o.tag)),
            "pingers", L(c.pingers, p => O("id", p.id, "pos", p.pos, "freq", p.freq, "period", p.period)),
            "tasks", L(c.tasks, t => O("id", t.id, "name", t.name, "pos", t.pos)));

        public static void CourseCases(Suite s)
        {
            var j = Load("course");
            var all = new List<object>();
            foreach (double seed in new double[] { 1, 2, 3, 42, 1234, 99991 })
                foreach (double depth in new double[] { 7, 12, 20 })
                {
                    var r = new RNG(seed);
                    var c = Course.Build(r.Next, depth);
                    var probe = L(new[] { new V3(6.9, -4.1, c.board.depth + 0.1), new V3(7.5, -3.6, 4), new V3(0, 0, 0) }, p => { var b = Course.BoardLocal(c.board, p); return O("u", b.u, "v", b.v, "n", b.n); });
                    all.Add(O("seed", seed, "depth", depth, "course", Course2(c), "probe", probe));
                }
            Compare(s, j, all, "course");
        }

        // ------------------------------------------------------------ stats, scenarios, setup
        static object EnvCfg(EnvConfig c) => O("temperature", c.temperature, "salinity", c.salinity, "current", c.current,
            "turbulence", O("sigma", c.turbulence.sigma, "tau", c.turbulence.tau), "waves", O("amplitude", c.waves.amplitude, "period", c.waves.period, "direction", c.waves.direction),
            "visibility", c.visibility, "pool", O("length", c.pool.length, "width", c.pool.width, "style", c.pool.style, "depth", c.pool.depth, "floor", c.pool.floor, "freeboard", c.pool.freeboard));

        public static void StatsCases(Suite s)
        {
            var j = Load("stats");
            var st = new OMap();
            foreach (var id in Bots.BOT_ORDER)
            {
                var b = Stats.BotStats(Bots.BOTS[id]);
                st[id] = O("surge", b.surge, "sway", b.sway, "heave", b.heave, "yawRate", b.yawRate, "rollRate", b.rollRate, "righting", b.righting, "thrust", b.thrust,
                    "mass", b.mass, "thrusters", b.thrusters, "dof", b.dof, "buoyancy", Map(b.buoyancy));
            }
            var sc = new OMap();
            foreach (var id in Scenarios.SCENARIO_ORDER)
            {
                var l = Scenarios.SCENARIOS[id].look;
                sc[id] = O("cfg", EnvCfg(Scenarios.EnvironmentConfig(id)), "cfg20", EnvCfg(Scenarios.EnvironmentConfig(id, 20)),
                    "look", O("water", l.water, "fog", l.fog, "fogDensity", l.fogDensity, "sky", l.sky, "floor", l.floor, "light", l.light, "sun", l.sun, "surface", l.surface, "particles", l.particles, "life", l.life, "ice", l.ice));
            }
            var su = MenuSetup.DefaultSetup();
            var setup = O("scenario", su.scenario, "minutes", su.minutes, "coinFlip", su.coinFlip, "depth", su.depth, "slots", L(su.slots, x => O("type", x.type, "name", x.name, "bot", x.bot, "color", x.color)));
            Compare(s, j, O("stats", st, "scenarios", sc, "setup", setup, "name", MenuSetup.GAME_NAME), "stats");
        }

        // ------------------------------------------------------------ whole matches
        static object PlayerState(Game g, Player p) => O(
            "p", p.veh.p, "q", p.veh.q, "v", p.veh.v, "w", p.veh.w, "dt", p.veh.depthTarget, "hold", p.veh.depthHold, "state", p.run.state, "total", g.Total(p), "best", p.best, "left", p.left,
            "markers", p.run.markers, "torpedoes", p.run.torpedoes, "chosen", p.run.chosen, "scores", ScoresOf(p.run.scores), "task", g.CurrentTask(p));

        public static Game RunMatch(JsonElement m, Suite s, string label)
        {
            var o = m.GetProperty("opts");
            var bots = new List<string>();
            foreach (var b in o.GetProperty("players").EnumerateArray()) bots.Add(b.GetString());
            var opts = new GameOptions
            {
                scenario = o.GetProperty("scenario").GetString(),
                seed = D(o.GetProperty("seed")),
                coinFlip = o.GetProperty("coinFlip").GetBoolean(),
                players = new List<PlayerConfig>(),
            };
            if (Has(o, "depth")) opts.depth = D(o.GetProperty("depth"));
            if (Has(o, "runMinutes")) opts.runMinutes = D(o.GetProperty("runMinutes"));
            for (int i = 0; i < bots.Count; i++) opts.players.Add(new PlayerConfig { bot = bots[i], name = i == 1 ? "" : "P" + (i + 1) });
            var g = new Game(opts);
            var aps = new List<Autopilot>();
            for (int i = 0; i < g.players.Count; i++) aps.Add(new Autopilot(g, i));
            var states = new List<object>();
            int n = (int)JsMath.Round(D(m.GetProperty("seconds")) / DT);
            for (int k = 1; k <= n && !g.over; k++)
            {
                foreach (var a in aps) a.Update(DT);
                g.Step(DT);
                if (k % 120 == 0)
                    states.Add(O("t", g.time, "over", g.over, "idle", g.idle, "k", L(aps, a => a.k), "players", L(g.players, p => PlayerState(g, p)),
                        "items", L(g.items, it => O("kind", it.kind, "state", it.state, "pos", it.pos, "on", it.on, "owner", it.owner != null ? it.owner.index : -1))));
            }
            if (s == null) return g;
            var actual = O("name", m.GetProperty("name").GetString(), "seconds", D(m.GetProperty("seconds")), "opts", ObjOpts(o), "time", g.time, "over", g.over, "steps", states,
                "events", L(g.events, e => O("player", e.player, "text", e.text, "pts", e.pts, "kind", e.kind, "t", e.t)),
                "final", L(g.players, p => O("name", p.name, "color", p.color, "bot", p.bot, "best", p.best,
                    "run", O("state", p.run.state, "reason", p.run.reason, "scores", ScoresOf(p.run.scores), "total", g.Total(p), "chosen", p.run.chosen, "gateSide", p.run.gateSide, "finished", p.run.finished),
                    "runs", L(p.runs, r => O("total", r.total, "scores", ScoresOf(r.scores), "reason", r.reason)))));
            Compare(s, m, actual, label);
            return g;
        }

        // the opts echo is input data: copy it so the comparison covers only results
        static object ObjOpts(JsonElement o)
        {
            var d = new Dictionary<string, object>();
            foreach (var p in o.EnumerateObject())
            {
                switch (p.Value.ValueKind)
                {
                    case JsonValueKind.Number: d[p.Name] = p.Value.GetDouble(); break;
                    case JsonValueKind.True: case JsonValueKind.False: d[p.Name] = p.Value.GetBoolean(); break;
                    case JsonValueKind.String: d[p.Name] = p.Value.GetString(); break;
                    default: { var l = new List<object>(); foreach (var x in p.Value.EnumerateArray()) l.Add(x.GetString()); d[p.Name] = l; break; }
                }
            }
            return d;
        }

        public static void MatchCases(Suite s, string file)
        {
            foreach (var m in Load(file).EnumerateArray()) RunMatch(m, s, m.GetProperty("name").GetString());
        }
    }
}
