// JavaScript's Math object with the exact results of V8 (Node 22 / Chrome):
//   Sin, Cos, Tan, Exp, Log, Atan, Atan2, Asin, Tanh, Pow  -> fdlibm port (Fdlibm.cs)
//   Hypot                                                 -> V8's Kahan-summed algorithm
//   Round, Sign, Max, Min                                 -> JS semantics (ties toward +inf,
//                                                            signed zeros, NaN propagation)
// All Core code calls these instead of System.Math, so a match gives the same
// numbers as the browser game, on every platform (Unity's System.Math goes to
// the platform C library, whose last bit differs between glibc, musl/WebGL, MSVC).
// UseSystemMath = true switches the transcendental functions to System.Math
// (slightly faster, platform-dependent last bit); the tests use it to show that
// the only difference is that last bit.

using System;

namespace Duburi.Core
{
    public static class JsMath
    {
        public const double PI = 3.141592653589793;     // Math.PI
        public const double SQRT1_2 = 0.7071067811865476; // Math.SQRT1_2

        /// false (default): bit-identical to V8. true: System.Math (platform libm).
        public static bool UseSystemMath;

        public static double Sin(double x) => UseSystemMath ? Math.Sin(x) : Fdlibm.Sin(x);
        public static double Cos(double x) => UseSystemMath ? Math.Cos(x) : Fdlibm.Cos(x);
        public static double Tan(double x) => UseSystemMath ? Math.Tan(x) : Fdlibm.Tan(x);
        public static double Exp(double x) => UseSystemMath ? Math.Exp(x) : Fdlibm.Exp(x);
        public static double Log(double x) => UseSystemMath ? Math.Log(x) : Fdlibm.Log(x);
        public static double Atan(double x) => UseSystemMath ? Math.Atan(x) : Fdlibm.Atan(x);
        public static double Atan2(double y, double x) => UseSystemMath ? Math.Atan2(y, x) : Fdlibm.Atan2(y, x);
        public static double Asin(double x) => UseSystemMath ? Math.Asin(x) : Fdlibm.Asin(x);
        public static double Tanh(double x) => UseSystemMath ? Math.Tanh(x) : Fdlibm.Tanh(x);
        public static double Expm1(double x) => UseSystemMath ? Math.Exp(x) - 1 : Fdlibm.Expm1(x);

        /// Math.pow and the ** operator. (JS: 1 ** Infinity is NaN, unlike IEEE.)
        public static double Pow(double x, double y)
        {
            if (!UseSystemMath) return Fdlibm.Pow(x, y);
            if (double.IsNaN(y) || (double.IsInfinity(y) && (x == 1 || x == -1))) return double.NaN;
            return Math.Pow(x, y);
        }

        // exact IEEE operations: the same everywhere
        public static double Sqrt(double x) => Math.Sqrt(x);
        public static double Abs(double x) => Math.Abs(x);
        public static double Floor(double x) => Math.Floor(x);
        public static double Ceil(double x) => Math.Ceiling(x);

        /// Math.round: nearest integer, ties toward +Infinity, keeps -0 (V8's Float64Round).
        public static double Round(double x)
        {
            double r = Math.Ceiling(x);
            if (r - 0.5 > x) r -= 1.0;
            return r;
        }

        /// Math.sign: 1, -1, or x itself for +-0 and NaN.
        public static double Sign(double x) => x > 0 ? 1.0 : x < 0 ? -1.0 : x;

        /// Math.max of two numbers: NaN if either is NaN, +0 is larger than -0.
        public static double Max(double a, double b)
        {
            if (a > b) return a;
            if (b > a) return b;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a == 0 && BitConverter.DoubleToInt64Bits(a) < 0) return b; // -0 vs +0
            return a;
        }

        /// Math.min of two numbers: NaN if either is NaN, -0 is smaller than +0.
        public static double Min(double a, double b)
        {
            if (a < b) return a;
            if (b < a) return b;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            if (a == 0 && BitConverter.DoubleToInt64Bits(b) < 0) return b; // +0 vs -0
            return a;
        }

        public static double Max(double a, double b, double c) => Max(Max(a, b), c);
        public static double Min(double a, double b, double c) => Min(Min(a, b), c);

        // ---------------------------------------------------------------- Math.hypot
        // V8 (src/builtins/math.tq): normalise by the largest magnitude, Kahan-sum the
        // squares in argument order, sqrt(sum) * max. Not the same bits as sqrt(a*a+b*b).
        public static double Hypot(double a, double b)
        {
            bool nan = false;
            double max = 0, xa = 0, xb = 0;
            if (double.IsNaN(a)) nan = true; else { xa = Math.Abs(a); if (xa > max) max = xa; }
            if (double.IsNaN(b)) nan = true; else { xb = Math.Abs(b); if (xb > max) max = xb; }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(xa / max, ref sum, ref comp);
            Kahan(xb / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }

        public static double Hypot(double a, double b, double c)
        {
            bool nan = false;
            double max = 0, xa = 0, xb = 0, xc = 0;
            if (double.IsNaN(a)) nan = true; else { xa = Math.Abs(a); if (xa > max) max = xa; }
            if (double.IsNaN(b)) nan = true; else { xb = Math.Abs(b); if (xb > max) max = xb; }
            if (double.IsNaN(c)) nan = true; else { xc = Math.Abs(c); if (xc > max) max = xc; }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(xa / max, ref sum, ref comp);
            Kahan(xb / max, ref sum, ref comp);
            Kahan(xc / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }

        public static double Hypot(double a, double b, double c, double d)
        {
            bool nan = false;
            double max = 0, xa = 0, xb = 0, xc = 0, xd = 0;
            if (double.IsNaN(a)) nan = true; else { xa = Math.Abs(a); if (xa > max) max = xa; }
            if (double.IsNaN(b)) nan = true; else { xb = Math.Abs(b); if (xb > max) max = xb; }
            if (double.IsNaN(c)) nan = true; else { xc = Math.Abs(c); if (xc > max) max = xc; }
            if (double.IsNaN(d)) nan = true; else { xd = Math.Abs(d); if (xd > max) max = xd; }
            if (max == double.PositiveInfinity) return double.PositiveInfinity;
            if (nan) return double.NaN;
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            Kahan(xa / max, ref sum, ref comp);
            Kahan(xb / max, ref sum, ref comp);
            Kahan(xc / max, ref sum, ref comp);
            Kahan(xd / max, ref sum, ref comp);
            return Math.Sqrt(sum) * max;
        }

        static void Kahan(double n, ref double sum, ref double comp)
        {
            double summand = n * n - comp;
            double preliminary = sum + summand;
            comp = (preliminary - sum) - summand;
            sum = preliminary;
        }
    }
}
