// src/render/post.js: per-viewport post-processing for the split-screen game, on URP's post stack.
//
// Every view has its own global Volume on its own layer (24 + view index) with a private clone of
// Resources/Duburi/PostProfile (Bloom, Tonemapping ACES, ColorAdjustments, Vignette — the only post
// effects whose shader variants are in the build), and its camera's volumeLayerMask selects it. Each
// frame Apply() writes the view's grade, mapped from the JS final pass:
//
//   JS (FINAL_FS)                                  URP override
//   bloom: soft-knee bright pass, 1 or 3 levels    Bloom threshold (gamma, knee = threshold / 2 like the JS),
//          weighted by uBloomW                     intensity = strength * sum(weights), Quarter downscale,
//                                                  maxIterations / scatter for the level layout
//   col *= uAbsorb (water column above the camera) ColorAdjustments colorFilter (hue) + postExposure (level)
//   saturation uSat, contrast uContrast            ColorAdjustments saturation / contrast ((x - 1) * 100)
//   vignette 1 - v * smoothstep(0.3, 1, |c| * √2) Vignette intensity / smoothness fitted to the same corner
//                                                  darkening (not rounded: follows the view's shape)
//   tone mapping ACES, exposure 1.05               Tonemapping ACES, postExposure log2(1.05)
//   grain + dither                                 the camera's dithering
//
// The refraction wobble and the chromatic aberration of the JS need custom shaders and have no
// counterpart (the port has none). Quality: "high" (3 bloom levels + 4x MSAA from GraphicsQuality),
// "low" (1 bloom level), null = no post (the JS renders straight to the canvas: tone mapping and
// exposure only, which URP also does in its post pass — so the stack stays on with every effect off).
//
// "ultra" (beyond the browser game) clones Resources/Duburi/PostProfileUltra instead: the same four
// effects plus DepthOfField, with bloom's high-quality (bicubic) filtering at half resolution and 4
// levels, and under water a subtle Gaussian distance blur that starts where the water's fog begins to
// matter. URP keeps only the post shader variants it finds in VolumeProfile assets, so HQ bloom and
// depth of field are used only when that profile (made by Set Up Project) loads; without it Ultra
// falls back to the "high" grade with a warning.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Duburi.Game
{
    public sealed class Post
    {
        /// first of the four per-view volume layers (24..27)
        public const int FirstLayer = 24;
        public const int MaxViews = 4;
        public const string ProfilePath = "Duburi/PostProfile";
        /// PostProfile + DepthOfField, HQ bloom filtering (Ultra only)
        public const string UltraProfilePath = "Duburi/PostProfileUltra";

        /// tunables (multipliers the game may tweak at run time), like the JS settings
        public sealed class Settings
        {
            public float bloom = 1;       // bloom strength multiplier
            public float threshold = 1;   // bloom threshold multiplier
            public float vignette = 1;
            public float grain = 1;       // > 0: dithering on
            public float absorption = 1;
            public float dof = 1;         // Ultra's under-water distance blur (0 = off)
        }

        // bloom layout per quality (PRESETS): level weights, URP mip count and scatter
        sealed class Preset { public float[] weights; public int iterations; public float scatter; }
        static readonly Preset High = new Preset { weights = new[] { 0.4f, 0.35f, 0.3f }, iterations = 3, scatter = 0.6f };
        static readonly Preset Low = new Preset { weights = new[] { 1.0f, 0f, 0f }, iterations = 2, scatter = 0f };
        // Ultra: the High weights over 4 half-resolution levels with bicubic upsampling
        static readonly Preset Ultra = new Preset { weights = new[] { 0.4f, 0.35f, 0.3f }, iterations = 4, scatter = 0.6f };

        static readonly float Exposure = Mathf.Log(1.05f, 2f); // renderer.toneMappingExposure = 1.05
        const float VignetteSmooth = 0.4f;                       // URP smoothness: pow(.., 5 * 0.4)
        static readonly Color DefaultWater = Colors.Rgb(0x2f9fc4).linear;

        sealed class View
        {
            public GameObject go;
            public Volume volume;
            public VolumeProfile profile;
            public Bloom bloom;
            public Tonemapping tone;
            public ColorAdjustments color;
            public Vignette vignette;
            public DepthOfField dof;      // Ultra profile only
            public bool ultra;            // the profile is a clone of PostProfileUltra
            public Camera cam;
            public UniversalAdditionalCameraData data;
        }

        readonly View[] views = new View[MaxViews];
        string quality;
        readonly bool follow;
        public readonly Settings settings = new Settings();

        static VolumeProfile template, ultraTemplate;
        static bool warned, warnedUltra, ultraLoaded;

        /// createPost(renderer, { quality }): "high" | "low" | null (no post). With followQuality the
        /// level follows GraphicsQuality.Set (the pause menu's Graphics button) on its own.
        public Post(string quality, bool followQuality = false)
        {
            SetQuality(quality);
            follow = followQuality;
            if (follow) GraphicsQuality.Changed += OnQualityChanged;
        }

        /// a Post at the current GraphicsQuality level that follows its changes
        public Post() : this(GraphicsQuality.Spec.post, true) { }

        void OnQualityChanged(string id) => SetQuality(GraphicsQuality.QUALITY[id].post);

        /// "ultra" | "high" | "low" | null
        public string Quality => quality;

        /// setQuality(q)
        public void SetQuality(string q)
        {
            quality = q == null ? null : q == "low" ? "low" : q == "ultra" ? "ultra" : "high";
        }

        /// The view's Volume (layer FirstLayer + view), created on first use; its profile follows the
        /// quality (PostProfileUltra on Ultra when Set Up Project made it, PostProfile otherwise).
        View Get(int view)
        {
            view = Mathf.Clamp(view, 0, MaxViews - 1);
            bool wantUltra = quality == "ultra" && UltraTemplate() != null;
            var v = views[view];
            if (v != null && v.go != null)
            {
                if (v.ultra != wantUltra) BuildProfile(v, wantUltra);
                return v;
            }
            v = new View();
            v.go = new GameObject("post view " + (view + 1)) { layer = FirstLayer + view };
            v.volume = v.go.AddComponent<Volume>();
            v.volume.isGlobal = true;
            v.volume.priority = 1;
            v.volume.weight = 1;
            BuildProfile(v, wantUltra);
            views[view] = v;
            return v;
        }

        /// (re)creates the view's private profile from the template of its kind
        static void BuildProfile(View v, bool ultra)
        {
            DestroyProfile(v.profile);
            v.ultra = ultra;
            v.profile = CloneProfile(ultra ? ultraTemplate : null);
            v.bloom = Component<Bloom>(v.profile);
            v.tone = Component<Tonemapping>(v.profile);
            v.color = Component<ColorAdjustments>(v.profile);
            v.vignette = Component<Vignette>(v.profile);
            v.tone.mode.Override(TonemappingMode.ACES);
            // Ultra: half-resolution bloom with bicubic upsampling (its variant is kept by PostProfileUltra)
            v.bloom.downscale.Override(ultra ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
            v.bloom.highQualityFiltering.Override(ultra);
            v.bloom.tint.Override(Color.white);
            v.color.hueShift.Override(0f);
            v.vignette.color.Override(Color.black);
            v.vignette.center.Override(new Vector2(0.5f, 0.5f));
            v.vignette.rounded.Override(false);
            v.vignette.smoothness.Override(VignetteSmooth);
            v.dof = null;
            if (ultra)
            {
                v.dof = Component<DepthOfField>(v.profile);
                v.dof.mode.Override(DepthOfFieldMode.Off);
                v.dof.highQualitySampling.Override(true);
            }
            v.volume.sharedProfile = v.profile;
        }

        /// Resources/Duburi/PostProfileUltra, or null (warned once) when Set Up Project has not made it yet
        static VolumeProfile UltraTemplate()
        {
            if (!ultraLoaded)
            {
                ultraLoaded = true;
                ultraTemplate = Resources.Load<VolumeProfile>(UltraProfilePath);
            }
            if (ultraTemplate == null && !warnedUltra)
            {
                warnedUltra = true;
                Debug.LogWarning("Life as Duburi: Resources/" + UltraProfilePath + " is missing — Ultra runs without depth of field and " +
                                 "high-quality bloom. Run Life as Duburi > Set Up Project again (needed after every update of the game files).");
            }
            return ultraTemplate;
        }

        /// Object.Instantiate(template) with its components copied too (Instantiate alone would share the
        /// template's components between all views). source null = PostProfile.
        static VolumeProfile CloneProfile(VolumeProfile source)
        {
            if (source == null)
            {
                if (template == null) template = Resources.Load<VolumeProfile>(ProfilePath);
                source = template;
            }
            VolumeProfile p;
            if (source != null)
            {
                p = UnityEngine.Object.Instantiate(source);
                for (int i = 0; i < p.components.Count; i++)
                    if (p.components[i] != null) p.components[i] = UnityEngine.Object.Instantiate(p.components[i]);
            }
            else
            {
                if (!warned)
                {
                    Debug.LogWarning("Life as Duburi: Resources/" + ProfilePath + " is missing — run Life as Duburi > Set Up Project. " +
                                     "Using a runtime profile (its post shaders may be stripped from builds).");
                    warned = true;
                }
                p = ScriptableObject.CreateInstance<VolumeProfile>();
            }
            p.name = "PostProfile (view)";
            return p;
        }

        static T Component<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c)) c = p.Add<T>(false);
            c.active = true;
            return c;
        }

        /// post.render(scene, camera, rect, { underwater, depth, time, waterColor, turbidity }) for one
        /// view: binds the camera to the view's volume (volumeLayerMask, post on, no post-AA, dithering)
        /// and writes the grade. depth = metres of water above the camera; waterColor is linear.
        public void Apply(Camera camera, int view, bool underwater, float depth, Color waterColor, float turbidity)
        {
            if (camera == null) return;
            var v = Get(view);
            if (v.cam != camera || v.data == null)
            {
                v.cam = camera;
                v.data = camera.GetUniversalAdditionalCameraData();
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
            }
            var d = v.data;
            int mask = 1 << v.go.layer;
            if (d.volumeLayerMask.value != mask) d.volumeLayerMask = mask;
            if (!d.renderPostProcessing) d.renderPostProcessing = true;
            if (d.antialiasing != AntialiasingMode.None) d.antialiasing = AntialiasingMode.None; // MSAA (GraphicsQuality) like the JS
            bool dither = quality != null && settings.grain > 0;
            if (d.dithering != dither) d.dithering = dither;
            if (!camera.allowHDR) camera.allowHDR = true;

            bool under = underwater;
            float dep = under ? Mathf.Max(0, depth) : 0;
            float turb = Mathf.Clamp01(turbidity);

            if (v.dof != null)
            {
                // Ultra, under water: a subtle distance blur that starts where the fog begins to matter
                // (fogDensity back from the turbidity: 0.02 + 0.05 turb), never on the vehicles near the camera
                if (under && quality != null && settings.dof > 0)
                {
                    float fd = 0.02f + 0.05f * turb;
                    float start = Mathf.Max(8f, 0.45f / fd);
                    v.dof.mode.Override(DepthOfFieldMode.Gaussian);
                    v.dof.gaussianStart.Override(start);
                    v.dof.gaussianEnd.Override(start + 0.9f / fd);
                    v.dof.gaussianMaxRadius.Override(Mathf.Clamp(0.6f * settings.dof, 0.5f, 1.5f));
                }
                else if (v.dof.mode.value != DepthOfFieldMode.Off) v.dof.mode.Override(DepthOfFieldMode.Off);
            }

            if (quality == null)
            {
                // r.render(scene, cam): the renderer's ACES tone mapping and exposure only
                v.bloom.active = false;
                v.vignette.active = false;
                v.color.postExposure.Override(Exposure);
                v.color.colorFilter.Override(Color.white);
                v.color.saturation.Override(0f);
                v.color.contrast.Override(0f);
                return;
            }

            // bloom: bright pass (soft knee = threshold / 2, as URP does) and the blur levels
            var P = v.ultra ? Ultra : quality == "low" ? Low : High;
            v.bloom.active = true;
            float thr = (under ? 0.85f : 1.0f) * settings.threshold;
            v.bloom.threshold.Override(Mathf.LinearToGammaSpace(Mathf.Max(0, thr))); // URP takes it in gamma
            float bs = (under ? 0.24f + 0.1f * turb : 0.16f) * settings.bloom;
            v.bloom.intensity.Override(bs * (P.weights[0] + P.weights[1] + P.weights[2]));
            v.bloom.maxIterations.Override(P.iterations);
            v.bloom.scatter.Override(P.scatter);

            // under water: absorption over the water column above the camera, and a slight grade
            Color a = Absorption(dep, turb, waterColor.maxColorComponent > 0 ? waterColor : DefaultWater, settings.absorption);
            float m = Mathf.Max(a.r, Mathf.Max(a.g, a.b));
            v.color.postExposure.Override(Exposure + Mathf.Log(m, 2f));
            v.color.colorFilter.Override(new Color(a.r / m, a.g / m, a.b / m, 1).gamma); // URP uses colorFilter.value.linear
            v.color.saturation.Override(((under ? 1.08f - 0.1f * turb : 1.0f) - 1) * 100);
            v.color.contrast.Override(((under ? 1.05f - 0.04f * turb : 1.0f) - 1) * 100);

            // soft vignette that follows the view's shape
            float vig = (under ? 0.34f + 0.1f * turb : 0.18f) * settings.vignette;
            v.vignette.active = vig > 0;
            if (vig > 0) v.vignette.intensity.Override(VignetteIntensity(vig));
        }

        /// The view's volume profile (for tweaking beyond Settings)
        public VolumeProfile Profile(int view) => Get(view).profile;

        /// absorption(out, depth, turb, water): per-channel transmittance of the water column above the
        /// camera, biased by the water's hue; a floor keeps team colours readable and the camera's
        /// exposure wins back part of the lost light. Linear colour.
        public static Color Absorption(float depth, float turb, Color water, float absorption = 1)
        {
            if (!(depth > 0)) return Color.white;
            float m = Mathf.Max(water.r, Mathf.Max(water.g, Mathf.Max(water.b, 1e-4f)));
            float s = (0.45f + 0.8f * turb) * absorption * depth;
            float tr = Mathf.Exp(-s * (0.11f + 0.05f * (1 - water.r / m)));
            float tg = Mathf.Exp(-s * (0.035f + 0.05f * (1 - water.g / m)));
            float tb = Mathf.Exp(-s * (0.02f + 0.05f * (1 - water.b / m)));
            float r = 0.2f + 0.8f * tr, g = 0.2f + 0.8f * tg, b = 0.2f + 0.8f * tb;
            float comp = Mathf.Pow(Mathf.Max(0.2126f * r + 0.7152f * g + 0.0722f * b, 0.05f), -0.6f);
            return new Color(r * comp, g * comp, b * comp, 1);
        }

        /// URP's vignette is pow(1 - |(uv - 0.5) * 3 i|^2, 5 s); with s = 0.4 the corners get the JS's
        /// 1 - v: (1 - 4.5 i^2)^2 = 1 - v
        public static float VignetteIntensity(float v)
        {
            v = Mathf.Clamp(v, 0, 0.99f);
            return Mathf.Clamp01(Mathf.Sqrt((1 - Mathf.Sqrt(1 - v)) / 4.5f));
        }

        /// dispose(): the volumes and their profiles
        public void Dispose()
        {
            if (follow) GraphicsQuality.Changed -= OnQualityChanged;
            for (int i = 0; i < views.Length; i++)
            {
                var v = views[i];
                if (v == null) continue;
                if (v.go != null) UnityEngine.Object.Destroy(v.go);
                DestroyProfile(v.profile);
                views[i] = null;
            }
        }

        static void DestroyProfile(VolumeProfile p)
        {
            if (p == null) return;
            foreach (var c in p.components) if (c != null) UnityEngine.Object.Destroy(c);
            UnityEngine.Object.Destroy(p);
        }

        // Enter Play Mode without a domain reload keeps statics: look for the templates again (Set Up
        // Project may have made them since).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            template = null; ultraTemplate = null;
            warned = false; warnedUltra = false; ultraLoaded = false;
        }
    }
}
