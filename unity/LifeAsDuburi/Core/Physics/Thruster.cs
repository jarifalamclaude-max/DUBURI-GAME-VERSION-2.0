// Port of src/physics/thruster.js.
// Thruster model: ESC PWM -> static thrust curve (mirrored about 1500 us for a
// reversed unit) -> bus-voltage scaling -> rotor-speed dynamics (separate
// acceleration / braking time constants, reversal dwell) -> in-flow,
// ventilation and hull-interaction corrections; electrical current draw
// I(T, V) and propeller reaction torque (CW/CCW handedness).
//
// A thruster curve table is double[][] of rows [pwm (us), thrust (N)], like the JS.

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    /// parametricCurve() options (JS destructured defaults)
    public sealed class CurveParams
    {
        public double fwdMax = 51.5, revMax = 40.2, deadband = 25, expo = 1.25, pwmMin = 1100, pwmMax = 1900;
        public CurveParams Clone() => (CurveParams)MemberwiseClone();
    }

    /// Full-throttle thrust (N) and current (A) vs supply voltage (V), JS ELECTRICAL entry
    public sealed class ElectricalData
    {
        public double[] V, fwd, rev, I;
    }

    /// JS THRUSTER_PRESETS entry
    public sealed class ThrusterPreset
    {
        public string key, label;
        public CurveParams curve;
        // tauUp / tauDown are rotor-speed time constants (thrust ~ n|n|)
        public double propDiameter, maxRpm, J0, J0Rev, tauUp, tauDown, mass, voltage;
        public string elec; // key into ELECTRICAL or null
        public double torqueRatio, reverseDwell;
    }

    /// One thruster as the JS passes it around: geometry (allocation, frames, bots)
    /// plus the optional physics overrides ThrusterState reads. A null field is the
    /// JS "undefined" (falls back to the preset, then to the default).
    public sealed class ThrusterDef
    {
        public string name;
        public V3 pos;   // m, body FRD relative to the CG
        public V3 axis;  // direction the thruster pushes the vehicle for a positive output
        public string preset; // key into THRUSTER_PRESETS
        public double? hand;  // +1 right-handed (CW seen from behind), -1 left-handed
        public double[][] table; // [pwm, N] rows

        // allocation
        public double? fwdMax, revMax;
        public bool? enabled;

        // physics overrides (ThrusterState)
        public double? tableVoltage, voltage, torqueRatio, reverseDwell, J0, J0Rev, escMinV, idleCurrent;
        public double? propDiameter, maxRpm, tauUp, tauDown, polarity, health, deduction, deductionRev;
        public bool? inflowModel;
        public bool tableOnReversedUnit;
        /// def.elec: when elecSet is false the preset's electrical data is used (JS undefined);
        /// otherwise elecData if given, else ELECTRICAL[elec] (null -> none).
        public bool elecSet;
        public string elec;
        public ElectricalData elecData;

        /// shallow copy ({ ...def }); vectors are values, the table is shared like in the JS
        public ThrusterDef Clone() => (ThrusterDef)MemberwiseClone();
    }

    public static class Thrusters
    {
        const double KGF = 9.80665;

        static double Clamp(double v, double lo, double hi) => MathUtil.Clamp(v, lo, hi);

        // Build a PWM->thrust table from a few shape parameters (used for presets and
        // for thrusters without measured data). The ESC deadband edges are explicit
        // breakpoints so thrust is exactly zero inside 1500 +/- deadband.
        public static double[][] ParametricCurve(CurveParams o = null)
        {
            if (o == null) o = new CurveParams();
            double fwdMax = o.fwdMax, revMax = o.revMax, expo = o.expo, pwmMin = o.pwmMin, pwmMax = o.pwmMax;
            double db = Clamp(Js.Or(o.deadband, 0), 0, 0.9 * JsMath.Min(1500 - pwmMin, pwmMax - 1500));
            double lo = 1500 - db, hi = 1500 + db;
            var pts = new List<double[]> { new[] { lo, 0.0 }, new[] { 1500.0, 0.0 }, new[] { hi, 0.0 } };
            for (double pwm = hi + 10; pwm < pwmMax; pwm += 10) pts.Add(new[] { pwm, Js.RoundFixed(fwdMax * JsMath.Pow((pwm - hi) / (pwmMax - hi), expo), 4) });
            for (double pwm = lo - 10; pwm > pwmMin; pwm -= 10) pts.Add(new[] { pwm, Js.RoundFixed(-revMax * JsMath.Pow((lo - pwm) / (lo - pwmMin), expo), 4) });
            pts.Add(new[] { pwmMax, fwdMax });
            pts.Add(new[] { pwmMin, -revMax });
            Js.StableSort(pts, (a, b) => a[0] - b[0]);
            var seen = new HashSet<double>();
            var outp = new List<double[]>(pts.Count);
            foreach (var p in pts) if (seen.Add(p[0] == 0 ? 0.0 : p[0])) outp.Add(p);
            return outp.ToArray();
        }

        // Full-throttle (1100 / 1900 us) thrust and current vs supply voltage.
        // T200: Blue Robotics published performance data (12 / 16 / 20 V points:
        // 3.71 / 5.25 / 6.70 kgf forward, 2.92 / 4.10 / 5.05 kgf reverse, about
        // 17 / 24 / 32 A), intermediate voltages on the fitted ~V^1.2 law.
        // T500: approximate, scaled from the 20 V point with the same law.
        public static readonly OrderedMap<string, ElectricalData> ELECTRICAL = new OrderedMap<string, ElectricalData>
        {
            ["t200"] = new ElectricalData
            {
                V = new double[] { 10, 12, 14, 16, 18, 20 },
                fwd = Kgf(2.98, 3.71, 4.46, 5.25, 6.0, 6.7),
                rev = Kgf(2.35, 2.92, 3.51, 4.1, 4.6, 5.05),
                I = new[] { 13.6, 17.0, 20.5, 24.0, 28.0, 32.0 },
            },
            ["t500"] = new ElectricalData
            {
                V = new double[] { 10, 12, 14, 16, 18, 20 },
                fwd = new[] { 68.3, 85.1, 102.4, 120.1, 138.3, 157 },
                rev = new[] { 50.9, 63.4, 76.3, 89.5, 103.1, 117 },
                I = new[] { 16.5, 20.6, 24.8, 29.1, 33.5, 38.0 },
            },
        };

        static double[] Kgf(params double[] k)
        {
            var r = new double[k.Length];
            for (int i = 0; i < k.Length; i++) r[i] = k[i] * KGF;
            return r;
        }

        public static readonly OrderedMap<string, ThrusterPreset> THRUSTER_PRESETS = new OrderedMap<string, ThrusterPreset>
        {
            ["t200-16v"] = new ThrusterPreset
            {
                key = "t200-16v",
                label = "Blue Robotics T200 @ 16 V (approx.)",
                curve = new CurveParams { fwdMax = 51.5, revMax = 40.2, deadband = 25, expo = 1.25 },
                propDiameter = 0.076, maxRpm = 3500, J0 = 0.9, J0Rev = 0.75, tauUp = 0.05, tauDown = 0.075, mass = 0.344,
                voltage = 16, elec = "t200", torqueRatio = 0.12, reverseDwell = 0.03,
            },
            ["t200-12v"] = new ThrusterPreset
            {
                key = "t200-12v",
                label = "Blue Robotics T200 @ 12 V (approx.)",
                curve = new CurveParams { fwdMax = 36.3, revMax = 28.4, deadband = 25, expo = 1.25 },
                propDiameter = 0.076, maxRpm = 3000, J0 = 0.9, J0Rev = 0.75, tauUp = 0.055, tauDown = 0.08, mass = 0.344,
                voltage = 12, elec = "t200", torqueRatio = 0.12, reverseDwell = 0.03,
            },
            ["t500-20v"] = new ThrusterPreset
            {
                key = "t500-20v",
                label = "Blue Robotics T500 @ 20 V (approx.)",
                curve = new CurveParams { fwdMax = 157, revMax = 117, deadband = 25, expo = 1.3 },
                propDiameter = 0.104, maxRpm = 3200, J0 = 0.9, J0Rev = 0.75, tauUp = 0.075, tauDown = 0.1, mass = 0.9,
                voltage = 20, elec = "t500", torqueRatio = 0.12, reverseDwell = 0.04,
            },
            ["generic-small"] = new ThrusterPreset
            {
                key = "generic-small",
                label = "Generic small thruster (≈2 kgf)",
                curve = new CurveParams { fwdMax = 20, revMax = 15, deadband = 30, expo = 1.3 },
                propDiameter = 0.06, maxRpm = 4000, J0 = 0.85, J0Rev = 0.7, tauUp = 0.045, tauDown = 0.065, mass = 0.2,
                voltage = 16, elec = null, torqueRatio = 0.12, reverseDwell = 0.03,
            },
        };

        /// THRUSTER_PRESETS[key] ?? null
        public static ThrusterPreset Preset(string key) => key != null && THRUSTER_PRESETS.TryGetValue(key, out var p) ? p : null;

        // piecewise-linear in range, ~V^1.2 power law outside
        internal static double InterpV(double[] Vs, double[] Ys, double V)
        {
            int n = Vs.Length;
            if (V <= Vs[0]) return Ys[0] * JsMath.Pow(JsMath.Max(V, 0) / Vs[0], 1.2);
            if (V >= Vs[n - 1]) return Ys[n - 1] * JsMath.Pow(V / Vs[n - 1], 1.2);
            int i = 0;
            while (V > Vs[i + 1]) i++;
            double f = (V - Vs[i]) / (Vs[i + 1] - Vs[i]);
            return Ys[i] + f * (Ys[i + 1] - Ys[i]);
        }

        // Parse "pwm, thrust" text (CSV / TSV / spaces), thrust in N or kgf.
        public static double[][] ParseCurveText(string text, string unit = "N")
        {
            double k = unit == "kgf" ? 9.80665 : unit == "lbf" ? 4.448222 : 1;
            var rows = new List<double[]>();
            var nums = new List<double>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.EndsWith("\r") ? raw.Substring(0, raw.Length - 1) : raw; // /\r?\n/
                nums.Clear();
                // line.split(/[\s,;\t]+/).map(Number).filter(Number.isFinite)
                int i = 0, n = line.Length;
                while (true)
                {
                    int a = i;
                    while (i < n && !IsSep(line[i])) i++;
                    double v = Js.Number(line.Substring(a, i - a));
                    if (Js.IsFinite(v)) nums.Add(v);
                    if (i >= n) break;
                    while (i < n && IsSep(line[i])) i++;
                }
                if (nums.Count >= 2 && nums[0] > 500 && nums[0] < 2500) rows.Add(new[] { nums[0], nums[1] * k });
            }
            if (rows.Count < 3) throw new FormatException("Need at least 3 rows of \"PWM, thrust\"");
            return rows.ToArray();
        }

        static bool IsSep(char c) => c == ',' || c == ';' || Js.IsWhiteSpace(c);
    }

    public sealed class ThrustCurve
    {
        public double[] pwm; // us, ascending
        public double[] T;   // N
        public double fwdMax, revMax; // N (>= 0)

        public ThrustCurve(double[][] table) { Set(table); }

        public void Set(double[][] table)
        {
            var src = table != null && table.Length >= 2 ? table : Thrusters.ParametricCurve();
            var t = new List<double[]>(src.Length);
            foreach (var r in src) if (Js.IsFinite(r[0]) && Js.IsFinite(r[1])) t.Add(new[] { r[0], r[1] });
            Js.StableSort(t, (a, b) => a[0] - b[0]);
            pwm = new double[t.Count];
            T = new double[t.Count];
            fwdMax = 0; revMax = 0;
            for (int i = 0; i < t.Count; i++)
            {
                pwm[i] = t[i][0];
                T[i] = t[i][1];
                fwdMax = JsMath.Max(fwdMax, T[i]);
                revMax = JsMath.Max(revMax, -T[i]);
            }
        }

        public double Thrust(double pwm)
        {
            double[] P = this.pwm, T = this.T;
            int n = P.Length;
            if (pwm <= P[0]) return T[0];
            if (pwm >= P[n - 1]) return T[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (P[mid] <= pwm) lo = mid; else hi = mid;
            }
            double f = (pwm - P[lo]) / (P[hi] - P[lo]);
            return T[lo] + f * (T[hi] - T[lo]);
        }
    }

    // Runtime state of one thruster.
    //   n: signed normalised rotor speed (+1 = full forward-thrust speed at the
    //      curve's voltage; sign = direction of thrust along the axis).
    //   T: static (bollard) thrust along the axis, T = Tside * n|n|.
    public sealed class ThrusterState
    {
        public ThrusterDef def;
        public ThrustCurve curve;
        public ThrusterPreset preset;
        public double Vnom;
        public ElectricalData elec;
        public double torqueRatio, reverseDwell, J0Rev, escMinV, idleA;
        public double n;
        public double dwell;
        public double Tcmd;
        public double Teff;    // force actually applied last step (N)
        public double Q;       // reaction torque about the axis applied last step (N m)
        public double vent;
        public double pwm;
        public double vbus;
        public double current; // A drawn from the bus

        public ThrusterState(ThrusterDef def)
        {
            this.def = def;
            curve = new ThrustCurve(def.table);
            preset = Thrusters.Preset(def.preset);
            var pr = preset;
            Vnom = def.tableVoltage ?? def.voltage ?? pr?.voltage ?? 16; // pick('tableVoltage', null) ?? pick('voltage', 16)
            if (def.elecData != null) elec = def.elecData;
            else
            {
                string ek = def.elecSet ? def.elec : pr?.elec;
                elec = ek != null && Thrusters.ELECTRICAL.TryGetValue(ek, out var e) ? e : null;
            }
            torqueRatio = def.torqueRatio ?? pr?.torqueRatio ?? 0.12;
            reverseDwell = JsMath.Max(0, def.reverseDwell ?? pr?.reverseDwell ?? 0.03);
            J0Rev = def.J0Rev ?? pr?.J0Rev ?? (def.J0 ?? 0.9) * 0.83;
            escMinV = def.escMinV ?? 6.5;
            idleA = def.idleCurrent ?? 0.05;
            n = 0;
            dwell = 0;
            Tcmd = 0;
            Teff = 0;
            Q = 0;
            vent = 1;
            pwm = 1500;
            vbus = Vnom;
            current = 0;
        }

        public double Side(double sign) => sign >= 0 ? curve.fwdMax : curve.revMax;

        public double T
        {
            get { double n = this.n; return n == 0 ? 0 : Side(n) * n * Math.Abs(n); }
            set { double s = Side(value); n = s > 0 && Js.Truthy(value) ? JsMath.Sign(value) * JsMath.Sqrt(Math.Abs(value) / s) : 0; }
        }

        // full-throttle thrust on one side at bus voltage V relative to the curve's voltage
        public double VoltageScale(double V, double sign)
        {
            if (!(V > escMinV)) return 0;
            var e = elec;
            if (e == null) return JsMath.Pow(V / Vnom, 1.2);
            double[] Y = sign >= 0 ? e.fwd : e.rev;
            return Thrusters.InterpV(e.V, Y, V) / Thrusters.InterpV(e.V, Y, Vnom);
        }

        // full-throttle current at bus voltage V (A)
        public double MaxCurrent(double V)
        {
            if (elec != null) return Thrusters.InterpV(elec.V, elec.I, V);
            // momentum theory: P = T^1.5 / sqrt(2 rho A) / eta, eta ~ 0.32 (fits the T200)
            double D = def.propDiameter ?? 0.076, A = (JsMath.PI * D * D) / 4;
            double Tm = JsMath.Max(curve.fwdMax, curve.revMax) * JsMath.Pow(V / Vnom, 1.2);
            return JsMath.Pow(Tm, 1.5) / JsMath.Sqrt(2 * 1000 * A) / 0.32 / JsMath.Max(V, 1);
        }

        public void Update(double pwm, double dt) => Update(pwm, dt, Vnom);

        // advance the rotor over dt for a given ESC PWM and bus voltage
        public void Update(double pwm, double dt, double vbus)
        {
            this.pwm = pwm;
            this.vbus = vbus;
            var d = def;
            // A reversed unit (CCW prop or swapped phases, MOT_n_DIRECTION = -1 on a
            // correctly built vehicle) reaches its efficient side with reverse rotation:
            // T_rev(pwm) = T(3000 - pwm), mirrored about 1500 us, not -T(pwm).
            double pol = d.polarity ?? 1;
            double bas;
            if (pol < 0) bas = d.tableOnReversedUnit ? -curve.Thrust(pwm) : curve.Thrust(3000 - pwm);
            else bas = curve.Thrust(pwm);
            double on = d.enabled == false ? 0 : 1;
            Tcmd = bas * on * (d.health ?? 1) * VoltageScale(vbus, bas);
            double sc = Side(Tcmd);
            double nCmd = sc > 0 && Js.Truthy(Tcmd) ? JsMath.Sign(Tcmd) * JsMath.Sqrt(Math.Abs(Tcmd) / sc) : 0;
            double tauUp = d.tauUp ?? 0.05, tauDown = d.tauDown ?? 0.075;
            if (dwell > 0)
            {
                // ESC re-commutating after a reversal: rotor held at rest
                dwell -= dt;
                if (nCmd == 0) dwell = 0;
            }
            else
            {
                double n = this.n;
                // braking (toward smaller |n|, including the first half of a reversal) uses tauDown
                bool decel = (n != 0 && JsMath.Sign(nCmd) != JsMath.Sign(n)) || Math.Abs(nCmd) < Math.Abs(n);
                double tau = decel ? tauDown : tauUp;
                double a = tau > 1e-6 ? 1 - JsMath.Exp(-dt / tau) : 1;
                double nn = n + (nCmd - n) * a;
                if (n != 0 && nCmd != 0 && JsMath.Sign(nn) != JsMath.Sign(n)) { nn = 0; dwell = reverseDwell; }
                if (Math.Abs(nn) < 1e-6) nn = 0;
                this.n = nn;
            }
            // bus current: I0 + Imax(V) (|T| / Tmax(V))^1.5 (shaft power ~ T^1.5)
            double T = this.T;
            if (on == 0 || T == 0) current = on != 0 ? idleA : 0;
            else
            {
                double Tm = Side(T) * JsMath.Max(VoltageScale(vbus, T), 1e-6);
                current = idleA + MaxCurrent(vbus) * JsMath.Pow(JsMath.Min(1.5, Math.Abs(T) / Tm), 1.5);
            }
        }

        // effective thrust given inflow velocity along the push direction and depth
        public double Effective(double inflowAlongPush, double depthBelowSurface)
        {
            var d = def;
            double T = this.T;
            if (T == 0) { vent = 1; return 0; }
            double k = 1;
            if (d.inflowModel != false)
            {
                // zero-thrust advance speed: J0 n D (reverse props are less efficient: J0Rev)
                double J0 = T > 0 ? (d.J0 ?? 0.9) : J0Rev;
                double V0 = J0 * (d.propDiameter ?? 0.076) * ((d.maxRpm ?? 3500) / 60) * JsMath.Max(Math.Abs(n), 0.05);
                k = MathUtil.Clamp(1 - inflowAlongPush / V0, 0, 1.25);
            }
            // ventilation / emergence near the free surface
            double D = d.propDiameter ?? 0.076;
            double v = 1;
            if (depthBelowSurface < 1.5 * D) v = depthBelowSurface <= 0 ? 0 : JsMath.Sqrt(MathUtil.Clamp(depthBelowSurface / (1.5 * D), 0, 1));
            vent = v;
            // forward thrust: jet leaves along -axis (deduction); reverse: along +axis (deductionRev)
            double dd = T >= 0 ? (d.deduction ?? 0) : (d.deductionRev ?? d.deduction ?? 0);
            double ded = 1 - MathUtil.Clamp(dd, 0, 0.9);
            return T * k * v * ded;
        }

        // Propeller reaction torque about +axis on the vehicle: Q = (KQ/KT) D T,
        // opposite to the prop's spin. hand = +1: the prop spins positively about
        // +axis when pushing along +axis (right-handed / CW seen from behind).
        public double ReactionTorque()
        {
            double T = this.T;
            if (T == 0) return 0;
            double hand = def.hand ?? 1;
            return -hand * torqueRatio * (def.propDiameter ?? 0.076) * T * vent;
        }
    }
}
