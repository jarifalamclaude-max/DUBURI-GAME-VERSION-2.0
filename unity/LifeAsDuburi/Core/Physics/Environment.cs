// Port of src/physics/environment.js.
// Water body: pool/tank geometry (any size, flat or sloped floor), water
// properties, hydrostatic pressure, current + turbulence, surface waves.
// World frame is NED with the origin at the still-water surface, pool centre.
//
// Note: the name clashes with System.Environment when a file has `using System;`
// and `using Duburi.Core;` at the top: write Duburi.Core.Environment there.

using System.Collections.Generic;

namespace Duburi.Core
{
    /// JS DEFAULT_POOL / cfg.pool. A new PoolConfig holds the defaults; the JS spread
    /// { ...DEFAULT_POOL, ...pool } is "set only the fields you change".
    public sealed class PoolConfig
    {
        public double length = 25;      // m, along north (x)
        public double width = 12;       // m, along east (y)
        public double depth = 4;        // m, deepest point (the whole floor when flat)
        public string floor = "flat";   // "flat" | "sloped"
        public double shallowDepth = 1.2; // m, shallow-end depth (sloped floor)
        public string shallowEnd = "south"; // "south" | "north"
        public double slopeStart = 8;   // m from the shallow-end wall where the floor starts to drop
        public double slopeEnd = 14;    // m from the shallow-end wall where it reaches full depth
        public double freeboard = 0.3;  // m of wall above the water (visual)
        public string style = "tiles";  // "tiles" (pool) | "natural" (lake / sea bottom, no visible walls)

        public PoolConfig Clone() => (PoolConfig)MemberwiseClone();
    }

    public sealed class TurbulenceConfig
    {
        public double sigma = 0; // m/s
        public double tau = 2.0; // s
        public TurbulenceConfig Clone() => (TurbulenceConfig)MemberwiseClone();
    }

    public sealed class WavesConfig
    {
        public double amplitude = 0; // m
        public double period = 2.0;  // s
        public double direction = 0; // deg (from north, heading of propagation)
        public WavesConfig Clone() => (WavesConfig)MemberwiseClone();
    }

    public sealed class ContactConfig
    {
        public double stiffness = 30000, damping = 900, friction = 0.5;
        public ContactConfig Clone() => (ContactConfig)MemberwiseClone();
    }

    /// JS DEFAULT_ENVIRONMENT / an environment config. A new EnvConfig holds the defaults.
    public sealed class EnvConfig
    {
        public double temperature = 26;     // degC (indoor pool)
        public double salinity = 0;         // g/kg (fresh)
        public double densityOverride = 0;  // kg/m^3, 0 = compute from temperature & salinity
        public double airPressure = 1013.25; // hPa at the water surface
        public PoolConfig pool = new PoolConfig();
        public V3 current = new V3(0, 0, 0); // m/s NED
        public TurbulenceConfig turbulence = new TurbulenceConfig(); // m/s, s
        public WavesConfig waves = new WavesConfig(); // m, s, deg (from north, heading of propagation)
        public double visibility = 30;      // m, water clarity (visual only)
        public double surfaceDrag = 0.6;    // extra translational drag factor when breaking the surface
        public ContactConfig contact = new ContactConfig();
        public double gravity = MathUtil.GRAVITY_MSS;

        /// deep copy ({ ...cfg, pool: { ...cfg.pool }, ... })
        public EnvConfig Clone()
        {
            var c = (EnvConfig)MemberwiseClone();
            c.pool = (pool ?? new PoolConfig()).Clone();
            c.turbulence = (turbulence ?? new TurbulenceConfig()).Clone();
            c.waves = (waves ?? new WavesConfig()).Clone();
            c.contact = (contact ?? new ContactConfig()).Clone();
            return c;
        }
    }

    /// Floor depth along the pool (JS floorProfile result).
    public sealed class FloorProfile
    {
        public double L, W;
        public bool sloped;
        public double minDepth, maxDepth, shallowDepth, deepDepth;
        public double[] breaks = new double[0];
        public double a, b;
        public bool north;
        double k; // (deep - shallow) / (b - a)

        public double DepthAt(double x)
        {
            if (!sloped) return deepDepth;
            double ss = S(x);
            return ss <= a ? shallowDepth : ss >= b ? deepDepth : shallowDepth + k * (ss - a);
        }
        // d(depth)/d(north)
        public double SlopeAt(double x)
        {
            if (!sloped) return 0;
            double ss = S(x);
            return ss > a && ss < b ? (north ? -k : k) : 0;
        }
        // distance from the shallow-end wall
        double S(double x) => north ? L / 2 - x : x + L / 2;
        double XOf(double ss) => north ? L / 2 - ss : ss - L / 2;

        static double Clamp(double v, double a, double b) => JsMath.Min(b, JsMath.Max(a, v));

        // Floor depth along the pool. A sloped floor is flat at the shallow end, drops
        // linearly between slopeStart and slopeEnd (metres from the shallow-end wall)
        // and is flat again at the deep end. x is the north coordinate.
        public static FloorProfile Of(PoolConfig pool = null)
        {
            var D = new PoolConfig();
            if (pool == null) pool = D;
            double L = JsMath.Max(0.1, Js.Or(pool.length, D.length));
            double W = JsMath.Max(0.1, Js.Or(pool.width, D.width));
            double Dd = JsMath.Max(0.05, Js.Or(pool.depth, D.depth));
            var flat = new FloorProfile { L = L, W = W, sloped = false, minDepth = Dd, maxDepth = Dd, shallowDepth = Dd, deepDepth = Dd, a = 0, b = L, north = false };
            if (pool.floor != "sloped") return flat;
            double Ds = Clamp(Js.Or(pool.shallowDepth, Dd), 0.05, Dd);
            if (Dd - Ds < 1e-6) return flat;
            bool north = pool.shallowEnd == "north";
            double a = Clamp(Js.IsFinite(pool.slopeStart) ? pool.slopeStart : 0, 0, L);
            double b = Clamp(Js.IsFinite(pool.slopeEnd) ? pool.slopeEnd : L, 0, L);
            if (b < a) { double t = a; a = b; b = t; }
            // steeper than 60° is a wall, not a slope: widen it around its middle
            double minRun = JsMath.Min(L, (Dd - Ds) * JsMath.Tan(JsMath.PI / 6));
            if (b - a < minRun)
            {
                double mid = Clamp((a + b) / 2, minRun / 2, L - minRun / 2);
                a = mid - minRun / 2; b = mid + minRun / 2;
            }
            var f = new FloorProfile { L = L, W = W, sloped = true, minDepth = Ds, maxDepth = Dd, shallowDepth = Ds, deepDepth = Dd, a = a, b = b, north = north };
            f.k = (Dd - Ds) / (b - a);
            var br = new List<double> { f.XOf(a), f.XOf(b) };
            Js.StableSort(br, (p, q) => p - q);
            f.breaks = br.ToArray();
            return f;
        }
    }

    /// waveProfile() result: wave number and the depth profiles of the horizontal
    /// velocity (fu), vertical velocity (fw) and dynamic pressure (fp)
    public struct WaveProfile
    {
        public double k, fu, fw, fp;
    }

    public sealed class Environment
    {
        public static PoolConfig DEFAULT_POOL => new PoolConfig();
        public static EnvConfig DEFAULT_ENVIRONMENT => new EnvConfig();

        public RNG rng;
        public GaussMarkov[] gm;
        public EnvConfig cfg;
        public double rho;   // surface density [kg/m^3]
        public double nu;    // kinematic viscosity [m^2/s]
        public double g;     // gravity [m/s^2]
        public double pAtm;  // air pressure at the surface [Pa]
        public FloorProfile floor;
        public double waveOmega, waveK;
        public V2 waveDir;   // [cos, sin] of the wave heading (north, east)
        public Dictionary<long, double> kCache;
        public double salinityPSU, T68;
        public double[] pTable;
        public double pDz;
        public int pN;
        public double time;
        public V3 gust, gustRaw, gustAccel;

        static double Clamp(double v, double a, double b) => JsMath.Min(b, JsMath.Max(a, v));

        public Environment(EnvConfig cfg = null, RNG rng = null)
        {
            this.rng = rng;
            gm = new[] { new GaussMarkov(rng), new GaussMarkov(rng), new GaussMarkov(rng) };
            Configure(cfg ?? new EnvConfig());
            time = 0;
            gust = new V3(0, 0, 0);
            gustRaw = new V3(0, 0, 0);
            gustAccel = new V3(0, 0, 0);
        }

        public void Configure(EnvConfig cfg)
        {
            this.cfg = (cfg ?? new EnvConfig()).Clone();
            rho = this.cfg.densityOverride > 0 ? this.cfg.densityOverride : Water.WaterDensity(this.cfg.temperature, this.cfg.salinity);
            nu = Water.KinematicViscosity(this.cfg.temperature, this.cfg.salinity);
            g = this.cfg.gravity;
            pAtm = (Js.IsFinite(this.cfg.airPressure) && this.cfg.airPressure > 0 ? this.cfg.airPressure : 1013.25) * 100; // Pa
            floor = FloorProfile.Of(this.cfg.pool);
            var t = this.cfg.turbulence;
            for (int i = 0; i < 3; i++) { gm[i].sigma = t.sigma * (i == 2 ? 0.5 : 1); gm[i].tau = t.tau; }
            var w = this.cfg.waves;
            waveOmega = w.period > 0 ? (2 * JsMath.PI) / w.period : 0;
            // finite-depth linear dispersion omega^2 = g k tanh(k h), solved per floor depth
            kCache = new Dictionary<long, double>();
            waveK = WaveNumber(floor.maxDepth);
            double b = (w.direction * JsMath.PI) / 180;
            waveDir = new V2(JsMath.Cos(b), JsMath.Sin(b));
            BuildPressureTable();
        }

        // wave number for water depth h: Newton on f(k) = g k tanh(k h) - omega^2
        public double WaveNumber(double h)
        {
            double w = waveOmega, g = this.g;
            if (!(w > 0)) return 0;
            long key = (long)JsMath.Round(JsMath.Max(0.01, h) * 200);
            if (kCache != null && kCache.TryGetValue(key, out double c)) return c;
            double hh = key / 200.0;
            double k = (w * w) / g / JsMath.Sqrt(JsMath.Tanh((w * w * hh) / g)); // Eckart/Fenton start
            for (int i = 0; i < 20; i++)
            {
                double th = JsMath.Tanh(k * hh);
                double f = g * k * th - w * w, df = g * th + g * k * hh * (1 - th * th);
                double dk = f / df;
                k -= dk;
                if (System.Math.Abs(dk) < 1e-12 * k) break;
            }
            if (kCache != null) kCache[key] = k;
            return k;
        }

        // Linear-wave depth profiles at depth z (>= 0, down) over a floor at h:
        // horizontal velocity cosh(k(h-z))/sinh(kh), vertical sinh(k(h-z))/sinh(kh),
        // dynamic pressure cosh(k(h-z))/cosh(kh); written with exponentials so deep water does not overflow.
        public WaveProfile WaveProfile(double x, double z)
        {
            double h = JsMath.Max(0.02, floor.DepthAt(x));
            double k = WaveNumber(h);
            double zz = Clamp(z, 0, h);
            double e1 = JsMath.Exp(-k * zz), e2 = JsMath.Exp(-k * (2 * h - zz)), e3 = JsMath.Exp(-2 * k * h);
            return new WaveProfile { k = k, fu = (e1 + e2) / (1 - e3 + 1e-300), fw = (e1 - e2) / (1 - e3 + 1e-300), fp = (e1 + e2) / (1 + e3) };
        }

        public double WavePhase(double x, double y, double k, double t) => k * (x * waveDir.x + y * waveDir.y) - waveOmega * t;

        // ---- geometry ----
        public double FloorDepth(double x) => floor.DepthAt(x);
        public double FloorSlope(double x) => floor.SlopeAt(x);

        // ---- hydrostatics ----
        // Water is compressible: density follows the EOS-80 secant bulk modulus,
        //   rho(p) = rho_surface / (1 - p / K(S, T, p)),
        // and the gauge pressure integrates dp/dz = rho(p) g (RK4, tabulated down to
        // just below the deepest floor, extrapolated beyond).
        public double DensityAtGauge(double pPa)
        {
            double pb = JsMath.Max(0, pPa) / 1e5;
            return rho / (1 - pb / Water.Eos80SecantBulkModulus(salinityPSU, T68, pb));
        }

        public void BuildPressureTable()
        {
            salinityPSU = JsMath.Max(0, Js.Or(cfg.salinity, 0));
            double tC = Js.IsFinite(cfg.temperature) ? cfg.temperature : 20;
            T68 = Water.ToT68(tC);
            double zmax = JsMath.Max(1, floor.maxDepth) * 1.02 + 2;
            const int N = 512;
            double dz = zmax / N;
            var p = new double[N + 1];
            for (int i = 0; i < N; i++)
            {
                double k1 = F(p[i]), k2 = F(p[i] + 0.5 * dz * k1), k3 = F(p[i] + 0.5 * dz * k2), k4 = F(p[i] + dz * k3);
                p[i + 1] = p[i] + (dz / 6) * (k1 + 2 * k2 + 2 * k3 + k4);
            }
            pTable = p; pDz = dz; pN = N;
        }
        double F(double pp) => g * DensityAtGauge(pp);

        // gauge pressure [Pa] at a depth below the still-water surface
        public double PressureGauge(double depth)
        {
            if (!(depth > 0)) return 0;
            double u = depth / pDz;
            if (u >= pN) { double pe = pTable[pN]; return pe + (depth - pN * pDz) * g * DensityAtGauge(pe); }
            double ifl = System.Math.Floor(u), fr = u - ifl;
            int i = (int)ifl;
            return pTable[i] + (pTable[i + 1] - pTable[i]) * fr;
        }

        public double DensityAtDepth(double depth) => DensityAtGauge(PressureGauge(depth));

        // gauge pressure at a world point (NED): hydrostatic + linear-wave dynamic part
        public double PressureAt(double x, double y, double z)
        {
            double eta = SurfaceElevation(x, y);
            if (z <= -eta) return 0; // above the local free surface
            double zEff = Js.Truthy(eta) ? z + eta * WaveProfile(x, JsMath.Max(0, z)).fp : z;
            return PressureGauge(zEff);
        }

        public double AbsolutePressureAt(double x, double y, double z) => pAtm + PressureAt(x, y, z);

        public void Step(double dt)
        {
            time += dt;
            // Gauss-Markov turbulence through a short first-order filter so the gust
            // velocity is differentiable (its acceleration drives the inertial terms)
            double ts = JsMath.Max(0.05, JsMath.Min(0.25, Js.Or(cfg.turbulence.tau, 1) / 4));
            for (int i = 0; i < 3; i++)
            {
                gustRaw[i] = gm[i].Step(dt);
                double a = (gustRaw[i] - gust[i]) / ts;
                gust[i] += a * JsMath.Min(1, dt / ts);
                gustAccel[i] = (gustRaw[i] - gust[i]) / ts;
            }
        }

        // free-surface elevation (positive up) at horizontal position
        public double SurfaceElevation(double x, double y) => SurfaceElevation(x, y, time);
        public double SurfaceElevation(double x, double y, double t)
        {
            var w = cfg.waves;
            if (!(w.amplitude > 0) || !Js.Truthy(waveOmega)) return 0;
            double k = floor.sloped ? WaveNumber(floor.DepthAt(x)) : waveK;
            return w.amplitude * JsMath.Cos(WavePhase(x, y, k, t));
        }

        // fluid velocity (NED) at a world point; z positive down
        public V3 FluidVelocity(double x, double y, double z) => FluidVelocity(x, y, z, time);
        public V3 FluidVelocity(double x, double y, double z, double t)
        {
            V3 c = cfg.current;
            double vx = c.x + gust.x, vy = c.y + gust.y, vz = c.z + gust.z;
            var w = cfg.waves;
            if (w.amplitude > 0 && Js.Truthy(waveOmega))
            {
                var pr = WaveProfile(x, JsMath.Max(0, z));
                double ph = WavePhase(x, y, pr.k, t);
                double aw = w.amplitude * waveOmega;
                double uh = aw * pr.fu * JsMath.Cos(ph);
                vx += uh * waveDir.x;
                vy += uh * waveDir.y;
                vz += -aw * pr.fw * JsMath.Sin(ph); // NED (down positive)
            }
            return new V3(vx, vy, vz);
        }

        // Fluid-particle acceleration Du/Dt (NED) at a world point: turbulence plus
        // the waves' local du/dt and convective (u.grad)u terms (analytic, linear
        // profiles). The convective part carries the second-order mean pressure
        // (Bernoulli -rho u^2 / 2) that keeps a neutrally buoyant body from sinking in waves.
        public V3 FluidAccel(double x, double y, double z) => FluidAccel(x, y, z, time);
        public V3 FluidAccel(double x, double y, double z, double t)
        {
            double ax = gustAccel.x, ay = gustAccel.y, az = gustAccel.z;
            var w = cfg.waves;
            if (w.amplitude > 0 && Js.Truthy(waveOmega))
            {
                var pr = WaveProfile(x, JsMath.Max(0, z));
                double ph = WavePhase(x, y, pr.k, t), sn = JsMath.Sin(ph), cs = JsMath.Cos(ph);
                double A = w.amplitude, om = waveOmega, k = pr.k;
                V2 d = waveDir;
                double aw2 = A * om * om;
                V3 c = cfg.current;
                double us = A * om * pr.fu * cs + (c.x + gust.x) * d.x + (c.y + gust.y) * d.y; // advecting speed along the wave
                double wz = -A * om * pr.fw * sn + c.z + gust.z;
                // d/ds and d/dz (z down) of the wave velocity: u_h = A w fu cos, u_z = -A w fw sin
                double duh = -A * om * k * pr.fu * sn, duhz = -A * om * k * pr.fw * cs;
                double dwz = -A * om * k * pr.fw * cs, dwzz = A * om * k * pr.fu * sn;
                double ah = aw2 * pr.fu * sn + us * duh + wz * duhz;
                ax += ah * d.x;
                ay += ah * d.y;
                az += aw2 * pr.fw * cs + us * dwz + wz * dwzz;
            }
            return new V3(ax, ay, az);
        }
    }
}
