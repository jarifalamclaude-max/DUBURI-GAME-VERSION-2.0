// Golden-data comparison: expected values come from the JavaScript original
// (tools/golden/make-golden.mjs -> tools/golden/out/*.json); the C# side builds
// the same tree (dictionaries / lists / numbers / strings) from Duburi.Core and
// Compare() walks both. Numbers are expected to be bit-identical (Fdlibm gives
// V8's Math); a tiny tolerance only keeps the report readable if they are not.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Duburi.Core;

namespace CoreTests
{
    sealed class Suite
    {
        public readonly string name;
        public long values, identical;
        public int failures;
        public double maxAbs, maxRel;
        public string worst = "";
        public readonly List<string> messages = new List<string>();
        public double absTol = 1e-12, relTol = 1e-11;
        public Suite(string name) { this.name = name; }

        public void Fail(string msg)
        {
            failures++;
            if (messages.Count < 12) messages.Add(msg);
        }

        public void Check(bool ok, string msg) { values++; identical += ok ? 1 : 0; if (!ok) Fail(msg); }
    }

    /// ordered object: key order is checked against the JSON (JS insertion order)
    sealed class OMap : Dictionary<string, object> { }

    static class G
    {
        public static string GoldenDir;

        public static JsonElement Load(string name)
        {
            string path = Path.Combine(GoldenDir, name + ".json");
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 256 }).RootElement;
        }

        // ------------------------------------------------------------ tree builders
        public static Dictionary<string, object> O(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        public static OMap Map<T>(OrderedMap<string, T> m, Func<T, object> f = null)
        {
            var d = new OMap();
            foreach (var kv in m) d[kv.Key] = f != null ? f(kv.Value) : kv.Value;
            return d;
        }

        public static List<object> L<T>(IEnumerable<T> items, Func<T, object> f)
        {
            var l = new List<object>();
            foreach (var x in items) l.Add(f(x));
            return l;
        }

        public static OMap ScoresOf(Scores s)
        {
            var d = new OMap();
            foreach (var k in Game.TASK_KEYS) d[k] = s[k];
            return d;
        }

        // ------------------------------------------------------------ JSON inputs
        public static double D(JsonElement e) => e.ValueKind == JsonValueKind.Null ? double.NaN : e.GetDouble();
        public static double[] Vec(JsonElement e) { var r = new double[e.GetArrayLength()]; int i = 0; foreach (var x in e.EnumerateArray()) r[i++] = D(x); return r; }
        public static double[][] Mat(JsonElement e) { var r = new double[e.GetArrayLength()][]; int i = 0; foreach (var x in e.EnumerateArray()) r[i++] = Vec(x); return r; }
        public static V3 V3Of(JsonElement e) { var v = Vec(e); return new V3(v[0], v[1], v[2]); }
        public static bool Has(JsonElement o, string key) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Undefined;

        // ------------------------------------------------------------ compare
        static object Norm(object a)
        {
            switch (a)
            {
                case null: return null;
                case V2 v: return new double[] { v.x, v.y };
                case V3 v: return new double[] { v.x, v.y, v.z };
                case Quat q: return new double[] { q.w, q.x, q.y, q.z };
                case Scores s: return ScoresOf(s);
                default: return a;
            }
        }

        static bool IsNumber(object a, out double d)
        {
            switch (a)
            {
                case double x: d = x; return true;
                case float x: d = x; return true;
                case int x: d = x; return true;
                case long x: d = x; return true;
                case uint x: d = x; return true;
            }
            d = 0;
            return false;
        }

        public static void Compare(Suite s, JsonElement e, object act, string path)
        {
            object a = Norm(act);
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    {
                        double x = e.GetDouble();
                        if (!IsNumber(a, out double y)) { s.Fail(path + ": expected number " + x.ToString("R", CultureInfo.InvariantCulture) + ", got " + Describe(a)); return; }
                        s.values++;
                        if (x == y) { s.identical++; return; }
                        double ad = Math.Abs(x - y), rd = ad / Math.Max(Math.Abs(x), 1e-300);
                        if (double.IsNaN(ad)) rd = ad = double.PositiveInfinity;
                        if (ad > s.maxAbs) { s.maxAbs = ad; s.worst = path; }
                        if (rd > s.maxRel && ad > 1e-300) s.maxRel = Math.Min(rd, ad);
                        if (ad > s.absTol && rd > s.relTol)
                            s.Fail(path + ": expected " + x.ToString("R", CultureInfo.InvariantCulture) + ", got " + y.ToString("R", CultureInfo.InvariantCulture));
                        return;
                    }
                case JsonValueKind.Null:
                    s.values++;
                    if (a == null || (IsNumber(a, out double nn) && !Js.IsFinite(nn))) { s.identical++; return; }
                    s.Fail(path + ": expected null, got " + Describe(a));
                    return;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    s.values++;
                    if (a is bool b && b == (e.ValueKind == JsonValueKind.True)) { s.identical++; return; }
                    s.Fail(path + ": expected " + e.GetRawText() + ", got " + Describe(a));
                    return;
                case JsonValueKind.String:
                    s.values++;
                    if (a is string str && str == e.GetString()) { s.identical++; return; }
                    s.Fail(path + ": expected \"" + e.GetString() + "\", got " + Describe(a));
                    return;
                case JsonValueKind.Array:
                    {
                        if (!(a is IList list) || a is string) { s.Fail(path + ": expected array, got " + Describe(a)); return; }
                        int n = e.GetArrayLength();
                        if (list.Count != n) { s.Fail(path + ": expected " + n + " elements, got " + list.Count); return; }
                        int i = 0;
                        foreach (var x in e.EnumerateArray()) { Compare(s, x, list[i], path + "[" + i + "]"); i++; }
                        return;
                    }
                case JsonValueKind.Object:
                    {
                        if (!(a is IDictionary<string, object> dict)) { s.Fail(path + ": expected object, got " + Describe(a)); return; }
                        var keys = new List<string>();
                        foreach (var p in e.EnumerateObject())
                        {
                            keys.Add(p.Name);
                            if (!dict.TryGetValue(p.Name, out var v)) { s.Fail(path + "." + p.Name + ": missing"); continue; }
                            Compare(s, p.Value, v, path + "." + p.Name);
                        }
                        foreach (var k in dict.Keys) if (!keys.Contains(k)) s.Fail(path + "." + k + ": not in the JS data");
                        if (a is OMap om)
                        {
                            var ak = new List<string>(om.Keys);
                            if (string.Join(",", ak) != string.Join(",", keys)) s.Fail(path + ": key order " + string.Join(",", ak) + " != JS " + string.Join(",", keys));
                        }
                        return;
                    }
            }
            s.Fail(path + ": unsupported JSON kind " + e.ValueKind);
        }

        static string Describe(object a) => a == null ? "null" : a is string s ? "\"" + s + "\"" : a is IList l ? "array[" + l.Count + "]" : a is IDictionary ? "object" : a.GetType().Name + " " + Convert.ToString(a, CultureInfo.InvariantCulture);
    }
}
