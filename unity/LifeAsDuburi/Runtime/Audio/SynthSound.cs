// Editor / desktop / Android sound: a C# version of src/ui/audio.js built from
// AudioClips rendered once (SynthDsp.cs) and pooled AudioSources. It follows the
// JS engine's mix (same gains, glide times, thruster model, ambience timers)
// with Unity's own tools in place of the Web Audio graph:
//   * a PeriodicWave oscillator at the shaft rate  -> a looping clip of whole
//     periods of the same spectrum, its pitch following the rotor speed
//   * the master / thruster low-pass filters        -> AudioLowPassFilter components
//   * band-passed noise with a moving centre        -> pre-filtered noise loops
//     whose pitch moves the band
//   * the convolution reverb                        -> a short dark reverb baked
//     into the one-shots that travel through the water
// No audio-thread callbacks (not available on WebGL and costly elsewhere).
//
// Thrusters. A brushless thruster in water produces
//   * a blade-pass hum at f_bp = rpm/60 * blades (T200 at 3500 rpm with 3 blades
//     is about 175 Hz) plus a few harmonics,
//   * the motor's electrical line at rpm/60 * pole pairs (7 on a T200, ~408 Hz)
//     and the six-step commutation ripple at 6x that (~2.4 kHz), the thin ESC
//     whine that rises with speed,
//   * broadband prop-wash noise (band-passed 150-900 Hz, amplitude ~ |n|^1.5),
//   * cavitation crackle close to full thrust.
// Every tonal line is an integer multiple of the shaft rate rpm/60, so ONE
// looping wave at the shaft rate makes all of them, and changing its pitch
// glides the whole spectrum. Each own vehicle gets two of these voices
// (energy-weighted mean speed and max speed, slightly detuned). Their beating
// is the warble of several thrusters that never spin at quite the same speed.
//
// Water. Under water everything passes a master low-pass (2.5 kHz near the
// surface, ~1.6 kHz deep). Above water the low-pass opens and the thrusters are
// heard faintly through the surface.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Duburi.Game
{
    sealed class SynthSound : SoundEngine
    {
        const int OwnSlots = 4;
        const int OtherSlots = 3;
        const float Glide = 0.08f;   // spin-up / spin-down time constant (s)
        const float IdleFree = 2.5f; // seconds of silence before a voice is stopped
        const int WaterPoolSize = 14;
        const int DryPoolSize = 8;

        // a parameter gliding to its target (Web Audio setTargetAtTime)
        sealed class Smooth
        {
            public float v, target, tau = 0.1f;
            public Smooth(float v) { this.v = target = v; }
            public void To(float t, float tau)
            {
                if (float.IsNaN(t) || float.IsInfinity(t)) return;
                target = t; this.tau = Mathf.Max(1e-4f, tau);
            }
            public void Jump(float t) { if (!float.IsNaN(t) && !float.IsInfinity(t)) v = target = t; }
            public void Step(float dt) { v += (target - v) * (1f - Mathf.Exp(-dt / tau)); }
        }

        sealed class Chan
        {
            public GameObject go;
            public AudioSource src;
            public AudioLowPassFilter lpf;
            public float started;
            public int prio;
        }

        sealed class ToneClip { public AudioClip clip; public float f0; }

        sealed class OwnVoice
        {
            public Chan a, b;
            public bool on;
            public ToneClip wave;
            public readonly Smooth fa = new Smooth(3), fb = new Smooth(3), g = new Smooth(0);
            public float level, idle, ph;
            public readonly sbyte[] signs = new sbyte[32];
            public float lastClunk = -1f;
        }

        sealed class OtherVoice
        {
            public Chan tone, wash;
            public bool on, fresh;
            public ToneClip wave;
            public readonly Smooth f = new Smooth(3), tg = new Smooth(0), lp = new Smooth(2400), pan = new Smooth(0);
            public int key = -1, claim = -1;
            public float level, idle, ph;
        }

        struct Stats { public int cnt; public float mean, max, pow, wmean, lvl; }

        readonly float sr;
        readonly System.Random rng = new System.Random();
        readonly SynthNoises noises;
        GameObject root;
        SoundDriver driver;
        bool started, muted, disposed;
        const float Volume = 0.6f;

        // persistent beds
        Chan amb, air, wash, cav;
        readonly Smooth wlpF = new Smooth(2300), thr = new Smooth(1), tlpF = new Smooth(8000);
        readonly Smooth ambGain = new Smooth(0), ambF = new Smooth(300), airGain = new Smooth(0), airF = new Smooth(700);
        readonly Smooth washGain = new Smooth(0), washF = new Smooth(350), cavGain = new Smooth(0);
        float cavPitch = 1f;
        bool cavOn;

        readonly OwnVoice[] own = new OwnVoice[OwnSlots];
        readonly OtherVoice[] oth = new OtherVoice[OtherSlots];
        readonly int[] topI = new int[OtherSlots];
        readonly float[] topL = new float[OtherSlots];
        readonly Chan[] waterPool = new Chan[WaterPoolSize];
        readonly Chan[] dryPool = new Chan[DryPoolSize];
        readonly Dictionary<int, ToneClip> toneClips = new Dictionary<int, ToneClip>();
        readonly Dictionary<string, AudioClip[]> shotClips = new Dictionary<string, AudioClip[]>();
        readonly List<AudioClip> allClips = new List<AudioClip>();

        // legacy motor() drive
        readonly List<VehicleSound> mveh = new List<VehicleSound>(1);
        readonly VehicleSound motorVeh = new VehicleSound { n = new float[1], maxRpm = 3500, blades = 3, size = "t200", dist = 0, pan = 0 };

        float updWall = -1e9f, vehWall = -1e9f, motorWall = -1e9f, staleAt;
        bool stale;
        float clk, ambT, swellT, blorpT = 1f, creakT;
        float lastBump = -1f, lastBumpK, lastSplash = -1f;
        float depthNow;
        Stats st;

        static float Now => Time.realtimeSinceStartup;
        static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        static float Smoothstep(float x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
        float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static bool IsOwn(VehicleSound v) => !(v.dist > 0.05f); // dist 0 = the listener's own vehicle; its pan is ignored

        public SynthSound(Transform parent)
        {
            sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            noises = new SynthNoises(sr, rng);
            creakT = Rnd(12, 30);
            mveh.Add(motorVeh);

            root = new GameObject("Sound");
            if (parent != null) root.transform.SetParent(parent, false);
            // all sound is 2D (pan by panStereo): the one listener lives here
            root.AddComponent<AudioListener>();
            driver = root.AddComponent<SoundDriver>();
            driver.owner = this;

            // ambience bed: brown noise, low-passed, slow swell driven from Tick()
            amb = MakeChan("Ambience", Loop("amb", SynthDsp.Filtered(noises.Get("brown"), "lowpass", 300, 0, sr)), true);
            // air bed (above water), unmuffled
            air = MakeChan("Air", Loop("air", SynthDsp.Filtered(noises.Get("pink"), "bandpass", 700, 0.5f, sr)), false);
            // prop wash of the own vehicles (one shared band: they are all at the listener)
            wash = MakeChan("Wash", Loop("wash", SynthDsp.Filtered(noises.Get("pink"), "bandpass", 350, 0.6f, sr)), true);
            // cavitation crackle (own vehicles), gated from Tick()
            cav = MakeChan("Cavitation", Loop("cav", SynthDsp.Filtered(noises.Get("white"), "highpass", 1300, 0, sr)), true);

            for (int i = 0; i < OwnSlots; i++)
                own[i] = new OwnVoice { a = MakeChan("Own" + i + "a", null, true), b = MakeChan("Own" + i + "b", null, true), ph = Rnd(0, 6.28f) };
            var pinkLoop = Loop("pink", noises.Get("pink"));
            for (int i = 0; i < OtherSlots; i++)
                oth[i] = new OtherVoice { tone = MakeChan("Other" + i, null, true), wash = MakeChan("Other" + i + "wash", pinkLoop, true), ph = Rnd(0, 6.28f) };
            for (int i = 0; i < WaterPoolSize; i++) waterPool[i] = MakeChan("Water" + i, null, true);
            for (int i = 0; i < DryPoolSize; i++) dryPool[i] = MakeChan("Dry" + i, null, false);
        }

        Chan MakeChan(string name, AudioClip clip, bool lowPass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = clip != null;
            src.clip = clip;
            src.volume = 0f;
            src.dopplerLevel = 0f;
            AudioLowPassFilter lpf = null;
            if (lowPass)
            {
                lpf = go.AddComponent<AudioLowPassFilter>();
                lpf.cutoffFrequency = 8000f;
                lpf.lowpassResonanceQ = 1f;
            }
            return new Chan { go = go, src = src, lpf = lpf };
        }

        AudioClip Loop(string name, float[] data) => MakeClip("duburi-" + name, data);

        AudioClip MakeClip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, (int)sr, false);
            c.SetData(data, 0);
            allClips.Add(c);
            return c;
        }

        // ---- lifecycle -------------------------------------------------------------

        public override void Ensure()
        {
            if (disposed || started) return;
            started = true;
            if (!muted) StartBeds();
        }

        void StartBeds()
        {
            foreach (var c in new[] { amb, air, wash, cav })
            {
                if (c.src.isPlaying) continue;
                c.src.time = Rnd(0, c.src.clip.length * 0.9f);
                c.src.Play();
            }
        }

        public override void SetMuted(bool m)
        {
            muted = m;
            if (disposed) return;
            if (muted) StopAll();
            else if (started) StartBeds();
        }

        void StopAll()
        {
            foreach (var c in new[] { amb, air, wash, cav }) c.src.Stop();
            foreach (var s in own) if (s.on) FreeOwn(s);
            foreach (var s in oth) if (s.on) FreeOther(s);
            foreach (var c in waterPool) c.src.Stop();
            foreach (var c in dryPool) c.src.Stop();
        }

        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (driver != null) driver.owner = null;
            foreach (var c in allClips) DestroyObj(c);
            foreach (var t in toneClips.Values) DestroyObj(t.clip);
            allClips.Clear();
            toneClips.Clear();
            shotClips.Clear();
            if (root != null) DestroyObj(root);
            root = null;
        }

        internal void OnDriverDestroyed()
        {
            // the scene went away with our GameObject
            if (!disposed) { root = null; Dispose(); }
        }

        static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        // ---- per-frame drive ---------------------------------------------------------

        public override void Tick(float dt, SoundFrame L)
        {
            if (disposed) return;
            Ensure(); // no gesture rule outside the browser
            float now = Now;
            updWall = now;
            stale = false;
            if (muted) return;
            dt = Clamp(float.IsNaN(dt) ? 0 : dt, 0, 0.1f);
            bool under = L.underwater;
            float depth = Mathf.Max(0, float.IsNaN(L.depth) ? 0 : L.depth);
            depthNow = depth;
            bool paused = L.paused;
            vehWall = now;
            // water: master low-pass, thruster bus
            wlpF.To(under ? 2500 - 900 * Clamp(depth / 8, 0, 1) : 9000, 0.12f);
            thr.To(paused ? 0 : under ? 1 : 0.14f, paused ? 0.25f : 0.12f);
            tlpF.To(under ? 8000 : 800, 0.12f);
            Thrusters(dt, L.vehicles, paused, under);
            Ambient(dt, under, depth, paused);
        }

        public override void Motor(float level)
        {
            if (disposed) return;
            float now = Now;
            if (now - vehWall < 0.5f) return;
            float dt = Clamp(now - motorWall, 0, 0.1f);
            motorVeh.n[0] = Clamp(float.IsNaN(level) ? 0 : level, 0, 1);
            motorWall = now;
            stale = false;
            if (now - updWall < 0.5f || muted) return; // Tick() picks it up
            Ensure();
            thr.To(1, 0.1f);
            Thrusters(dt, mveh, false, true);
        }

        void VStats(float[] n, ref Stats o)
        {
            int cnt = 0;
            float sum = 0, mx = 0, p3 = 0, w1 = 0, w2 = 0;
            int len = n != null ? Mathf.Min(n.Length, 64) : 0;
            for (int i = 0; i < len; i++)
            {
                float a = Mathf.Abs(n[i]);
                if (!(a > 0)) a = 0; else if (a > 1) a = 1;
                cnt++; sum += a; if (a > mx) mx = a;
                float a15 = a * Mathf.Sqrt(a);
                p3 += a15 * a15; w1 += a15; w2 += a15 * a;
            }
            o.cnt = cnt;
            o.mean = cnt > 0 ? sum / cnt : 0;
            o.max = mx;
            o.pow = cnt > 0 ? p3 / cnt : 0;
            o.wmean = w1 > 1e-9f ? w2 / w1 : 0;
            o.lvl = 0.65f * o.mean + 0.35f * mx;
        }

        static float Rps(VehicleSound v) => Clamp(v.maxRpm > 0 ? v.maxRpm : 3500, 300, 30000) / 60f;
        static int Blades(VehicleSound v) => (int)Clamp(v.blades != 0 ? v.blades : 3, 2, 8);

        void Thrusters(float dt, List<VehicleSound> veh, bool paused, bool under)
        {
            clk += dt;
            float now = Now;
            // own vehicles (dist 0): up to 4, mixed with a total cap
            int nOwn = 0;
            for (int i = 0; i < veh.Count; i++) if (veh[i] != null && IsOwn(veh[i])) nOwn++;
            if (nOwn > OwnSlots) nOwn = OwnSlots;
            float norm = nOwn > 1 ? Mathf.Pow(nOwn, -0.4f) : 1;
            int k = 0;
            float washP = 0, washFs = 0, washW = 0, cavK = 0;
            for (int i = 0; i < veh.Count && k < OwnSlots; i++)
            {
                var v = veh[i];
                if (v == null || !IsOwn(v)) continue;
                var s = own[k++];
                VStats(v.n, ref st);
                var S = SynthSize.Of(v.size);
                float rps = Rps(v);
                int blades = Blades(v);
                float tone = paused ? 0 : 0.11f * S.tone * Mathf.Pow(st.lvl, 1.2f) * norm;
                if (!s.on && tone > 0.0006f) MkOwn(s);
                if (s.on)
                {
                    var w = Tone(S, blades, rps);
                    if (s.wave != w) { s.wave = w; SetLoop(s.a, w.clip); SetLoop(s.b, w.clip); }
                    float wob = 1 + 0.0025f * Mathf.Sin(clk * 6.1f + s.ph) + 0.0015f * Mathf.Sin(clk * 13.7f + 2 * s.ph);
                    s.fa.To(Mathf.Max(3, st.wmean * rps) * wob, Glide);
                    s.fb.To(Mathf.Max(3, st.max * rps) * (1.0045f + 0.0035f * Mathf.Sin(clk * 0.9f + s.ph)), Glide);
                    s.g.To(tone, 0.06f);
                }
                IdleOwn(s, tone, dt);
                float wa = paused ? 0 : Mathf.Sqrt(st.pow) * S.wash * Mathf.Sqrt(Clamp(st.cnt / 6f, 0.5f, 1.6f)) * norm;
                washP += wa * wa; washFs += wa * (180 + 340 * st.lvl) * S.washF; washW += wa;
                if (!paused)
                {
                    cavK = Mathf.Max(cavK, Smoothstep((st.max - 0.7f) / 0.3f) * S.cav);
                    Reversal(v.n, s, now);
                }
            }
            for (; k < OwnSlots; k++)
            {
                var s = own[k];
                if (s.on) s.g.To(0, 0.08f);
                IdleOwn(s, 0, dt);
                Array.Clear(s.signs, 0, s.signs.Length);
            }
            washGain.To(0.6f * Mathf.Sqrt(washP), 0.07f);
            if (washW > 1e-4f) washF.To(washFs / washW, 0.1f);
            // cavitation: randomly gated high-passed noise
            if (cavK > 0.01f && under)
            {
                float g = rng.NextDouble() > 0.6 ? cavK * 0.2f * Rnd(0.3f, 1) : cavK * 0.015f;
                cavGain.To(g, 0.004f);
                cavPitch = Rnd(1000, 2500) / 1300f;
                cavOn = true;
            }
            else if (cavOn) { cavOn = false; cavGain.To(0, 0.03f); }

            // other vehicles: the loudest few, distance-attenuated and low-passed
            int K = Mathf.Min(OtherSlots, Mathf.Max(1, 4 - nOwn)); // fewer when split screen is busy
            for (int q = 0; q < OtherSlots; q++) { topI[q] = -1; topL[q] = 0; }
            for (int i = 0; i < veh.Count; i++)
            {
                var v = veh[i];
                if (v == null || IsOwn(v)) continue;
                VStats(v.n, ref st);
                var S = SynthSize.Of(v.size);
                float loud = (st.lvl * S.tone + Mathf.Sqrt(st.pow) * S.wash) / (1 + Mathf.Max(0, v.dist) / 2.5f);
                if (!(loud > 0.004f)) continue;
                for (int q = 0; q < K; q++)
                {
                    if (loud > topL[q])
                    {
                        for (int r = K - 1; r > q; r--) { topI[r] = topI[r - 1]; topL[r] = topL[r - 1]; }
                        topI[q] = i; topL[q] = loud;
                        break;
                    }
                }
            }
            foreach (var s in oth) s.claim = -1;
            for (int q = 0; q < K; q++)
            { // keep vehicles on the slot they already have
                int i = topI[q];
                if (i < 0) continue;
                foreach (var s in oth) if (s.key == i && s.claim < 0) { s.claim = i; break; }
            }
            for (int q = 0; q < K; q++)
            { // then give the rest the quietest free slot
                int i = topI[q];
                if (i < 0) continue;
                bool have = false;
                foreach (var s in oth) if (s.claim == i) have = true;
                if (have) continue;
                OtherVoice best = null;
                foreach (var s in oth) if (s.claim < 0 && (best == null || s.level < best.level)) best = s;
                if (best == null) break;
                best.claim = i; best.key = i; best.fresh = true;
            }
            foreach (var s in oth)
            {
                if (s.claim >= 0) DriveOther(s, veh[s.claim], dt, paused);
                else
                {
                    if (s.on) s.tg.To(0, 0.1f);
                    IdleOther(s, 0, dt);
                }
            }
        }

        void DriveOther(OtherVoice s, VehicleSound v, float dt, bool paused)
        {
            VStats(v.n, ref st);
            var S = SynthSize.Of(v.size);
            float rps = Rps(v);
            int blades = Blades(v);
            float d = Mathf.Max(0, v.dist);
            float att = 0.55f / (1 + d / 2.5f);
            // tonal and wash share one gain: the thruster wave and the pink bed
            // (0.25 rms) enter it together, so their ratio is fixed by the sources
            float gain = paused ? 0 : 0.1f * att * (0.6f * S.tone * Mathf.Pow(st.lvl, 1.2f) + 0.4f * S.wash * Mathf.Sqrt(st.pow) * Mathf.Sqrt(Clamp(st.cnt / 6f, 0.5f, 1.6f)));
            float lpF = Clamp(2600 / (1 + d / 3), 180, 2400);
            float pan = Clamp(float.IsNaN(v.pan) ? 0 : v.pan, -1, 1);
            bool fresh = s.fresh;
            s.fresh = false;
            if (!s.on)
            {
                if (gain <= 0.0006f) { IdleOther(s, 0, dt); return; }
                MkOther(s, lpF, pan);
                fresh = true;
            }
            var w = Tone(S, blades, rps);
            if (s.wave != w) { s.wave = w; SetLoop(s.tone, w.clip); }
            float f = Mathf.Max(3, st.wmean * rps) * (1 + 0.003f * Mathf.Sin(clk * 5.3f + s.ph));
            if (fresh && s.level < 0.004f) s.f.Jump(f); // silent slot: jump, do not glide from the old vehicle's pitch
            else s.f.To(f, fresh ? 0.03f : Glide);
            s.tg.To(gain, 0.08f);
            s.lp.To(lpF, 0.15f);
            s.pan.To(pan, 0.08f);
            IdleOther(s, gain, dt);
        }

        // thruster reversal: the ESC brakes and the prop flips, a small 'tock'
        void Reversal(float[] n, OwnVoice s, float now)
        {
            if (n == null) return;
            var sg = s.signs;
            int len = Mathf.Min(n.Length, 32);
            float flip = 0;
            for (int i = 0; i < len; i++)
            {
                float x = float.IsNaN(n[i]) ? 0 : n[i];
                sbyte g = (sbyte)(x > 0.12f ? 1 : x < -0.12f ? -1 : 0);
                if (g != 0) { if (sg[i] != 0 && sg[i] != g) flip = Mathf.Max(flip, Mathf.Abs(x)); sg[i] = g; }
            }
            if (flip > 0 && now - s.lastClunk > 0.18f) { s.lastClunk = now; Clunk(0.4f + 0.6f * Mathf.Min(1, flip)); }
        }

        void Ambient(float dt, bool under, float depth, bool paused)
        {
            ambT += dt;
            swellT -= dt;
            if (swellT <= 0)
            {
                swellT = 0.1f;
                float a = ambT;
                float sw = 0.78f + 0.16f * Mathf.Sin(a * 0.38f) + 0.06f * Mathf.Sin(a * 0.11f + 1.3f);
                float quiet = paused ? 0.06f : 1;
                ambGain.To(under ? 0.14f * sw * (1 - 0.3f * Clamp(depth / 10, 0, 1)) * quiet : 0, 0.4f);
                ambF.To(250 + 90 * sw, 0.5f);
                float gust = 0.7f + 0.3f * Mathf.Sin(a * 0.23f) * Mathf.Sin(a * 0.07f + 0.5f);
                airGain.To(under ? 0 : 0.02f * gust * quiet, 0.3f);
                airF.To(500 + 350 * gust, 0.5f);
            }
            if (!under || paused) return;
            blorpT -= dt;
            if (blorpT <= 0) { blorpT = Rnd(0.3f, 2.5f); AmbBlorp(depth); }
            creakT -= dt;
            if (creakT <= 0) { creakT = Rnd(18, 55); if (rng.NextDouble() < 0.7) Groan(); }
        }

        // ---- voices ----------------------------------------------------------------

        void MkOwn(OwnVoice s)
        {
            s.fa.Jump(3); s.fb.Jump(3); s.g.Jump(0);
            s.on = true; s.idle = 0; s.wave = null; s.level = 0;
        }

        void FreeOwn(OwnVoice s)
        {
            s.a.src.Stop(); s.b.src.Stop();
            s.on = false; s.wave = null; s.level = 0; s.idle = 0;
        }

        void MkOther(OtherVoice s, float lpF, float pan)
        {
            s.f.Jump(3); s.tg.Jump(0); s.lp.Jump(lpF); s.pan.Jump(pan);
            s.on = true; s.idle = 0; s.wave = null; s.level = 0;
            s.wash.src.time = Rnd(0, s.wash.src.clip.length * 0.9f);
            s.wash.src.Play();
        }

        void FreeOther(OtherVoice s)
        {
            s.tone.src.Stop(); s.wash.src.Stop();
            s.on = false; s.wave = null; s.level = 0; s.idle = 0; s.key = -1;
        }

        void IdleOwn(OwnVoice s, float level, float dt)
        {
            s.level = level;
            if (level > 0.0006f) { s.idle = 0; return; }
            s.idle += dt;
            if (s.on && s.idle > IdleFree) FreeOwn(s);
        }

        void IdleOther(OtherVoice s, float level, float dt)
        {
            s.level = level;
            if (level > 0.0006f) { s.idle = 0; return; }
            s.idle += dt;
            if (s.on && s.idle > IdleFree) FreeOther(s);
        }

        void SetLoop(Chan c, AudioClip clip)
        {
            c.src.clip = clip;
            c.src.loop = true;
            c.src.time = Rnd(0, clip.length * 0.9f);
            c.src.Play();
        }

        // the thruster wave of one size / blade count at the vehicle's full shaft rate
        ToneClip Tone(SynthSize S, int blades, float rps)
        {
            int P = Mathf.Max(8, Mathf.RoundToInt(sr / rps));
            int key = (S == SynthSize.Small ? 0 : S == SynthSize.T500 ? 2 : 1) * 10000000 + blades * 1000000 + P;
            if (toneClips.TryGetValue(key, out var t)) return t;
            var data = SynthDsp.ThrusterWave(S, blades, rps, sr, out float f0);
            var c = AudioClip.Create("duburi-thruster-" + S.name + blades + "-" + P, data.Length, 1, (int)sr, false);
            c.SetData(data, 0);
            t = new ToneClip { clip = c, f0 = f0 };
            toneClips[key] = t;
            return t;
        }

        // glide everything and push it to the AudioSources (SoundDriver.Update)
        internal void Advance(float dt)
        {
            if (disposed || root == null) return;
            dt = Clamp(dt, 0, 0.25f);
            Watch();
            wlpF.Step(dt); thr.Step(dt); tlpF.Step(dt);
            ambGain.Step(dt); ambF.Step(dt); airGain.Step(dt); airF.Step(dt);
            washGain.Step(dt); washF.Step(dt); cavGain.Step(dt);
            float m = muted || !started ? 0 : Volume;
            float water = wlpF.v, thrCut = Mathf.Min(tlpF.v, water), bus = m * thr.v;

            amb.src.volume = m * ambGain.v; amb.src.pitch = Clamp(ambF.v / 300f, 0.2f, 3); amb.lpf.cutoffFrequency = water;
            air.src.volume = m * airGain.v; air.src.pitch = Clamp(airF.v / 700f, 0.2f, 3);
            wash.src.volume = bus * washGain.v; wash.src.pitch = Clamp(washF.v / 350f, 0.2f, 3); wash.lpf.cutoffFrequency = thrCut;
            cav.src.volume = bus * cavGain.v; cav.src.pitch = Clamp(cavPitch, 0.2f, 3); cav.lpf.cutoffFrequency = thrCut;

            foreach (var s in own)
            {
                if (!s.on) continue;
                s.fa.Step(dt); s.fb.Step(dt); s.g.Step(dt);
                float f0 = s.wave != null ? s.wave.f0 : 60f;
                s.a.src.volume = bus * s.g.v; s.a.src.pitch = Clamp(s.fa.v / f0, 0.01f, 3); s.a.lpf.cutoffFrequency = thrCut;
                s.b.src.volume = bus * s.g.v; s.b.src.pitch = Clamp(s.fb.v / f0, 0.01f, 3); s.b.lpf.cutoffFrequency = thrCut;
            }
            foreach (var s in oth)
            {
                if (!s.on) continue;
                s.f.Step(dt); s.tg.Step(dt); s.lp.Step(dt); s.pan.Step(dt);
                float f0 = s.wave != null ? s.wave.f0 : 60f;
                float cut = Mathf.Min(s.lp.v, thrCut);
                s.tone.src.volume = bus * s.tg.v; s.tone.src.pitch = Clamp(s.f.v / f0, 0.01f, 3);
                s.tone.src.panStereo = s.pan.v; s.tone.lpf.cutoffFrequency = cut;
                s.wash.src.volume = bus * s.tg.v; s.wash.src.panStereo = s.pan.v; s.wash.lpf.cutoffFrequency = cut;
            }
            foreach (var c in waterPool) if (c.src.isPlaying) c.lpf.cutoffFrequency = water;
        }

        // Fades the beds if Tick()/Motor() stop being called (menu, hidden app)
        // and releases idle voices.
        void Watch()
        {
            float now = Now;
            if (now - Mathf.Max(updWall, motorWall) <= 0.4f) return;
            if (!stale)
            {
                stale = true; staleAt = now;
                thr.To(0, 0.12f);
                ambGain.To(0, 0.3f);
                airGain.To(0, 0.3f);
                washGain.To(0, 0.1f);
                cavGain.To(0, 0.02f);
                cavOn = false;
            }
            else if (now - staleAt > 3f)
            {
                foreach (var s in own) if (s.on) FreeOwn(s);
                foreach (var s in oth) if (s.on) FreeOther(s);
            }
        }

        // ---- one-shots -------------------------------------------------------------

        // Play one variant of a rendered one-shot. prio: 0 ambient, 1 fx, 2 ui;
        // ambient sounds only take a free channel, the others steal the oldest.
        void Play(Chan[] pool, AudioClip clip, int prio, float volume, float pitch = 1f, float pan = 0f, float delay = 0f)
        {
            if (clip == null || muted || disposed || root == null) return;
            Ensure();
            Chan pick = null, oldest = null;
            foreach (var c in pool)
            {
                if (!c.src.isPlaying) { pick = c; break; }
                if (c.prio <= prio && (oldest == null || c.started < oldest.started)) oldest = c;
            }
            if (pick == null)
            {
                if (prio == 0 || oldest == null) return;
                pick = oldest;
                pick.src.Stop();
            }
            pick.prio = prio;
            pick.started = Now;
            var s = pick.src;
            s.clip = clip;
            s.loop = false;
            s.volume = Clamp(Volume * volume, 0, 1);
            s.pitch = Clamp(pitch, 0.05f, 3);
            s.panStereo = Clamp(pan, -1, 1);
            if (pick.lpf != null) pick.lpf.cutoffFrequency = wlpF.v;
            if (delay > 0) s.PlayDelayed(delay); else s.Play();
        }

        AudioClip[] Clips(string name)
        {
            if (shotClips.TryGetValue(name, out var c)) return c;
            c = Bake(name);
            shotClips[name] = c;
            return c;
        }

        AudioClip Pick(string name)
        {
            var c = Clips(name);
            return c.Length == 0 ? null : c[rng.Next(c.Length)];
        }

        // variant of a clip rendered at several levels: the one nearest to x
        AudioClip Nearest(string name, float[] levels, float x, out float level)
        {
            int best = 0;
            for (int i = 1; i < levels.Length; i++) if (Mathf.Abs(levels[i] - x) < Mathf.Abs(levels[best] - x)) best = i;
            level = levels[best];
            var c = Clips(name);
            int per = c.Length / levels.Length;
            return c[best * per + (per > 1 ? rng.Next(per) : 0)];
        }

        static readonly float[] SplashLevels = { 0.3f, 0.65f, 1f };
        static readonly float[] BubbleLevels = { 0.2f, 0.45f, 0.7f, 1f };
        static readonly float[] BumpLevels = { 0.25f, 0.5f, 0.75f, 1f };

        public override void Ping(float freqKHz, float loud, float pan)
        {
            float l = loud;
            if (muted || !(l >= 0.02f)) return;
            float f = Clamp(2500 + ((freqKHz > 0 ? freqKHz : 30) - 25) * 100, 2200, 4500);
            Play(dryPool, Pick("ping"), 2, Mathf.Min(1, l), f / 3000f, float.IsNaN(pan) ? 0 : pan);
        }

        public override void Splash(float strength)
        {
            if (muted) return;
            float s = Clamp(float.IsNaN(strength) ? 0 : strength, 0, 1);
            if (s < 0.02f) return;
            float now = Now;
            if (now - lastSplash < 0.35f) return; // bobbing at the surface
            lastSplash = now;
            var c = Nearest("splash", SplashLevels, s, out float sv);
            Play(dryPool, c, 1, (0.05f + 0.3f * s) / (0.05f + 0.3f * sv));
            BubblesAt(0.35f + 0.6f * s, 0.05f, 1);
        }

        public override void Bubbles(float amount)
        {
            if (muted) return;
            BubblesAt(amount, 0, 1);
        }

        void BubblesAt(float amount, float delay, int prio)
        {
            float a = Clamp(float.IsNaN(amount) ? 0 : amount, 0, 1);
            if (a < 0.02f) return;
            var c = Nearest("bubbles", BubbleLevels, a, out float av);
            Play(waterPool, c, prio, (0.05f + 0.07f * a) / (0.05f + 0.07f * av), 1f, Rnd(-0.5f, 0.5f), delay);
        }

        public override void Launch()
        {
            if (muted) return;
            Play(waterPool, Pick("launch"), 1, 1f);
            BubblesAt(0.75f, 0.03f, 1);
        }

        public override void Drop()
        {
            if (muted) return;
            Play(waterPool, Pick("drop"), 1, 1f);
            BubblesAt(0.3f, 0.04f, 1);
        }

        public override void Bump(float k)
        {
            if (muted) return;
            float kk = Clamp((float.IsNaN(k) ? 0 : k) / 1.5f, 0, 1);
            if (kk <= 0.01f) return;
            float now = Now;
            if (now - lastBump < 0.08f && kk < lastBumpK * 1.5f) return;
            lastBump = now; lastBumpK = kk;
            var c = Nearest("bump", BumpLevels, kk, out float kv);
            Play(waterPool, c, 1, (0.12f + 0.3f * kk) / (0.12f + 0.3f * kv));
        }

        public override void Grab() { if (!muted) Play(waterPool, Pick("grab"), 1, 1f); }
        public override void Release() { if (!muted) Play(waterPool, Pick("release"), 1, 1f); }
        public override void Score() { if (!muted) Play(dryPool, Pick("score"), 2, 1f); }
        public override void Bad() { if (!muted) Play(dryPool, Pick("bad"), 2, 1f); }
        public override void Click() { if (!muted) Play(dryPool, Pick("click"), 2, 1f); }
        public override void UiMove() { if (!muted) Play(dryPool, Pick("uiMove"), 2, 1f); }
        public override void UiBack() { if (!muted) Play(dryPool, Pick("uiBack"), 2, 1f); }
        public override void UiSelect() { if (!muted) Play(dryPool, Pick("uiSelect"), 2, 1f); }

        void Clunk(float k) { if (!muted) Play(waterPool, Pick("clunk"), 0, k); }

        void AmbBlorp(float depth)
        {
            Play(waterPool, Pick("blorp"), 0, Rnd(0.02f, 0.05f) / 0.035f * (1 - 0.3f * Clamp(depth / 10, 0, 1)), 1f, Rnd(-0.9f, 0.9f));
        }

        // rare and distant: a whale-like groan or a hull/structure creak
        void Groan() => Play(waterPool, Pick("groan"), 0, 1f, 1f, Rnd(-0.8f, 0.8f));

        // ---- rendering the one-shots (the JS recipes) --------------------------------

        const double T0 = 0.002;

        AudioClip[] Bake(string name)
        {
            var list = new List<AudioClip>(8);
            switch (name)
            {
                case "ping": for (int i = 0; i < 3; i++) list.Add(Render(name, false, RPing)); break;
                case "splash": foreach (var s in SplashLevels) list.Add(Render(name, false, r => RSplash(r, s))); break;
                case "bubbles": foreach (var a in BubbleLevels) for (int i = 0; i < 2; i++) list.Add(Render(name, true, r => RBubbles(r, a))); break;
                case "launch": for (int i = 0; i < 2; i++) list.Add(Render(name, true, RLaunch)); break;
                case "drop": for (int i = 0; i < 2; i++) list.Add(Render(name, true, RDrop)); break;
                case "bump": foreach (var k in BumpLevels) list.Add(Render(name, true, r => RBump(r, k))); break;
                case "grab": list.Add(Render(name, true, r => RServo(r, true))); break;
                case "release": list.Add(Render(name, true, r => RServo(r, false))); break;
                case "score": list.Add(Render(name, false, RScore)); break;
                case "bad": list.Add(Render(name, false, RBad)); break;
                case "click": list.Add(Render(name, false, r => RBlip(r, "triangle", 1250, 1250, 0.025f, 0.05f))); break;
                case "uiMove": list.Add(Render(name, false, r => RBlip(r, "triangle", 900, 900, 0.03f, 0.04f))); break;
                case "uiBack": list.Add(Render(name, false, r => RBlip(r, "sine", 600, 360, 0.09f, 0.06f))); break;
                case "uiSelect": list.Add(Render(name, false, RUiSelect)); break;
                case "clunk": list.Add(Render(name, true, RClunk)); break;
                case "blorp": for (int i = 0; i < 6; i++) list.Add(Render(name, true, RBlorp)); break;
                case "groan": for (int i = 0; i < 4; i++) list.Add(Render(name, true, RGroan)); break;
            }
            return list.ToArray();
        }

        // water: the sound travels through the water (dark room baked in, live low-pass on the channel)
        AudioClip Render(string name, bool water, Action<SynthShot> build)
        {
            var r = new SynthShot(sr, rng, noises);
            build(r);
            var data = r.Render(water ? 0.65 : 0.05);
            if (water) SynthDsp.WaterRoom(data, sr, 0.32f);
            // short fade at the very end against clicks
            int fade = Mathf.Min(data.Length, (int)(0.004f * sr));
            for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
            return MakeClip("duburi-" + name, data);
        }

        void RPing(SynthShot r)
        {
            double t = T0;
            const float f = 3000;
            var o = r.Osc("sine", f, t);
            var g = r.Gain();
            float pk = 0.17f;
            var gp = g.gain;
            gp.SetValueAtTime(0.0001f, t);
            gp.LinearRampToValueAtTime(pk, t + 0.008);
            gp.ExponentialRampToValueAtTime(pk * 0.012f, t + 0.068);
            double te = t + 0.068 + r.Rnd(0.008f, 0.02f); // faint reflection off the pool
            gp.SetValueAtTime(pk * 0.012f, te);
            gp.LinearRampToValueAtTime(pk * 0.2f, te + 0.005);
            gp.ExponentialRampToValueAtTime(0.0001f, te + 0.07);
            o.frequency.LinearRampToValueAtTime(f * 0.996f, te + 0.07);
            r.Chain(o, g);
            r.end = te + 0.08;
        }

        void RSplash(SynthShot r, float s)
        {
            double t = T0;
            // bright breaking water, not muffled: the hydrophone is in the boundary
            var n = r.Noise("white", t);
            var bp = r.Filt("bandpass", 2600, 0.8f);
            var g = r.Gain();
            bp.frequency.SetValueAtTime(2600, t);
            bp.frequency.ExponentialRampToValueAtTime(650, t + 0.35);
            r.Pluck(g.gain, t, 0.05f + 0.3f * s, 0.012, 0.25 + 0.35 * s);
            r.Chain(n, bp, g);
            // the low whump of displaced water
            var o = r.Osc("sine", 90, t);
            var og = r.Gain();
            o.frequency.ExponentialRampToValueAtTime(40, t + 0.3);
            r.Pluck(og.gain, t, 0.04f + 0.16f * s, 0.01, 0.3);
            r.Chain(o, og);
            r.end = t + 0.65;
        }

        void RBubbles(SynthShot r, float a)
        {
            double t = T0;
            int streams = a > 0.4f ? 2 : 1;
            int n = Mathf.RoundToInt(2 + a * 12);
            float span = 0.15f + a * 0.65f;
            double end = t;
            for (int s = 0; s < streams; s++)
                end = Math.Max(end, BubbleStream(r, t + r.Rnd(0, 0.04f), Mathf.CeilToInt(n / (float)streams), span, 0.05f + 0.07f * a, 260 + 60 * s));
            r.end = end + 0.02;
        }

        // A string of bubble 'blorps' on one oscillator: a bubble pinching off rings
        // at its Minnaert frequency, rising as it detaches (here 250 -> 1200 Hz in
        // 40-80 ms). Consecutive blorps never overlap, so one voice plays them all.
        double BubbleStream(SynthShot r, double t, int n, float span, float gain, float fLo)
        {
            var o = r.Osc("sine", fLo, t);
            var g = r.Gain();
            var fp = o.frequency;
            var gp = g.gain;
            double tt = t;
            for (int k = 0; k < n; k++)
            {
                float d = r.Rnd(0.04f, 0.08f), f0 = fLo * r.Rnd(0.8f, 1.5f), f1 = Mathf.Min(1400, f0 * r.Rnd(2.4f, 4));
                fp.SetValueAtTime(f0, tt);
                fp.ExponentialRampToValueAtTime(f1, tt + d);
                gp.SetValueAtTime(0.0001f, tt);
                gp.LinearRampToValueAtTime(gain * r.Rnd(0.5f, 1), tt + 0.004);
                gp.ExponentialRampToValueAtTime(0.0001f, tt + d);
                tt += d + 0.004 + r.rng.NextDouble() * (span / n) * 1.6;
            }
            r.Chain(o, g);
            return tt;
        }

        void RLaunch(SynthShot r)
        {
            double t = T0;
            // pneumatic 'pfft': a falling band of noise
            var n = r.Noise("white", t);
            var bp = r.Filt("bandpass", 1800, 1.1f);
            var g = r.Gain();
            bp.frequency.SetValueAtTime(1800, t);
            bp.frequency.ExponentialRampToValueAtTime(450, t + 0.28);
            r.Pluck(g.gain, t, 0.3f, 0.004, 0.32);
            r.Chain(n, bp, g);
            var o = r.Osc("sine", 140, t);
            var og = r.Gain();
            o.frequency.ExponentialRampToValueAtTime(55, t + 0.16);
            r.Pluck(og.gain, t, 0.22f, 0.004, 0.18);
            r.Chain(o, og);
            r.end = t + 0.4;
        }

        void RDrop(SynthShot r)
        {
            double t = T0;
            var c = r.Osc("triangle", 1100, t); // latch
            var cg = r.Gain();
            c.frequency.ExponentialRampToValueAtTime(700, t + 0.015);
            r.Pluck(cg.gain, t, 0.05f, 0.002, 0.018);
            r.Chain(c, cg);
            var o = r.Osc("sine", 190, t); // soft clunk
            var og = r.Gain();
            o.frequency.ExponentialRampToValueAtTime(85, t + 0.09);
            r.Pluck(og.gain, t, 0.16f, 0.004, 0.12);
            r.Chain(o, og);
            r.end = t + 0.2;
        }

        void RBump(SynthShot r, float kk)
        {
            double t = T0;
            float f0 = 88 - 25 * kk, d = 0.16f + 0.18f * kk;
            var o = r.Osc("sine", f0, t);
            var og = r.Gain();
            o.frequency.ExponentialRampToValueAtTime(f0 * 0.6f, t + d);
            r.Pluck(og.gain, t, 0.12f + 0.3f * kk, 0.004, d);
            r.Chain(o, og);
            var n = r.Noise("white", t);
            var lp = r.Filt("lowpass", 380 + 300 * kk, 0);
            var ng = r.Gain();
            r.Pluck(ng.gain, t, 0.1f + 0.35f * kk, 0.003, 0.1 + 0.1 * kk);
            r.Chain(n, lp, ng);
            r.end = t + d + 0.03;
        }

        // gripper: servo whirr through the gearbox, then the jaw clunk
        void RServo(SynthShot r, bool close)
        {
            double t = T0;
            double dur = close ? 0.22 : 0.16;
            var o = r.Osc("sawtooth", close ? 210 : 320, t);
            var bp = r.Filt("bandpass", 1100, 1.6f);
            var g = r.Gain();
            o.frequency.LinearRampToValueAtTime(close ? 320 : 220, t + dur);
            g.gain.SetValueAtTime(0.0001f, t);
            g.gain.LinearRampToValueAtTime(0.05f, t + 0.02);
            g.gain.SetValueAtTime(0.05f, t + dur - 0.02);
            g.gain.ExponentialRampToValueAtTime(0.0001f, t + dur + 0.04);
            r.Chain(o, bp, g);
            double tc = t + dur + 0.01;
            var c = r.Osc("sine", close ? 160 : 130, tc);
            var cg = r.Gain();
            c.frequency.ExponentialRampToValueAtTime(close ? 75 : 70, tc + 0.08);
            r.Pluck(cg.gain, tc, close ? 0.17f : 0.1f, 0.003, 0.1);
            r.Chain(c, cg);
            r.end = tc + 0.13;
        }

        // game cues: clean, not muffled
        void RScore(SynthShot r)
        {
            double t = T0;
            float[,] notes = { { 659.25f, 0, 0.22f }, { 987.77f, 0.09f, 0.34f } };
            for (int i = 0; i < 2; i++)
            {
                float f = notes[i, 0], dt = notes[i, 1], d = notes[i, 2];
                var o = r.Osc("triangle", f, t + dt);
                var g = r.Gain();
                r.Pluck(g.gain, t + dt, 0.16f, 0.006, d);
                r.Chain(o, g);
            }
            r.end = t + 0.45;
        }

        void RBad(SynthShot r)
        {
            double t = T0;
            var lp = r.Filt("lowpass", 900, 2);
            var g = r.Gain();
            foreach (var f in new[] { 98f, 103.8f })
            {
                var o = r.Osc("sawtooth", f, t);
                o.frequency.ExponentialRampToValueAtTime(f * 0.8f, t + 0.45);
                lp.inputs.Add(o);
            }
            g.gain.SetValueAtTime(0.0001f, t);
            g.gain.LinearRampToValueAtTime(0.11f, t + 0.01);
            g.gain.LinearRampToValueAtTime(0.08f, t + 0.28);
            g.gain.ExponentialRampToValueAtTime(0.0001f, t + 0.45);
            r.Chain(lp, g);
            r.end = t + 0.47;
        }

        void RUiSelect(SynthShot r)
        {
            double t = T0;
            var o = r.Osc("sine", 784, t);
            var g = r.Gain();
            o.frequency.SetValueAtTime(1175, t + 0.05);
            r.Pluck(g.gain, t, 0.07f, 0.004, 0.14);
            r.Chain(o, g);
            r.end = t + 0.16;
        }

        void RBlip(SynthShot r, string type, float f0, float f1, float d, float peak)
        {
            double t = T0;
            var o = r.Osc(type, f0, t);
            var g = r.Gain();
            if (f1 != f0) o.frequency.ExponentialRampToValueAtTime(f1, t + d);
            r.Pluck(g.gain, t, peak, 0.003, d);
            r.Chain(o, g);
            r.end = t + d + 0.02;
        }

        void RClunk(SynthShot r)
        {
            double t = T0;
            var o = r.Osc("sine", 150, t);
            var g = r.Gain();
            o.frequency.ExponentialRampToValueAtTime(70, t + 0.06);
            r.Pluck(g.gain, t, 0.05f, 0.003, 0.07);
            r.Chain(o, g);
            r.end = t + 0.09;
        }

        void RBlorp(SynthShot r)
        {
            int n = r.rng.NextDouble() < 0.75 ? 1 : 2 + r.rng.Next(2);
            r.end = BubbleStream(r, T0, n, 0.25f, 0.035f, r.Rnd(250, 380)) + 0.02;
        }

        void RGroan(SynthShot r)
        {
            double t = T0;
            bool creak = r.rng.NextDouble() < 0.45;
            float dur = creak ? r.Rnd(0.8f, 1.6f) : r.Rnd(2.4f, 4.2f);
            float bas = creak ? r.Rnd(180, 360) : r.Rnd(48, 84);
            var o = r.Osc(creak ? "triangle" : "sawtooth", bas, t);
            var curve = new float[16];
            float w = 0;
            float ph = r.Rnd(0, 6.28f);
            for (int i = 0; i < 16; i++)
            {
                w = creak ? Clamp(w + r.Rnd(-0.06f, 0.06f), -0.15f, 0.15f) : 0.15f * Mathf.Sin(ph + i * 0.35f);
                curve[i] = bas * (1 + w);
            }
            o.frequency.SetValueCurveAtTime(curve, t + 0.001, dur);
            var bp = r.Filt("bandpass", creak ? r.Rnd(500, 900) : r.Rnd(160, 280), creak ? 6 : 3);
            var g = r.Gain();
            float pk = creak ? 0.012f : 0.022f;
            g.gain.SetValueAtTime(0.0001f, t);
            g.gain.LinearRampToValueAtTime(pk, t + dur * 0.35);
            g.gain.LinearRampToValueAtTime(pk * 0.7f, t + dur * 0.7);
            g.gain.ExponentialRampToValueAtTime(0.0001f, t + dur);
            r.Chain(o, bp, g);
            r.end = t + dur + 0.05;
        }
    }
}
