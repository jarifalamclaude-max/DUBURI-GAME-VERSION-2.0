// Port of src/sim/stats.js.
// Numbers for the garage screen, worked out from each design's physics (not
// typed in): top speed where full thrust meets the hull drag, turn rate,
// controllable axes from the thruster allocation, net buoyancy per water.

using System.Collections.Generic;

namespace Duburi.Core
{
    /// botStats() result
    public sealed class BotStatsResult
    {
        public double surge, sway, heave;  // m/s top speed per axis
        public double yawRate, rollRate;   // deg/s (rollRate 0 without roll authority)
        public double righting;            // N m per rad of heel
        public double thrust;              // N, full surge force
        public double mass;                // kg
        public int thrusters;
        public List<string> dof;           // controllable axes: Surge, Sway, Heave, Roll, Pitch, Yaw
        public OrderedMap<string, double> buoyancy; // scenario id -> net buoyancy (N), SCENARIOS order
    }

    public static class Stats
    {
        // steady speed where F = lin v + quad v^2
        static double Terminal(double F, double lin, double quad) => quad > 1e-9 ? (-lin + JsMath.Sqrt(lin * lin + 4 * quad * F)) / (2 * quad) : F / JsMath.Max(lin, 1e-9);

        static readonly Dictionary<string, BotStatsResult> cache = new Dictionary<string, BotStatsResult>();
        static readonly string[] axes = { "Surge", "Sway", "Heave", "Roll", "Pitch", "Yaw" };

        /// botStats(design), cached per design id
        public static BotStatsResult BotStats(BotDesign design)
        {
            if (cache.TryGetValue(design.id, out var c)) return c;
            var env = new Environment(Scenarios.EnvironmentConfig("pool"), new RNG(1));
            var v = new Vehicle(design, env);
            var d = design;
            var dof = new List<string>();
            for (int k = 0; k < axes.Length; k++) if (v.wmax[k] > (k < 3 ? 2 : 0.3)) dof.Add(axes[k]);
            var buoyancy = new OrderedMap<string, double>();
            foreach (var s in Scenarios.SCENARIOS.Values)
                buoyancy[s.id] = Water.WaterDensity(s.env.temperature, s.env.salinity) * env.g * v.volume - d.mass * env.g;
            var st = new BotStatsResult
            {
                surge = Terminal(v.wmax[0], d.dampLin[0], d.dampQuad[0]),
                sway = Terminal(v.wmax[1], d.dampLin[1], d.dampQuad[1]),
                heave = Terminal(v.wmax[2], d.dampLin[2], d.dampQuad[2]),
                yawRate = (Terminal(v.wmax[5], d.dampLin[5], d.dampQuad[5]) * 180) / JsMath.PI,
                rollRate = v.wmax[3] > 0.3 ? (Terminal(v.wmax[3], d.dampLin[3], d.dampQuad[3]) * 180) / JsMath.PI : 0,
                righting = d.zB * d.mass * env.g, // N m per rad of heel
                thrust = v.wmax[0],
                mass = d.mass,
                thrusters = d.thrusters.Length,
                dof = dof,
                buoyancy = buoyancy,
            };
            cache[design.id] = st;
            return st;
        }
    }
}
