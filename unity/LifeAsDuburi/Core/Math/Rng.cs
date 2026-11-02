// Port of src/core/rng.js.
// Deterministic pseudo-random numbers so every run (and every auto-tune trial)
// is reproducible from a seed.

namespace Duburi.Core
{
    public sealed class RNG
    {
        public uint state;
        double spare;
        bool hasSpare;

        public RNG(double seed = 1) { Seed(seed); }

        public void Seed(double s)
        {
            state = Js.ToUint32(s); // (s >>> 0) || 0x9e3779b9
            if (state == 0) state = 0x9e3779b9;
            hasSpare = false;
        }

        // mulberry32
        public double Next()
        {
            unchecked
            {
                state += 0x6d2b79f5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Uniform(double a = 0, double b = 1) => a + (b - a) * Next();

        // standard normal via Box-Muller (cached spare)
        public double Gauss()
        {
            if (hasSpare)
            {
                hasSpare = false;
                return spare;
            }
            double u = 0, v;
            while (u <= 1e-12) u = Next();
            v = Next();
            double mag = JsMath.Sqrt(-2 * JsMath.Log(u));
            spare = mag * JsMath.Sin(2 * JsMath.PI * v);
            hasSpare = true;
            return mag * JsMath.Cos(2 * JsMath.PI * v);
        }
    }

    // First-order Gauss-Markov process (coloured noise), used for turbulence and
    // sensor bias drift. sigma = stationary std-dev, tau = correlation time [s].
    public sealed class GaussMarkov
    {
        public RNG rng;
        public double sigma, tau, x;

        public GaussMarkov(RNG rng, double sigma = 0, double tau = 1)
        {
            this.rng = rng;
            this.sigma = sigma;
            this.tau = tau;
            x = 0;
        }

        public double Step(double dt)
        {
            if (sigma <= 0)
            {
                x = 0;
                return 0;
            }
            double a = JsMath.Exp(-dt / JsMath.Max(tau, 1e-6));
            x = a * x + sigma * JsMath.Sqrt(1 - a * a) * rng.Gauss();
            return x;
        }
    }
}
