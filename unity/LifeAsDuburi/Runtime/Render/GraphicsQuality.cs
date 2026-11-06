// src/render/graphics.js QUALITY table + loadQuality / saveQuality, and the global settings each
// level stands for: render scale (the canvas pixel ratio, supersampling on Ultra), MSAA, main-light
// shadows (map size, cascades, soft-shadow filter, bias), screen-space ambient occlusion and texture
// filtering — applied to a private copy of the active UniversalRenderPipelineAsset (so the project's
// asset is never modified in the Editor) and to QualitySettings (put back by Restore()).
//
// Ultra is the best URP 17 can do in a WebGL 2 browser on a desktop GPU (real-time ray tracing does not
// exist there): it renders at up to 2x the screen's resolution (internal image <= 3840 x 2160, URP
// downsamples it: "4K quality on any screen") with 4x MSAA, 7x7-filtered soft shadows from a 4096 map
// in 4 cascades, SSAO (the URP renderer feature Set Up Project adds), a live reflection probe of the
// arena (SceneExtras), depth of field + high-quality bloom (Post, PostProfileUltra), 16x anisotropic
// filtering and larger generated textures. High / Medium / Low are the browser game's levels.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Duburi.Game
{
    /// one QUALITY entry
    public sealed class QualitySpec
    {
        public string label;
        public string post;        // "ultra" | "high" | "low" | null (no post-processing)
        public int shadows;        // shadow map size, 0 = off
        public float pixelRatio;   // max device pixel ratio
        public int bubbles;        // bubble pool size
        public int cascades = 1;   // main-light shadow cascades (1, 2 or 4)
        public int msaa = 4;       // MSAA samples of the scene (1 = off)
        /// max render scale above the device resolution: screens smaller than 4K render up to this many
        /// times larger (internal image <= MaxInternalWidth x MaxInternalHeight) and URP downsamples it
        public float supersample = 1;
        public bool ssao;          // URP ScreenSpaceAmbientOcclusion renderer feature on
        public bool reflections;   // live reflection probe of the arena (SceneExtras)
        public bool hqShadows;     // 7x7 soft-shadow filter, tighter cascades, smaller bias
        public int anisotropic;    // > 0: anisotropic filtering forced on for every texture, at least this level
        public bool hiResTextures; // larger generated textures (floor caustics, the water's look)
    }

    public static class GraphicsQuality
    {
        public static readonly Dictionary<string, QualitySpec> QUALITY = new Dictionary<string, QualitySpec>
        {
            // Ultra: full resolution, supersampled up to 4K, 4x MSAA, SSAO, live reflections, 7x7 soft shadows
            // (4096, 4 cascades), depth of field + HQ bloom, 16x anisotropic filtering
            ["ultra"] = new QualitySpec
            {
                label = "Ultra (4K)", post = "ultra", shadows = 4096, pixelRatio = 4f, bubbles = 3000, cascades = 4, msaa = 4,
                supersample = 2f, ssao = true, reflections = true, hqShadows = true, anisotropic = 16, hiResTextures = true,
            },
            ["high"] = new QualitySpec { label = "High", post = "high", shadows = 2048, pixelRatio = 1.25f, bubbles = 2000, msaa = 4 },
            // post 'low' has no MSAA (the JS renders the scene into a plain target there)
            ["medium"] = new QualitySpec { label = "Medium", post = "low", shadows = 1024, pixelRatio = 1f, bubbles = 1200, msaa = 1 },
            // without post the JS canvas itself is antialiased (WebGLRenderer({ antialias: !post }))
            ["low"] = new QualitySpec { label = "Low", post = null, shadows = 0, pixelRatio = 1f, bubbles = 600, msaa = 4 },
        };
        public static readonly string[] Order = { "ultra", "high", "medium", "low" };

        const string STORE = "life-as-duburi-quality";
        /// shadow distance (m): the JS shadow camera covers the whole arena (max(L, W) / 2 + 4 around its centre)
        public const float ShadowDistance = 60f;
        /// supersampling limit: the internal image of Ultra stays within 4K UHD
        public const int MaxInternalWidth = 3840, MaxInternalHeight = 2160;
        /// Ultra's 4 cascades end at 4.8, 13.2, 30 and 60 m: the first one holds the vehicles next to the
        /// camera (about 5 mm shadow texels), so thin frames and thrusters shadow themselves cleanly
        static readonly Vector3 UltraCascades = new Vector3(0.08f, 0.22f, 0.5f);
        /// Ultra's shadow bias (in shadow texels; URP widens it by the 7x7 filter's radius 3.5)
        const float UltraDepthBias = 0.6f, UltraNormalBias = 0.5f;

        static string current;
        static UniversalRenderPipelineAsset asset;
        // the copy's own values (the project asset's), used by every level but Ultra
        static float baseDepthBias = 1f, baseNormalBias = 1f, baseCascadeBorder = 0.2f;
        static bool baseConservative;
        static int lastW = -1, lastH = -1;

        /// raised after Set() with the new level (Post, scenes and bubbles follow it)
        public static event Action<string> Changed;

        /// current level id ("ultra" | "high" | "medium" | "low"), loaded from PlayerPrefs the first time
        public static string Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        public static QualitySpec Spec => QUALITY[Current];

        public static string Label(string id) => id != null && QUALITY.TryGetValue(id, out var q) ? q.label : id;

        /// loadQuality()
        public static string Load()
        {
            try
            {
                string q = PlayerPrefs.GetString(STORE, "");
                if (QUALITY.ContainsKey(q)) return q;
            }
            catch (Exception) { /* storage unavailable */ }
            // first start: full resolution on computers, High on phones and tablets
            return Application.isMobilePlatform ? "high" : "ultra";
        }

        /// saveQuality(q)
        public static void Save(string q)
        {
            try { PlayerPrefs.SetString(STORE, q); PlayerPrefs.Save(); } catch (Exception) { /* ignore */ }
        }

        /// remember (PlayerPrefs) + apply global settings; listeners (Post, RenderScene) follow
        public static void Set(string id)
        {
            if (id == null || !QUALITY.ContainsKey(id)) return;
            current = id;
            Save(id);
            Apply();
            Changed?.Invoke(id);
        }

        /// The next level in Order (the pause menu's "Graphics" button cycles through them).
        public static string Next() => Order[(Array.IndexOf(Order, Current) + 1) % Order.Length];

        /// applyQuality(): render scale, MSAA, main-light shadows and SSAO of the pipeline asset, texture filtering
        public static void Apply()
        {
            var q = Spec;
            ApplyTextureFiltering(q);
            var a = Asset();
            if (a == null) return;
            // renderer.setPixelRatio(min(devicePixelRatio, q.pixelRatio)): Unity renders at device pixels;
            // Ultra supersamples on screens smaller than 4K
            lastW = Screen.width; lastH = Screen.height;
            a.renderScale = RenderScaleFor(q, lastW, lastH);
            a.msaaSampleCount = q.msaa;
            if (q.shadows > 0)
            {
                a.mainLightShadowmapResolution = q.shadows;
                a.shadowDistance = ShadowDistance;
                int cascades = q.cascades >= 4 ? 4 : q.cascades >= 2 ? 2 : 1; // URP throws outside 1..4
                a.shadowCascadeCount = cascades;
                if (cascades == 4) a.cascade4Split = UltraCascades;
                a.shadowDepthBias = q.hqShadows ? UltraDepthBias : baseDepthBias;
                a.shadowNormalBias = q.hqShadows ? UltraNormalBias : baseNormalBias;
                a.cascadeBorder = q.hqShadows ? 0.1f : baseCascadeBorder;
                // the tightest sphere around each cascade: more of the 4096 map lands on what is visible
                a.conservativeEnclosingSphere = q.hqShadows || baseConservative;
            }
            // HDR stays on at every level: the JS tone-maps (ACES) on Low too
            a.supportsHDR = true;
            bool ssaoFound = SetSsao(a, q.ssao);
            if (q == QUALITY["ultra"]) CheckUltraSetUp(a, ssaoFound);
        }

        /// Follows a resized window / canvas (Views.DrawView calls it every frame; cheap when nothing changed).
        public static void Refresh()
        {
            int w = Screen.width, h = Screen.height;
            if (w == lastW && h == lastH) return;
            lastW = w; lastH = h;
            if (asset == null || (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset) != asset) return;
            asset.renderScale = RenderScaleFor(Spec, w, h);
        }

        /// The render scale of a level for a w x h (device pixel) screen: the JS pixel-ratio cap, and on
        /// Ultra supersampling up to q.supersample with the internal image within 3840 x 2160.
        public static float RenderScaleFor(QualitySpec q, int w, int h)
        {
            float dpr = DevicePixelRatio();
            float s = Mathf.Min(dpr, q.pixelRatio) / dpr;
            if (q.supersample > 1f && w > 0 && h > 0)
            {
                float longSide = Mathf.Max(w, h), shortSide = Mathf.Min(w, h);
                float ss = Mathf.Min(q.supersample, Mathf.Min(MaxInternalWidth / longSide, MaxInternalHeight / shortSide));
                if (ss < 1.05f) ss = 1f; // URP ignores scales within 0.05 of 1 anyway
                s = Mathf.Max(s, ss);
            }
            return Mathf.Clamp(s, 0.1f, 2f);
        }

        /// the render scale in use (1 without a URP asset)
        public static float RenderScale
        {
            get
            {
                var cur = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                return cur != null ? cur.renderScale : 1f;
            }
        }

        /// The browser's devicePixelRatio (Unity WebGL reports dpi = 96 * devicePixelRatio).
        public static float DevicePixelRatio()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            float dpi = Screen.dpi;
            return dpi > 0 ? Mathf.Max(1f, dpi / 96f) : 1f;
#else
            return 1f;
#endif
        }

        // ---- SSAO: the renderer feature Set Up Project adds (active in the asset, so the build keeps its
        // shader); switched per level. The renderer data is a project asset shared with the private copy,
        // so the original states are put back by Restore() (in the Editor they would otherwise stick).

        static readonly List<ScriptableRendererFeature> ssao = new List<ScriptableRendererFeature>();
        static readonly List<bool> ssaoBefore = new List<bool>();
        static UniversalRenderPipelineAsset ssaoScannedFor;

        /// switches every ScreenSpaceAmbientOcclusion feature of the asset's renderers; false when there is none
        static bool SetSsao(UniversalRenderPipelineAsset a, bool on)
        {
            if (ssaoScannedFor != a)
            {
                PutBackSsao();
                ssaoScannedFor = a;
                var list = a.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                {
                    var data = list[i];
                    if (data == null || data.rendererFeatures == null) continue;
                    foreach (var f in data.rendererFeatures)
                        if (f is ScreenSpaceAmbientOcclusion && !ssao.Contains(f)) { ssao.Add(f); ssaoBefore.Add(f.isActive); }
                }
            }
            bool found = false;
            for (int i = 0; i < ssao.Count; i++)
            {
                var f = ssao[i];
                if (f == null) continue;
                found = true;
                if (f.isActive != on) f.SetActive(on);
            }
            return found;
        }

        static void PutBackSsao()
        {
            for (int i = 0; i < ssao.Count; i++)
                if (ssao[i] != null && ssao[i].isActive != ssaoBefore[i]) ssao[i].SetActive(ssaoBefore[i]);
            ssao.Clear();
            ssaoBefore.Clear();
            ssaoScannedFor = null;
        }

        static bool warnedSetUp;

        // Ultra without what only Set Up Project can configure: one warning, the rest still works
        static void CheckUltraSetUp(UniversalRenderPipelineAsset a, bool ssaoFound)
        {
            if (warnedSetUp) return;
            string missing = "";
            if (!ssaoFound) missing += ", ambient occlusion (SSAO renderer feature)";
            if (!a.supportsSoftShadows) missing += ", soft shadows";
            if (!a.reflectionProbeBoxProjection) missing += ", box-projected reflections";
            if (Resources.Load<VolumeProfile>(Post.UltraProfilePath) == null) missing += ", depth of field + HQ bloom (PostProfileUltra)";
            if (missing.Length == 0) return;
            warnedSetUp = true;
            Debug.LogWarning("Life as Duburi: Ultra graphics run without " + missing.Substring(2) +
                             ". Run Life as Duburi > Set Up Project again (needed after every update of the game files); everything else works.");
        }

        // ---- texture filtering and real-time reflection probes (QualitySettings, put back by Restore)

        static bool qualityKept;
        static AnisotropicFiltering anisoBefore;
        static bool realtimeProbesBefore;

        static void KeepQualitySettings()
        {
            if (qualityKept) return;
            qualityKept = true;
            anisoBefore = QualitySettings.anisotropicFiltering;
            realtimeProbesBefore = QualitySettings.realtimeReflectionProbes;
        }

        static void ApplyTextureFiltering(QualitySpec q)
        {
            KeepQualitySettings();
            if (q.anisotropic > 0)
            {
                // every texture (also the generated ones, made with anisoLevel 1) at least q.anisotropic
                Texture.SetGlobalAnisotropicFilteringLimits(q.anisotropic, 16);
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            }
            else if (QualitySettings.anisotropicFiltering != anisoBefore) QualitySettings.anisotropicFiltering = anisoBefore;
        }

        /// LiveReflection needs real-time reflection probes on in the quality level (put back by Restore()).
        public static void UseRealtimeReflectionProbes()
        {
            KeepQualitySettings();
            if (!QualitySettings.realtimeReflectionProbes) QualitySettings.realtimeReflectionProbes = true;
        }

        /// A private copy of the active URP asset (made once and installed for the current quality level).
        public static UniversalRenderPipelineAsset Asset()
        {
            var cur = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (cur == null) return asset;
            if (cur == asset) return asset;
            if (asset == null) { qualityBefore = QualitySettings.renderPipeline; installed = true; }
            baseDepthBias = cur.shadowDepthBias;
            baseNormalBias = cur.shadowNormalBias;
            baseCascadeBorder = cur.cascadeBorder;
            baseConservative = cur.conservativeEnclosingSphere;
            asset = UnityEngine.Object.Instantiate(cur);
            asset.name = cur.name + " (Life as Duburi)";
            QualitySettings.renderPipeline = asset;
            return asset;
        }

        static RenderPipelineAsset qualityBefore;
        static bool installed;

        /// Puts the quality level's own pipeline asset, the SSAO features and the quality settings back
        /// (App.Dispose): in the Editor they would otherwise keep the game's values after Play mode.
        public static void Restore()
        {
            PutBackSsao();
            if (qualityKept)
            {
                qualityKept = false;
                if (QualitySettings.anisotropicFiltering != anisoBefore) QualitySettings.anisotropicFiltering = anisoBefore;
                if (QualitySettings.realtimeReflectionProbes != realtimeProbesBefore) QualitySettings.realtimeReflectionProbes = realtimeProbesBefore;
            }
            if (!installed) return;
            installed = false;
            if (QualitySettings.renderPipeline == asset) QualitySettings.renderPipeline = qualityBefore;
            asset = null; // not destroyed: the pipeline it created is disposed by Unity when it notices the switch
            qualityBefore = null;
            lastW = lastH = -1;
        }

        // Enter Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            asset = null; qualityBefore = null; installed = false; current = null; Changed = null;
            ssao.Clear(); ssaoBefore.Clear(); ssaoScannedFor = null;
            qualityKept = false; warnedSetUp = false; lastW = lastH = -1;
        }
    }
}
