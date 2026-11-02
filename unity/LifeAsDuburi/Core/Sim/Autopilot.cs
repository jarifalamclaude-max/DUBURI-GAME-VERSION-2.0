// Port of src/sim/autopilot.js.
// A simple AUV mission: the same task sequence a team would script, flown
// through the same pilot inputs a player uses. Drives the CPU opponents and
// the end-to-end tests.

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    public sealed class Autopilot
    {
        /// one step of the mission plan
        public sealed class PlanStep
        {
            public string type;       // dive | go | act | wait | roll | checkGate | spin | surface | face | done
            public Func<V3> fn;       // go / roll: target point (NED)
            public Func<double> angle; // face: heading to face (rad)
            public double tol;        // go: position tolerance (m)
            public double? hdg;       // go: final heading (rad)
            public double settle;     // go: 0 = no settling, else wait until slow
            public bool precise;      // go
            public string what;       // act: Game.Action name
            public double wait;       // act: s
            public double s;          // wait: s
            public double z;          // dive: depth (m)
            public double turns;      // roll / spin
            public int back;          // checkGate: plan index to go back to
        }

        /// steer() result
        public struct SteerResult { public double d, speed, eh; }

        public Game g;
        public int i;
        public double skill;
        public List<PlanStep> plan;
        public int k;          // current plan step
        public double t;       // s in the current step
        public double stepT;
        public double side;    // gate side the mission goes for (the reef shark)
        public bool noRoll;
        public double rollAcc, spinAcc;

        static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;
        static V2 Add2(V2 p, V2 d, double s) => new V2(p.x + d.x * s, p.y + d.y * s);
        static V3 P3(V2 xy, double z) => new V3(xy.x, xy.y, z);

        public Autopilot(Game game, int playerIndex, double skill = 1)
        {
            g = game;
            i = playerIndex;
            this.skill = skill;
            plan = BuildPlan();
            k = 0;
            t = 0;
            stepT = 0;
        }

        public Player p => i >= 0 && i < g.players.Count ? g.players[i] : null;

        List<PlanStep> BuildPlan()
        {
            var c = g.course;
            var gate = c.gate;
            var sl = c.slalom;
            var bin = c.bin;
            var b = c.board;
            var oct = c.octagon;
            double Side() => p.run.chosen != null ? Js.Or(p.run.gateSide, 1) : side;
            side = gate.sharkSide; // go for the reef shark
            var plan = new List<PlanStep>();
            void Go(Func<V3> fn, double tol, double? hdg = null, double settle = 0, bool precise = false) => plan.Add(new PlanStep { type = "go", fn = fn, tol = tol, hdg = hdg, settle = settle, precise = precise });
            void Act(string what, double wait = 0.5) => plan.Add(new PlanStep { type = "act", what = what, wait = wait });
            const double mid = 1.3;
            plan.Add(new PlanStep { type = "dive", z = mid });
            Go(() => P3(Add2(Add2(gate.pos, gate.dir, -2.5), gate.right, 0.8 * side), mid), 0.5);
            Go(() => P3(Add2(Add2(gate.pos, gate.dir, -0.6), gate.right, 0.8 * side), mid), 0.35, gate.heading);
            plan.Add(new PlanStep { type = "roll", turns = 1, fn = () => P3(Add2(Add2(gate.pos, gate.dir, -0.2), gate.right, 0.8 * side), mid) });
            Go(() => P3(Add2(Add2(gate.pos, gate.dir, 2.2), gate.right, 0.8 * side), mid), 0.5, gate.heading);
            plan.Add(new PlanStep { type = "checkGate", back = 1 });
            double band = (sl.band.x + sl.band.y) / 2;
            foreach (var set in sl.sets)
            {
                var st = set;
                Go(() => P3(Add2(Add2(st.pos, sl.dir, -1.0), sl.right, 0.76 * Side()), band), 0.4, sl.heading);
                Go(() => P3(Add2(Add2(st.pos, sl.dir, 0.9), sl.right, 0.76 * Side()), band), 0.45, sl.heading);
            }
            double HalfOff()
            {
                string chosen = p.run.chosen ?? "shark";
                return bin.halves[0] == chosen ? -0.23 : 0.23;
            }
            double MarkerFix() => p.design.marker.x;
            Go(() => P3(Add2(bin.pos, bin.dir, HalfOff() - MarkerFix()), c.floor - 1.0), 0.1, bin.heading, 1.6, true);
            Act("marker", 2.5);
            Go(() => P3(Add2(bin.pos, bin.dir, HalfOff() * 0.6 - MarkerFix()), c.floor - 1.0), 0.1, bin.heading, 1.2, true);
            Act("marker", 2.5);
            // torpedoes from the far line, chosen animal first, then the other
            V3 Aim(bool first)
            {
                string chosen = p.run.chosen ?? "shark";
                var o = b.openings.Find(x => first ? x.animal == chosen : x.animal != chosen);
                V3 m = p.design.torpedo;
                // board (u, v) -> world: u along -right, v up; torpedo leaves from the mount offset
                double along = b.farDistance + 0.35;
                V2 xy = Add2(Add2(b.pos, b.dir, along), b.right, -o.u);
                // the mount is m[1] to the right of the CG (body), facing the board: body right = board right... shift back
                V2 xy2 = Add2(xy, b.right, m.y);
                return P3(xy2, b.depth - o.v - m.z - 0.04 * along);
            }
            Go(() => Aim(true), 0.07, b.heading + JsMath.PI, 2.0, true);
            Act("torpedo", 2.0);
            Go(() => Aim(false), 0.07, b.heading + JsMath.PI, 2.0, true);
            Act("torpedo", 2.0);
            // octagon: pick the cup, put it in the cup basket, one rotation, surface, face the chosen image
            V3 CupPos() => g.items.Find(it => it.kind == "cup").pos;
            V3 Grip() => p.design.gripper;
            Go(() => P3(Add2(oct.pos, new V2(1, 0), -2.2), oct.tableTop - 1.6), 0.5);
            Go(() => { V3 q = CupPos(); return new V3(q.x - Grip().x, q.y, q.z - Grip().z - 0.12); }, 0.08, 0, 1.5, true);
            Go(() => { V3 q = CupPos(); return new V3(q.x - Grip().x, q.y, q.z - Grip().z); }, 0.06, 0, 1.0, true);
            Act("grip", 0.6);
            Go(() => { V3 q = CupPos(); return new V3(q.x - Grip().x, q.y, oct.tableTop - 0.6); }, 0.15, 0);
            Go(() => { var bk = oct.baskets.Find(x => x.item == "cup"); return new V3(bk.c.x - Grip().x, bk.c.y, bk.depthTop - 0.25 - Grip().z); }, 0.06, 0, 1.2, true);
            Act("grip", 2.5);
            Go(() => new V3(oct.pos.x, oct.pos.y, JsMath.Min(2.0, oct.tableTop - 1.2)), 0.3);
            plan.Add(new PlanStep { type = "spin", turns = 1.15 });
            plan.Add(new PlanStep { type = "surface" });
            plan.Add(new PlanStep { type = "face", angle = () => oct.images.Find(im => im.animal == (p.run.chosen ?? "shark")).angle });
            plan.Add(new PlanStep { type = "dive", z = mid });
            Go(() => P3(Add2(gate.pos, gate.dir, 2.5), mid), 0.5);
            Go(() => P3(Add2(gate.pos, gate.dir, -2.5), mid), 0.5, gate.heading + JsMath.PI);
            plan.Add(new PlanStep { type = "done" });
            return plan;
        }

        // fly toward a point (NED) with an optional final heading; returns the distance
        public SteerResult Steer(V3 target, double? hdg, double dt, bool precise)
        {
            var v = p.veh;
            double dx = target.x - v.p.x, dy = target.y - v.p.y;
            double d = JsMath.Hypot(dx, dy);
            double yaw = v.Euler().z;
            double want;
            if (hdg == null || (!precise && d > 1.2)) want = d > 0.3 ? JsMath.Atan2(dy, dx) : yaw;
            else want = hdg.Value;
            double eh = MathUtil.WrapPI(want - yaw);
            v.input.yaw = Clamp(eh * 1.8 - v.w.z * 0.6, -1, 1);
            // position error in the heading frame (holonomic vehicles also use sway)
            double ch = JsMath.Cos(yaw), sh = JsMath.Sin(yaw);
            double ex = dx * ch + dy * sh, ey = -dx * sh + dy * ch;
            V3 vw = v.WorldVel();
            double vx = vw.x * ch + vw.y * sh, vy = -vw.x * sh + vw.y * ch;
            double kp = precise ? 1.2 : 0.9, kd = precise ? 2.2 : 0.8;
            double turnSlow = JsMath.Max(0.15, JsMath.Cos(JsMath.Min(Math.Abs(eh), JsMath.PI / 2)));
            v.input.surge = Clamp(kp * ex - kd * vx, -1, 1) * (precise ? 1 : turnSlow);
            v.input.sway = v.wmax[1] > 1 ? Clamp(kp * ey - kd * vy, -1, 1) : 0;
            v.depthTarget = target.z;
            v.input.heave = 0;
            return new SteerResult { d = JsMath.Hypot(d, (target.z - v.p.z) * 0.6), speed = JsMath.Hypot(vw.x, vw.y, vw.z), eh = eh };
        }

        void Next() { k++; t = 0; stepT = 0; }

        public void Update(double dt)
        {
            var p = this.p;
            if (p == null || p.run.state != "running") return;
            var v = p.veh;
            v.input.roll = 0; v.input.pitch = 0;
            if (k < 0 || k >= plan.Count) return;
            var s = plan[k];
            t += dt;
            if (t > 70) { Next(); return; } // give up on a step that does not converge
            switch (s.type)
            {
                case "dive":
                    v.depthHold = true; v.depthTarget = s.z; v.input.surge = 0; v.input.sway = 0; v.input.yaw = 0;
                    if (Math.Abs(v.p.z - s.z) < 0.25) Next();
                    break;
                case "go":
                    {
                        V3 tgt = s.fn();
                        var r = Steer(tgt, s.hdg, dt, s.precise);
                        bool ok = r.d < s.tol && (s.hdg == null || Math.Abs(r.eh) < 0.12);
                        if (ok && (!Js.Truthy(s.settle) || r.speed < 0.06)) { stepT += dt; if (stepT >= (Js.Truthy(s.settle) ? 0.6 : 0)) Next(); }
                        else stepT = 0;
                        break;
                    }
                case "act":
                    v.input.surge = 0; v.input.sway = 0; v.input.yaw = 0;
                    if (stepT == 0) { g.Action(i, s.what); stepT = 1e-6; }
                    if (t > s.wait) Next();
                    break;
                case "checkGate":
                    if (p.run.gatePassed) Next();
                    else { noRoll = true; k = s.back; t = 0; stepT = 0; }
                    break;
                case "roll":
                    if (noRoll) { Next(); break; }
                    Steer(s.fn(), g.course.gate.heading, dt, true);
                    v.input.surge = JsMath.Max(v.input.surge, 0.2);
                    rollAcc = rollAcc + Math.Abs(v.w.x) * dt;
                    if (p.veh.th.Length > 0 && v.wmax[3] > 1) v.input.roll = 1;
                    else if (v.wmax[4] > 1) v.input.pitch = 1;
                    rollAcc += Math.Abs(v.w.y) * dt;
                    if (rollAcc > s.turns * 2 * JsMath.PI - 0.6 || t > 6) { v.input.roll = 0; v.input.pitch = 0; rollAcc = 0; Next(); }
                    break;
                case "spin":
                    v.input.surge = 0; v.input.sway = 0; v.input.yaw = 1;
                    spinAcc = spinAcc + v.w.z * dt;
                    if (Math.Abs(spinAcc) > s.turns * 2 * JsMath.PI) { v.input.yaw = 0; spinAcc = 0; Next(); }
                    break;
                case "surface":
                    {
                        v.input.yaw = 0;
                        var oct = g.course.octagon;
                        Steer(new V3(oct.pos.x, oct.pos.y, 0), null, dt, true);
                        v.depthTarget = 0;
                        if (v.depth < p.design.dims.z * 0.4) { stepT += dt; if (stepT > 1) Next(); }
                        break;
                    }
                case "face":
                    {
                        var oct = g.course.octagon;
                        var r = Steer(new V3(oct.pos.x, oct.pos.y, 0), s.angle(), dt, true);
                        if (Math.Abs(r.eh) < 0.15) { stepT += dt; if (stepT > 1.6) Next(); }
                        break;
                    }
                case "done":
                    v.input.surge = 0; v.input.sway = 0; v.input.yaw = 0;
                    break;
                default: Next(); break;
            }
        }
    }
}
