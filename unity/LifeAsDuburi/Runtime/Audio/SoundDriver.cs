// Per-frame helper of the Editor / desktop / Android sound engine (SynthSound):
// glides the parameters toward their targets (the Web Audio setTargetAtTime of
// the JS engine), fades the beds when Sound.Tick() stops being called (menus,
// paused app: the JS _watch timer) and cleans up with its GameObject.

using UnityEngine;

namespace Duburi.Game
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SoundDriver : MonoBehaviour
    {
        internal SynthSound owner;

        // runs outside the App's frame: an error is reported with its place (logged once) and the sound
        // keeps going on the next frame
        void Update()
        {
            if (owner == null) return;
            try { owner.Advance(Time.unscaledDeltaTime); }
            catch (System.Exception e) { Faults.Report("sound engine", e); }
        }

        void OnDestroy()
        {
            var o = owner;
            owner = null;
            if (o != null) o.OnDriverDestroyed();
        }
    }
}
