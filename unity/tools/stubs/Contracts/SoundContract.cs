// CONTRACT STUB (compile-check only, never shipped). The real code lives in
// LifeAsDuburi/Runtime/Audio. JS original: src/ui/audio.js (class Sound).
using System.Collections.Generic;
using UnityEngine;

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

    public sealed class Sound
    {
        public static Sound Create(Transform parent) => new Sound();
        public bool Muted { get; private set; }
        public void SetMuted(bool muted) { Muted = muted; }
        public void Ensure() { }                      // unlock / start audio after a user gesture
        public void Tick(float dt, SoundFrame frame) { } // JS update(dt, listener)
        public void Motor(float level) { }
        public void Ping(float freqKHz, float loud, float pan = 0f) { }
        public void Splash(float strength = 0.6f) { }
        public void Bubbles(float amount = 0.5f) { }
        public void Launch() { }
        public void Drop() { }
        public void Bump(float k) { }
        public void Grab() { }
        public void Release() { }
        public void Score() { }
        public void Bad() { }
        public void Click() { }
        public void UiMove() { }
        public void UiBack() { }
        public void UiSelect() { }
        public void Sfx(string name) { }              // by JS method name ("uiMove", ...); Click() if unknown
        public void Dispose() { }
    }
}
