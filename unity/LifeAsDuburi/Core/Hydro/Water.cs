// Port of src/hydro/water.js.
// Water properties and closed-form hydrodynamic estimates used as defaults
// before (or instead of) the CFD/BEM analysis.

using System;

namespace Duburi.Core
{
    public static class Water
    {
        // ---- UNESCO EOS-80 equation of state of seawater -------------------------
        // Fofonoff & Millard (1983), UNESCO Technical Papers in Marine Science 44.
        // S practical salinity (≈ g/kg), T in °C on the IPTS-68 scale, p sea pressure
        // (absolute minus one atmosphere) in bar.
        public static double ToT68(double t90) => t90 * 1.00024;

        // one-atmosphere density [kg/m^3]
        public static double Eos80Density0(double S, double T)
        {
            double rw = 999.842594 + T * (6.793952e-2 + T * (-9.095290e-3 + T * (1.001685e-4 + T * (-1.120083e-6 + T * 6.536332e-9))));
            double A = 8.24493e-1 + T * (-4.0899e-3 + T * (7.6438e-5 + T * (-8.2467e-7 + T * 5.3875e-9)));
            double B = -5.72466e-3 + T * (1.0227e-4 - T * 1.6546e-6);
            return rw + A * S + B * S * JsMath.Sqrt(S) + 4.8314e-4 * S * S;
        }

        // secant bulk modulus K(S,T,p) [bar]
        public static double Eos80SecantBulkModulus(double S, double T, double p)
        {
            double Kw = 19652.21 + T * (148.4206 + T * (-2.327105 + T * (1.360477e-2 - T * 5.155288e-5)));
            double Aw = 3.239908 + T * (1.43713e-3 + T * (1.16092e-4 - T * 5.77905e-7));
            double Bw = 8.50935e-5 + T * (-6.12293e-6 + T * 5.2787e-8);
            double sr = JsMath.Sqrt(S);
            double K0 = Kw + S * (54.6746 + T * (-0.603459 + T * (1.09987e-2 - T * 6.1670e-5))) + S * sr * (7.944e-2 + T * (1.6483e-2 - T * 5.3009e-4));
            double A = Aw + S * (2.2838e-3 + T * (-1.0981e-5 - T * 1.6078e-6)) + 1.91075e-4 * S * sr;
            double B = Bw + S * (-9.9348e-7 + T * (2.0816e-8 + T * 9.1697e-10));
            return K0 + p * (A + B * p);
        }

        // in-situ density [kg/m^3]
        public static double Eos80Density(double S, double T, double p) => Eos80Density0(S, T) / (1 - p / Eos80SecantBulkModulus(S, T, p));

        // Water density at the surface. Fresh water from Tanaka et al. (2001), the
        // most accurate pure-water formula; the salt contribution from EOS-80.
        public static double WaterDensity(double tempC = 20, double salinity = 0)
        {
            double T = tempC;
            double rhoFresh = 999.974950 * (1 - (JsMath.Pow(T - 3.983035, 2) * (T + 301.797)) / (522528.9 * (T + 69.34881)));
            if (!(salinity > 0)) return rhoFresh;
            double T68 = ToT68(T);
            return rhoFresh + Eos80Density0(salinity, T68) - Eos80Density0(0, T68);
        }

        // Standard atmosphere: air pressure [hPa] at a site elevation [m above sea level].
        public static double AirPressureAtAltitude(double h) => 1013.25 * JsMath.Pow(1 - 2.25577e-5 * h, 5.25588);

        // Dynamic viscosity of water [Pa s] (Vogel equation) with a mild salinity factor.
        public static double WaterViscosity(double tempC = 20, double salinity = 0)
        {
            double mu = 2.414e-5 * JsMath.Pow(10, 247.8 / (tempC + 273.15 - 140));
            return mu * (1 + 0.0023 * salinity);
        }

        public static double KinematicViscosity(double tempC = 20, double salinity = 0) => WaterViscosity(tempC, salinity) / WaterDensity(tempC, salinity);

        public sealed class AddedMass
        {
            public double[] coeff;  // [alpha, beta, gamma] (Lamb's integrals)
            public double[] values; // [X, Y, Z, K, M, N] added mass / inertia
        }

        // Lamb's added-mass coefficients for an ellipsoid with semi-axes a, b, c
        // (a along x, b along y, c along z), deeply submerged in unbounded fluid.
        public static AddedMass EllipsoidAddedMass(double a, double b, double c, double rho = 1000)
        {
            double Integral(double s1)
            {
                // integral_0^inf dl / ((s1+l) * sqrt((a2+l)(b2+l)(c2+l))), with l = t/(1-t)
                double a2 = a * a, b2 = b * b, c2 = c * c;
                const int n = 4000;
                double sum = 0;
                for (int i = 0; i < n; i++)
                {
                    double t = (i + 0.5) / n;
                    double l = t / (1 - t);
                    double dl = 1 / ((1 - t) * (1 - t));
                    sum += dl / ((s1 + l) * JsMath.Sqrt((a2 + l) * (b2 + l) * (c2 + l)));
                }
                return (sum / n) * a * b * c;
            }
            double al = Integral(a * a), be = Integral(b * b), ga = Integral(c * c);
            double V = (4.0 / 3) * JsMath.PI * a * b * c;
            double m = rho * V;
            double Rot(double p2, double q2, double ip, double iq)
            {
                double num = JsMath.Pow(p2 - q2, 2) * (iq - ip);
                double den = 2 * (p2 - q2) + (p2 + q2) * (ip - iq);
                if (Math.Abs(p2 - q2) < 1e-12 * (p2 + q2) || Math.Abs(den) < 1e-18) return 0;
                return (m / 5) * (num / den);
            }
            return new AddedMass
            {
                coeff = new[] { al, be, ga },
                values = new[] {
                    (al / (2 - al)) * m,
                    (be / (2 - be)) * m,
                    (ga / (2 - ga)) * m,
                    Rot(b * b, c * c, be, ga),
                    Rot(c * c, a * a, ga, al),
                    Rot(a * a, b * b, al, be),
                },
            };
        }

        /// projected areas for DragFromProjections: area = [Ax, Ay, Az], k3 = { roll, pitch, yaw }
        public sealed class Projections
        {
            public double[] area = new double[3];
            public double roll, pitch, yaw; // JS proj.k3.{roll, pitch, yaw}
        }

        public sealed class DragModel
        {
            public double[] linear;
            public double[] quadratic;
        }

        // Default drag model from projected areas (bluff body, Cd ~ 1) with a small
        // linear term so the model behaves sensibly at very low speed.
        public static DragModel DragFromProjections(Projections proj, double rho = 1000, double Cd = 1.0, double linearSpeed = 0.1, double linearRate = 0.3)
        {
            double q(double A) => 0.5 * rho * Cd * A;
            var quad = new[] { q(proj.area[0]), q(proj.area[1]), q(proj.area[2]), q(proj.roll), q(proj.pitch), q(proj.yaw) };
            var lin = new[] { quad[0] * linearSpeed, quad[1] * linearSpeed, quad[2] * linearSpeed, quad[3] * linearRate, quad[4] * linearRate, quad[5] * linearRate };
            return new DragModel { linear = lin, quadratic = quad };
        }
    }
}
