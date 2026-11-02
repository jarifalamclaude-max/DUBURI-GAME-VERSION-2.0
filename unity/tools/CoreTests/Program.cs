// Core tests: runs the C# game logic (Assets/LifeAsDuburi/Core) and compares it
// with the JavaScript original, value by value (tools/golden/make-golden.mjs
// writes the expected data; it is regenerated here when the JS is newer).
//   dotnet run --project tools/CoreTests [-- suite ...]
// Suites: math water thruster env alloc vehicle course stats matches long game alloc-free sysmath

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Duburi.Core;

namespace CoreTests
{
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CoreRandom.Seed(12345); // item ids only
            string tools = FindTools();
            G.GoldenDir = Path.Combine(tools, "golden", "out");
            if (!EnsureGolden(tools)) return 2;

            var only = new HashSet<string>(args);
            bool Want(string n) => only.Count == 0 || only.Contains(n);
            var suites = new List<Suite>();
            void RunSuite(string name, Action<Suite> fn, double relTol = 1e-11)
            {
                if (!Want(name)) return;
                var s = new Suite(name) { relTol = relTol };
                var sw = Stopwatch.StartNew();
                try { fn(s); }
                catch (Exception e) { s.Fail("threw " + e); }
                suites.Add(s);
                Console.WriteLine($"== {name,-9} {s.values,9} values, {s.identical,9} bit-identical, max |diff| {s.maxAbs:G3}{(s.maxAbs > 0 ? " at " + s.worst : "")}  ({sw.Elapsed.TotalSeconds:F1} s){(s.failures > 0 ? "  FAILED " + s.failures : "")}");
                foreach (var m in s.messages) Console.WriteLine("     " + m);
            }

            RunSuite("math", GoldenTests.MathCases);
            RunSuite("water", GoldenTests.WaterCases);
            RunSuite("thruster", GoldenTests.ThrusterCases);
            RunSuite("env", GoldenTests.EnvCases);
            RunSuite("alloc", GoldenTests.AllocCases);
            RunSuite("vehicle", GoldenTests.VehicleCases);
            RunSuite("course", GoldenTests.CourseCases);
            RunSuite("stats", GoldenTests.StatsCases);
            RunSuite("matches", s => GoldenTests.MatchCases(s, "matches"));
            RunSuite("long", s => GoldenTests.MatchCases(s, "matches-long"));
            if (Want("game")) Console.WriteLine("== game (tests/game.test.mjs)");
            RunSuite("game", GameTests.All);
            RunSuite("alloc-free", AllocationFree);
            if (Want("sysmath")) SystemMathNoise();

            int failed = 0;
            foreach (var s in suites) failed += s.failures;
            Console.WriteLine(failed == 0 ? "CORE TESTS PASSED" : "CORE TESTS FAILED (" + failed + ")");
            return failed == 0 ? 0 : 1;
        }

        static string FindTools()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "CoreTests", "CoreTests.csproj"))) d = d.Parent;
            if (d == null) throw new DirectoryNotFoundException("tools/ (with CoreTests/CoreTests.csproj) not found above " + AppContext.BaseDirectory);
            return d.FullName;
        }

        // regenerate the golden data when it is missing or older than the JS sources / the script
        static bool EnsureGolden(string tools)
        {
            string manifest = Path.Combine(G.GoldenDir, "manifest.json");
            string script = Path.Combine(tools, "golden", "make-golden.mjs");
            string src = Path.GetFullPath(Path.Combine(tools, "..", "..", "src"));
            DateTime made = File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest) : DateTime.MinValue;
            DateTime newest = File.GetLastWriteTimeUtc(script);
            if (Directory.Exists(src))
                foreach (var f in Directory.GetFiles(src, "*.js", SearchOption.AllDirectories))
                    if (File.GetLastWriteTimeUtc(f) > newest) newest = File.GetLastWriteTimeUtc(f);
            if (made >= newest) return true;
            Console.WriteLine("== golden data out of date: node " + script);
            try
            {
                var psi = new ProcessStartInfo("node", "\"" + script + "\"") { UseShellExecute = false };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode == 0) return true;
                    Console.WriteLine("make-golden.mjs failed with exit code " + p.ExitCode);
                }
            }
            catch (Exception e) { Console.WriteLine("cannot run node (needed for the golden data): " + e.Message); }
            return false;
        }

        // Game.Step (environment, 4 vehicles, contacts, items, judging) must not allocate:
        // WebGL's GC is non-incremental, garbage per frame shows up as hitches.
        static void AllocationFree(Suite s)
        {
            foreach (var sc in Scenarios.SCENARIO_ORDER)
            {
                var players = new List<PlayerConfig>();
                foreach (var b in new[] { "duburi", "manta", "kraken", "barracuda" }) players.Add(new PlayerConfig { bot = b });
                var g = new Game(new GameOptions { scenario = sc, seed = 9, coinFlip = false, players = players });
                foreach (var p in g.players) { p.veh.depthTarget = 1.5; p.veh.input.surge = 0.3; p.veh.input.yaw = 0.2; }
                g.players[1].veh.input.roll = 1;
                for (int i = 0; i < 1500; i++) g.Step(Vehicle.DT); // warm-up (JIT tiers, caches, list capacities)
                var windows = new List<long>();
                for (int w = 0; w < 3; w++)
                {
                    int events = g.events.Count;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < 300; i++) g.Step(Vehicle.DT);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    windows.Add(bytes);
                    s.Check(g.events.Count != events || bytes == 0, sc + ": Game.Step allocated " + bytes + " bytes in 300 steps");
                }
                Console.WriteLine($"     {sc}: bytes allocated by 3 x 300 Game.Step (4 vehicles): {string.Join(", ", windows)}");
            }
        }

        // Informational: the same long match with System.Math (the platform's libm) instead
        // of the V8-identical Fdlibm, to show what the last bit of sin/cos/exp/pow does.
        static void SystemMathNoise()
        {
            var j = G.Load("matches-long");
            foreach (var m in j.EnumerateArray())
            {
                if (m.GetProperty("name").GetString() != "pool-solo-duburi-3") continue;
                var s = new Suite("sysmath");
                JsMath.UseSystemMath = true;
                try { GoldenTests.RunMatch(m, s, "sysmath"); }
                finally { JsMath.UseSystemMath = false; }
                Console.WriteLine($"== sysmath  (info) pool-solo-duburi-3 with System.Math: {s.values - s.identical} of {s.values} values differ, max |diff| {s.maxAbs:G3}; with Fdlibm: bit-identical (suite 'long')");
            }
        }
    }
}
