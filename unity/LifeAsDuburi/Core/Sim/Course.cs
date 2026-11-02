// Port of src/sim/course.js.
// The competition course, laid out in the world NED frame (x north, y east,
// z down, origin at the still surface in the pool centre). Dimensions follow
// the AUV competition team handbook (section 3.2); positions are chosen so no
// three elements are on a line and the path markers point at the next task.
// Floor-mounted elements follow the water depth; the gate and the octagon
// float at the surface.
// Horizontal positions / directions are V2 (north, east); 3-D points V3 (NED); m and rad.

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    /// JS ARENA: the boundary of the play area (m); depth = floor depth
    public sealed class Arena
    {
        public double length = 50, width = 25, depth = 7;
        public Arena Clone() => (Arena)MemberwiseClone();
    }

    public sealed class CourseRandom
    {
        public double sharkSide;     // gate side with the reef shark (-1 = left / west, +1 = right / east)
        public bool boardSharkTop;   // torpedo board version
        public bool binSharkNear;    // which bin half is the shark
    }

    public sealed class Dock { public V2 pos; public double radius; }

    public sealed class Gate
    {
        public V2 pos;
        public double heading;
        public double width;
        public V2 legDepth;       // [top, bottom] depth of the legs
        public double topDepth, barH;
        public V2 dividerDepth;   // [top, bottom] of the red divider
        public double sharkSide;
        public V2 dir, right;
    }

    public sealed class PathMarker { public V2 pos; public double heading; }

    public sealed class SlalomPipe { public V2 p; public string color; } // "white" | "red"
    public sealed class SlalomSet { public V2 pos; public List<SlalomPipe> pipes = new List<SlalomPipe>(); }

    public sealed class Slalom
    {
        public V2 pos;
        public List<SlalomSet> sets = new List<SlalomSet>();
        public double heading;
        public V2 dir, right;
        public V2 band; // [top, bottom] depth of the pipes
    }

    public sealed class Bin
    {
        public V2 pos;
        public V2 size;   // opening (along, across)
        public V2 plate;  // top plate
        public double height;
        public bool sharkNear;
        public double heading;
        public V2 dir, right;
        public string[] halves; // [near half, far half] animals
    }

    public sealed class BoardOpening
    {
        public double u, v, r;   // board coordinates (m), radius / half side
        public string shape;     // "circle" | "square"
        public string animal;    // "shark" | "sawfish"
        public string label;     // "upper" | "lower"
    }

    public sealed class Board
    {
        public V2 pos;
        public double depth, size;
        public bool sharkTop;
        public double heading;   // normal points at the approach side
        public V2 dir, right;
        public List<BoardOpening> openings = new List<BoardOpening>();
        public double farDistance;
    }

    public sealed class OctImage { public string animal; public double angle; }
    public sealed class Basket { public string item; public V2 c; public V2 h; public double depthTop; }
    public sealed class Trash { public string kind; public V3 pos; }

    public sealed class Octagon
    {
        public V2 pos;
        public double radius, tableTop, tableHalf, heading;
        public List<OctImage> images = new List<OctImage>();
        public List<Basket> baskets = new List<Basket>();
        public List<Trash> trash = new List<Trash>();
    }

    /// one collision obstacle; which fields are used depends on type
    public sealed class Obstacle
    {
        public string type;     // "cyl" | "box" | "board" | "ring"
        public string tag;      // gateLeg, gateBar, gateDivider, slalomRed, slalomWhite, bin, board, boardPole, table, octagon
        public V2 p;            // cyl, ring: axis position
        public double r;        // cyl: radius, ring: pipe radius
        public double z0, z1;   // cyl: top / bottom depth
        public V3 c, h;         // box: centre and half sizes
        public double yaw;      // box
        public Board board;     // board
        public double R;        // ring: radius
        public double z;        // ring: depth
    }

    public sealed class Pinger { public string id; public V3 pos; public double freq; public double period; } // kHz, s
    public sealed class CourseTask { public string id, name; public V3 pos; }

    /// boardLocal() result: u right as seen from the front, v up, n toward the viewer (m)
    public struct BoardCoords { public double u, v, n; }

    public sealed class Course
    {
        public static readonly Arena ARENA = new Arena();

        public double floor;
        public List<Obstacle> obstacles = new List<Obstacle>();
        public CourseRandom random;
        public Dock dock;
        public Gate gate;
        public List<PathMarker> paths = new List<PathMarker>();
        public Slalom slalom;
        public Bin bin;
        public Board board;
        public Octagon octagon;
        public List<Pinger> pingers;
        public List<CourseTask> tasks;

        static V2 Rot2(double h) => new V2(JsMath.Cos(h), JsMath.Sin(h));
        static V2 Add2(V2 p, V2 d, double s) => new V2(p.x + d.x * s, p.y + d.y * s);
        static V2 Perp(V2 d) => new V2(-d.y, d.x); // 90 deg to the right of d (NED, looking down)

        /// buildCourse(rng = Math.random, depth = ARENA.depth)
        public static Course Build(Func<double> rng = null, double depth = double.NaN)
        {
            if (rng == null) rng = CoreRandom.Next;
            if (double.IsNaN(depth)) depth = ARENA.depth;
            double floor = depth;
            var course = new Course { floor = floor };

            // ---- randomised per run (like the judges' set-up) ----
            double sharkSide = rng() < 0.5 ? -1 : 1; // gate side (-1 = left / west, +1 = right / east) with the reef shark
            bool boardSharkTop = rng() < 0.5; // torpedo board version
            bool binSharkNear = rng() < 0.5; // which bin half is the shark
            course.random = new CourseRandom { sharkSide = sharkSide, boardSharkTop = boardSharkTop, binSharkNear = binSharkNear };

            // ---- start dock ----
            course.dock = new Dock { pos = new V2(-21, -3), radius = 3.0 };

            // ---- Task 1: gate (3 m x 1.5 m, floating just below the surface) ----
            var gate = new Gate { pos = new V2(-15.5, -1.2), heading = 0, width = 3.048, legDepth = new V2(0.45, 1.95), topDepth = 0.45, barH = 0.076, dividerDepth = new V2(0.45, 1.06), sharkSide = sharkSide };
            gate.dir = Rot2(gate.heading);
            gate.right = Perp(gate.dir);
            course.gate = gate;
            foreach (double s in new[] { -1.0, 1.0 })
            {
                V2 p = Add2(gate.pos, gate.right, (s * gate.width) / 2);
                course.obstacles.Add(new Obstacle { type = "cyl", tag = "gateLeg", p = p, r = 0.06, z0 = gate.legDepth.x, z1 = gate.legDepth.y });
            }
            course.obstacles.Add(new Obstacle { type = "box", tag = "gateBar", c = new V3(gate.pos.x, gate.pos.y, gate.topDepth - 0.04), h = new V3(0.05, gate.width / 2 + 0.06, 0.045), yaw = gate.heading });
            course.obstacles.Add(new Obstacle { type = "box", tag = "gateDivider", c = new V3(gate.pos.x, gate.pos.y, (gate.dividerDepth.x + gate.dividerDepth.y) / 2), h = new V3(0.012, 0.026, (gate.dividerDepth.y - gate.dividerDepth.x) / 2), yaw = gate.heading });

            // ---- path 1 (orange, on the floor) right after the gate, pointing to the slalom ----
            var slalom = new Slalom { pos = new V2(-7.4, 2.6) };
            V2 p1 = Add2(gate.pos, gate.dir, 2.4);
            double toSlalom = JsMath.Atan2(slalom.pos.y - p1.y, slalom.pos.x - p1.x);
            course.paths.Add(new PathMarker { pos = p1, heading = toSlalom });

            // ---- Task 2: slalom, 3 sets of white / red / white pipes ----
            slalom.heading = toSlalom;
            slalom.dir = Rot2(toSlalom);
            slalom.right = Perp(slalom.dir);
            slalom.band = new V2(floor - 1.35, floor - 0.45); // pipes 0.9 m tall, moored above the floor
            for (int k = -1; k <= 1; k++)
            {
                V2 c = Add2(Add2(slalom.pos, slalom.dir, k * 2.6), slalom.right, k == 0 ? 0.35 : -0.2 * k);
                var set = new SlalomSet { pos = c };
                foreach (var (lat, color) in new[] { (-1.524, "white"), (0.0, "red"), (1.524, "white") })
                {
                    V2 p = Add2(c, slalom.right, lat);
                    set.pipes.Add(new SlalomPipe { p = p, color = color });
                    course.obstacles.Add(new Obstacle { type = "cyl", tag = color == "red" ? "slalomRed" : "slalomWhite", p = p, r = 0.045, z0 = slalom.band.x, z1 = slalom.band.y });
                }
                slalom.sets.Add(set);
            }
            course.slalom = slalom;

            // ---- path 2 after the slalom, pointing at the bin ----
            // 36 x 24 in top plate with a 24 x 12 in, 6 in deep opening (handbook figure)
            var bin = new Bin { pos = new V2(0.6, 7.2), size = new V2(0.61, 0.305), plate = new V2(0.914, 0.61), height = 0.152, sharkNear = binSharkNear };
            V2 p2 = Add2(slalom.sets[2].pos, slalom.dir, 2.2);
            double toBin = JsMath.Atan2(bin.pos.y - p2.y, bin.pos.x - p2.x);
            course.paths.Add(new PathMarker { pos = p2, heading = toBin });

            // ---- Task 3: bin on the floor, long axis along the approach ----
            bin.heading = toBin;
            bin.dir = Rot2(toBin);
            bin.right = Perp(bin.dir);
            // the shark half is the near half (toward the approach) or the far half
            bin.halves = binSharkNear ? new[] { "shark", "sawfish" } : new[] { "sawfish", "shark" };
            course.bin = bin;
            course.obstacles.Add(new Obstacle { type = "box", tag = "bin", c = new V3(bin.pos.x, bin.pos.y, floor - bin.height / 2), h = new V3(bin.plate.x / 2, bin.plate.y / 2, bin.height / 2), yaw = bin.heading });

            // ---- Task 4: torpedo board, 0.6 m square, facing back toward the bin ----
            var board = new Board { pos = new V2(6.8, -4.2), depth = floor - 1.6, size = 0.61, sharkTop = boardSharkTop }; // on a stand 1.3 m off the floor
            board.heading = JsMath.Atan2(bin.pos.y - board.pos.y, bin.pos.x - board.pos.x); // normal points at the approach side
            board.dir = Rot2(board.heading);
            board.right = Perp(board.dir);
            // openings in board coordinates (u to the viewer's right, v up), as seen from the front
            // two 4 in (102 mm) openings with a 0.5 in red border, one beside each animal
            board.openings.Add(new BoardOpening { u = -0.12, v = 0.07, r = 0.051, shape = "circle", animal = boardSharkTop ? "shark" : "sawfish", label = "upper" });
            board.openings.Add(new BoardOpening { u = 0.14, v = -0.07, r = 0.051, shape = "square", animal = boardSharkTop ? "sawfish" : "shark", label = "lower" });
            board.farDistance = 2.0; // horizontal bars on the floor mark "far"
            course.board = board;
            course.obstacles.Add(new Obstacle { type = "board", tag = "board", board = board });
            course.obstacles.Add(new Obstacle { type = "cyl", tag = "boardPole", p = Add2(board.pos, board.dir, -0.08), r = 0.04, z0 = board.depth + board.size / 2, z1 = floor });

            // ---- Task 5: octagon (2.7 m) floating on the surface, table + trash below ----
            var oct = new Octagon { pos = new V2(13.6, 3.4), radius = 1.37, tableTop = floor - 0.75, tableHalf = 0.305 };
            oct.heading = 0;
            // images hang inside facing inward on two opposite sides
            oct.images.Add(new OctImage { animal = "shark", angle = JsMath.PI / 2 }); // on the east side, faced from inside looking east
            oct.images.Add(new OctImage { animal = "sawfish", angle = -JsMath.PI / 2 });
            // baskets along two opposite table edges: north edge for the cup, south edge for the spoon
            oct.baskets.Add(new Basket { item = "cup", c = new V2(oct.pos.x + oct.tableHalf + 0.13, oct.pos.y), h = new V2(0.12, 0.3), depthTop = oct.tableTop - 0.1 });
            oct.baskets.Add(new Basket { item = "spoon", c = new V2(oct.pos.x - oct.tableHalf - 0.13, oct.pos.y), h = new V2(0.12, 0.3), depthTop = oct.tableTop - 0.1 });
            oct.trash.Add(new Trash { kind = "cup", pos = new V3(oct.pos.x + 0.12, oct.pos.y - 0.14, oct.tableTop - 0.05) });
            oct.trash.Add(new Trash { kind = "spoon", pos = new V3(oct.pos.x - 0.12, oct.pos.y + 0.14, oct.tableTop - 0.015) });
            course.octagon = oct;
            course.obstacles.Add(new Obstacle { type = "box", tag = "table", c = new V3(oct.pos.x, oct.pos.y, oct.tableTop + 0.02), h = new V3(oct.tableHalf, oct.tableHalf, 0.02), yaw = 0 });
            foreach (double sx in new[] { -1.0, 1.0 })
                foreach (double sy in new[] { -1.0, 1.0 })
                    course.obstacles.Add(new Obstacle { type = "cyl", tag = "table", p = new V2(oct.pos.x + sx * 0.28, oct.pos.y + sy * 0.28), r = 0.025, z0 = oct.tableTop, z1 = floor });
            course.obstacles.Add(new Obstacle { type = "ring", tag = "octagon", p = oct.pos, R = oct.radius, r = 0.05, z = 0.0 });

            // ---- pingers: octagon (floor centre) and the torpedo task (community-poll task) ----
            course.pingers = new List<Pinger>
            {
                new Pinger { id = "octagon", pos = new V3(oct.pos.x, oct.pos.y, floor - 0.05), freq = 30, period = 2.0 },
                new Pinger { id = "torpedo", pos = new V3(board.pos.x, board.pos.y, floor - 0.05), freq = 25, period = 1.5 },
            };
            // task buffers (3 m spheres around each task, handbook 4.2)
            course.tasks = new List<CourseTask>
            {
                new CourseTask { id = "gate", name = "Collecting Data (Gate)", pos = new V3(gate.pos.x, gate.pos.y, 1.2) },
                new CourseTask { id = "slalom", name = "Navigate the Channel", pos = new V3(slalom.pos.x, slalom.pos.y, floor - 0.9) },
                new CourseTask { id = "bin", name = "Drop a BRUVS (Bin)", pos = new V3(bin.pos.x, bin.pos.y, floor - 0.3) },
                new CourseTask { id = "torpedo", name = "Tagging (Torpedoes)", pos = new V3(board.pos.x, board.pos.y, board.depth) },
                new CourseTask { id = "octagon", name = "Ocean Cleanup (Octagon)", pos = new V3(oct.pos.x, oct.pos.y, 0.5) },
                new CourseTask { id = "home", name = "Return Home", pos = new V3(gate.pos.x, gate.pos.y, 1.2) },
            };
            return course;
        }

        // world point -> board coordinates (u right as seen from the front, v up, n toward the viewer)
        public static BoardCoords BoardLocal(Board board, V3 p)
        {
            double dx = p.x - board.pos.x, dy = p.y - board.pos.y;
            double n = dx * board.dir.x + dy * board.dir.y;
            // seen from the front (looking along -dir) the viewer's right is -right
            double u = -(dx * board.right.x + dy * board.right.y);
            double v = board.depth - p.z;
            return new BoardCoords { u = u, v = v, n = n };
        }
    }
}
