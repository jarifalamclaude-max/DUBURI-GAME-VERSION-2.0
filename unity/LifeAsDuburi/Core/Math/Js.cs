// JavaScript semantics the port needs where C# behaves differently:
//   ToUint32 / ToInt32 (x >>> 0, x | 0), truthiness of numbers (0, -0 and NaN are falsy),
//   Number.prototype.toFixed (exact decimal rounding, ties away from zero) and +x.toFixed(d),
//   Number -> string, padStart, toLocaleString (en-US), Number(string),
//   a stable sort (Array.prototype.sort is stable, List.Sort is not),
//   and a seedable stand-in for Math.random (CoreRandom).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Duburi.Core
{
    public static class Js
    {
        // ------------------------------------------------------------ conversions
        /// x >>> 0
        public static uint ToUint32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double t = Math.Truncate(x);
            // t mod 2^32 (floor modulo): every step is exact for any finite t
            double m = t - 4294967296.0 * Math.Floor(t / 4294967296.0);
            return (uint)m;
        }

        /// x | 0
        public static int ToInt32(double x) => unchecked((int)ToUint32(x));

        /// JS truthiness of a number: 0, -0 and NaN are false.
        public static bool Truthy(double v) => v != 0 && !double.IsNaN(v);

        /// a || b for numbers
        public static double Or(double a, double b) => Truthy(a) ? a : b;

        /// Number.isFinite
        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // ------------------------------------------------------------ toFixed
        static readonly double[] Pow10 = {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        // round(|x| * 10^f) with ties up, exactly (x finite, >= 0). Returns false when the
        // result does not fit a long (then use the BigInteger path).
        static bool ScaledRound(double x, int f, out long n)
        {
            n = 0;
            if (f <= 22)
            {
                double y = x * Pow10[f];
                if (y < 4503599627370496.0) // 2^52: y - floor(y) is exact
                {
                    double fl = Math.Floor(y), frac = y - fl;
                    // the rounded product is within half an ulp of the exact one; away from .5
                    // that cannot change the decision (see the comment in ScaledRoundExact)
                    if (Math.Abs(frac - 0.5) > y * 4.5e-16 + 1e-300)
                    {
                        n = (long)(frac >= 0.5 ? fl + 1 : fl);
                        return true;
                    }
                }
            }
            BigInteger b = ScaledRoundExact(x, f);
            if (b > long.MaxValue) return false;
            n = (long)b;
            return true;
        }

        // n = floor(x * 10^f + 1/2) on the exact binary value of x (what V8's toFixed does)
        static BigInteger ScaledRoundExact(double x, int f)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            e -= 1075; // x = m * 2^e
            BigInteger num = m * BigInteger.Pow(10, f);
            if (e >= 0) return num << e;
            int sh = -e;
            return ((num << 1) + (BigInteger.One << sh)) >> (sh + 1);
        }

        /// Number.prototype.toFixed(f) (0 <= f <= 100)
        public static string ToFixed(double x, int f)
        {
            if (f < 0 || f > 100) throw new ArgumentOutOfRangeException(nameof(f), "toFixed() digits argument must be between 0 and 100");
            if (double.IsNaN(x)) return "NaN";
            if (!(Math.Abs(x) < 1e21)) return Str(x);
            bool neg = x < 0;
            if (neg) x = -x;
            string digits = ScaledRound(x, f, out long n) ? n.ToString(CultureInfo.InvariantCulture) : ScaledRoundExact(x, f).ToString(CultureInfo.InvariantCulture);
            if (f > 0)
            {
                if (digits.Length <= f) digits = new string('0', f + 1 - digits.Length) + digits;
                digits = digits.Substring(0, digits.Length - f) + "." + digits.Substring(digits.Length - f);
            }
            return neg ? "-" + digits : digits;
        }

        /// +x.toFixed(f): x rounded to f decimals, as the nearest double (what the JS gets back
        /// when it parses the string). n / 10^f is that nearest double when both are exact.
        public static double RoundFixed(double x, int f)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (!(Math.Abs(x) < 1e21)) return x;
            bool neg = x < 0;
            double a = neg ? -x : x;
            if (f <= 22 && ScaledRound(a, f, out long n) && n <= 9007199254740992L)
            {
                double r = n / Pow10[f];
                return neg ? -r : r;
            }
            return double.Parse(ToFixed(x, f), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ Number -> string
        /// String(x) / `${x}` for a number (shortest round-trip digits, JS layout)
        public static string Str(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (x == 0) return "0";
            if (double.IsPositiveInfinity(x)) return "Infinity";
            if (double.IsNegativeInfinity(x)) return "-Infinity";
            if (Math.Abs(x) < 9.2e18 && x == Math.Floor(x)) return ((long)x).ToString(CultureInfo.InvariantCulture);
            ShortestDigits(Math.Abs(x), out string digits, out int exp10);
            string s;
            int k = digits.Length, n = exp10 + 1; // value = 0.d1d2..dk * 10^n
            if (k <= n && n <= 21) s = digits + new string('0', n - k);
            else if (0 < n && n <= 21) s = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) s = "0." + new string('0', -n) + digits;
            else
            {
                int e = n - 1;
                s = digits.Substring(0, 1) + (k > 1 ? "." + digits.Substring(1) : "") + "e" + (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
            }
            return x < 0 ? "-" + s : s;
        }

        // shortest decimal digits that read back as x (x finite, > 0); x = 0.digits * 10^(exp10+1)
        static void ShortestDigits(double x, out string digits, out int exp10)
        {
            string e = null;
            for (int p = 1; p <= 17; p++)
            {
                e = x.ToString("E" + (p - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (double.Parse(e, NumberStyles.Float, CultureInfo.InvariantCulture) == x) break;
            }
            int ePos = e.IndexOf('E');
            string mant = e.Substring(0, ePos).Replace(".", "");
            exp10 = int.Parse(e.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            digits = mant.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
        }

        /// String.prototype.padStart
        public static string PadStart(string s, int length, char fill = ' ') => s.Length >= length ? s : new string(fill, length - s.Length) + s;

        /// Number.prototype.toLocaleString() in en-US: grouping commas, at most 3 decimals (half away from zero)
        public static string ToLocaleString(double x)
        {
            if (double.IsNaN(x)) return "NaN";
            if (double.IsInfinity(x)) return x > 0 ? "∞" : "-∞";
            bool neg = x < 0 || (x == 0 && BitConverter.DoubleToInt64Bits(x) < 0);
            double a = Math.Abs(x);
            string intPart, frac = "";
            if (a == Math.Floor(a) && a < 9.2e18) intPart = ((long)a).ToString(CultureInfo.InvariantCulture);
            else
            {
                // round the shortest decimal representation to 3 decimals, half away from zero
                ShortestDigits(a, out string digits, out int exp10);
                int point = exp10 + 1; // digits before the decimal point
                string d = digits;
                if (point <= 0) { d = new string('0', 1 - point) + d; point = 1; } // 0.00ddd
                if (d.Length < point) d = d + new string('0', point - d.Length);
                string ip = d.Substring(0, point), fp = d.Substring(point);
                if (fp.Length > 3)
                {
                    bool up = fp[3] >= '5';
                    fp = fp.Substring(0, 3);
                    if (up)
                    {
                        var num = BigInteger.Parse(ip + fp, CultureInfo.InvariantCulture) + 1;
                        string t = num.ToString(CultureInfo.InvariantCulture).PadLeft(ip.Length + 3, '0');
                        ip = t.Substring(0, t.Length - 3); fp = t.Substring(t.Length - 3);
                    }
                }
                fp = fp.TrimEnd('0');
                ip = ip.TrimStart('0'); if (ip.Length == 0) ip = "0";
                intPart = ip; frac = fp;
            }
            var sb = new StringBuilder();
            if (neg) sb.Append('-');
            int first = intPart.Length % 3; if (first == 0) first = 3;
            sb.Append(intPart, 0, Math.Min(first, intPart.Length));
            for (int i = first; i < intPart.Length; i += 3) { sb.Append(','); sb.Append(intPart, i, 3); }
            if (frac.Length > 0) { sb.Append('.'); sb.Append(frac); }
            return sb.ToString();
        }

        // ------------------------------------------------------------ Number(string)
        /// JS WhiteSpace / LineTerminator (what String.prototype.trim and the regex \s remove)
        public static bool IsWhiteSpace(char c)
        {
            switch ((int)c)
            {
                case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D: case 0x20:
                case 0xA0: case 0x1680: case 0x2028: case 0x2029: case 0x202F:
                case 0x205F: case 0x3000: case 0xFEFF:
                    return true;
            }
            return c >= 0x2000 && c <= 0x200A;
        }

        /// Number(str): trims JS whitespace; "" -> 0; decimal / Infinity / 0x 0o 0b literals; anything else NaN.
        public static double Number(string str)
        {
            if (str == null) return 0;
            int a = 0, b = str.Length;
            while (a < b && IsWhiteSpace(str[a])) a++;
            while (b > a && IsWhiteSpace(str[b - 1])) b--;
            if (a == b) return 0;
            string s = str.Substring(a, b - a);
            if (s.Length > 2 && s[0] == '0')
            {
                int radix = (s[1] == 'x' || s[1] == 'X') ? 16 : (s[1] == 'o' || s[1] == 'O') ? 8 : (s[1] == 'b' || s[1] == 'B') ? 2 : 0;
                if (radix != 0)
                {
                    double v = 0;
                    for (int i = 2; i < s.Length; i++)
                    {
                        int d = DigitValue(s[i]);
                        if (d < 0 || d >= radix) return double.NaN;
                        v = v * radix + d;
                    }
                    return v;
                }
            }
            int p = 0;
            bool neg = false;
            if (s[p] == '+' || s[p] == '-') { neg = s[p] == '-'; p++; }
            if (string.CompareOrdinal(s, p, "Infinity", 0, 8) == 0 && s.Length - p == 8) return neg ? double.NegativeInfinity : double.PositiveInfinity;
            int intDigits = 0, fracDigits = 0;
            while (p < s.Length && s[p] >= '0' && s[p] <= '9') { p++; intDigits++; }
            if (p < s.Length && s[p] == '.') { p++; while (p < s.Length && s[p] >= '0' && s[p] <= '9') { p++; fracDigits++; } }
            if (intDigits + fracDigits == 0) return double.NaN;
            if (p < s.Length && (s[p] == 'e' || s[p] == 'E'))
            {
                p++;
                if (p < s.Length && (s[p] == '+' || s[p] == '-')) p++;
                int expDigits = 0;
                while (p < s.Length && s[p] >= '0' && s[p] <= '9') { p++; expDigits++; }
                if (expDigits == 0) return double.NaN;
            }
            if (p != s.Length) return double.NaN;
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'z') return c - 'a' + 10;
            if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
            return -1;
        }

        // ------------------------------------------------------------ stable sort
        /// Array.prototype.sort(cmp) with a numeric comparator: stable, in place.
        /// cmp(a, b) < 0 puts a first (a NaN result counts as "not less", as in V8).
        public static void StableSort<T>(IList<T> list, Func<T, T, double> cmp)
        {
            int n = list.Count;
            if (n < 2) return;
            var a = new T[n];
            for (int i = 0; i < n; i++) a[i] = list[i];
            var tmp = new T[n];
            MergeSort(a, tmp, 0, n, cmp);
            for (int i = 0; i < n; i++) list[i] = a[i];
        }

        static void MergeSort<T>(T[] a, T[] tmp, int lo, int hi, Func<T, T, double> cmp)
        {
            if (hi - lo <= 12)
            { // stable insertion sort
                for (int i = lo + 1; i < hi; i++)
                {
                    T x = a[i];
                    int j = i - 1;
                    while (j >= lo && cmp(x, a[j]) < 0) { a[j + 1] = a[j]; j--; }
                    a[j + 1] = x;
                }
                return;
            }
            int mid = (lo + hi) >> 1;
            MergeSort(a, tmp, lo, mid, cmp);
            MergeSort(a, tmp, mid, hi, cmp);
            int p = lo, q = mid, k = lo;
            while (p < mid && q < hi) tmp[k++] = cmp(a[q], a[p]) < 0 ? a[q++] : a[p++];
            while (p < mid) tmp[k++] = a[p++];
            while (q < hi) tmp[k++] = a[q++];
            for (int i = lo; i < hi; i++) a[i] = tmp[i];
        }
    }

    /// Seedable stand-in for Math.random() (default seeds, item ids). Seeded from the
    /// clock unless Seed() is called (the tests seed it so every run is reproducible).
    public static class CoreRandom
    {
        static uint state;
        static bool seeded;

        public static void Seed(uint seed) { state = seed; seeded = true; }

        /// a number in [0, 1) (mulberry32)
        public static double Next()
        {
            if (!seeded) Seed(unchecked((uint)DateTime.UtcNow.Ticks ^ (uint)(DateTime.UtcNow.Ticks >> 32)));
            unchecked
            {
                state += 0x6d2b79f5;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// like Math.random().toString(36).slice(2): a random base-36 id
        public static string Id()
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var c = new char[11];
            for (int i = 0; i < c.Length; i++) c[i] = digits[(int)(Next() * 36)];
            return new string(c);
        }
    }

    /// A JS object used as a map: keys keep their insertion order.
    public sealed class OrderedMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        readonly List<TKey> keys = new List<TKey>();
        readonly Dictionary<TKey, TValue> map = new Dictionary<TKey, TValue>();

        public int Count => keys.Count;
        public IReadOnlyList<TKey> Keys => keys;
        public IEnumerable<TValue> Values { get { foreach (var k in keys) yield return map[k]; } }
        public bool ContainsKey(TKey key) => map.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value);
        /// obj[key] ?? fallback
        public TValue Get(TKey key, TValue fallback = default) => map.TryGetValue(key, out var v) ? v : fallback;

        public TValue this[TKey key]
        {
            get => map[key];
            set { if (!map.ContainsKey(key)) keys.Add(key); map[key] = value; }
        }

        public void Add(TKey key, TValue value) => this[key] = value;

        public OrderedMap<TKey, TValue> Clone()
        {
            var c = new OrderedMap<TKey, TValue>();
            foreach (var k in keys) c[k] = map[k];
            return c;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() { foreach (var k in keys) yield return new KeyValuePair<TKey, TValue>(k, map[k]); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
