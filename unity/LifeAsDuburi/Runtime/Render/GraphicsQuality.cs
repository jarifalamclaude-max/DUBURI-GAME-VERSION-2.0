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
    }

    public static class GraphicsQuality
    {
        public static readonly Dictionary<string, QualitySpec> QUALITY = new Dictionary<string, QualitySpec>
        {
            ["high"] = new QualitySpec { label = "High", post = "high", shadows = 2048, pixelRatio = 1.25f, bubbles = 2000 },
            ["medium"] = new QualitySpec { label = "Medium", post = "low", shadows = 1024, pixelRatio = 1f, bubbles = 1200 },
            ["low"] = new QualitySpec { label = "Low", post = null, shadows = 0, pixelRatio = 1f, bubbles = 600 },
        };
        public static readonly string[] Order = { "high", "medium", "low" };

        const string STORE = "life-as-duburi-quality";
        /// shadow distance (m): the JS shadow camera covers the whole arena (max(L, W) / 2 + 4 around its centre)
        public const float ShadowDistance = 60f;

        static string current;
        static UniversalRenderPipelineAsset asset;

        /// raised after Set() with the new level (Post, scenes and bubbles follow it)
        public static event Action<string> Changed;

        /// current level id ("high" | "medium" | "low"), loaded from PlayerPrefs the first time
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
            return "high";
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
            // post 'high' renders the scene with 4x MSAA; 'low' and no post have none
            a.msaaSampleCount = q.post == "high" ? 4 : 1;
            if (q.shadows > 0)
            {
                a.mainLightShadowmapResolution = q.shadows;
                a.shadowDistance = ShadowDistance;
                a.shadowCascadeCount = 1;
            }
            // HDR stays on at every level: the JS tone-maps (ACES) on Low too
            a.supportsHDR = true;
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
            asset = UnityEngine.Object.Instantiate(cur);
            asset.name = cur.name + " (Life as Duburi)";
            QualitySettings.renderPipeline = asset;
            return asset;
        }
    }
}
