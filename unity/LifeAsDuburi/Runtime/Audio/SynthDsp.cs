// Offline DSP for the Editor / desktop / Android sound (SynthSound.cs): every
// sound of src/ui/audio.js is rendered once into an AudioClip (AudioClip.Create
// with stream = false + SetData) and then played by pooled AudioSources. The
// small node graph below mirrors the Web Audio nodes the JS one-shots use
// (oscillators, looping noise buffers, biquad filters, gains with
// setValueAtTime / linearRamp / exponentialRamp / setValueCurve automation), so
// the one-shot recipes are ported almost line by line.
// Nothing here runs per frame; WebGL builds use the JS engine itself (DuburiAudio.jslib).

using System;
using System.Collections.Generic;

namespace Duburi.Game
{
    // An AudioParam timeline (Web Audio semantics, enough for the one-shots).
    sealed class SynthParam
    {
        const int SetEv = 0, LinEv = 1, ExpEv = 2, CurveEv = 3;
        struct Ev { public int type; public double t; public float v; public float[] curve; public double dur; }

        readonly List<Ev> ev = new List<Ev>(8);
        float value;
        int cursor;

        public SynthParam(float value) { this.value = value; }

        public float Value { set => this.value = value; }
        public void SetValueAtTime(float v, double t) => ev.Add(new Ev { type = SetEv, t = t, v = v });
        public void LinearRampToValueAtTime(float v, double t) => ev.Add(new Ev { type = LinEv, t = t, v = v });
        public void ExponentialRampToValueAtTime(float v, double t) => ev.Add(new Ev { type = ExpEv, t = t, v = v });
        public void SetValueCurveAtTime(float[] c, double t, double dur)
        {
            ev.Add(new Ev { type = CurveEv, t = t, v = c[c.Length - 1], curve = c, dur = dur });
        }

        public void Prepare()
        {
            // stable sort by time (insertion order breaks ties, as in Web Audio)
            for (int i = 1; i < ev.Count; i++)
            {
                var e = ev[i];
                int j = i - 1;
                while (j >= 0 && ev[j].t > e.t) { ev[j + 1] = ev[j]; j--; }
                ev[j + 1] = e;
            }
            cursor = -1;
        }

        // value at time t; t must not decrease between calls
        public float At(double t)
        {
            while (cursor + 1 < ev.Count && ev[cursor + 1].t <= t) cursor++;

            double t0; float v0;
            if (cursor < 0) { t0 = 0; v0 = value; }
            else
            {
                var e = ev[cursor];
                if (e.type == CurveEv)
                {
                    double x = (t - e.t) / Math.Max(1e-9, e.dur);
                    if (x < 1)
                    {
                        double p = Math.Max(0, x) * (e.curve.Length - 1);
                        int i = (int)p;
                        float f = (float)(p - i);
                        return i + 1 < e.curve.Length ? e.curve[i] + (e.curve[i + 1] - e.curve[i]) * f : e.curve[i];
                    }
                    t0 = e.t + e.dur; v0 = e.v;
                }
                else { t0 = e.t; v0 = e.v; }
            }
            if (cursor + 1 < ev.Count)
            {
                var n = ev[cursor + 1];
                if (n.type == LinEv)
                {
                    double x = (t - t0) / Math.Max(1e-9, n.t - t0);
                    return (float)(v0 + (n.v - v0) * Math.Max(0, Math.Min(1, x)));
                }
                if (n.type == ExpEv)
                {
                    if (v0 == 0 || n.v == 0 || (v0 > 0) != (n.v > 0)) return v0;
                    double x = (t - t0) / Math.Max(1e-9, n.t - t0);
                    return (float)(v0 * Math.Pow(n.v / v0, Math.Max(0, Math.Min(1, x))));
                }
            }
            return v0;
        }
    }

    abstract class SynthNode
    {
        public readonly List<SynthNode> inputs = new List<SynthNode>(2);
        public double start = 0, stop = double.MaxValue;
        public virtual void Prepare() { foreach (var n in inputs) n.Prepare(); }
        public abstract float Process(double t);
        protected float Inputs(double t)
        {
            float s = 0;
            for (int i = 0; i < inputs.Count; i++) s += inputs[i].Process(t);
            return s;
        }
    }

    sealed class SynthOsc : SynthNode
    {
        public readonly SynthParam frequency;
        readonly string type;
        readonly float sr;
        double phase;

        public SynthOsc(string type, float f, float sr) { this.type = type; this.sr = sr; frequency = new SynthParam(f); }
        public override void Prepare() { frequency.Prepare(); }

        public override float Process(double t)
        {
            float f = frequency.At(t);
            if (t < start || t >= stop) return 0;
            double dp = f / sr;
            double p = phase;
            phase += dp;
            phase -= Math.Floor(phase);
            switch (type)
            {
                case "triangle": return (float)(p < 0.5 ? 4 * p - 1 : 3 - 4 * p);
                case "sawtooth":
                    {
                        // band-limited (polyBLEP) like the Web Audio oscillator
                        double v = 2 * p - 1;
                        if (p < dp) { double x = p / dp; v -= x + x - x * x - 1; }
                        else if (p > 1 - dp) { double x = (p - 1) / dp; v -= x * x + x + x + 1; }
                        return (float)v;
                    }
                case "square": return p < 0.5 ? 1f : -1f;
                default: return (float)Math.Sin(2 * Math.PI * p);
            }
        }
    }

    sealed class SynthNoise : SynthNode
    {
        readonly float[] buf;
        int pos;
        public SynthNoise(float[] buf, int offset) { this.buf = buf; pos = offset % buf.Length; }
        public override void Prepare() { }
        public override float Process(double t)
        {
            if (t < start || t >= stop) return 0;
            float v = buf[pos];
            if (++pos >= buf.Length) pos = 0;
            return v;
        }
    }

    sealed class SynthGain : SynthNode
    {
        public readonly SynthParam gain = new SynthParam(0);
        public override void Prepare() { gain.Prepare(); base.Prepare(); }
        public override float Process(double t) => Inputs(t) * gain.At(t);
    }

    // RBJ biquad with the Web Audio conventions (lowpass / highpass Q in dB, bandpass Q linear)
    sealed class SynthBiquad : SynthNode
    {
        public readonly SynthParam frequency;
        readonly string type;
        readonly float q, sr;
        double b0, b1, b2, a1, a2, x1, x2, y1, y2;
        int count;

        public SynthBiquad(string type, float f, float q, float sr) { this.type = type; this.q = q; this.sr = sr; frequency = new SynthParam(f); }
        public override void Prepare() { frequency.Prepare(); base.Prepare(); count = 0; }

        public override float Process(double t)
        {
            if ((count++ & 15) == 0) Coeffs(frequency.At(t));
            else frequency.At(t);
            double x = Inputs(t);
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return (float)y;
        }

        void Coeffs(float f)
        {
            Design(type, f, q, sr, out b0, out b1, out b2, out a1, out a2);
        }

        public static void Design(string type, float f, float q, float sr, out double b0, out double b1, out double b2, out double a1, out double a2)
        {
            double w0 = 2 * Math.PI * Math.Max(1, Math.Min(f, sr * 0.49)) / sr;
            double cs = Math.Cos(w0), sn = Math.Sin(w0);
            double a0, alpha;
            switch (type)
            {
                case "highpass":
                    alpha = sn / (2 * Math.Pow(10, q / 20));
                    b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2;
                    break;
                case "bandpass":
                    alpha = sn / (2 * Math.Max(1e-4, q));
                    b0 = alpha; b1 = 0; b2 = -alpha;
                    break;
                default: // lowpass
                    alpha = sn / (2 * Math.Pow(10, q / 20));
                    b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2;
                    break;
            }
            a0 = 1 + alpha; a1 = -2 * cs; a2 = 1 - alpha;
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
        }
    }

    // One one-shot being built: the JS _shot(prio, cost, build) record.
    sealed class SynthShot
    {
        public readonly float sr;
        public readonly Random rng;
        public readonly SynthNoises noise;
        public readonly List<SynthNode> outputs = new List<SynthNode>(4);
        readonly List<SynthNode> sources = new List<SynthNode>(8);
        public double end;

        public SynthShot(float sr, Random rng, SynthNoises noise) { this.sr = sr; this.rng = rng; this.noise = noise; }

        public float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public SynthOsc Osc(string type, float f, double t)
        {
            var o = new SynthOsc(type, f, sr) { start = t };
            o.frequency.SetValueAtTime(f, t);
            sources.Add(o);
            return o;
        }

        public SynthNoise Noise(string kind, double t)
        {
            var buf = noise.Get(kind);
            var s = new SynthNoise(buf, (int)(rng.NextDouble() * Math.Max(1, buf.Length - 0.8 * sr))) { start = t };
            sources.Add(s);
            return s;
        }

        public SynthGain Gain() => new SynthGain();
        public SynthBiquad Filt(string type, float f, float q) => new SynthBiquad(type, f, q, sr);

        public void Pluck(SynthParam p, double t, float peak, double a, double d)
        {
            float pk = Math.Max(0.0002f, peak);
            p.SetValueAtTime(0.0001f, t);
            p.LinearRampToValueAtTime(pk, t + a);
            p.ExponentialRampToValueAtTime(0.0001f, t + a + d);
        }

        // connect a -> b -> c ...; the last node goes to the output (the JS bus)
        public void Chain(params SynthNode[] nodes)
        {
            SynthNode prev = null;
            foreach (var n in nodes)
            {
                if (n == null) continue;
                if (prev != null) n.inputs.Add(prev);
                prev = n;
            }
            if (prev != null) outputs.Add(prev);
        }

        // mono samples; tail seconds of silence / reverb room after r.end
        public float[] Render(double tail)
        {
            foreach (var s in sources) s.stop = end;
            foreach (var o in outputs) o.Prepare();
            int n = Math.Max(16, (int)Math.Ceiling((end + tail) * sr));
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                float v = 0;
                for (int k = 0; k < outputs.Count; k++) v += outputs[k].Process(t);
                buf[i] = v;
            }
            return buf;
        }
    }

    // The looping noise buffers of the JS engine (white 2.3 s, pink 3.1 s, brown 3.7 s).
    sealed class SynthNoises
    {
        readonly float sr;
        readonly Random rng;
        float[] white, pink, brown;
        public SynthNoises(float sr, Random rng) { this.sr = sr; this.rng = rng; }

        public float[] Get(string kind)
        {
            switch (kind)
            {
                case "pink": return pink ??= SynthDsp.MakeNoise(sr, 3.1f, "pink", 0.25f, rng);
                case "brown": return brown ??= SynthDsp.MakeNoise(sr, 3.7f, "brown", 0.3f, rng);
                default: return white ??= SynthDsp.MakeNoise(sr, 2.3f, "white", 0.3f, rng);
            }
        }
    }

    static class SynthDsp
    {
        static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;

        // Looping mono noise buffer with a crossfaded seam, normalised to an RMS level.
        public static float[] MakeNoise(float sr, float seconds, string kind, float rms, Random rng)
        {
            int N = Math.Max(256, (int)(sr * seconds)), F = Math.Min(N >> 2, (int)(sr * 0.05f));
            var tmp = new float[N + F];
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0, last = 0, mean = 0;
            for (int i = 0; i < N + F; i++)
            {
                double w = rng.NextDouble() * 2 - 1;
                double v;
                if (kind == "pink")
                { // Paul Kellet's refined filter
                    b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759;
                    b2 = 0.969 * b2 + w * 0.153852; b3 = 0.8665 * b3 + w * 0.3104856;
                    b4 = 0.55 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.016898;
                    v = b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362; b6 = w * 0.115926;
                }
                else if (kind == "brown") { last = (last + 0.02 * w) / 1.02; v = last; }
                else v = w;
                tmp[i] = (float)v; mean += v;
            }
            mean /= N + F;
            var d = new float[N];
            double ss = 0;
            for (int i = 0; i < N; i++)
            {
                double v = tmp[i] - mean;
                if (i < F) { double x = i / (double)F; v = v * x + (tmp[N + i] - mean) * (1 - x); }
                d[i] = (float)v; ss += v * v;
            }
            double k = rms / Math.Max(1e-9, Math.Sqrt(ss / N));
            for (int i = 0; i < N; i++) d[i] = Clamp((float)(d[i] * k), -1, 1);
            return d;
        }

        // A looping noise bed through a fixed biquad. The filter runs over the loop
        // twice and keeps the second pass, so the seam stays continuous.
        public static float[] Filtered(float[] src, string type, float f, float q, float sr)
        {
            SynthBiquad.Design(type, f, q, sr, out var b0, out var b1, out var b2, out var a1, out var a2);
            var o = new float[src.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < src.Length; i++)
                {
                    double x = src[i];
                    double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    if (pass == 1) o[i] = (float)y;
                }
            return o;
        }

        // One PeriodicWave at the shaft rate holding the blade-pass harmonics, the
        // electrical line and the commutation whine (all integer multiples), as a
        // loop of whole periods. Returns the loop and its exact fundamental.
        public static float[] ThrusterWave(SynthSize S, int blades, float refHz, float sr, out float f0)
        {
            int P = Math.Max(8, (int)Math.Round(sr / refHz));
            f0 = sr / P;
            int p = S.pole, H = Math.Max(6 * p, 4 * blades) + 1;
            var re = new double[H + 1];
            var im = new double[H + 1];
            void Add(int h, double a)
            {
                if (h >= 1 && h <= H) { double ph = h * 2.399963; re[h] += a * Math.Cos(ph); im[h] += a * Math.Sin(ph); }
            }
            Add(1, S.shaft); Add(2, S.shaft * 0.4);
            for (int i = 0; i < S.bp.Length; i++) Add(blades * (i + 1), S.bp[i]);
            Add(p, S.el); Add(2 * p, S.el2); Add(6 * p, S.comm);
            int periods = Math.Max(1, (int)Math.Round(2048.0 / P));
            var w = new float[P * periods];
            float peak = 1e-9f;
            for (int i = 0; i < P; i++)
            {
                double x = 2 * Math.PI * i / P, v = 0;
                for (int h = 1; h <= H; h++)
                {
                    if (h * f0 > sr * 0.45f) break; // keep below Nyquist when pitched up
                    // Web Audio: real -> cosine terms, imag -> sine terms
                    v += re[h] * Math.Cos(h * x) + im[h] * Math.Sin(h * x);
                }
                w[i] = (float)v;
                if (Math.Abs(w[i]) > peak) peak = Math.Abs(w[i]);
            }
            // the PeriodicWave is normalised to a peak of 1
            for (int i = 0; i < P; i++) w[i] /= peak;
            for (int k = 1; k < periods; k++) Array.Copy(w, 0, w, k * P, P);
            return w;
        }

        // "Underwater room" for clips on the water path: a short dark Schroeder
        // reverb (~0.6 s) mixed in at the JS wet level (0.32).
        public static void WaterRoom(float[] buf, float sr, float wet)
        {
            int[] combs = { (int)(0.0297f * sr), (int)(0.0371f * sr), (int)(0.0411f * sr), (int)(0.0437f * sr) };
            const float rt = 0.6f;
            var outBuf = new float[buf.Length];
            foreach (int d in combs)
            {
                var line = new float[d];
                float g = (float)Math.Pow(10, -3.0 * d / sr / rt);
                float lp = 0;
                int pos = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    float y = line[pos];
                    lp += 0.45f * (y - lp); // high frequencies die first
                    line[pos] = buf[i] + g * lp;
                    if (++pos >= d) pos = 0;
                    outBuf[i] += y * 0.25f;
                }
            }
            int[] aps = { (int)(0.005f * sr), (int)(0.0017f * sr) };
            foreach (int d in aps)
            {
                var line = new float[Math.Max(1, d)];
                int pos = 0;
                for (int i = 0; i < outBuf.Length; i++)
                {
                    float bufout = line[pos];
                    float x = outBuf[i];
                    float y = -0.7f * x + bufout;
                    line[pos] = x + 0.7f * y;
                    if (++pos >= line.Length) pos = 0;
                    outBuf[i] = y;
                }
            }
            for (int i = 0; i < buf.Length; i++) buf[i] += wet * outBuf[i];
        }

        // one-pole low-pass, for baking the "through the water" colour
        public static void OnePoleLowPass(float[] buf, float f, float sr)
        {
            float a = 1 - (float)Math.Exp(-2 * Math.PI * f / sr), y = 0;
            for (int i = 0; i < buf.Length; i++) { y += a * (buf[i] - y); buf[i] = y; }
        }
    }

    // per thruster size: pole pairs, loudness, spectrum (shaft, blade-pass harmonics,
    // electrical line, its 2nd harmonic, commutation whine), wash colour, cavitation
    sealed class SynthSize
    {
        public string name; public int pole; public float tone, wash, washF, cav, shaft; public float[] bp; public float el, el2, comm;

        public static readonly SynthSize Small = new SynthSize { name = "s", pole = 6, tone = 0.75f, wash = 0.7f, washF = 1.35f, cav = 0.7f, shaft = 0.06f, bp = new[] { 0.65f, 0.3f, 0.14f, 0.05f }, el = 0.34f, el2 = 0.1f, comm = 0.18f };
        public static readonly SynthSize T200 = new SynthSize { name = "m", pole = 7, tone = 1f, wash = 1f, washF = 1f, cav = 1f, shaft = 0.14f, bp = new[] { 1f, 0.45f, 0.2f, 0.08f }, el = 0.2f, el2 = 0.06f, comm = 0.1f };
        public static readonly SynthSize T500 = new SynthSize { name = "l", pole = 7, tone = 1.25f, wash = 1.4f, washF = 0.72f, cav = 1.2f, shaft = 0.26f, bp = new[] { 1f, 0.6f, 0.3f, 0.14f }, el = 0.13f, el2 = 0.05f, comm = 0.06f };

        public static SynthSize Of(string size) => size == "small" ? Small : size == "t500" ? T500 : T200;
    }
}
