// Life as Duburi: procedural sound (port of src/ui/audio.js, class Sound).
// Nothing is downloaded; every sound is synthesised.
//
// Two engines behind one API:
//  * WebGL build: the browser game's own Web Audio engine (src/ui/audio.js),
//    bundled into Plugins/WebGL/DuburiAudio.jslib by tools/build-audio-jslib.mjs
//    and called through [DllImport("__Internal")]. Same sound as the browser game.
//  * Editor / desktop / Android: SynthSound, a C# version that renders the same
//    recipes into AudioClips played by pooled AudioSources (pitch / volume /
//    panStereo): thruster hum per vehicle following rotor speed, prop wash,
//    cavitation, water ambience, pings, splashes, bubbles and the game / UI cues.
// Muted is remembered in PlayerPrefs ("life-as-duburi-muted").

using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Duburi.Game
{
    public sealed class VehicleSound
    {
        public float[] n;          // per-thruster rotor speed fraction -1..1 (ThrusterState.n)
        public float maxRpm;
        public int blades = 3;
        public string size = "t200"; // "t200" | "t500" | "small"
        public float dist;         // m from the listener (0 = own vehicle)
        public float pan;          // -1..1
    }

    public sealed class SoundFrame
    {
        public bool underwater = true;
        public float depth;
        public bool paused;
        public readonly List<VehicleSound> vehicles = new List<VehicleSound>();
    }

    // what both engines implement
    abstract class SoundEngine
    {
        public abstract void Ensure();
        public abstract void SetMuted(bool muted);
        public abstract void Tick(float dt, SoundFrame frame);
        public abstract void Motor(float level);
        public abstract void Ping(float freqKHz, float loud, float pan);
        public abstract void Splash(float strength);
        public abstract void Bubbles(float amount);
        public abstract void Launch();
        public abstract void Drop();
        public abstract void Bump(float k);
        public abstract void Grab();
        public abstract void Release();
        public abstract void Score();
        public abstract void Bad();
        public abstract void Click();
        public abstract void UiMove();
        public abstract void UiBack();
        public abstract void UiSelect();
        public abstract void Dispose();
    }

    public sealed class Sound
    {
        const string MutedKey = "life-as-duburi-muted";

        readonly SoundEngine engine;
        bool disposed;

        Sound(SoundEngine engine, bool muted)
        {
            this.engine = engine;
            Muted = muted;
            engine.SetMuted(muted);
        }

        // parent: where the fallback engine puts its AudioSources (and the one AudioListener)
        public static Sound Create(Transform parent)
        {
            bool muted = false;
            try { muted = PlayerPrefs.GetInt(MutedKey, 0) != 0; } catch (System.Exception) { /* storage unavailable */ }
#if UNITY_WEBGL && !UNITY_EDITOR
            return new Sound(new WebAudioSound(), muted);
#else
            return new Sound(new SynthSound(parent), muted);
#endif
        }

        public bool Muted { get; private set; }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            try { PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { /* ignore */ }
            if (!disposed) engine.SetMuted(muted);
        }

        // unlock / start audio after a user gesture
        public void Ensure() { if (!disposed) engine.Ensure(); }
        // JS update(dt, listener)
        public void Tick(float dt, SoundFrame frame) { if (!disposed && frame != null) engine.Tick(dt, frame); }
        // Legacy single-level thruster drive (0..1). Ignored while Tick() receives
        // vehicles; otherwise it drives one T200-like own voice.
        public void Motor(float level) { if (!disposed) engine.Motor(level); }
        public void Ping(float freqKHz, float loud, float pan = 0f) { if (!disposed) engine.Ping(freqKHz, loud, pan); }
        public void Splash(float strength = 0.6f) { if (!disposed) engine.Splash(strength); }
        public void Bubbles(float amount = 0.5f) { if (!disposed) engine.Bubbles(amount); }
        public void Launch() { if (!disposed) engine.Launch(); }
        public void Drop() { if (!disposed) engine.Drop(); }
        public void Bump(float k) { if (!disposed) engine.Bump(k); }
        public void Grab() { if (!disposed) engine.Grab(); }
        public void Release() { if (!disposed) engine.Release(); }
        public void Score() { if (!disposed) engine.Score(); }
        public void Bad() { if (!disposed) engine.Bad(); }
        public void Click() { if (!disposed) engine.Click(); }
        public void UiMove() { if (!disposed) engine.UiMove(); }
        public void UiBack() { if (!disposed) engine.UiBack(); }
        public void UiSelect() { if (!disposed) engine.UiSelect(); }

        // by JS method name ("uiMove", ...); Click() if unknown
        public void Sfx(string name)
        {
            switch (name)
            {
                case "splash": Splash(); break;
                case "bubbles": Bubbles(); break;
                case "launch": Launch(); break;
                case "drop": Drop(); break;
                case "bump": Bump(1f); break;
                case "grab": Grab(); break;
                case "release": Release(); break;
                case "score": Score(); break;
                case "bad": Bad(); break;
                case "uiMove": UiMove(); break;
                case "uiBack": UiBack(); break;
                case "uiSelect": UiSelect(); break;
                case "ping": Ping(30f, 1f); break;
                default: Click(); break;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            engine.Dispose();
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // The browser game's Web Audio engine, through Plugins/WebGL/DuburiAudio.jslib.
    sealed class WebAudioSound : SoundEngine
    {
        // floats per vehicle, the same layout as tools/build-audio-jslib.mjs:
        // n[0..7] (NaN = no thruster), maxRpm, blades, size code (0 t200, 1 t500, 2 small), dist, pan
        const int MaxN = 8;
        const int Stride = 13;

        [DllImport("__Internal")] static extern int DuburiAudio_Init();
        [DllImport("__Internal")] static extern void DuburiAudio_Update(float dt, int underwater, float depth, int paused, float[] vehicles, int count);
        [DllImport("__Internal")] static extern int DuburiAudio_Call(string name, float a, float b, float c);

        float[] buf = new float[Stride * 4];
        readonly bool ok;

        public WebAudioSound()
        {
            try { ok = DuburiAudio_Init() != 0; }
            catch (System.Exception e) { Debug.LogWarning("Life as Duburi: Web Audio unavailable: " + e.Message); ok = false; }
        }

        void Call(string name, float a = 0f, float b = 0f, float c = 0f)
        {
            if (ok) DuburiAudio_Call(name, a, b, c);
        }

        public override void Ensure() => Call("ensure");
        public override void SetMuted(bool muted) => Call("setMuted", muted ? 1f : 0f);

        public override void Tick(float dt, SoundFrame frame)
        {
            if (!ok || frame == null) return;
            var veh = frame.vehicles;
            int count = veh != null ? veh.Count : 0;
            if (buf.Length < count * Stride) buf = new float[Mathf.NextPowerOfTwo(count) * Stride];
            int used = 0;
            for (int i = 0; i < count; i++)
            {
                var v = veh[i];
                if (v == null) continue;
                int p = used * Stride;
                int len = v.n != null ? Mathf.Min(v.n.Length, MaxN) : 0;
                for (int j = 0; j < MaxN; j++) buf[p + j] = j < len ? v.n[j] : float.NaN;
                buf[p + MaxN] = v.maxRpm;
                buf[p + MaxN + 1] = v.blades;
                buf[p + MaxN + 2] = v.size == "t500" ? 1f : v.size == "small" ? 2f : 0f;
                buf[p + MaxN + 3] = v.dist;
                buf[p + MaxN + 4] = v.pan;
                used++;
            }
            DuburiAudio_Update(dt, frame.underwater ? 1 : 0, frame.depth, frame.paused ? 1 : 0, buf, used);
        }

        public override void Motor(float level) => Call("motor", level);
        public override void Ping(float freqKHz, float loud, float pan) => Call("ping", freqKHz, loud, pan);
        public override void Splash(float strength) => Call("splash", strength);
        public override void Bubbles(float amount) => Call("bubbles", amount);
        public override void Launch() => Call("launch");
        public override void Drop() => Call("drop");
        public override void Bump(float k) => Call("bump", k);
        public override void Grab() => Call("grab");
        public override void Release() => Call("release");
        public override void Score() => Call("score");
        public override void Bad() => Call("bad");
        public override void Click() => Call("click");
        public override void UiMove() => Call("uiMove");
        public override void UiBack() => Call("uiBack");
        public override void UiSelect() => Call("uiSelect");
        public override void Dispose() => Call("dispose");
    }
#endif
}
