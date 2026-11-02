// Port of src/sim/game.js.
// One match for 1-4 vehicles in the same water: the clock, the vehicles, the
// things they drop / fire / carry, and the judging of every task by the AUV
// competition handbook rules (sections 3.2 and 4.2).
//
// The handbook's official point breakdown is "to be released"; the values in
// POINTS are this game's, chosen to keep the handbook's ordering
// (roll / pitch style worth more than yaw, the chosen animal worth more, the
// correct basket worth more, ...).
//
// Note for the Unity layer (namespace Duburi.Game): inside that namespace the
// simple name `Game` is the namespace; write `Duburi.Core.Game` or add
// `using CoreGame = Duburi.Core.Game;`.

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    public static class POINTS
    {
        public const double coinFlip = 300;
        public const double gatePass = 200, gateClean = 200, gateAnimal = 200, styleRollPitch = 200, styleYaw = 100, styleMax = 1600;
        public const double slalomSet = 300, slalomSide = 150, slalomInside = 150;
        public const double binAny = 400, binCorrect = 200;
        public const double torpedoTouch = 200, torpedoThrough = 600, torpedoAnimal = 300, torpedoFar = 200, torpedoOrder = 400;
        public const double octSurface = 1000, octWithItem = 400, octFaceAny = 300, octFaceChosen = 600, basketAny = 400, basketCorrect = 300, octRotation = 250;
        public const double home = 500;
        public const double timePerMinute = 100;
    }

    /// per-task points of a run (keys = TASK_KEYS); also usable as scores["gate"]
    public sealed class Scores
    {
        public double coin, gate, style, slalom, bin, torpedo, octagon, home, time;

        public double this[string key]
        {
            get
            {
                switch (key)
                {
                    case "coin": return coin; case "gate": return gate; case "style": return style;
                    case "slalom": return slalom; case "bin": return bin; case "torpedo": return torpedo;
                    case "octagon": return octagon; case "home": return home; case "time": return time;
                }
                return 0; // JS: undefined || 0
            }
            set
            {
                switch (key)
                {
                    case "coin": coin = value; break; case "gate": gate = value; break; case "style": style = value; break;
                    case "slalom": slalom = value; break; case "bin": bin = value; break; case "torpedo": torpedo = value; break;
                    case "octagon": octagon = value; break; case "home": home = value; break; case "time": time = value; break;
                    default: throw new KeyNotFoundException(key);
                }
            }
        }

        public Scores Clone() => (Scores)MemberwiseClone();
    }

    public sealed class TorpedoHit
    {
        public bool through;
        public string animal; // null when it only hit the board
        public bool far;
        public double t;
    }

    /// p.run: one attempt (JS object of the same fields)
    public sealed class Run
    {
        public string state = "running"; // "running" | "ended"
        public string reason = "";
        public double startTime;
        public string coin;              // "heads" | "tails" | null
        public int markers = 2, torpedoes = 2;
        public Item held;
        public Scores scores = new Scores();
        public bool leftStart, gatePassed;
        public string chosen;            // "shark" | "sawfish" | null (picked at the gate)
        public double gateSide;          // -1 left, +1 right, 0 not yet
        public double? gateS, homeS;
        public bool inGateBuf;
        public V3 styleAcc;              // rad turned about x, y, z inside the gate zone
        public bool gateTouched, gateTouchedThis;
        /// JS r.gateAttempt = { time }: the time of the gate pass (null before)
        public double? gateAttempt;
        public double?[] slalomS = new double?[3];
        public double[] slalomBest = new double[3];
        public OrderedMap<string, double> binBest = new OrderedMap<string, double>();
        public List<TorpedoHit> torpedoHits = new List<TorpedoHit>();
        public double octSurface;
        public HashSet<string> octItems = new HashSet<string>();
        public double faceTimer, faceBest, rotAcc, rotBest;
        public OrderedMap<string, double> basketPts = new OrderedMap<string, double>();
        public double? totalTime;
        public bool finished;
        public bool? timeBonusMet;
        public double total;             // set when the run ends
        public HashSet<string> skipped = new HashSet<string>();
        public bool gateSkipped;
    }

    /// p.runs entry
    public sealed class RunResult
    {
        public double total;
        public Scores scores;
        public string reason;
    }

    public sealed class Player
    {
        public int index;
        public string name, color, bot;
        public BotDesign design;
        public Vehicle veh;
        public Run run;
        public List<RunResult> runs = new List<RunResult>();
        public double best;
        public bool left;
        /// obstacle tags touched in the last step (Collide); reused set
        public HashSet<string> touched = new HashSet<string>();
    }

    /// payload physics (in-water net weight, added mass and quadratic drag)
    public sealed class ItemPhys
    {
        public double mass, vol, cq, cl, r; // kg, m^3, quadratic / linear drag, radius m
    }

    /// a marker, torpedo, cup or spoon
    public sealed class Item
    {
        public string id;
        public string kind;       // "marker" | "torpedo" | "cup" | "spoon"
        public Player owner;      // null for the trash
        public V3 pos, vel;       // m, m/s NED
        public string state;      // "rest" | "fall" | "held"
        public ItemPhys ph;
        public double meff;       // kg with added mass
        public double t;          // s since it started falling
        public V3 home;           // trash: start position
        public bool hasHome;
        public string on;         // what it rests on: "table" | "bin" | "basket" | "floor"
        public Player holder;
        public string inBasket;   // basket item it is in ("cup" | "spoon") or null
        public Player releasedBy;
        public double fireDist, prevN; // torpedo: distance from the board when fired, last board-normal coordinate
        public V3 dir;            // torpedo: firing direction
    }

    public sealed class GameEvent
    {
        public int player; // index, -1 for the match
        public string text;
        public double pts;
        public string kind; // "info" | "warn" | "score" | "bad"
        public double t;
    }

    public sealed class PlayerConfig
    {
        public string name;
        public string bot = "duburi";
        public string color;
        public string device; // input device id ("kbA", "pad0", "cpu", ...); not used by the Core
    }

    public sealed class GameOptions
    {
        public string scenario = "pool";
        public List<PlayerConfig> players;   // null: [{ name: 'Player 1', bot: 'duburi' }]
        public double runMinutes = 15;
        public bool coinFlip = true;
        public double depth = 0;             // 0 = the scenario's own depth
        public double? seed;                 // null: random
    }

    public struct TaskStart { public V3 pos; public double hdg; }

    public struct PingerBearing
    {
        public double rel;   // rad relative to the vehicle heading
        public double dist;  // m
        public Pinger pinger;
    }

    /// game.js mulberry(seed): the match's random numbers
    public sealed class Mulberry32
    {
        uint s;
        public Mulberry32(double seed) { s = Js.ToUint32(seed); }
        public double Next()
        {
            unchecked
            {
                s += 0x6d2b79f5;
                uint t = s;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    public sealed class Game
    {
        public static readonly string[] TASK_KEYS = { "coin", "gate", "style", "slalom", "bin", "torpedo", "octagon", "home", "time" };
        public static readonly OrderedMap<string, string> TASK_LABEL = new OrderedMap<string, string>
        {
            ["coin"] = "Coin flip", ["gate"] = "Gate", ["style"] = "Gate style", ["slalom"] = "Slalom", ["bin"] = "Bin (BRUVS)",
            ["torpedo"] = "Torpedoes", ["octagon"] = "Octagon", ["home"] = "Return home", ["time"] = "Time bonus",
        };

        const double HALF_PI = JsMath.PI / 2;
        static double Hdist(V3 a, V2 b) => JsMath.Hypot(a.x - b.x, a.y - b.y);
        static double Dot2(V2 a, V2 b) => a.x * b.x + a.y * b.y;

        // payload physics (in-water net weight, added mass and quadratic drag)
        public static readonly OrderedMap<string, ItemPhys> ITEM_PHYS = new OrderedMap<string, ItemPhys>
        {
            ["marker"] = new ItemPhys { mass = 0.4, vol = 0.00025, cq = 3.2, cl = 0.4, r = 0.03 },
            ["torpedo"] = new ItemPhys { mass = 0.255, vol = 0.00025, cq = 0.09, cl = 0.05, r = 0.025 },
            ["cup"] = new ItemPhys { mass = 0.12, vol = 0.00009, cq = 1.4, cl = 0.3, r = 0.045 },
            ["spoon"] = new ItemPhys { mass = 0.1, vol = 0.00007, cq = 1.0, cl = 0.25, r = 0.03 },
        };

        public string scenario;
        public double seed;
        public Func<double> rand;
        public double depth;       // m, floor depth of this match
        public Arena arena;
        public Environment env;
        public Course course;
        public double duration;    // s
        public bool coinFlip;
        public double time;        // s since the start
        public bool over;
        public List<GameEvent> events = new List<GameEvent>();
        public List<Item> items = new List<Item>();
        public List<Player> players = new List<Player>();
        public double idle;

        // scratch for Step (no garbage)
        readonly List<Player> active = new List<Player>(4);
        readonly List<Vehicle> others = new List<Vehicle>(4);

        public Game() : this(null) { }

        public Game(GameOptions o)
        {
            if (o == null) o = new GameOptions();
            var pcs = o.players ?? new List<PlayerConfig> { new PlayerConfig { name = "Player 1", bot = "duburi" } };
            scenario = o.scenario ?? "pool";
            seed = o.seed ?? Js.ToInt32(CoreRandom.Next() * 1e9);
            var mb = new Mulberry32(seed);
            rand = mb.Next;
            depth = Scenarios.WaterDepth(scenario, o.depth);
            arena = Course.ARENA.Clone();
            arena.depth = depth;
            env = new Environment(Scenarios.EnvironmentConfig(scenario, depth), new RNG(seed + 7));
            course = Course.Build(rand, depth);
            duration = o.runMinutes * 60;
            coinFlip = o.coinFlip;
            time = 0;
            over = false;
            foreach (var t in course.octagon.trash)
            {
                var it = MakeItem(t.kind, null, t.pos, V3.Zero, "rest");
                it.home = t.pos; it.hasHome = true; it.on = "table";
                items.Add(it);
            }
            for (int i = 0; i < pcs.Count; i++) players.Add(MakePlayer(pcs[i], i));
            foreach (var p in players) StartRun(p, true);
        }

        Player MakePlayer(PlayerConfig pc, int i)
        {
            var design = Bots.Get(pc.bot, Bots.BOTS["duburi"]);
            var veh = new Vehicle(design, env);
            return new Player
            {
                index = i,
                name = string.IsNullOrEmpty(pc.name) ? "Player " + (i + 1) : pc.name,
                color = string.IsNullOrEmpty(pc.color) ? design.color : pc.color,
                bot = design.id,
                design = design,
                veh = veh,
                run = null,
                best = 0,
            };
        }

        Item MakeItem(string kind, Player owner, V3 pos, V3 vel, string state)
        {
            var ph = ITEM_PHYS[kind];
            double meff = ph.mass + 0.5 * env.rho * ph.vol;
            return new Item { id = CoreRandom.Id(), kind = kind, owner = owner, pos = pos, vel = vel, state = state, ph = ph, meff = meff, t = 0 };
        }

        void Emit(Player player, string text, double pts = 0, string kind = "info") => events.Add(new GameEvent { player = player != null ? player.index : -1, text = text, pts = pts, kind = kind, t = time });

        static string S(double v) => Js.Str(v);

        // ------------------------------------------------------------ runs
        public void StartRun(Player p, bool first = false)
        {
            var c = course;
            var dock = c.dock;
            int n = players.Count > 0 ? players.Count : 1;
            double lane = (p.index - (n - 1) / 2.0) * 1.5;
            // coin flip (3.2.1): heads = parallel to the gate, tails = tail toward the gate
            double heading = c.gate.heading;
            string coin = null;
            if (coinFlip)
            {
                coin = rand() < 0.5 ? "heads" : "tails";
                heading = coin == "heads" ? c.gate.heading + (rand() < 0.5 ? HALF_PI : -HALF_PI) : c.gate.heading + JsMath.PI;
            }
            p.veh.Reset(new V3(dock.pos.x, dock.pos.y + lane, 0.02), MathUtil.WrapPI(heading));
            p.veh.depthTarget = 0;
            // forfeit: this player's dropped markers / torpedoes are cleared
            items.RemoveAll(it => !(it.owner != p || it.kind == "cup" || it.kind == "spoon"));
            foreach (var it in items) if (it.holder == p) { it.holder = null; it.state = "fall"; }
            if (p.run != null && !first) Emit(p, "New run — previous points forfeited", 0, "warn");
            p.run = new Run { state = "running", reason = "", startTime = time, coin = coin };
            if (coin != null) Emit(p, "Coin flip: " + coin.ToUpperInvariant() + " — " + (coin == "heads" ? "start parallel to the gate" : "start facing away from the gate"), 0, "info");
        }

        public void EndRun(Player p, string reason = "Run ended by team lead")
        {
            var r = p.run;
            if (r == null || r.state != "running") return;
            r.state = "ended";
            r.reason = reason;
            // time bonus (4.2): requires gate + (marker in bin or torpedo through) + surfaced in the octagon
            bool anyThrough = false;
            foreach (var h in r.torpedoHits) if (h.through) { anyThrough = true; break; }
            bool met = r.gatePassed && (r.scores.bin > 0 || anyThrough) && r.octSurface > 0;
            double remaining = JsMath.Max(0, duration - time);
            if (met && remaining > 0)
            {
                r.scores.time = JsMath.Round((remaining / 60) * POINTS.timePerMinute);
                Emit(p, "Time bonus " + S(JsMath.Floor(remaining / 60)) + ":" + Js.PadStart(S(JsMath.Floor(remaining % 60)), 2, '0') + " left", r.scores.time, "score");
            }
            r.total = Total(p);
            p.runs.Add(new RunResult { total = r.total, scores = r.scores.Clone(), reason = reason });
            p.best = JsMath.Max(p.best, r.total);
            if (r.held != null) { r.held.holder = null; r.held.state = "fall"; r.held = null; }
            Emit(p, reason, 0, reason.StartsWith("Breach", StringComparison.Ordinal) ? "bad" : "info");
        }

        public double Total(Player p)
        {
            double s = 0;
            foreach (var k in TASK_KEYS) s = s + Js.Or(p.run.scores[k], 0);
            return s;
        }

        // ------------------------------------------------------------ actions
        /// what: restart | forfeit | leave | skip | depthHold | endRun | marker | torpedo | grip
        public void Action(int i, string what)
        {
            var p = i >= 0 && i < players.Count ? players[i] : null;
            if (p == null || over) return;
            var r = p.run;
            var v = p.veh;
            if (p.left) return;
            if (what == "restart" || what == "forfeit") { StartRun(p); return; }
            if (what == "leave") { Leave(p); return; }
            if (r == null || r.state != "running") return;
            if (what == "skip") { SkipTask(p); return; }
            if (what == "depthHold") { v.depthHold = !v.depthHold; v.depthTarget = v.p.z; Emit(p, v.depthHold ? "Depth hold ON" : "Depth hold OFF (manual)", 0, "info"); return; }
            if (what == "endRun") { EndRun(p, "Run ended by team lead"); return; }
            if (what == "marker")
            {
                if (r.markers <= 0) { Emit(p, "No markers left", 0, "warn"); return; }
                r.markers--;
                V3 pos = v.ToWorld(p.design.marker);
                items.Add(MakeItem("marker", p, pos, v.WorldVel(), "fall"));
                Emit(p, "Marker dropped (" + r.markers + " left)", 0, "info");
                return;
            }
            if (what == "torpedo")
            {
                if (r.torpedoes <= 0) { Emit(p, "No torpedoes left", 0, "warn"); return; }
                r.torpedoes--;
                V3 pos = v.ToWorld(p.design.torpedo);
                V3 dir = v.BodyToWorldDir(new V3(1, 0, 0));
                V3 vw = v.WorldVel();
                const double speed = 2.6; // m/s: a "safe" speed (5.4.2)
                var it = MakeItem("torpedo", p, pos, new V3(vw.x + dir.x * speed, vw.y + dir.y * speed, vw.z + dir.z * speed), "fall");
                var bl = Course.BoardLocal(course.board, pos);
                it.fireDist = Math.Abs(bl.n);
                it.prevN = bl.n;
                it.dir = dir;
                items.Add(it);
                Emit(p, "Torpedo away (" + r.torpedoes + " left)", 0, "info");
                return;
            }
            if (what == "grip")
            {
                if (r.held != null)
                {
                    var it = r.held;
                    it.holder = null; it.state = "fall"; it.vel = v.WorldVel(); it.releasedBy = p;
                    r.held = null;
                    Emit(p, "Released the " + it.kind, 0, "info");
                    return;
                }
                V3 g = v.ToWorld(p.design.gripper);
                Item best = null;
                double bd = 0.32;
                foreach (var it in items)
                {
                    if ((it.kind != "cup" && it.kind != "spoon") || it.holder != null) continue;
                    double d = JsMath.Hypot(it.pos.x - g.x, it.pos.y - g.y, it.pos.z - g.z);
                    if (d < bd) { bd = d; best = it; }
                }
                if (best == null) { Emit(p, "Gripper closed — nothing in reach", 0, "warn"); return; }
                best.holder = p; best.state = "held"; best.inBasket = null;
                r.held = best;
                Emit(p, "Grabbed the " + best.kind, 0, "info");
            }
        }

        // ------------------------------------------------------------ skip / leave
        // The task a player is on: the first one in course order not done or skipped.
        public string CurrentTask(Player p)
        {
            var r = p.run;
            var sk = r.skipped;
            if (!r.gatePassed) return "gate";
            int done = 0;
            foreach (double x in r.slalomBest) if (x > 0) done++;
            if (!sk.Contains("slalom") && done < 3) return "slalom";
            if (!sk.Contains("bin") && r.markers > 0) return "bin";
            if (!sk.Contains("torpedo") && r.torpedoes > 0) return "torpedo";
            if (!sk.Contains("octagon") && r.octSurface == 0) return "octagon";
            return "home";
        }

        static V2 Add2(V2 p, V2 d, double k) => new V2(p.x + d.x * k, p.y + d.y * k);

        // where a vehicle is put to start a task (NED position + heading)
        public TaskStart GetTaskStart(string task)
        {
            var c = course;
            if (task == "slalom") { var sl = c.slalom; V2 a = Add2(sl.sets[0].pos, sl.dir, -2.6); return new TaskStart { pos = new V3(a.x, a.y, (sl.band.x + sl.band.y) / 2), hdg = sl.heading }; }
            if (task == "bin") { var b = c.bin; V2 a = Add2(b.pos, b.dir, -2.4); return new TaskStart { pos = new V3(a.x, a.y, c.floor - 1.2), hdg = b.heading }; }
            if (task == "torpedo") { var b = c.board; V2 a = Add2(b.pos, b.dir, b.farDistance + 0.8); return new TaskStart { pos = new V3(a.x, a.y, b.depth), hdg = b.heading + JsMath.PI }; }
            if (task == "octagon") { var o = c.octagon; return new TaskStart { pos = new V3(o.pos.x - 2.6, o.pos.y, o.tableTop - 1.4), hdg = 0 }; }
            var g = c.gate; // home: north of the gate, facing back through it
            V2 h = Add2(g.pos, g.dir, 3);
            return new TaskStart { pos = new V3(h.x, h.y, 1.3), hdg = g.heading + JsMath.PI };
        }

        static string TaskName(string task)
        {
            switch (task)
            {
                case "gate": return "Gate"; case "slalom": return "Slalom"; case "bin": return "Bin";
                case "torpedo": return "Torpedoes"; case "octagon": return "Octagon"; case "home": return "Return home";
            }
            return "undefined";
        }

        // Skip the current task: no points for it, the vehicle is moved to the start
        // of the next one. Skipping the gate picks an animal at random (no gate points).
        public void SkipTask(Player p)
        {
            var r = p.run;
            string task = CurrentTask(p);
            r.skipped.Add(task);
            if (task == "home") { r.finished = true; EndRun(p, "Run complete — return home skipped"); return; }
            if (task == "gate")
            {
                r.gatePassed = true;
                r.gateSkipped = true;
                r.gateSide = rand() < 0.5 ? -1 : 1;
                r.chosen = r.gateSide == course.gate.sharkSide ? "shark" : "sawfish";
                r.gateAttempt = time - 60;
                r.leftStart = true;
            }
            if (r.held != null) { r.held.holder = null; r.held.state = "fall"; r.held = null; }
            string next = CurrentTask(p);
            var st = GetTaskStart(next);
            p.veh.Reset(st.pos, MathUtil.WrapPI(st.hdg));
            r.gateS = null; r.slalomS[0] = null; r.slalomS[1] = null; r.slalomS[2] = null; r.inGateBuf = false;
            Emit(p, "Skipped " + TaskName(task) + (task == "gate" ? " — your animal: " + (r.chosen == "shark" ? "reef shark" : "sawfish") : "") + " → " + TaskName(next), 0, "warn");
        }

        // Leave the match: the run ends, the vehicle is taken out of the water.
        public void Leave(Player p)
        {
            if (p.run.state == "running") EndRun(p, "Left the match");
            p.left = true;
            p.veh.Reset(new V3(course.dock.pos.x - 3, course.dock.pos.y + 30, 0), 0);
            Emit(p, p.name + " left the match", 0, "info");
        }

        // ------------------------------------------------------------ simulation
        public void Step() => Step(Vehicle.DT);

        public void Step(double dt)
        {
            if (over) return;
            env.Step(dt);
            time += dt;
            active.Clear();
            foreach (var p in players) if (!p.left) active.Add(p);
            foreach (var p in active)
            {
                bool running = p.run.state == "running";
                p.veh.Step(dt, running);
                others.Clear();
                foreach (var q in active) if (q.veh != p.veh) others.Add(q.veh);
                Collide.CollideVehicle(p.veh, course, arena, others, p.touched);
                if (running) Judge(p, dt);
            }
            StepItems(dt);
            // the match ends when the clock runs out, or when nobody has a run going
            // for a few seconds (time to choose a new run after a breach)
            if (time >= duration)
            {
                foreach (var p in players) EndRun(p, "Time is up");
                over = true;
            }
            else
            {
                bool allIdle = true, allDone = true;
                foreach (var p in players)
                {
                    if (!(p.left || p.run.state != "running")) allIdle = false;
                    if (!(p.left || p.run.finished)) allDone = false;
                }
                if (allIdle)
                {
                    idle = idle + dt;
                    if (idle > (allDone ? 2 : 10)) over = true;
                }
                else idle = 0;
            }
        }

        void StepItems(double dt)
        {
            var env = this.env;
            var c = course;
            double g = env.g;
            for (int ii = 0; ii < items.Count; ii++)
            {
                var it = items[ii];
                if (it.state == "held")
                {
                    var p = it.holder;
                    var v = p.veh;
                    it.pos = v.ToWorld(p.design.gripper);
                    it.vel = v.WorldVel();
                    continue;
                }
                if (it.state != "fall") continue;
                it.t += dt;
                var ph = it.ph;
                V3 fl = env.FluidVelocity(it.pos.x, it.pos.y, JsMath.Max(0, it.pos.z));
                V3 vr = new V3(it.vel.x - fl.x, it.vel.y - fl.y, it.vel.z - fl.z);
                double sp = JsMath.Hypot(vr.x, vr.y, vr.z);
                double sub = it.pos.z > 0 ? 1 : 0;
                double net = (ph.mass * g - env.rho * g * ph.vol * sub) / it.meff;
                double k = (ph.cl + ph.cq * sp) / it.meff;
                for (int i = 0; i < 3; i++) it.vel[i] = (it.vel[i] + dt * (i == 2 ? net : 0) + dt * k * fl[i]) / (1 + dt * k);
                V3 prev = it.pos;
                for (int i = 0; i < 3; i++) it.pos[i] += it.vel[i] * dt;
                if (it.pos.z < -0.05) { it.pos.z = -0.05; it.vel.z = JsMath.Max(0, it.vel.z); }
                if (it.kind == "torpedo") TorpedoBoard(it, prev);
                if (it.state != "fall") continue;
                LandItem(it, prev, c);
                // arena walls
                double hx = Course.ARENA.length / 2 - 0.05, hy = Course.ARENA.width / 2 - 0.05;
                if (Math.Abs(it.pos.x) > hx) { it.pos.x = JsMath.Sign(it.pos.x) * hx; it.vel.x *= -0.3; }
                if (Math.Abs(it.pos.y) > hy) { it.pos.y = JsMath.Sign(it.pos.y) * hy; it.vel.y *= -0.3; }
            }
        }

        void LandItem(Item it, V3 prev, Course c)
        {
            double floor = c.floor, r = it.ph.r;
            var bin = c.bin;
            var oct = c.octagon;
            // bin: lands inside on its bottom, otherwise on the rim / outside
            V2 bl = BinLocal(it.pos);
            if (Math.Abs(bl.x) < bin.size.x / 2 && Math.Abs(bl.y) < bin.size.y / 2)
            {
                if (it.pos.z >= floor - 0.02 - r) { Rest(it, floor - 0.02 - r, "bin"); return; }
            }
            // baskets at the table edges
            foreach (var b in oct.baskets)
            {
                if (Math.Abs(it.pos.x - b.c.x) < b.h.x && Math.Abs(it.pos.y - b.c.y) < b.h.y && it.pos.z >= b.depthTop + 0.08 - r && prev.z < b.depthTop + 0.08) { Rest(it, b.depthTop + 0.08 - r, "basket", b); return; }
            }
            // table top
            if (Math.Abs(it.pos.x - oct.pos.x) < oct.tableHalf && Math.Abs(it.pos.y - oct.pos.y) < oct.tableHalf && it.pos.z >= oct.tableTop - r && prev.z <= oct.tableTop - r + 0.02) { Rest(it, oct.tableTop - r, "table"); return; }
            if (it.pos.z >= floor - r) Rest(it, floor - r, "floor");
        }

        /// world point -> bin coordinates (along the bin heading, across to its right)
        public V2 BinLocal(V3 p)
        {
            var b = course.bin;
            double dx = p.x - b.pos.x, dy = p.y - b.pos.y;
            var d = new V2(dx, dy);
            return new V2(Dot2(d, b.dir), Dot2(d, b.right));
        }

        void Rest(Item it, double z, string on, Basket basket = null)
        {
            it.pos.z = z;
            it.vel = V3.Zero;
            it.state = "rest";
            it.on = on;
            var owner = it.owner ?? it.releasedBy;
            if (owner == null || owner.run.state != "running") return;
            var r = owner.run;
            if (it.kind == "marker" && on == "bin")
            {
                if (!r.gatePassed) { Emit(owner, "Marker in the bin — but the gate comes first (no points)", 0, "warn"); return; }
                string half = BinLocal(it.pos).x < 0 ? course.bin.halves[0] : course.bin.halves[1];
                double pts = POINTS.binAny + (half == r.chosen ? POINTS.binCorrect : 0);
                r.binBest[it.id] = pts;
                var vals = new List<double>(r.binBest.Values);
                Js.StableSort(vals, (a, b) => b - a);
                double before = r.scores.bin;
                double sum = 0;
                for (int i = 0; i < vals.Count && i < 2; i++) sum = sum + vals[i];
                r.scores.bin = sum;
                Emit(owner, "Marker in the bin (" + (half == "shark" ? "reef shark" : "sawfish") + " half" + (half == r.chosen ? ", correct!" : "") + ")", r.scores.bin - before, "score");
            }
            else if (it.kind == "marker") Emit(owner, "Marker missed the bin", 0, "warn");
            if ((it.kind == "cup" || it.kind == "spoon") && on == "basket")
            {
                it.inBasket = basket.item;
                if (!r.gatePassed) return;
                double pts = POINTS.basketAny + (basket.item == it.kind ? POINTS.basketCorrect : 0);
                double before = r.scores.octagon;
                r.basketPts[it.kind] = JsMath.Max(Js.Or(r.basketPts.Get(it.kind, 0), 0), pts);
                r.rotAcc = 0;
                OctagonScore(owner);
                Emit(owner, (it.kind == "cup" ? "Cup" : "Spoon") + " in the " + basket.item + " basket" + (basket.item == it.kind ? " — correct!" : ""), r.scores.octagon - before, "score");
            }
        }

        void TorpedoBoard(Item it, V3 prev)
        {
            var b = course.board;
            var bl = Course.BoardLocal(b, it.pos);
            double pn = it.prevN;
            it.prevN = bl.n;
            if (!(pn > 0 && bl.n <= 0)) return;
            // where it crossed the board plane
            double f = pn / (pn - bl.n);
            V3 cp = new V3(prev.x + (it.pos.x - prev.x) * f, prev.y + (it.pos.y - prev.y) * f, prev.z + (it.pos.z - prev.z) * f);
            var L = Course.BoardLocal(b, cp);
            double half = b.size / 2;
            if (Math.Abs(L.u) > half + it.ph.r || Math.Abs(L.v) > half + it.ph.r) return; // missed the board
            var p = it.owner;
            var r = p.run;
            BoardOpening through = null;
            foreach (var o in b.openings)
            {
                double dd = o.shape == "square" ? JsMath.Max(Math.Abs(L.u - o.u), Math.Abs(L.v - o.v)) : JsMath.Hypot(L.u - o.u, L.v - o.v);
                if (dd <= o.r - it.ph.r * 0.5) through = o;
            }
            if (through == null)
            {
                // hit the backing: stops and sinks in front of the board
                it.vel = new V3(b.dir.x * 0.15, b.dir.y * 0.15, 0.05);
                it.pos = new V3(cp.x + b.dir.x * 0.03, cp.y + b.dir.y * 0.03, cp.z);
                it.prevN = 0.03;
            }
            if (r.state != "running") return;
            if (!r.gatePassed) { Emit(p, "Torpedo hit — but the gate comes first (no points)", 0, "warn"); return; }
            bool far = it.fireDist >= b.farDistance;
            var hit = new TorpedoHit { through = through != null, animal = through != null ? through.animal : null, far = far, t = time };
            r.torpedoHits.Add(hit);
            double before = r.scores.torpedo;
            // best two torpedoes + order bonus
            var vals = new List<double>(r.torpedoHits.Count);
            foreach (var h in r.torpedoHits) vals.Add((h.through ? POINTS.torpedoThrough + (h.animal == r.chosen ? POINTS.torpedoAnimal : 0) : POINTS.torpedoTouch) + (h.far ? POINTS.torpedoFar : 0));
            Js.StableSort(vals, (a, b2) => b2 - a);
            double s = 0;
            for (int i = 0; i < vals.Count && i < 2; i++) s = s + vals[i];
            var th = r.torpedoHits.FindAll(h => h.through);
            if (th.Count >= 2 && th[0].animal == r.chosen && th[1].animal != r.chosen) s += POINTS.torpedoOrder;
            r.scores.torpedo = JsMath.Max(r.scores.torpedo, s);
            string what = through != null ? "through the " + through.label + " opening (" + (through.animal == "shark" ? "reef shark" : "sawfish") + ")" : "hit the board";
            Emit(p, "Torpedo " + what + (far ? " from far" : ""), r.scores.torpedo - before, "score");
        }

        // ------------------------------------------------------------ judging
        void Judge(Player p, double dt)
        {
            var r = p.run;
            var v = p.veh;
            var c = course;
            var d = p.design;
            V3 pos = v.p;
            double depth = v.depth;
            double H = d.dims.z;
            bool surfaced = depth < H * 0.45;
            var gate = c.gate;

            // start zone: submerge before leaving it (3.2.1)
            if (!r.leftStart && Hdist(pos, c.dock.pos) > c.dock.radius + 1.5) r.leftStart = true;

            // ---------- gate & return home ----------
            V2 rel = new V2(pos.x - gate.pos.x, pos.y - gate.pos.y);
            double s = Dot2(rel, gate.dir), lat = Dot2(rel, gate.right);
            bool inBuf = JsMath.Hypot(s, lat, depth - 1.2) < 3.0;
            if (inBuf && !r.inGateBuf) { r.styleAcc = V3.Zero; r.gateTouchedThis = false; }
            r.inGateBuf = inBuf;
            if (inBuf) for (int i = 0; i < 3; i++) r.styleAcc[i] += v.w[i] * dt;
            if (p.touched != null && (p.touched.Contains("gateLeg") || p.touched.Contains("gateBar"))) r.gateTouchedThis = true;
            bool between = Math.Abs(lat) < gate.width / 2 && depth > gate.topDepth;
            if (r.gateS != null && r.gateS.Value < 0 && s >= 0 && between && !r.gatePassed)
            {
                r.gatePassed = true;
                r.gateSide = lat < 0 ? -1 : 1;
                r.chosen = r.gateSide == gate.sharkSide ? "shark" : "sawfish";
                r.gateAttempt = time;
                bool clean = !r.gateTouchedThis;
                double pts = POINTS.gatePass + (clean ? POINTS.gateClean : 0) + POINTS.gateAnimal;
                r.scores.gate = pts;
                Emit(p, "Through the gate on the " + (r.chosen == "shark" ? "REEF SHARK" : "SAWFISH") + " side" + (clean ? ", clean" : " (touched a leg)"), pts, "score");
                if (r.coin != null) { r.scores.coin = POINTS.coinFlip; Emit(p, "Coin flip start (" + r.coin + ")", POINTS.coinFlip, "score"); }
            }
            if (r.gatePassed && r.inGateBuf && time - r.gateAttempt.Value < 12)
            {
                double n0 = JsMath.Floor(Math.Abs(r.styleAcc.x) / HALF_PI), n1 = JsMath.Floor(Math.Abs(r.styleAcc.y) / HALF_PI), n2 = JsMath.Floor(Math.Abs(r.styleAcc.z) / HALF_PI);
                double sp = JsMath.Min(POINTS.styleMax, (n0 + n1) * POINTS.styleRollPitch + n2 * POINTS.styleYaw);
                if (sp > r.scores.style) { Emit(p, "Style: " + S(n0) + "× roll, " + S(n1) + "× pitch, " + S(n2) + "× yaw quarter-turns", sp - r.scores.style, "score"); r.scores.style = sp; }
            }
            // return home: back through the gate, underwater, after doing something else
            if (r.gatePassed && r.gateS != null && r.gateS.Value > 0 && s <= 0 && between && time - r.gateAttempt.Value > 15 && r.scores.home == 0)
            {
                r.scores.home = POINTS.home;
                Emit(p, "Returned home through the gate", POINTS.home, "score");
                r.finished = true;
                EndRun(p, "Run complete — returned home");
            }
            r.gateS = s;
            if (r.state != "running") return;

            // ---------- slalom ----------
            var sl = c.slalom;
            for (int k = 0; k < sl.sets.Count; k++)
            {
                var set = sl.sets[k];
                V2 rr = new V2(pos.x - set.pos.x, pos.y - set.pos.y);
                double a = Dot2(rr, sl.dir), l = Dot2(rr, sl.right);
                double? prevS = r.slalomS[k];
                r.slalomS[k] = a;
                if (prevS == null || JsMath.Sign(prevS.Value) == JsMath.Sign(a) || Math.Abs(l) > 1.524) continue;
                double prev = prevS.Value;
                if (!r.gatePassed) { Emit(p, "Slalom set passed — but the gate comes first", 0, "warn"); continue; }
                double travel = a > prev ? 1 : -1;
                double side = JsMath.Sign(l) * travel;
                bool inside = depth - H / 2 < sl.band.y && depth + H / 2 > sl.band.x;
                double pts = POINTS.slalomSet + (side == r.gateSide ? POINTS.slalomSide : 0) + (inside ? POINTS.slalomInside : 0);
                if (pts > r.slalomBest[k])
                {
                    double before = r.scores.slalom;
                    r.slalomBest[k] = pts;
                    r.scores.slalom = ((0 + r.slalomBest[0]) + r.slalomBest[1]) + r.slalomBest[2];
                    Emit(p, "Slalom set " + (k + 1) + (side == r.gateSide ? ", same side" : "") + (inside ? ", between the pipes" : " (above / below)"), r.scores.slalom - before, "score");
                }
            }

            // ---------- octagon / surfacing ----------
            var oct = c.octagon;
            double dO = Hdist(pos, oct.pos);
            double halfW = JsMath.Max(d.dims.x, d.dims.y) / 2;
            double frac = JsMath.Max(0, JsMath.Min(1, (oct.radius - dO + halfW) / (2 * halfW)));
            if (dO < oct.radius && r.octItems.Count + r.basketPts.Count > 0)
            {
                r.rotAcc += v.w.z * dt;
            }
            else if (dO >= oct.radius) r.rotAcc = 0;
            if (surfaced)
            {
                if (frac > 0.02)
                {
                    if (r.gatePassed)
                    {
                        double sp = JsMath.Round(POINTS.octSurface * frac);
                        if (sp > r.octSurface) { double before = r.scores.octagon; r.octSurface = sp; OctagonScore(p); Emit(p, "Surfaced in the octagon" + (frac < 1 ? " (" + S(JsMath.Round(frac * 100)) + "% inside)" : ""), r.scores.octagon - before, "score"); }
                        if (r.held != null && !r.octItems.Contains(r.held.kind)) { double before = r.scores.octagon; r.octItems.Add(r.held.kind); OctagonScore(p); Emit(p, "Surfaced with the " + r.held.kind, r.scores.octagon - before, "score"); }
                        // facing an image hanging inside the octagon
                        double hd = v.Euler().z;
                        OctImage facing = null;
                        foreach (var im in oct.images) if (Math.Abs(MathUtil.WrapPI(hd - im.angle)) < 0.35) facing = im;
                        if (facing != null)
                        {
                            r.faceTimer += dt;
                            double fp = facing.animal == r.chosen ? POINTS.octFaceChosen : POINTS.octFaceAny;
                            if (r.faceTimer > 1.0 && fp > r.faceBest) { double before = r.scores.octagon; r.faceBest = fp; OctagonScore(p); Emit(p, "Facing the " + (facing.animal == "shark" ? "reef shark" : "sawfish") + " image", r.scores.octagon - before, "score"); }
                        }
                        else r.faceTimer = 0;
                    }
                }
                else if (r.leftStart || Hdist(pos, c.dock.pos) > c.dock.radius)
                {
                    EndRun(p, r.leftStart ? "Breached outside the octagon — run over" : "Breach: left the start zone on the surface — run over");
                    return;
                }
            }
            else r.faceTimer = 0;
            // rotations = number of trash items in baskets (3.2.6)
            int nb = r.basketPts.Count;
            if (nb > 0)
            {
                double turns = JsMath.Floor(Math.Abs(r.rotAcc) / (2 * JsMath.PI));
                double rp = JsMath.Min(turns, nb) >= nb ? POINTS.octRotation * nb : 0;
                if (rp > r.rotBest) { double before = r.scores.octagon; r.rotBest = rp; OctagonScore(p); Emit(p, nb + " rotation" + (nb > 1 ? "s" : "") + " for " + nb + " item" + (nb > 1 ? "s" : "") + " in the baskets", r.scores.octagon - before, "score"); }
            }
        }

        void OctagonScore(Player p)
        {
            var r = p.run;
            double bp = 0;
            foreach (var kv in r.basketPts) bp = bp + kv.Value;
            r.scores.octagon = r.octSurface + r.octItems.Count * POINTS.octWithItem + r.faceBest + bp + r.rotBest;
        }

        // hydrophone: bearing (rad, relative to the vehicle heading) and distance to a pinger
        public PingerBearing GetPingerBearing(Player p, string id)
        {
            var pg = course.pingers.Find(x => x.id == id);
            var v = p.veh;
            double dx = pg.pos.x - v.p.x, dy = pg.pos.y - v.p.y;
            return new PingerBearing { rel = MathUtil.WrapPI(JsMath.Atan2(dy, dx) - v.Euler().z), dist = JsMath.Hypot(dx, dy, pg.pos.z - v.p.z), pinger = pg };
        }

        /// s left on the clock
        public double Remaining() => JsMath.Max(0, duration - time);
    }
}
