// Port of src/sim/vehicle.js.
// One player's vehicle: 6-DOF rigid body in water plus an ArduSub-style
// STABILIZE / DEPTH HOLD pilot loop.
//
// Plant (Fossen, body FRD about the CG, diagonal mass, implicit damping):
//   (M_RB + M_A) nu_dot + C_RB(nu) nu + D(nu_r) nu_r + g(eta) = tau_thr
// nu_r is the velocity relative to the water (current, turbulence and wave
// orbital velocity come from the simulator's Environment). Buoyancy uses the
// in-situ EOS-80 density at the vehicle's depth and the submerged fraction
// near the free surface. Thrusters are the simulator's ThrusterState: PWM ->
// thrust curve, rotor lag, in-flow loss and ventilation near the surface.
//
// Pilot inputs (all -1..1): surge, sway, heave, yaw, roll, pitch. Roll / pitch
// inputs are rate commands (hold to flip, release and the vehicle levels
// itself again, keeping its heading).
//
// Step / Control allocate nothing (scratch arrays are kept on the instance).

using System;

namespace Duburi.Core
{
    /// JS veh.input: pilot inputs, all -1..1
    public sealed class VehicleInput
    {
        public double surge, sway, heave, yaw, roll, pitch;
        public void Clear() { surge = sway = heave = yaw = roll = pitch = 0; }
    }

    /// JS veh.diag: what the plant saw last step
    public sealed class VehicleDiag
    {
        public double submerged; // 0..1 fraction of the hull under the surface
        public double buoyancy;  // N
        public double weight;    // N
        public double rho;       // kg/m^3 at the vehicle depth
        public double thrust;    // N, sum of |effective thrust|
        public double current;   // A drawn by the thrusters
        public double net;       // N, buoyancy - weight
        public V3 flow;          // m/s NED, water velocity at the vehicle
    }

    public sealed class Vehicle
    {
        public const double DT = 1.0 / 120;
        const double DESIGN_RHO = 1000; // ballasted in fresh water (kg/m^3)
        const double ROLL_RATE = 2.6;   // rad/s commanded at full roll/pitch input
        const double YAW_RATE = 1.4;    // rad/s at full yaw input
        const double LEVEL_RATE = 2.4;  // rad/s the target moves back to level

        public BotDesign d;
        public Environment env;
        public double mass;      // kg
        public double volume;    // m^3 displaced (ballasted for Bnet in fresh water)
        public double[] Mt;      // [3] mass + added mass per body axis (kg)
        public double[] It;      // [3] inertia + added inertia per body axis (kg m^2)
        public ThrusterState[] th;
        public double voltage;   // V bus
        public double[] nom;     // [N] nominal thrust per thruster (N)
        public double[][] P;     // N x 6 allocation pseudo-inverse: u = P * wrench
        public double[] wmax;    // [6] largest wrench along each axis before a thruster saturates (N, N m)

        // state (NED world, FRD body)
        public V3 p;             // position (m, NED)
        public V3 v;             // body velocity (m/s, FRD)
        public V3 w;             // body rates (rad/s, FRD)
        public Quat q;           // attitude body -> NED
        public Quat targetQ;     // attitude the pilot loop steers to
        public double hdg;       // rad, commanded heading
        public double depthTarget; // m (depth hold)
        public bool depthHold;
        public double depthI;
        public bool acro;
        public double[] u;       // [N] thruster commands -1..1 (fraction of nominal thrust)
        public double[] R = new double[9]; // rotation body -> NED (row-major), from q
        public VehicleDiag diag = new VehicleDiag();
        public double contactTimer;
        public VehicleInput input = new VehicleInput();
        /// speed change of the last contact (m/s, set by Collide); NaN until the first knock
        public double lastImpact = double.NaN;

        // scratch (no garbage per step)
        double[] uA, uT;

        static V3 MulMat(double[] R, V3 v) => new V3(R[0] * v.x + R[1] * v.y + R[2] * v.z, R[3] * v.x + R[4] * v.y + R[5] * v.z, R[6] * v.x + R[7] * v.y + R[8] * v.z);
        static V3 MulMatT(double[] R, V3 v) => new V3(R[0] * v.x + R[3] * v.y + R[6] * v.z, R[1] * v.x + R[4] * v.y + R[7] * v.z, R[2] * v.x + R[5] * v.y + R[8] * v.z);
        static double Clamp(double v, double lo, double hi) => MathUtil.Clamp(v, lo, hi);

        // Rotate the quaternion `from` toward `to` by at most `maxAngle` radians.
        static Quat QStepToward(Quat from, Quat to, double maxAngle)
        {
            Quat e = MathUtil.QMul(MathUtil.QConj(from), to);
            if (e.w < 0) e = new Quat(-e.w, -e.x, -e.y, -e.z);
            V3 rv = MathUtil.QToAxisAngle(e);
            double a = JsMath.Hypot(rv.x, rv.y, rv.z);
            if (a <= maxAngle || a < 1e-9) return to;
            double k = maxAngle / a;
            return MathUtil.QNormalize(MathUtil.QMul(from, MathUtil.QFromAxisAngleVec(new V3(rv.x * k, rv.y * k, rv.z * k))));
        }

        public Vehicle(BotDesign design, Environment env)
        {
            d = design;
            this.env = env;
            double g = env.g;
            mass = design.mass;
            volume = (design.mass * g + design.Bnet) / (DESIGN_RHO * g);
            Mt = new double[3];
            It = new double[3];
            for (int i = 0; i < 3; i++)
            {
                Mt[i] = design.mass + design.addedMass[i];
                It[i] = design.inertia[i] + design.addedMass[3 + i];
            }
            int N = design.thrusters.Length;
            th = new ThrusterState[N];
            for (int i = 0; i < N; i++)
            {
                var t = design.thrusters[i];
                var pr = Thrusters.THRUSTER_PRESETS[t.preset];
                var def = t.Clone();
                def.table = Thrusters.ParametricCurve(pr.curve);
                th[i] = new ThrusterState(def);
            }
            voltage = Thrusters.THRUSTER_PRESETS[design.thrusters[0].preset].voltage;
            // allocation: u (fraction of nominal thrust per thruster) = P * wrench
            var defs = new ThrusterDef[N];
            for (int i = 0; i < N; i++) defs[i] = new ThrusterDef { pos = design.thrusters[i].pos, axis = design.thrusters[i].axis, fwdMax = th[i].curve.fwdMax, revMax = th[i].curve.revMax };
            var bb = Allocation.BuildB(defs, V3.Zero);
            nom = bb.nom;
            P = MathUtil.Pinv(bb.B);
            // largest wrench along each axis before a thruster saturates
            wmax = new double[6];
            for (int k = 0; k < 6; k++)
            {
                double m = 0;
                for (int i = 0; i < P.Length; i++) m = JsMath.Max(m, Math.Abs(P[i][k]));
                wmax[k] = m > 1e-9 ? 1 / m : 0;
            }
            uA = new double[N];
            uT = new double[N];
            ChooseHands();
            Reset(V3.Zero, 0);
        }

        // Pick CW / CCW propellers so the reaction torques cancel for pure surge,
        // sway and heave (what a team does when it orders props).
        void ChooseHands()
        {
            int N = th.Length;
            if (N > 12) return;
            bool have = false;
            double bestCost = 0;
            int bestMask = 0;
            for (int mask = 0; mask < 1 << N; mask++)
            {
                double cost = 0;
                for (int k = 0; k < 3; k++)
                {
                    double t0 = 0, t1 = 0, t2 = 0;
                    for (int i = 0; i < N; i++)
                    {
                        double T = P[i][k] * wmax[k] * nom[i];
                        double h = (mask & (1 << i)) != 0 ? 1 : -1;
                        var td = d.thrusters[i];
                        double c = h * th[i].torqueRatio * (td.propDiameter ?? Thrusters.THRUSTER_PRESETS[td.preset].propDiameter) * T;
                        t0 -= c * td.axis.x;
                        t1 -= c * td.axis.y;
                        t2 -= c * td.axis.z;
                    }
                    cost += t0 * t0 + t1 * t1 + t2 * t2 * 0.2;
                }
                if (!have || cost < bestCost - 1e-12) { have = true; bestCost = cost; bestMask = mask; }
            }
            for (int i = 0; i < N; i++)
            {
                var def = th[i].def.Clone();
                def.hand = (bestMask & (1 << i)) != 0 ? 1 : -1;
                th[i].def = def;
            }
        }

        /// put the vehicle at pos (NED, m) with a heading (rad), at rest and level
        public void Reset(V3 pos, double heading)
        {
            p = pos;
            v = V3.Zero;
            w = V3.Zero;
            q = MathUtil.QFromEuler(0, 0, heading);
            targetQ = q;
            hdg = heading;
            depthTarget = JsMath.Max(0, pos.z);
            depthHold = true;
            depthI = 0;
            acro = false;
            if (u == null || u.Length != th.Length) u = new double[th.Length];
            else Array.Clear(u, 0, u.Length);
            foreach (var t in th) { t.n = 0; t.dwell = 0; }
            MathUtil.QToMat(q, R);
            diag.submerged = 0;
            diag.buoyancy = mass * env.g;
            diag.weight = mass * env.g;
            diag.rho = env.rho;
            diag.thrust = 0;
            diag.current = 0;
            diag.net = 0;
            diag.flow = V3.Zero;
            contactTimer = 0;
            input.Clear();
        }

        /// depth of the CG below the local (wavy) surface, m
        public double depth => p.z + env.SurfaceElevation(p.x, p.y);
        /// (roll, pitch, yaw) in rad, ArduPilot 3-2-1
        public V3 Euler() => MathUtil.QToEuler(q);
        /// body point (m) -> world NED
        public V3 ToWorld(V3 b) { V3 r = MulMat(R, b); return new V3(p.x + r.x, p.y + r.y, p.z + r.z); }
        /// velocity in the world frame (m/s NED)
        public V3 WorldVel() => MulMat(R, v);
        public V3 BodyToWorldDir(V3 b) => MulMat(R, b);

        // heading of the body x axis projected on the horizontal (keeps the last one when vertical)
        public double ProjectedHeading(Quat q)
        {
            double w = q.w, x = q.x, y = q.y, z = q.z;
            double fx = 1 - 2 * (y * y + z * z), fy = 2 * (x * y + w * z); // qToMat(q)[0], [3]
            if (JsMath.Hypot(fx, fy) > 0.35) return JsMath.Atan2(fy, fx);
            return hdg;
        }

        public double PwmFor(int i, double T)
        {
            // smallest PWM giving thrust T on thruster i's curve (monotonic table)
            var c = th[i].curve;
            if (Math.Abs(T) < 1e-6) return 1500;
            double[] P = c.pwm, Tt = c.T;
            int n = P.Length;
            if (T > 0)
            {
                if (T >= Tt[n - 1]) return P[n - 1];
                for (int k = 0; k < n - 1; k++) if (Tt[k + 1] >= T && Tt[k] < T && Tt[k + 1] > 0) return P[k] + ((T - Tt[k]) / (Tt[k + 1] - Tt[k])) * (P[k + 1] - P[k]);
                return 1500;
            }
            if (T <= Tt[0]) return P[0];
            for (int k = n - 1; k > 0; k--) if (Tt[k - 1] <= T && Tt[k] > T && Tt[k - 1] < 0) return P[k] - ((Tt[k] - T) / (Tt[k] - Tt[k - 1])) * (P[k] - P[k - 1]);
            return 1500;
        }

        static double Dz(double v) => Math.Abs(v) < 0.06 ? 0 : v;

        // ---------------- pilot loop ----------------
        public void Control(double dt)
        {
            var inp = input;
            double roll = Dz(inp.roll), pitch = Dz(inp.pitch), yaw = Dz(inp.yaw), heave = Dz(inp.heave);
            hdg = MathUtil.WrapPI(hdg + yaw * YAW_RATE * dt);
            V3 ff;
            if (Js.Truthy(roll) || Js.Truthy(pitch))
            {
                // acro on roll / pitch: advance the target in body axes
                acro = true;
                ff = new V3(roll * ROLL_RATE, pitch * ROLL_RATE, yaw * YAW_RATE);
                targetQ = MathUtil.QNormalize(MathUtil.QMul(targetQ, MathUtil.QFromAxisAngleVec(new V3(ff.x * dt, ff.y * dt, ff.z * dt))));
                hdg = ProjectedHeading(targetQ);
            }
            else
            {
                if (acro) { hdg = ProjectedHeading(q); acro = false; }
                targetQ = QStepToward(targetQ, MathUtil.QFromEuler(0, 0, hdg), LEVEL_RATE * dt);
                ff = new V3(0, 0, yaw * YAW_RATE * 0.6);
            }
            // attitude error -> rate -> torque
            Quat qe = MathUtil.QMul(MathUtil.QConj(q), targetQ);
            if (qe.w < 0) qe = new Quat(-qe.w, -qe.x, -qe.y, -qe.z);
            V3 e = MathUtil.QToAxisAngle(qe);
            V3 tau = default;
            for (int i = 0; i < 3; i++)
            {
                double rateDes = Clamp(5.0 * e[i] + ff[i], -4, 4);
                tau[i] = It[i] * 9 * (rateDes - w[i]) + (d.dampLin[3 + i] + d.dampQuad[3 + i] * Math.Abs(w[i])) * w[i] * 0.5;
            }
            // depth: pilot throttle or depth hold (PID on depth, world vertical)
            double depth = p.z; // pressure sensor: still-water depth (wave pressure decays with depth)
            double vzWorld = WorldVel().z;
            double Fz;
            double wz = wmax[2];
            if (Js.Truthy(heave))
            {
                Fz = -heave * wz;
                depthTarget = JsMath.Max(0, depth);
                depthI *= 0.98;
            }
            else if (depthHold)
            {
                double err = Clamp(depthTarget - depth, -1.5, 1.5);
                depthI = Clamp(depthI + err * dt * 2.5, -0.6, 0.6);
                Fz = Mt[2] * (4 * err - 4.5 * vzWorld) + depthI * wz;
                // buoyancy feed-forward (pilot knows the trim) so the hold is crisp
                Fz += diag.buoyancy - mass * env.g;
                Fz = Clamp(Fz, -wz, wz);
                if (depthTarget < 0.05 && depth < 0.25) Fz = JsMath.Min(Fz, 0); // let it float on the surface
            }
            else Fz = 0;
            V3 fzBody = MulMatT(R, new V3(0, 0, Fz));
            V3 trans = new V3(inp.surge * wmax[0] + fzBody.x, inp.sway * wmax[1] + fzBody.y, fzBody.z);
            // allocate attitude first, translation scaled down to fit
            int N = th.Length;
            double maxA = 0;
            for (int i = 0; i < N; i++)
            {
                double[] Pi = P[i];
                uA[i] = Pi[3] * tau.x + Pi[4] * tau.y + Pi[5] * tau.z;
                uT[i] = Pi[0] * trans.x + Pi[1] * trans.y + Pi[2] * trans.z;
                maxA = JsMath.Max(maxA, Math.Abs(uA[i]));
            }
            if (maxA > 1) for (int i = 0; i < N; i++) uA[i] /= maxA;
            double s = 1;
            for (int i = 0; i < N; i++)
            {
                double tot = uA[i] + uT[i];
                if (Math.Abs(tot) > 1 && Math.Abs(uT[i]) > 1e-9)
                {
                    double room = JsMath.Sign(uT[i]) * JsMath.Sign(tot) > 0 ? (JsMath.Sign(tot) - uA[i]) / uT[i] : 1;
                    s = JsMath.Min(s, Clamp(room, 0, 1));
                }
            }
            for (int i = 0; i < N; i++) u[i] = Clamp(uA[i] + uT[i] * s, -1, 1);
        }

        // ---------------- plant ----------------
        public void Step(double dt, bool armed = true)
        {
            if (armed) Control(dt); else Array.Clear(u, 0, u.Length);
            var env = this.env;
            var d = this.d;
            double g = env.g;
            MathUtil.QToMat(q, R);
            double[] R_ = R;
            double eta = env.SurfaceElevation(p.x, p.y);
            double depthC = p.z + eta;
            double H = d.dims.z;
            double sub = Clamp(depthC / H + 0.5, 0, 1);
            double rho = env.DensityAtDepth(JsMath.Max(0, depthC));
            V3 fl = env.FluidVelocity(p.x, p.y, JsMath.Max(0, p.z));
            V3 vcb = MulMatT(R_, fl);
            V3 vr = new V3(v.x - vcb.x, v.y - vcb.y, v.z - vcb.z);
            double W = mass * g;
            double Bf = rho * g * volume * sub;
            V3 gb = MulMatT(R_, new V3(0, 0, 1));
            V3 F = new V3((W - Bf) * gb.x, (W - Bf) * gb.y, (W - Bf) * gb.z);
            V3 rB = new V3(0, 0, -d.zB);
            V3 tb = MathUtil.Vcross(rB, new V3(-Bf * gb.x, -Bf * gb.y, -Bf * gb.z));
            V3 T = tb;
            // thrusters
            double thrustSum = 0, current = 0;
            for (int i = 0; i < th.Length; i++)
            {
                var t = th[i];
                var td = d.thrusters[i];
                double pwm = armed ? PwmFor(i, u[i] * nom[i]) : 1500;
                t.Update(pwm, dt, voltage);
                V3 pv = MathUtil.Vcross(w, td.pos);
                double inflow = (vr.x + pv.x) * td.axis.x + (vr.y + pv.y) * td.axis.y + (vr.z + pv.z) * td.axis.z;
                V3 tw = ToWorld(td.pos);
                double Te = t.Effective(inflow, tw.z + env.SurfaceElevation(tw.x, tw.y));
                t.Teff = Te;
                V3 f = new V3(td.axis.x * Te, td.axis.y * Te, td.axis.z * Te);
                V3 m = MathUtil.Vcross(td.pos, f);
                double Q = t.ReactionTorque(); // (the JS never stores it in th.Q, so neither do we)
                F.x += f.x; T.x += m.x + td.axis.x * Q;
                F.y += f.y; T.y += m.y + td.axis.y * Q;
                F.z += f.z; T.z += m.z + td.axis.z * Q;
                thrustSum += Math.Abs(Te);
                current += t.current;
            }
            // Coriolis (rigid body + gyroscopic)
            V3 Iw = new V3(d.inertia.x * w.x, d.inertia.y * w.y, d.inertia.z * w.z);
            V3 gyro = MathUtil.Vcross(w, Iw);
            double surf = 1 + 0.6 * 4 * sub * (1 - sub);
            V3 wv = MathUtil.Vcross(w, v);
            for (int i = 0; i < 3; i++)
            {
                double Dl = (d.dampLin[i] + d.dampQuad[i] * Math.Abs(vr[i])) * surf;
                double a = F[i] / Mt[i] - (mass / Mt[i]) * wv[i];
                v[i] = (v[i] + dt * a + (dt * Dl / Mt[i]) * vcb[i]) / (1 + (dt * Dl) / Mt[i]);
                double Dr = (d.dampLin[3 + i] + d.dampQuad[3 + i] * Math.Abs(w[i])) * surf;
                double ar = (T[i] - gyro[i]) / It[i];
                w[i] = (w[i] + dt * ar) / (1 + (dt * Dr) / It[i]);
            }
            V3 vw = MulMat(R_, v);
            p.x += vw.x * dt; p.y += vw.y * dt; p.z += vw.z * dt;
            // can't fly: above the water the body falls back
            Quat qd = MathUtil.QMul(q, new Quat(0, w.x, w.y, w.z));
            q = MathUtil.QNormalize(new Quat(q.w + 0.5 * dt * qd.w, q.x + 0.5 * dt * qd.x, q.y + 0.5 * dt * qd.y, q.z + 0.5 * dt * qd.z));
            MathUtil.QToMat(q, R);
            diag.submerged = sub;
            diag.buoyancy = Bf;
            diag.weight = W;
            diag.rho = rho;
            diag.thrust = thrustSum;
            diag.current = current;
            diag.net = Bf - W;
            diag.flow = fl;
        }

        // speed through the water and over ground (m/s)
        public double Speed() { V3 vw = WorldVel(); return JsMath.Hypot(vw.x, vw.y, vw.z); }
    }
}
