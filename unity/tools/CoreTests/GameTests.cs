// Port of tests/game.test.mjs: headless tests of Life as Duburi — vehicle physics,
// task judging by the handbook rules and complete autopilot runs for every
// vehicle and water.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Duburi.Core;

namespace CoreTests
{
    static class GameTests
    {
        const double DT = Vehicle.DT;

        static void Run(Game g, double sec, Action fn = null)
        {
            int n = (int)JsMath.Round(sec / DT);
            for (int i = 0; i < n && !g.over; i++) { fn?.Invoke(); g.Step(DT); }
        }

        static Game Solo(string scenario = "pool") => new Game(new GameOptions { scenario = scenario, players = Players("duburi"), seed = 3, coinFlip = false });

        static List<PlayerConfig> Players(params string[] bots)
        {
            var l = new List<PlayerConfig>();
            foreach (var b in bots) l.Add(new PlayerConfig { bot = b });
            return l;
        }

        static string F2(double v) => Js.ToFixed(v, 2);

        public static void All(Suite s)
        {
            var tests = new List<(string, Action<Suite>)>
            {
                ("every vehicle floats slightly positive and dives on depth hold", FloatsAndDives),
                ("cold salt water is denser, so the same vehicle is more buoyant (EOS-80)", ColdSalt),
                ("current pushes an idle vehicle downstream in the Pacific, not in the pool", Current),
                ("roll input flips the vehicle and the stabiliser levels it again", RollFlip),
                ("surfacing outside the octagon after leaving the start zone is a breach", Breach),
                ("leaving the start zone on the surface is a breach", BreachStart),
                ("markers in the bin before the gate score nothing (gate first)", GateFirst),
                ("new run forfeits the points and restocks payloads", NewRun),
                ("board coordinates: openings are on the board, far line is 2 m out", BoardCoords),
                ("a scripted run completes every task in the pool (DUBURI)", ScriptedRun),
                ("four vehicles in the same water: every one finishes in each scenario", FourVehicles),
                ("every water has its own depth; the floor tasks follow it", Depths),
                ("Dubo Mini: 5 thrusters, no roll authority, finishes a full run", DuboMini),
                ("skip moves through the tasks in order and scores nothing for skipped ones", SkipOrder),
                ("after skipping the gate the next tasks still score", SkipThenScore),
                ("a player can leave; the others keep playing and the match ends when everyone is done", Leave),
                ("exactly three waters: pool, Pacific, Arctic (the Atlantic was removed)", ThreeWaters),
            };
            foreach (var (name, fn) in tests)
            {
                int before = s.failures;
                try { fn(s); }
                catch (Exception e) { s.Fail(name + ": threw " + e); }
                Console.WriteLine("  " + (s.failures == before ? "ok  " : "FAIL") + " " + name);
            }
        }

        static void FloatsAndDives(Suite s)
        {
            foreach (var id in Bots.BOT_ORDER)
            {
                var g = new Game(new GameOptions { players = Players(id), seed = 1, coinFlip = false });
                var v = g.players[0].veh;
                s.Check(Stats.BotStats(Bots.BOTS[id]).buoyancy["pool"] > 0, id + " positive in fresh water");
                v.depthTarget = 1.5;
                Run(g, 10);
                s.Check(Math.Abs(v.p.z - 1.5) < 0.2, id + " holds 1.5 m (at " + F2(v.p.z) + ")");
                var e = v.Euler();
                s.Check(Math.Abs(e.x) < 0.1 && Math.Abs(e.y) < 0.1, id + " stays level");
            }
        }

        static void ColdSalt(Suite s)
        {
            var b = Stats.BotStats(Bots.BOTS["duburi"]).buoyancy;
            s.Check(b["arctic"] > b["pacific"] && b["pacific"] > b["pool"], "arctic > pacific > pool");
            s.Check(b["arctic"] - b["pool"] > 2, "about 3% more buoyancy than in the pool");
        }

        static void Current(Suite s)
        {
            double Drift(string sc) { var g = Solo(sc); var v = g.players[0].veh; v.depthTarget = 2; Run(g, 3); double y0 = v.p.y; Run(g, 10); return v.p.y - y0; }
            s.Check(Math.Abs(Drift("pool")) < 0.1, "no drift in the pool");
            s.Check(Drift("pacific") > 0.3, "drifts east in the Pacific");
        }

        static void RollFlip(Suite s)
        {
            var g = Solo();
            var v = g.players[0].veh;
            v.depthTarget = 1.5;
            Run(g, 6);
            v.input.roll = 1;
            double acc = 0;
            Run(g, 2.4, () => { acc += v.w.x * DT; });
            v.input.roll = 0;
            s.Check(acc > JsMath.PI * 1.5, "rolled " + Js.ToFixed(acc * 57.3, 0) + " deg");
            Run(g, 4);
            s.Check(Math.Abs(v.Euler().x) < 0.1, "level again");
        }

        static void Breach(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            var v = p.veh;
            v.depthTarget = 1.2;
            Run(g, 5);
            v.input.surge = 1;
            Run(g, 6);
            v.input.surge = 0;
            s.Check(p.run.state == "running", "still running underwater");
            v.depthTarget = 0; v.input.heave = 1;
            Run(g, 8);
            s.Check(p.run.state == "ended", "ended");
            s.Check(Regex.IsMatch(p.run.reason, "Breach"), "reason mentions Breach: " + p.run.reason);
        }

        static void BreachStart(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            p.veh.input.surge = 1;
            Run(g, 15);
            s.Check(p.run.state == "ended", "ended");
        }

        static void GateFirst(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            var bin = g.course.bin;
            p.veh.Reset(new V3(bin.pos.x, bin.pos.y, 3.6), 0);
            p.veh.depthTarget = 3.6;
            Run(g, 2);
            g.Action(0, "marker");
            Run(g, 6);
            s.Check(p.run.scores.bin == 0, "no bin points");
            s.Check(p.run.markers == 1, "one marker used");
        }

        static void NewRun(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            p.run.scores.gate = 600; p.run.markers = 0;
            g.Action(0, "restart");
            s.Check(g.Total(p) == 0, "total 0");
            s.Check(p.run.markers == 2, "2 markers");
            s.Check(p.run.torpedoes == 2, "2 torpedoes");
        }

        static void BoardCoords(Suite s)
        {
            var g = Solo();
            var b = g.course.board;
            foreach (var o in b.openings) s.Check(Math.Abs(o.u) + o.r < b.size / 2 && Math.Abs(o.v) + o.r < b.size / 2, "opening " + o.label + " on the board");
            var front = new V3(b.pos.x + b.dir.x * 2, b.pos.y + b.dir.y * 2, b.depth);
            s.Check(Math.Abs(Course.BoardLocal(b, front).n - 2) < 1e-9, "far line 2 m out");
        }

        static void ScriptedRun(Suite s)
        {
            var g = new Game(new GameOptions { players = Players("duburi"), seed = 3, coinFlip = true });
            var ap = new Autopilot(g, 0);
            while (!g.over && g.time < 600) { ap.Update(DT); g.Step(DT); }
            var sc = g.players[0].run.scores;
            s.Check(g.players[0].run.reason == "Run complete — returned home", "returned home: " + g.players[0].run.reason);
            s.Check(sc.coin == POINTS.coinFlip, "coin");
            s.Check(sc.gate >= POINTS.gatePass + POINTS.gateAnimal, "gate");
            s.Check(sc.style > 0, "style");
            s.Check(sc.slalom == 3 * (POINTS.slalomSet + POINTS.slalomSide + POINTS.slalomInside), "slalom " + sc.slalom);
            s.Check(sc.bin == 2 * (POINTS.binAny + POINTS.binCorrect), "bin " + sc.bin);
            s.Check(sc.torpedo >= POINTS.torpedoThrough, "torpedo " + sc.torpedo);
            s.Check(sc.octagon >= POINTS.octSurface + POINTS.basketAny, "octagon " + sc.octagon);
            s.Check(sc.home == POINTS.home, "home");
            s.Check(sc.time > 0, "time bonus earned");
        }

        static void FourVehicles(Suite s)
        {
            foreach (var sc in Scenarios.SCENARIO_ORDER)
            {
                var g = new Game(new GameOptions { scenario = sc, seed = 5, coinFlip = true, players = Players("duburi", "manta", "kraken", "barracuda") });
                var aps = new List<Autopilot>();
                for (int i = 0; i < g.players.Count; i++) aps.Add(new Autopilot(g, i));
                while (!g.over && g.time < 900) { foreach (var a in aps) a.Update(DT); g.Step(DT); }
                foreach (var p in g.players)
                {
                    s.Check(p.run.state == "ended", sc + " " + p.bot + " ended");
                    s.Check(p.run.gatePassed, sc + " " + p.bot + " passed the gate");
                    s.Check(p.best > 3000, sc + " " + p.bot + " scored " + p.best);
                }
            }
        }

        static void Depths(Suite s)
        {
            foreach (var (sc, d) in new[] { ("pool", 7.0), ("pacific", 12.0), ("arctic", 15.0) })
            {
                var g = new Game(new GameOptions { scenario = sc, players = Players("duburi"), seed = 2 });
                s.Check(g.course.floor == d, sc + " floor " + d);
                s.Check(Math.Abs(g.course.board.depth - (d - 1.6)) < 1e-9, sc + " board follows the floor");
            }
            s.Check(new Game(new GameOptions { scenario = "pool", depth = 20, players = Players("duburi") }).course.floor == 20, "depth override");
        }

        static void DuboMini(Suite s)
        {
            s.Check(Bots.BOTS["dubomini"].thrusters.Length == 5, "5 thrusters");
            s.Check(!Stats.BotStats(Bots.BOTS["dubomini"]).dof.Contains("Roll"), "no roll");
            var g = new Game(new GameOptions { players = Players("dubomini"), seed = 4, coinFlip = false });
            var ap = new Autopilot(g, 0);
            while (!g.over && g.time < 800) { ap.Update(DT); g.Step(DT); }
            var r = g.players[0].run;
            s.Check(r.gatePassed && r.scores.slalom > 0 && r.scores.octagon > 0, "gate " + r.gatePassed + " slalom " + r.scores.slalom + " octagon " + r.scores.octagon);
        }

        static void SkipOrder(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            var seen = new List<string>();
            for (int k = 0; k < 6; k++) { seen.Add(g.CurrentTask(p)); g.Action(0, "skip"); Run(g, 0.5); }
            s.Check(string.Join(",", seen) == "gate,slalom,bin,torpedo,octagon,home", "order " + string.Join(",", seen));
            s.Check(p.run.state == "ended", "ended");
            s.Check(g.Total(p) == 0, "no points");
            s.Check(p.run.chosen == "shark" || p.run.chosen == "sawfish", "an animal was chosen");
        }

        static void SkipThenScore(Suite s)
        {
            var g = Solo();
            var p = g.players[0];
            var bin = g.course.bin;
            g.Action(0, "skip"); // gate
            g.Action(0, "skip"); // slalom -> bin start
            p.veh.Reset(new V3(bin.pos.x - p.design.marker.x, bin.pos.y, g.course.floor - 1.0), bin.heading);
            Run(g, 3);
            g.Action(0, "marker");
            Run(g, 6);
            s.Check(p.run.scores.bin >= POINTS.binAny, "bin " + p.run.scores.bin);
        }

        static void Leave(Suite s)
        {
            var g = new Game(new GameOptions { players = Players("duburi", "manta"), seed = 3, coinFlip = false });
            g.Action(1, "leave");
            s.Check(g.players[1].left, "left");
            s.Check(g.players[0].run.state == "running", "other still running");
            Run(g, 3);
            s.Check(!g.over, "not over");
            g.Action(0, "forfeit");
            s.Check(g.players[0].run.state == "running", "new run running");
            g.Action(0, "leave");
            Run(g, 3);
            s.Check(g.over, "over");
        }

        static void ThreeWaters(Suite s)
        {
            s.Check(string.Join(",", Scenarios.SCENARIO_ORDER) == "pool,pacific,arctic", "order");
            var keys = new List<string>(Scenarios.SCENARIOS.Keys);
            keys.Sort(StringComparer.Ordinal);
            s.Check(string.Join(",", keys) == "arctic,pacific,pool", "keys");
        }
    }
}
