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

        void Update()
        {
            if (owner != null) owner.Advance(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            var o = owner;
            owner = null;
            if (o != null) o.OnDriverDestroyed();
        }
    }
}
