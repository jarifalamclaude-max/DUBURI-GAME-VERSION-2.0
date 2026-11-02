// Port of src/alloc/allocation.js.
// Thruster allocation analysis and automatic "maximum-DOF" mixer generation.
//
// Wrench order used here: [Fx, Fy, Fz, Mx, My, Mz] in the body FRD frame about
// the CG. ArduSub's six pilot/controller inputs map onto unit wrenches:
//   roll -> +Mx, pitch -> +My, yaw -> +Mz, throttle -> -Fz (up),
//   forward -> +Fx, lateral -> +Fy.

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    public struct ThrusterNominalResult
    {
        public double fwd, rev, nom;
    }

    public sealed class AllocationMatrix
    {
        public double[][] B;  // 6 x N, columns scaled by nominal thrust
        public double[] nom;  // N, nominal thrust per thruster (0 when disabled)
    }

    public sealed class AxisAuthority
    {
        public double pos, neg;
        public string unit;
    }

    public sealed class LayoutCandidate
    {
        public List<string> axes;
        public double rel;
    }

    /// analyzeLayout() result
    public sealed class LayoutAnalysis
    {
        public int N;
        public List<string> controllable = new List<string>();
        public OrderedMap<string, string> status = new OrderedMap<string, string>(); // full | weak | none | disabled
        public double[][] factors; // N x 6 (FACTOR_AXES order)
        public OrderedMap<string, AxisAuthority> authority = new OrderedMap<string, AxisAuthority>();
        public OrderedMap<string, OrderedMap<string, double>> coupling = new OrderedMap<string, OrderedMap<string, double>>();
        public double[] singular = new double[0];
        public int rank;
        public List<string> warnings = new List<string>();
        public double conditioning;
        public List<LayoutCandidate> candidates;
        public double lever, Fref;
        public string summary;
    }

    public sealed class AxisOff
    {
        public string axis;
        public double value;
    }

    public sealed class AxisQuality
    {
        public string axis;
        public double main;
        public List<AxisOff> off;
        public bool reversed;
    }

    public sealed class TableQualityResult
    {
        public double[][] W;
        public List<AxisQuality> rows;
    }

    public sealed class LayoutOptions
    {
        public double? threshold;
        public Dictionary<string, bool> enabledAxes; // axis -> false disables it
    }

    public static class Allocation
    {
        public static readonly string[] AXES = Frames.FACTOR_AXES; // roll, pitch, yaw, throttle, forward, lateral
        public static readonly OrderedMap<string, string> AXIS_LABEL = new OrderedMap<string, string> { ["roll"] = "Roll", ["pitch"] = "Pitch", ["yaw"] = "Yaw", ["throttle"] = "Heave", ["forward"] = "Surge", ["lateral"] = "Sway" };
        public static readonly OrderedMap<string, int> AXIS_ROW = new OrderedMap<string, int> { ["forward"] = 0, ["lateral"] = 1, ["throttle"] = 2, ["roll"] = 3, ["pitch"] = 4, ["yaw"] = 5 };
        public static readonly OrderedMap<string, double> AXIS_SIGN = new OrderedMap<string, double> { ["forward"] = 1, ["lateral"] = 1, ["throttle"] = -1, ["roll"] = 1, ["pitch"] = 1, ["yaw"] = 1 };
        public static readonly OrderedMap<string, string> AXIS_UNIT = new OrderedMap<string, string> { ["forward"] = "N", ["lateral"] = "N", ["throttle"] = "N", ["roll"] = "N·m", ["pitch"] = "N·m", ["yaw"] = "N·m" };

        public static ThrusterNominalResult ThrusterNominal(ThrusterDef t)
        {
            double fwd = JsMath.Max(1e-6, t.fwdMax ?? 50), rev = JsMath.Max(1e-6, t.revMax ?? 40);
            return new ThrusterNominalResult { fwd = fwd, rev = rev, nom = 0.5 * (fwd + rev) };
        }

        // 6 x N allocation matrix (columns scaled by nominal thrust) about point `cg`.
        public static AllocationMatrix BuildB(IList<ThrusterDef> thrusters) => BuildB(thrusters, V3.Zero);
        public static AllocationMatrix BuildB(IList<ThrusterDef> thrusters, V3 cg)
        {
            int N = thrusters.Count;
            var B = MathUtil.Zeros(6, N);
            var nom = new double[N];
            for (int i = 0; i < N; i++)
            {
                var t = thrusters[i];
                V3 a = MathUtil.Vnorm(t.axis);
                V3 r = MathUtil.Vsub(t.pos, cg);
                V3 m = MathUtil.Vcross(r, a);
                double F = ThrusterNominal(t).nom * (t.enabled == false ? 0 : 1);
                nom[i] = F;
                B[0][i] = a.x * F; B[1][i] = a.y * F; B[2][i] = a.z * F;
                B[3][i] = m.x * F; B[4][i] = m.y * F; B[5][i] = m.z * F;
            }
            return new AllocationMatrix { B = B, nom = nom };
        }

        static double[][] RowsOf(double[][] B, IList<string> axes)
        {
            var r = new double[axes.Count][];
            for (int k = 0; k < axes.Count; k++)
            {
                double[] row = B[AXIS_ROW[axes[k]]];
                double sg = AXIS_SIGN[axes[k]];
                r[k] = new double[row.Length];
                for (int j = 0; j < row.Length; j++) r[k][j] = row[j] * sg;
            }
            return r;
        }

        static List<List<string>> Subsets(IList<string> list)
        {
            var outp = new List<List<string>>();
            int n = list.Count;
            for (int mask = 1; mask < 1 << n; mask++)
            {
                var s = new List<string>();
                for (int i = 0; i < n; i++) if ((mask & (1 << i)) != 0) s.Add(list[i]);
                outp.Add(s);
            }
            Js.StableSort(outp, (a, b) => b.Count - a.Count);
            return outp;
        }

        static double Dot(double[] row, double[] x)
        {
            double s = 0; // row.reduce((s, v, j) => s + v * x[j], 0)
            for (int j = 0; j < row.Length; j++) s = s + row[j] * x[j];
            return s;
        }

        /// <summary>Analyse a thruster layout.</summary>
        /// <param name="thrusters">[{pos, axis, fwdMax, revMax, enabled}]</param>
        /// <param name="cg">centre of gravity in the same frame</param>
        /// <param name="opts">{ threshold (relative sigma), enabledAxes: {roll:true,...} }</param>
        public static LayoutAnalysis AnalyzeLayout(IList<ThrusterDef> thrusters, V3 cg = default, LayoutOptions opts = null)
        {
            if (opts == null) opts = new LayoutOptions();
            double threshold = opts.threshold ?? 0.02;
            var enabledAxes = new List<string>();
            foreach (var a in AXES) if (opts.enabledAxes == null || !opts.enabledAxes.TryGetValue(a, out bool en) || en) enabledAxes.Add(a);
            int N = thrusters.Count;
            var result = new LayoutAnalysis { N = N, factors = MathUtil.Zeros(N, 6) };
            if (N == 0)
            {
                foreach (var a in AXES) result.status[a] = "none";
                result.warnings.Add("No thrusters defined.");
                return result;
            }
            var bb = BuildB(thrusters, cg);
            double[][] B = bb.B;
            double[] nom = bb.nom;
            double levSum = 0;
            foreach (var t in thrusters)
            {
                V3 d = MathUtil.Vsub(t.pos, cg);
                levSum = levSum + (((0 + d.x * d.x) + d.y * d.y) + d.z * d.z);
            }
            double lever = Js.Or(JsMath.Sqrt(levSum / N), 0.1);
            double nomSum = 0;
            foreach (double v in nom) nomSum = nomSum + v;
            double Fref = Js.Or(nomSum / N, 1);
            var scale = new double[AXES.Length];
            for (int k = 0; k < AXES.Length; k++) scale[k] = AXES[k] == "roll" || AXES[k] == "pitch" || AXES[k] == "yaw" ? 1 / (Fref * lever) : 1 / Fref;
            double[][] Scaled(IList<string> axes)
            {
                var rows = RowsOf(B, axes);
                for (int i = 0; i < rows.Length; i++)
                {
                    double sc = scale[Array.IndexOf(AXES, axes[i])];
                    for (int j = 0; j < rows[i].Length; j++) rows[i][j] = rows[i][j] * sc;
                }
                return rows;
            }
            var full = MathUtil.Svd(Scaled(AXES));
            result.singular = full.S;
            double smax = Js.Or(full.S[0], 1);
            int rank = 0;
            foreach (double s in full.S) if (s > smax * 1e-9) rank++;
            result.rank = rank;

            // Pick the largest axis subset that is independently controllable.
            LayoutCandidate best = null;
            var candidates = new List<LayoutCandidate>();
            foreach (var S in Subsets(enabledAxes))
            {
                if (S.Count > N) continue;
                if (best != null && S.Count < best.axes.Count) break;
                double[] sv = MathUtil.Svd(Scaled(S)).S;
                int si = Math.Min(S.Count, N) - 1;
                double smin = si >= 0 && si < sv.Length ? sv[si] : 0;
                double rel = smin / smax;
                candidates.Add(new LayoutCandidate { axes = S, rel = rel });
                if (rel >= threshold && (best == null || rel > best.rel)) best = new LayoutCandidate { axes = S, rel = rel };
            }
            var chosen = best != null ? best.axes : new List<string>();
            foreach (var a in AXES) if (chosen.Contains(a)) result.controllable.Add(a);
            result.conditioning = best != null ? best.rel : 0;

            // Per-axis status: controllable / weak (achievable only coupled) / none
            var P = MathUtil.Pinv(Scaled(AXES));
            foreach (var a in AXES)
            {
                if (chosen.Contains(a)) { result.status[a] = "full"; continue; }
                if (!enabledAxes.Contains(a)) { result.status[a] = "disabled"; continue; }
                // how much of a pure command on this axis lies in the reachable space
                var e = new double[AXES.Length];
                for (int k = 0; k < AXES.Length; k++) e[k] = AXES[k] == a ? 1 : 0;
                var u = new double[P.Length];
                for (int i = 0; i < P.Length; i++) u[i] = Dot(P[i], e);
                var sAll = Scaled(AXES);
                var w = new double[sAll.Length];
                for (int i = 0; i < sAll.Length; i++) w[i] = Dot(sAll[i], u);
                double along = w[Array.IndexOf(AXES, a)];
                result.status[a] = along > 0.15 ? "weak" : "none";
            }

            if (chosen.Count > 0)
            {
                var BS = RowsOf(B, chosen);
                var Pinv = MathUtil.Pinv(BS);
                for (int ci = 0; ci < chosen.Count; ci++)
                {
                    string ax = chosen[ci];
                    var target = new double[chosen.Count];
                    for (int j = 0; j < chosen.Count; j++) target[j] = j == ci ? 1 : 0;
                    var u = new double[Pinv.Length]; // normalised thrust units
                    for (int i = 0; i < Pinv.Length; i++) u[i] = Dot(Pinv[i], target);
                    double umax = double.NegativeInfinity;
                    foreach (double v in u) umax = JsMath.Max(umax, Math.Abs(v));
                    umax = Js.Or(umax, 1);
                    int k = Array.IndexOf(AXES, ax);
                    for (int i = 0; i < u.Length; i++) result.factors[i][k] = Clean(u[i] / umax);
                    // authority (pure axis, respecting asymmetric thrust)
                    double Lim(double sgn)
                    {
                        double s = double.PositiveInfinity;
                        for (int i = 0; i < u.Length; i++)
                        {
                            double v = u[i];
                            if (Math.Abs(v) < 1e-12) continue;
                            var tn = ThrusterNominal(thrusters[i]);
                            double cap = (v * sgn > 0 ? tn.fwd : tn.rev) / tn.nom;
                            s = JsMath.Min(s, cap / Math.Abs(v));
                        }
                        return Js.IsFinite(s) ? s : 0;
                    }
                    result.authority[ax] = new AxisAuthority { pos = Lim(1), neg = Lim(-1), unit = AXIS_UNIT[ax] };
                    // coupling into the other axes (per unit of this axis)
                    var wc = new double[AXES.Length];
                    for (int j = 0; j < AXES.Length; j++) wc[j] = Dot(RowsOf(B, new[] { AXES[j] })[0], u);
                    var cpl = new OrderedMap<string, double>();
                    result.coupling[ax] = cpl;
                    for (int j = 0; j < AXES.Length; j++)
                        if (AXES[j] != ax && Math.Abs(wc[j]) > 1e-9) cpl[AXES[j]] = wc[j];
                }
            }
            foreach (var a in AXES) if (!result.authority.ContainsKey(a)) result.authority[a] = new AxisAuthority { pos = 0, neg = 0, unit = AXIS_UNIT[a] };
            result.candidates = candidates.GetRange(0, Math.Min(12, candidates.Count));
            result.lever = lever;
            result.Fref = Fref;
            if (result.controllable.Count == 6) result.summary = "Full 6-DOF control";
            else
            {
                var names = new List<string>();
                foreach (var a in result.controllable) names.Add(AXIS_LABEL[a]);
                string joined = string.Join(", ", names);
                result.summary = result.controllable.Count + "-DOF control (" + (joined.Length > 0 ? joined : "none") + ")";
            }
            return result;
        }

        static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0 : JsMath.Round(v * 1e6) / 1e6;

        /// <summary>
        /// Wrench produced per unit of each ArduSub input for a given factor table,
        /// including motor direction / prop polarity signs.
        /// Returns 6x6 matrix W[input][wrenchRow] in N / N·m.
        /// </summary>
        public static double[][] EffectiveWrench(IList<ThrusterDef> thrusters, double[][] factors, V3 cg = default, double[] netSigns = null)
        {
            var B = BuildB(thrusters, cg).B;
            var W = MathUtil.Zeros(6, 6);
            for (int k = 0; k < 6; k++)
            {
                for (int i = 0; i < thrusters.Count; i++)
                {
                    double f = (i < factors.Length && factors[i] != null ? factors[i][k] : 0) * (netSigns != null ? netSigns[i] : 1);
                    if (!Js.Truthy(f)) continue;
                    for (int r = 0; r < 6; r++) W[k][r] += B[r][i] * f;
                }
            }
            return W;
        }

        // Compare what a factor table actually does against the ideal per-axis command.
        public static TableQualityResult TableQuality(IList<ThrusterDef> thrusters, double[][] factors, V3 cg = default, double[] netSigns = null)
        {
            var W = EffectiveWrench(thrusters, factors, cg, netSigns);
            var rows = new List<AxisQuality>();
            for (int k = 0; k < AXES.Length; k++)
            {
                string ax = AXES[k];
                double[] w = W[k];
                double main = w[AXIS_ROW[ax]] * AXIS_SIGN[ax];
                var off = new List<AxisOff>();
                foreach (var o in AXES)
                {
                    if (o == ax) continue;
                    double value = w[AXIS_ROW[o]] * AXIS_SIGN[o];
                    if (Math.Abs(value) > 1e-6) off.Add(new AxisOff { axis = o, value = value });
                }
                rows.Add(new AxisQuality { axis = ax, main = main, off = off, reversed = main < -1e-9 });
            }
            return new TableQualityResult { W = W, rows = rows };
        }
    }
}
