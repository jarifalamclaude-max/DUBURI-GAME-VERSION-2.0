// COMPILE-CHECK STUB (never shipped): the parts of com.unity.render-pipelines.core 17 (Unity 6000.3)
// that LifeAsDuburi/Runtime/Render uses. Only members that exist in the real package with the
// same signature; '// file:line' = where it is declared in
// Packages/com.unity.render-pipelines.core/Runtime. Bodies are dummies.
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering
{
    // Volume/Volume.cs:15
    public partial class Volume : MonoBehaviour
    {
        public bool isGlobal { get; set; }                       // Volume/Volume.cs:23
        public float priority = 0f;                              // Volume/Volume.cs:38
        public float weight = 1f;                                // Volume/Volume.cs:51
        public VolumeProfile sharedProfile = null;               // Volume/Volume.cs:63
        public VolumeProfile profile { get; set; }               // Volume/Volume.cs:77
    }

    // Volume/VolumeProfile.cs:14
    public sealed partial class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();                       // Volume/VolumeProfile.cs:19
        public T Add<T>(bool overrides = false) where T : VolumeComponent => null;                          // Volume/VolumeProfile.cs:92
        public bool TryGet<T>(out T component) where T : VolumeComponent { component = null; return false; } // Volume/VolumeProfile.cs:231
    }

    // Volume/VolumeComponent.cs:133
    public partial class VolumeComponent : ScriptableObject
    {
        public bool active = true;                               // Volume/VolumeComponent.cs:154
    }

    // Volume/VolumeParameter.cs:30
    public abstract partial class VolumeParameter
    {
        public virtual bool overrideState { get; set; }          // Volume/VolumeParameter.cs:59
    }

    // Volume/VolumeParameter.cs:168
    public partial class VolumeParameter<T> : VolumeParameter
    {
        public virtual T value { get; set; }                     // Volume/VolumeParameter.cs:182
        public VolumeParameter() { }                             // Volume/VolumeParameter.cs:191
        protected VolumeParameter(T value, bool overrideState = false) { } // Volume/VolumeParameter.cs:201
        public void Override(T x) { }                            // Volume/VolumeParameter.cs:234
    }

    // Volume/VolumeParameter.cs:351
    public partial class BoolParameter : VolumeParameter<bool>
    {
        public BoolParameter(bool value, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:358
    }

    // Volume/VolumeParameter.cs:434
    public partial class IntParameter : VolumeParameter<int>
    {
        public IntParameter(int value, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:441
    }

    // Volume/VolumeParameter.cs:672
    public partial class ClampedIntParameter : IntParameter
    {
        public override int value { get; set; }                  // Volume/VolumeParameter.cs:692
        public ClampedIntParameter(int value, int min, int max, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:705
    }

    // Volume/VolumeParameter.cs:779
    public partial class FloatParameter : VolumeParameter<float>
    {
        public FloatParameter(float value, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:786
    }

    // Volume/VolumeParameter.cs:838
    public partial class MinFloatParameter : FloatParameter
    {
        public override float value { get; set; }                // Volume/VolumeParameter.cs:852
        public MinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:864
    }

    // Volume/VolumeParameter.cs:1025
    public partial class ClampedFloatParameter : FloatParameter
    {
        public override float value { get; set; }                // Volume/VolumeParameter.cs:1045
        public ClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:1058
    }

    // Volume/VolumeParameter.cs:1256
    public partial class ColorParameter : VolumeParameter<Color>
    {
        public ColorParameter(Color value, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:1281
        public ColorParameter(Color value, bool hdr, bool showAlpha, bool showEyeDropper, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:1292
    }

    // Volume/VolumeParameter.cs:1378
    public partial class Vector2Parameter : VolumeParameter<Vector2>
    {
        public Vector2Parameter(Vector2 value, bool overrideState = false) : base(value, overrideState) { } // Volume/VolumeParameter.cs:1385
    }
}
