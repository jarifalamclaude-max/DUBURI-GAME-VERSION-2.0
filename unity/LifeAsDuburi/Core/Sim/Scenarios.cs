// Port of src/sim/scenarios.js.
// Water bodies the course can be set in. Every scenario is a real
// configuration of the simulator's Environment (Physics/Environment.cs):
// temperature and salinity go through EOS-80 for the density, so the same
// vehicle is more buoyant in cold salt water than in the pool. Current,
// turbulence and waves feed the vehicle dynamics; the rest is look.

namespace Duburi.Core
{
    /// scenario.look: colours are 0xRRGGBB ints (Colors.Rgb in the Unity layer)
    public sealed class ScenarioLook
    {
        public int water, fog;
        public double fogDensity;   // three.js FogExp2 density (1/m)
        public int sky;
        public string floor;        // "tiles" | "sand" | "gravel"
        public double light;        // light intensity factor
        public int sun, surface;
        public double particles;    // amount of floating particles 0..1
        public string life;         // "none" | "reef" | "arctic"
        public bool ice;            // broken ice sheet on the surface
    }

    /// scenario.pool (merged over DEFAULT_POOL by environmentConfig)
    public sealed class ScenarioPool
    {
        public double length, width;
        public string style; // "tiles" | "natural"
    }

    /// JS SCENARIOS entry
    public sealed class Scenario
    {
        public string id, name, place, blurb;
        /// environment settings (temperature, salinity, current, turbulence, waves, visibility);
        /// the other fields keep the Environment defaults
        public EnvConfig env;
        public double depth; // m, the water's own depth
        public ScenarioPool pool;
        public ScenarioLook look;
    }

    public static class Scenarios
    {
        static EnvConfig Env(double temperature, double salinity, V3 current, double sigma, double tau, double amplitude, double period, double direction, double visibility) => new EnvConfig
        {
            temperature = temperature,
            salinity = salinity,
            current = current,
            turbulence = new TurbulenceConfig { sigma = sigma, tau = tau },
            waves = new WavesConfig { amplitude = amplitude, period = period, direction = direction },
            visibility = visibility,
        };

        public static readonly OrderedMap<string, Scenario> SCENARIOS = new OrderedMap<string, Scenario>
        {
            ["pool"] = new Scenario
            {
                id = "pool",
                name = "Swimming Pool",
                place = "Indoor competition pool (Woollett-style)",
                blurb = "Clear, warm fresh water. Tiled floor, lane lines, no current. The place every team tunes first.",
                env = Env(26, 0, new V3(0, 0, 0), 0, 2, 0.01, 2.2, 30, 40),
                depth = 7, // m: a deep competition / diving pool
                pool = new ScenarioPool { length = 50, width = 25, style = "tiles" },
                look = new ScenarioLook { water = 0x2f9fc4, fog = 0x3aa6c9, fogDensity = 0.022, sky = 0x9fd8ec, floor = "tiles", light = 1.25, sun = 0xfff6e0, surface = 0x7fd6ee, particles = 0.2, life = "none", ice = false },
            },
            ["pacific"] = new Scenario
            {
                id = "pacific",
                name = "Pacific Ocean",
                place = "Tropical reef lagoon, 22 °C",
                blurb = "Bright blue open sea over white sand and coral. Gentle swell and a slow tidal set to the east.",
                env = Env(22, 34.5, new V3(0.03, 0.08, 0), 0.03, 3, 0.12, 4.5, 60, 28),
                depth = 12,
                pool = new ScenarioPool { length = 50, width = 25, style = "natural" },
                look = new ScenarioLook { water = 0x1677b8, fog = 0x1a7fbf, fogDensity = 0.034, sky = 0x7cc4f0, floor = "sand", light = 1.1, sun = 0xfff2d0, surface = 0x58b8e8, particles = 0.5, life = "reef", ice = false },
            },
            ["arctic"] = new Scenario
            {
                id = "arctic",
                name = "Arctic Ocean",
                place = "Under the sea ice, −1.5 °C",
                blurb = "Freezing, dense water under a broken ice sheet. Dim light, marine snow, very calm.",
                env = Env(-1.5, 34, new V3(0.02, -0.03, 0), 0.015, 4, 0.0, 3, 0, 18),
                depth = 15,
                pool = new ScenarioPool { length = 50, width = 25, style = "natural" },
                look = new ScenarioLook { water = 0x0f4a5c, fog = 0x13505f, fogDensity = 0.05, sky = 0xd8ecf4, floor = "gravel", light = 0.7, sun = 0xdfefff, surface = 0x9fd2e0, particles = 1.0, life = "arctic", ice = true },
            },
        };

        public static readonly string[] SCENARIO_ORDER = { "pool", "pacific", "arctic" };

        public static readonly double[] DEPTH_CHOICES = { 0, 5, 8, 12, 16, 20 }; // 0 = the scenario's own depth

        /// SCENARIOS[id] ?? SCENARIOS.pool
        public static Scenario Get(string id) => id != null && SCENARIOS.TryGetValue(id, out var s) ? s : SCENARIOS["pool"];

        public static double WaterDepth(string id, double overrideDepth = 0) => overrideDepth > 0 ? overrideDepth : Get(id).depth;

        /// { ...s.env, pool: { ...s.pool, depth, floor: 'flat', freeboard: 0.4 } } (a fresh copy)
        public static EnvConfig EnvironmentConfig(string id, double depth = 0)
        {
            var s = Get(id);
            var c = s.env.Clone();
            c.pool = new PoolConfig { length = s.pool.length, width = s.pool.width, style = s.pool.style, depth = WaterDepth(id, depth), floor = "flat", freeboard = 0.4 };
            return c;
        }
    }
}
