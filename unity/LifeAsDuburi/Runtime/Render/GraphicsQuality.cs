// src/render/graphics.js QUALITY table + loadQuality / saveQuality, and the global settings each
// level stands for: render scale (the canvas pixel ratio), MSAA (the post pass's own MSAA on High),
// main-light shadow map size / distance — applied to a private copy of the active
// UniversalRenderPipelineAsset (so the project's asset is never modified in the Editor).

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
        public string post;        // "high" | "low" | null (no post-processing)
        public int shadows;        // shadow map size, 0 = off
        public float pixelRatio;   // max device pixel ratio
        public int bubbles;        // bubble pool size
        public int cascades = 1;   // main-light shadow cascades (2 = sharper shadows near the camera)
    }

    public static class GraphicsQuality
    {
        public static readonly Dictionary<string, QualitySpec> QUALITY = new Dictionary<string, QualitySpec>
        {
            // Ultra: the screen's full resolution (4K on a 4K screen), 4x MSAA, 4096 shadows in two cascades
            ["ultra"] = new QualitySpec { label = "Ultra (4K)", post = "high", shadows = 4096, pixelRatio = 4f, bubbles = 3000, cascades = 2 },
            ["high"] = new QualitySpec { label = "High", post = "high", shadows = 2048, pixelRatio = 1.25f, bubbles = 2000 },
            ["medium"] = new QualitySpec { label = "Medium", post = "low", shadows = 1024, pixelRatio = 1f, bubbles = 1200 },
            ["low"] = new QualitySpec { label = "Low", post = null, shadows = 0, pixelRatio = 1f, bubbles = 600 },
        };
        public static readonly string[] Order = { "ultra", "high", "medium", "low" };

        const string STORE = "life-as-duburi-quality";
        /// shadow distance (m): the JS shadow camera covers the whole arena (max(L, W) / 2 + 4 around its centre)
        public const float ShadowDistance = 60f;

        static string current;
        static UniversalRenderPipelineAsset asset;

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

        /// applyQuality(): render scale, MSAA and main-light shadows of the pipeline asset
        public static void Apply()
        {
            var q = Spec;
            var a = Asset();
            if (a == null) return;
            // renderer.setPixelRatio(min(devicePixelRatio, q.pixelRatio)): Unity renders at device pixels
            float dpr = DevicePixelRatio();
            a.renderScale = Mathf.Clamp(Mathf.Min(dpr, q.pixelRatio) / dpr, 0.1f, 2f);
            // post 'high' renders the scene into a 4x MSAA target, post 'low' has none, and without post
            // the canvas itself is antialiased (WebGLRenderer({ antialias: !post }))
            a.msaaSampleCount = q.post == "low" ? 1 : 4;
            if (q.shadows > 0)
            {
                a.mainLightShadowmapResolution = q.shadows;
                a.shadowDistance = ShadowDistance;
                a.shadowCascadeCount = q.cascades;
            }
            // HDR stays on at every level: the JS tone-maps (ACES) on Low too
            a.supportsHDR = true;
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

        /// A private copy of the active URP asset (made once and installed for the current quality level).
        public static UniversalRenderPipelineAsset Asset()
        {
            var cur = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (cur == null) return asset;
            if (cur == asset) return asset;
            if (asset == null) { qualityBefore = QualitySettings.renderPipeline; installed = true; }
            asset = UnityEngine.Object.Instantiate(cur);
            asset.name = cur.name + " (Life as Duburi)";
            QualitySettings.renderPipeline = asset;
            return asset;
        }

        static RenderPipelineAsset qualityBefore;
        static bool installed;

        /// Puts the quality level's own pipeline asset back (App.Dispose): in the Editor the quality
        /// settings would otherwise keep pointing at the destroyed runtime copy after Play mode.
        public static void Restore()
        {
            if (!installed) return;
            installed = false;
            if (QualitySettings.renderPipeline == asset) QualitySettings.renderPipeline = qualityBefore;
            asset = null; // not destroyed: the pipeline it created is disposed by Unity when it notices the switch
            qualityBefore = null;
        }

        // Enter Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            asset = null; qualityBefore = null; installed = false; current = null; Changed = null;
        }
    }
}
