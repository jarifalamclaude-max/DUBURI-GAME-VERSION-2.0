// COMPILE-CHECK STUB (never shipped): the parts of com.unity.render-pipelines.universal 17 (Unity 6000.3)
// that LifeAsDuburi/Runtime/Render uses. Only members that exist in the real package with the same
// signature; '// file:line' = where it is declared in Packages/com.unity.render-pipelines.universal/Runtime.
// Bodies are dummies.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    // UniversalAdditionalCameraData.cs:42
    public enum AntialiasingMode
    {
        None,                                  // UniversalAdditionalCameraData.cs:48
        FastApproximateAntialiasing,           // UniversalAdditionalCameraData.cs:54
        SubpixelMorphologicalAntiAliasing,     // UniversalAdditionalCameraData.cs:61
        TemporalAntiAliasing,                  // UniversalAdditionalCameraData.cs:67
    }

    // UniversalAdditionalCameraData.cs:171
    public static partial class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera) => null; // UniversalAdditionalCameraData.cs:182
        public static void SetVolumeFrameworkUpdateMode(this Camera camera, VolumeFrameworkUpdateMode mode) { } // UniversalAdditionalCameraData.cs:216
    }

    // UniversalAdditionalCameraData.cs:442
    public partial class UniversalAdditionalCameraData : MonoBehaviour
    {
        public bool renderShadows { get; set; }                  // UniversalAdditionalCameraData.cs:536
        public bool requiresDepthTexture { get; set; }           // UniversalAdditionalCameraData.cs:626
        public bool requiresColorTexture { get; set; }           // UniversalAdditionalCameraData.cs:646
        public LayerMask volumeLayerMask { get; set; }           // UniversalAdditionalCameraData.cs:696
        public bool renderPostProcessing { get; set; }           // UniversalAdditionalCameraData.cs:789
        public AntialiasingMode antialiasing { get; set; }       // UniversalAdditionalCameraData.cs:799
        public bool stopNaN { get; set; }                        // UniversalAdditionalCameraData.cs:861
        public bool dithering { get; set; }                      // UniversalAdditionalCameraData.cs:870
    }

    // UniversalAdditionalLightData.cs:8
    public static partial class LightExtensions
    {
        public static UniversalAdditionalLightData GetUniversalAdditionalLightData(this Light light) => null; // UniversalAdditionalLightData.cs:17
    }

    // UniversalAdditionalLightData.cs:34
    public partial class UniversalAdditionalLightData : MonoBehaviour
    {
        public bool usePipelineSettings { get; set; }            // UniversalAdditionalLightData.cs:42
        public Vector2 lightCookieSize { get; set; }             // UniversalAdditionalLightData.cs:139
        public Vector2 lightCookieOffset { get; set; }           // UniversalAdditionalLightData.cs:150
        public SoftShadowQuality softShadowQuality { get; set; } // UniversalAdditionalLightData.cs:161
    }

    // Data/UniversalRenderPipelineAsset.cs:39
    public enum SoftShadowQuality
    {
        UsePipelineSettings,                   // Data/UniversalRenderPipelineAsset.cs:45
        Low,                                   // Data/UniversalRenderPipelineAsset.cs:50
        Medium,                                // Data/UniversalRenderPipelineAsset.cs:54
        High,                                  // Data/UniversalRenderPipelineAsset.cs:58
    }

    // ScriptableRendererData.cs:18
    public abstract partial class ScriptableRendererData : ScriptableObject
    {
        public List<ScriptableRendererFeature> rendererFeatures => null; // ScriptableRendererData.cs:51
    }

    // ScriptableRendererFeature.cs:12 (the abstract Create / AddRenderPasses are left out of the stub)
    public abstract partial class ScriptableRendererFeature : ScriptableObject, IDisposable
    {
        public bool isActive => true;                            // ScriptableRendererFeature.cs:18
        public void SetActive(bool active) { }                   // ScriptableRendererFeature.cs:123
        public void Dispose() { }                                // ScriptableRendererFeature.cs:132
    }

    // RendererFeatures/ScreenSpaceAmbientOcclusion.cs:118
    public partial class ScreenSpaceAmbientOcclusion : ScriptableRendererFeature
    {
    }

    // Data/UniversalRenderPipelineAsset.cs:318
    public enum VolumeFrameworkUpdateMode
    {
        EveryFrame = 0,                        // Data/UniversalRenderPipelineAsset.cs:324
        ViaScripting = 1,                      // Data/UniversalRenderPipelineAsset.cs:330
        UsePipelineSettings = 2,               // Data/UniversalRenderPipelineAsset.cs:336
    }

    // Data/UniversalRenderPipelineAsset.cs:458 — the real base is RenderPipelineAsset<UniversalRenderPipeline>
    // (a Unity 2023+ generic subclass of RenderPipelineAsset that the 2021.3 reference assemblies lack);
    // declared abstract here only so the stub needs no CreatePipeline body.
    public abstract partial class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public bool supportsHDR { get; set; }                    // Data/UniversalRenderPipelineAsset.cs:1091
        public int msaaSampleCount { get; set; }                 // Data/UniversalRenderPipelineAsset.cs:1110
        public float renderScale { get; set; }                   // Data/UniversalRenderPipelineAsset.cs:1119
        public int mainLightShadowmapResolution { get; set; }    // Data/UniversalRenderPipelineAsset.cs:1320
        public float shadowDistance { get; set; }                // Data/UniversalRenderPipelineAsset.cs:1463
        public int shadowCascadeCount { get; set; }              // Data/UniversalRenderPipelineAsset.cs:1472
        public ReadOnlySpan<ScriptableRendererData> rendererDataList => default; // Data/UniversalRenderPipelineAsset.cs:688
        public bool supportsCameraDepthTexture { get; set; }     // Data/UniversalRenderPipelineAsset.cs:1051
        public bool supportsMainLightShadows => false;           // Data/UniversalRenderPipelineAsset.cs:1305 (setter internal)
        public bool reflectionProbeBlending => false;            // Data/UniversalRenderPipelineAsset.cs:1413 (setter internal)
        public bool reflectionProbeBoxProjection => false;       // Data/UniversalRenderPipelineAsset.cs:1431 (setter internal)
        public float cascade2Split { get; set; }                 // Data/UniversalRenderPipelineAsset.cs:1489
        public Vector3 cascade4Split { get; set; }               // Data/UniversalRenderPipelineAsset.cs:1509
        public float cascadeBorder { get; set; }                 // Data/UniversalRenderPipelineAsset.cs:1518
        public float shadowDepthBias { get; set; }               // Data/UniversalRenderPipelineAsset.cs:1527
        public float shadowNormalBias { get; set; }              // Data/UniversalRenderPipelineAsset.cs:1536
        public bool supportsSoftShadows => false;                // Data/UniversalRenderPipelineAsset.cs:1545 (setter internal)
        public bool supportsLightCookies => false;               // Data/UniversalRenderPipelineAsset.cs:1579
        public bool conservativeEnclosingSphere { get; set; }    // Data/UniversalRenderPipelineAsset.cs:1699
    }

    // Overrides/Bloom.cs:8
    public enum BloomDownscaleMode
    {
        Half,                                  // Overrides/Bloom.cs:13
        Quarter,                               // Overrides/Bloom.cs:18
    }

    // Overrides/Bloom.cs:259
    public sealed partial class DownscaleParameter : VolumeParameter<BloomDownscaleMode>
    {
        public DownscaleParameter(BloomDownscaleMode value, bool overrideState = false) : base(value, overrideState) { } // Overrides/Bloom.cs:266
    }

    // Overrides/Bloom.cs:167
    public sealed partial class Bloom : VolumeComponent
    {
        public MinFloatParameter threshold;                      // Overrides/Bloom.cs:176
        public MinFloatParameter intensity;                      // Overrides/Bloom.cs:182
        public ClampedFloatParameter scatter;                    // Overrides/Bloom.cs:188
        public ColorParameter tint;                              // Overrides/Bloom.cs:201
        public BoolParameter highQualityFiltering;               // Overrides/Bloom.cs:208
        public DownscaleParameter downscale;                     // Overrides/Bloom.cs:220
        public ClampedIntParameter maxIterations;                // Overrides/Bloom.cs:226
    }

    // Overrides/DepthOfField.cs:8
    public enum DepthOfFieldMode
    {
        Off,                                   // Overrides/DepthOfField.cs:13
        Gaussian,                              // Overrides/DepthOfField.cs:18
        Bokeh,                                 // Overrides/DepthOfField.cs:23
    }

    // Overrides/DepthOfField.cs:149
    public sealed partial class DepthOfField : VolumeComponent
    {
        public DepthOfFieldModeParameter mode;                   // Overrides/DepthOfField.cs:155
        public MinFloatParameter gaussianStart;                  // Overrides/DepthOfField.cs:161
        public MinFloatParameter gaussianEnd;                    // Overrides/DepthOfField.cs:167
        public ClampedFloatParameter gaussianMaxRadius;          // Overrides/DepthOfField.cs:173
        public BoolParameter highQualitySampling;                // Overrides/DepthOfField.cs:179
    }

    // Overrides/DepthOfField.cs:241
    public sealed partial class DepthOfFieldModeParameter : VolumeParameter<DepthOfFieldMode>
    {
        public DepthOfFieldModeParameter(DepthOfFieldMode value, bool overrideState = false) : base(value, overrideState) { } // Overrides/DepthOfField.cs:248
    }

    // Overrides/Tonemapping.cs:8
    public enum TonemappingMode
    {
        None,                                  // Overrides/Tonemapping.cs:13
        Neutral,                               // Overrides/Tonemapping.cs:19
        ACES,                                  // Overrides/Tonemapping.cs:26
    }

    // Overrides/Tonemapping.cs:180
    public sealed partial class Tonemapping : VolumeComponent
    {
        public TonemappingModeParameter mode;                    // Overrides/Tonemapping.cs:186
    }

    // Overrides/Tonemapping.cs:257
    public sealed partial class TonemappingModeParameter : VolumeParameter<TonemappingMode>
    {
        public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState) { } // Overrides/Tonemapping.cs:264
    }

    // Overrides/ColorAdjustments.cs:107
    public sealed partial class ColorAdjustments : VolumeComponent
    {
        public FloatParameter postExposure;                      // Overrides/ColorAdjustments.cs:114
        public ClampedFloatParameter contrast;                   // Overrides/ColorAdjustments.cs:120
        public ColorParameter colorFilter;                       // Overrides/ColorAdjustments.cs:126
        public ClampedFloatParameter hueShift;                   // Overrides/ColorAdjustments.cs:132
        public ClampedFloatParameter saturation;                 // Overrides/ColorAdjustments.cs:138
    }

    // Overrides/Vignette.cs:109
    public sealed partial class Vignette : VolumeComponent
    {
        public ColorParameter color;                             // Overrides/Vignette.cs:115
        public Vector2Parameter center;                          // Overrides/Vignette.cs:121
        public ClampedFloatParameter intensity;                  // Overrides/Vignette.cs:127
        public ClampedFloatParameter smoothness;                 // Overrides/Vignette.cs:133
        public BoolParameter rounded;                            // Overrides/Vignette.cs:139
    }
}
