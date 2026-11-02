// Menu "Life as Duburi / Set Up Project": everything the game needs in a fresh
// Unity 6.3 "Universal 3D" project, safe to run again at any time:
//   * checks: Unity 6000.3+, URP active, Input System active
//   * template materials in Resources/Duburi/Materials (Render/Mats.cs clones them)
//   * Resources/Duburi/PostProfile.asset (Bloom, Tonemapping ACES, ColorAdjustments, Vignette)
//   * import settings for Resources/Duburi/Art
//   * Graphics fog stripping that keeps Linear / Exp / Exp2
//   * Scenes/LifeAsDuburi.unity (fog on, one "Life as Duburi" object with Duburi.Game.Boot),
//     first in the build list, opened
//   * player settings (names, landscape, run in background, WebGL: Gzip + fallback, caching, WebGL 2)
// Shows a dialog and writes the full report to the Console.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Duburi.EditorTools
{
    public static class ProjectSetup
    {
        public const string Root = "Assets/LifeAsDuburi";
        public const string ResourcesDir = Root + "/Resources/Duburi";
        public const string MaterialsDir = ResourcesDir + "/Materials";
        public const string ArtDir = ResourcesDir + "/Art";
        public const string PostProfilePath = ResourcesDir + "/PostProfile.asset";
        public const string ScenesDir = Root + "/Scenes";
        public const string ScenePath = ScenesDir + "/LifeAsDuburi.unity";
        public const string BootObjectName = "Life as Duburi";

        const string LitShader = "Universal Render Pipeline/Lit";
        const string UnlitShader = "Universal Render Pipeline/Unlit";
        const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        // BaseShaderGUI.SurfaceType / BlendMode values (URP 17)
        const int Opaque = 0, Transparent = 1;
        const int BlendAlpha = 0, BlendPremultiply = 1, BlendAdditive = 2, BlendMultiply = 3;

        sealed class MatSpec
        {
            public string name, shader;
            public int surface, blend;
            public bool clip, emission;
        }

        // the templates PORTING.md lists (Render/Mats.cs loads them by these names)
        static readonly MatSpec[] Materials =
        {
            new MatSpec { name = "Lit", shader = LitShader, surface = Opaque },
            new MatSpec { name = "LitTransparent", shader = LitShader, surface = Transparent, blend = BlendAlpha },
            new MatSpec { name = "LitEmissive", shader = LitShader, surface = Opaque, emission = true },
            new MatSpec { name = "LitCutout", shader = LitShader, surface = Opaque, clip = true },
            new MatSpec { name = "Unlit", shader = UnlitShader, surface = Opaque },
            new MatSpec { name = "UnlitTransparent", shader = UnlitShader, surface = Transparent, blend = BlendAlpha },
            new MatSpec { name = "UnlitAdditive", shader = UnlitShader, surface = Transparent, blend = BlendAdditive },
            new MatSpec { name = "Particle", shader = ParticleShader, surface = Transparent, blend = BlendAlpha },
            new MatSpec { name = "ParticleAdditive", shader = ParticleShader, surface = Transparent, blend = BlendAdditive },
        };

        [MenuItem("Life as Duburi/Set Up Project", false, 1)]
        public static void SetUpMenu()
        {
            var report = new Report();
            bool ok = Run(report, interactive: true);
            report.Show(ok ? "Life as Duburi is set up" : "Life as Duburi: set-up needs attention",
                ok ? "Press Play to try it, or use Life as Duburi > Build WebGL." : "Fix the items marked ✗ and run Set Up Project again.");
        }

        // The whole set-up; false when something needs the user's attention.
        public static bool Run(Report report, bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.Fail("Leave Play mode first.");
                return false;
            }
            bool ok = Check(report);
            try
            {
                EditorUtility.DisplayProgressBar("Life as Duburi", "Materials", 0.1f);
                ok &= CreateMaterials(report);
                EditorUtility.DisplayProgressBar("Life as Duburi", "Post-processing profile", 0.3f);
                ok &= CreatePostProfile(report);
                EditorUtility.DisplayProgressBar("Life as Duburi", "Texture import settings", 0.45f);
                SetTextureImport(report);
                EditorUtility.DisplayProgressBar("Life as Duburi", "Fog shader stripping", 0.55f);
                ok &= SetFogStripping(report);
                EditorUtility.DisplayProgressBar("Life as Duburi", "Player settings", 0.65f);
                SetPlayerSettings(report);
                AssetDatabase.SaveAssets();
            }
            finally { EditorUtility.ClearProgressBar(); }
            // the scene last: it may ask to save the open scene
            ok &= CreateScene(report, interactive);
            AssetDatabase.SaveAssets();
            return ok;
        }

        // ---- checks ---------------------------------------------------------------------

        public static bool Check(Report report)
        {
            bool ok = true;
            ParseVersion(Application.unityVersion, out int major, out int minor);
            if (major > 6000 || (major == 6000 && minor >= 3)) report.Ok("Unity " + Application.unityVersion);
            else
            {
                report.Fail("Unity " + Application.unityVersion + " is too old. Install Unity 6.3 LTS (6000.3) with Unity Hub and open the project with it.");
                ok = false;
            }

            var rp = GraphicsSettings.defaultRenderPipeline;
            var q = QualitySettings.renderPipeline;
            if (IsUrp(rp) && (q == null || IsUrp(q))) report.Ok("Universal Render Pipeline active (" + (q != null ? q.name : rp.name) + ")");
            else
            {
                report.Fail("The Universal Render Pipeline is not active. Easiest: create the project from the \"Universal 3D\" template in Unity Hub. " +
                            "Or: Edit > Project Settings > Graphics > Default Render Pipeline = a Universal Render Pipeline Asset " +
                            "(Assets > Create > Rendering > URP Asset (with Universal Renderer)), and clear the Render Pipeline override in Project Settings > Quality.");
                ok = false;
            }

#if ENABLE_INPUT_SYSTEM
            report.Ok("Input System package active");
#else
            report.Fail("The Input System is not active. Window > Package Manager > Unity Registry > \"Input System\" > Install, then " +
                        "Edit > Project Settings > Player > Other Settings > Active Input Handling = \"Input System Package (New)\" " +
                        "and let Unity restart.");
            ok = false;
#endif
            return ok;
        }

        static void ParseVersion(string v, out int major, out int minor)
        {
            major = minor = 0;
            if (string.IsNullOrEmpty(v)) return;
            var parts = v.Split('.');
            if (parts.Length > 0) int.TryParse(parts[0], out major);
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
        }

        static bool IsUrp(RenderPipelineAsset a)
        {
            for (var t = a != null ? a.GetType() : null; t != null; t = t.BaseType)
                if (t.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") return true;
            return false;
        }

        // ---- folders / reflection helpers -------------------------------------------------

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash), leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        // a type from any loaded assembly (URP / Core types without a compile-time reference)
        static Type FindType(string fullName)
        {
            if (TypeCache.TryGetValue(fullName, out var t)) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, false);
                if (t != null) break;
            }
            TypeCache[fullName] = t;
            return t;
        }

        // ---- materials --------------------------------------------------------------------

        static bool CreateMaterials(Report report)
        {
            EnsureFolder(MaterialsDir);
            bool ok = true;
            int made = 0, updated = 0;
            foreach (var spec in Materials)
            {
                var shader = Shader.Find(spec.shader);
                if (shader == null)
                {
                    report.Fail("Shader \"" + spec.shader + "\" not found (is URP installed?) — material " + spec.name + " not created.");
                    ok = false;
                    continue;
                }
                string path = MaterialsDir + "/" + spec.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew) mat = new Material(shader) { name = spec.name };
                else if (mat.shader != shader) mat.shader = shader;
                Configure(mat, spec);
                if (isNew) { AssetDatabase.CreateAsset(mat, path); made++; }
                else { EditorUtility.SetDirty(mat); updated++; }
            }
            report.Ok("Template materials in " + MaterialsDir + " (" + made + " created, " + updated + " updated)");
            return ok;
        }

        static void Configure(Material m, MatSpec s)
        {
            Set(m, "_BaseColor", Color.white);
            Set(m, "_Color", Color.white);
            Set(m, "_Cutoff", 0.5f);
            Set(m, "_Surface", s.surface);
            Set(m, "_Blend", s.blend);
            Set(m, "_AlphaClip", s.clip ? 1f : 0f);
            Set(m, "_QueueOffset", 0f);
            if (m.HasProperty("_EmissionColor"))
            {
                // white, so that URP's FixupEmissiveFlag keeps _EMISSION on; Mats sets the real colour per clone
                m.SetColor("_EmissionColor", s.emission ? Color.white : Color.black);
                m.globalIlluminationFlags = s.emission ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            SetupBlendMode(m);
            SetKeyword(m, "_EMISSION", s.emission);
            CallUrpSetMaterialKeywords(m);
        }

        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void Set(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
        static void SetKeyword(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); }

        static void SetBlend(Material m, BlendMode srcRgb, BlendMode dstRgb, BlendMode srcA, BlendMode dstA)
        {
            Set(m, "_SrcBlend", (float)srcRgb);
            Set(m, "_DstBlend", (float)dstRgb);
            Set(m, "_SrcBlendAlpha", (float)srcA);
            Set(m, "_DstBlendAlpha", (float)dstA);
        }

        // By hand, the way URP 17's BaseShaderGUI.SetupMaterialBlendModeInternal and
        // UpdateMaterialSurfaceOptions set a material up (Editor/ShaderGUI/BaseShaderGUI.cs),
        // so the templates are right even without the URP editor assembly.
        static void SetupBlendMode(Material material)
        {
            bool alphaClip = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") >= 0.5f;
            SetKeyword(material, "_ALPHATEST_ON", alphaClip);

            // default is to use the shader render queue
            int renderQueue = material.shader.renderQueue;
            material.SetOverrideTag("RenderType", ""); // clear override tag
            bool opaque = true;
            if (material.HasProperty("_Surface"))
            {
                int surfaceType = (int)material.GetFloat("_Surface");
                opaque = surfaceType == Opaque;
                bool zwrite;
                SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", surfaceType == Transparent);
                bool alphaToMask = false;
                if (surfaceType == Opaque)
                {
                    if (alphaClip)
                    {
                        renderQueue = (int)RenderQueue.AlphaTest;
                        material.SetOverrideTag("RenderType", "TransparentCutout");
                        alphaToMask = true;
                    }
                    else
                    {
                        renderQueue = (int)RenderQueue.Geometry;
                        material.SetOverrideTag("RenderType", "Opaque");
                    }
                    SetBlend(material, BlendMode.One, BlendMode.Zero, BlendMode.One, BlendMode.Zero);
                    zwrite = true;
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHAMODULATE_ON");
                }
                else
                {
                    int blendMode = material.HasProperty("_Blend") ? (int)material.GetFloat("_Blend") : BlendAlpha;
                    var srcBlendRGB = BlendMode.One;
                    var dstBlendRGB = BlendMode.OneMinusSrcAlpha;
                    var srcBlendA = BlendMode.One;
                    var dstBlendA = BlendMode.OneMinusSrcAlpha;
                    switch (blendMode)
                    {
                        // srcRGB * srcAlpha + dstRGB * (1 - srcAlpha)
                        case BlendAlpha:
                            srcBlendRGB = BlendMode.SrcAlpha; dstBlendRGB = BlendMode.OneMinusSrcAlpha;
                            srcBlendA = BlendMode.One; dstBlendA = dstBlendRGB;
                            break;
                        // srcRGB < srcAlpha, (alpha multiplied in asset)
                        case BlendPremultiply:
                            srcBlendRGB = BlendMode.One; dstBlendRGB = BlendMode.OneMinusSrcAlpha;
                            srcBlendA = srcBlendRGB; dstBlendA = dstBlendRGB;
                            break;
                        // srcRGB * srcAlpha + dstRGB * 1, (alpha controls amount of addition)
                        case BlendAdditive:
                            srcBlendRGB = BlendMode.SrcAlpha; dstBlendRGB = BlendMode.One;
                            srcBlendA = BlendMode.One; dstBlendA = dstBlendRGB;
                            break;
                        // srcRGB * 0 + dstRGB * srcRGB
                        case BlendMultiply:
                            srcBlendRGB = BlendMode.DstColor; dstBlendRGB = BlendMode.Zero;
                            srcBlendA = BlendMode.Zero; dstBlendA = BlendMode.One;
                            break;
                    }
                    // Lift alpha multiply from ROP to shader by setting pre-multiplied _SrcBlend mode.
                    bool preserveSpecular = material.HasProperty("_BlendModePreserveSpecular") &&
                                            material.GetFloat("_BlendModePreserveSpecular") > 0 &&
                                            blendMode != BlendMultiply && blendMode != BlendPremultiply;
                    if (preserveSpecular) srcBlendRGB = BlendMode.One;
                    SetBlend(material, srcBlendRGB, dstBlendRGB, srcBlendA, dstBlendA);
                    SetKeyword(material, "_ALPHAPREMULTIPLY_ON", preserveSpecular);
                    SetKeyword(material, "_ALPHAMODULATE_ON", blendMode == BlendMultiply);
                    // General Transparent Material Settings
                    material.SetOverrideTag("RenderType", "Transparent");
                    zwrite = false;
                    renderQueue = (int)RenderQueue.Transparent;
                }
                Set(material, "_AlphaToMask", alphaToMask ? 1f : 0f);
                Set(material, "_ZWrite", zwrite ? 1f : 0f);
                material.SetShaderPassEnabled("DepthOnly", zwrite);
            }
            else material.SetShaderPassEnabled("DepthOnly", true);

            // must always apply queue offset, even if not set to material control
            if (material.HasProperty("_QueueOffset")) renderQueue += (int)material.GetFloat("_QueueOffset");
            material.renderQueue = renderQueue;

            // Cast Shadows: Lit.shader / Unlit.shader set it from the transparency
            bool castShadows = material.HasProperty("_CastShadows") ? material.GetFloat("_CastShadows") != 0f : opaque;
            material.SetShaderPassEnabled("ShadowCaster", castShadows);
            // Receive Shadows
            if (material.HasProperty("_ReceiveShadows"))
                SetKeyword(material, "_RECEIVE_SHADOWS_OFF", material.GetFloat("_ReceiveShadows") == 0f);
        }

        // UnityEditor.BaseShaderGUI.SetMaterialKeywords(material, shadingModelFunc, shaderFunc)
        // with the functions the URP shader GUIs pass (LitShader / ParticlesUnlitShader
        // .ValidateMaterial), through reflection: no compile-time URP editor dependency.
        static void CallUrpSetMaterialKeywords(Material m)
        {
            var gui = FindType("UnityEditor.BaseShaderGUI");
            var set = gui?.GetMethod("SetMaterialKeywords", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Material), typeof(Action<Material>), typeof(Action<Material>) }, null);
            if (set == null) return;
            Action<Material> shading = null, shaderFn = null;
            string sh = m.shader.name;
            if (sh == LitShader)
            {
                shading = KeywordFunc("UnityEditor.Rendering.Universal.ShaderGUI.LitGUI");
                shaderFn = KeywordFunc("UnityEditor.Rendering.Universal.ShaderGUI.LitDetailGUI");
            }
            else if (sh.StartsWith("Universal Render Pipeline/Particles/", StringComparison.Ordinal))
                shaderFn = KeywordFunc("UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI");
            try { set.Invoke(null, new object[] { m, shading, shaderFn }); }
            catch (TargetInvocationException e) { Debug.LogWarning("Life as Duburi: BaseShaderGUI.SetMaterialKeywords failed on " + m.name + ": " + e.InnerException?.Message); }
        }

        static Action<Material> KeywordFunc(string typeName)
        {
            var mi = FindType(typeName)?.GetMethod("SetMaterialKeywords", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            return mi != null ? (Action<Material>)Delegate.CreateDelegate(typeof(Action<Material>), mi) : null;
        }

        // ---- post-processing profile ------------------------------------------------------

        // A VolumeProfile with Bloom, Tonemapping (ACES), ColorAdjustments and Vignette, all
        // overrides active, so their shader variants are kept in the build (URP keeps the
        // post-processing variants of the components found in VolumeProfile assets).
        static bool CreatePostProfile(Report report)
        {
            var profileType = FindType("UnityEngine.Rendering.VolumeProfile");
            if (profileType == null)
            {
                report.Fail("VolumeProfile type not found (Core RP package missing?) — PostProfile.asset not created.");
                return false;
            }
            EnsureFolder(ResourcesDir);
            var profile = AssetDatabase.LoadAssetAtPath(PostProfilePath, profileType) as ScriptableObject;
            bool isNew = profile == null;
            if (isNew)
            {
                profile = ScriptableObject.CreateInstance(profileType);
                AssetDatabase.CreateAsset(profile, PostProfilePath);
            }
            var components = profileType.GetField("components")?.GetValue(profile) as IList;
            var add = profileType.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            if (components == null || add == null)
            {
                report.Fail("VolumeProfile API not as expected — PostProfile.asset not completed.");
                return false;
            }
            bool ok = true;
            string[] names =
            {
                "UnityEngine.Rendering.Universal.Bloom",
                "UnityEngine.Rendering.Universal.Tonemapping",
                "UnityEngine.Rendering.Universal.ColorAdjustments",
                "UnityEngine.Rendering.Universal.Vignette",
            };
            foreach (var n in names)
            {
                var t = FindType(n);
                if (t == null) { report.Fail(n + " not found (URP missing?)"); ok = false; continue; }
                ScriptableObject comp = null;
                foreach (var c in components) if (c != null && c.GetType() == t) comp = (ScriptableObject)c;
                if (comp == null)
                {
                    comp = (ScriptableObject)add.Invoke(profile, new object[] { t, true });
                    AssetDatabase.AddObjectToAsset(comp, profile);
                }
                using (var so = new SerializedObject(comp))
                {
                    var active = so.FindProperty("active");
                    if (active != null) active.boolValue = true;
                    // every parameter's override on
                    var it = so.GetIterator();
                    while (it.Next(true)) if (it.name == "m_OverrideState") it.boolValue = true;
                    if (t.Name == "Tonemapping")
                    {
                        var mode = so.FindProperty("mode.m_Value");
                        if (mode != null) mode.intValue = 2; // TonemappingMode.ACES
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(comp);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
            if (ok) report.Ok("Post-processing profile " + PostProfilePath + (isNew ? " (created)" : " (checked)"));
            return ok;
        }

        // ---- textures -----------------------------------------------------------------------

        static void SetTextureImport(Report report)
        {
            if (!AssetDatabase.IsValidFolder(ArtDir)) { report.Warn("No " + ArtDir + " folder (logo / handbook images missing?)"); return; }
            int changed = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                total++;
                bool dirty = ti.textureType != TextureImporterType.Default || ti.npotScale != TextureImporterNPOTScale.None ||
                             !ti.isReadable || ti.wrapMode != TextureWrapMode.Clamp || !ti.mipmapEnabled || !ti.sRGBTexture ||
                             ti.maxTextureSize != 2048;
                if (!dirty) continue;
                ti.textureType = TextureImporterType.Default;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.isReadable = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.sRGBTexture = true;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
                changed++;
            }
            report.Ok("Art import settings (" + total + " images, " + changed + " re-imported)");
        }

        // ---- fog stripping -------------------------------------------------------------------

        // Graphics settings > Shader Stripping > Fog Modes = Custom, keeping Linear, Exponential
        // and Exponential Squared (serialized names from Editor/Mono/Inspector/GraphicsSettingsInspector.cs).
        static bool SetFogStripping(Report report)
        {
            var gs = GraphicsSettings.GetGraphicsSettings();
            if (gs == null) { report.Fail("Graphics settings asset not found."); return false; }
            using (var so = new SerializedObject(gs))
            {
                var mode = so.FindProperty("m_FogStripping");
                var lin = so.FindProperty("m_FogKeepLinear");
                var exp = so.FindProperty("m_FogKeepExp");
                var exp2 = so.FindProperty("m_FogKeepExp2");
                if (mode == null || lin == null || exp == null || exp2 == null)
                {
                    report.Fail("Fog stripping settings not found; set Edit > Project Settings > Graphics > Fog Modes = Custom with all three modes by hand.");
                    return false;
                }
                mode.intValue = 1; // StrippingModes.Custom
                lin.boolValue = true;
                exp.boolValue = true;
                exp2.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            report.Ok("Fog shader stripping: keep Linear, Exponential, Exponential Squared");
            return true;
        }

        // ---- player settings -----------------------------------------------------------------

        static void SetPlayerSettings(Report report)
        {
            PlayerSettings.productName = "Life as Duburi";
            PlayerSettings.companyName = "Team DUBURI";
            // landscape on phones and tablets
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.runInBackground = true;
            try
            {
                PlayerSettings.SplashScreen.show = false;
                PlayerSettings.SplashScreen.showUnityLogo = false;
            }
            catch (Exception) { /* not allowed with this licence */ }
            string splash = PlayerSettings.SplashScreen.show ? "splash screen stays on (licence)" : "splash screen off";

            // WebGL: Gzip with the decompression fallback works on any static host (no server headers needed)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 }); // WebGL 2
            report.Ok("Player settings: \"Life as Duburi\" by Team DUBURI, landscape, runs in background, " + splash +
                      "; WebGL 2, Gzip + decompression fallback, data caching, explicitly thrown exceptions only");
        }

        // ---- scene + build list --------------------------------------------------------------

        static bool CreateScene(Report report, bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                report.Warn("Scene step skipped (you cancelled saving the open scene). Run Set Up Project again to finish.");
                return false;
            }
            EnsureFolder(ScenesDir);
            bool exists = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ScenePath) != null;
            Scene scene = exists ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                                 : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject boot = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<Duburi.Game.Boot>() != null) { boot = go; break; }
                if (boot == null && go.name == BootObjectName) boot = go;
            }
            if (boot == null) boot = new GameObject(BootObjectName);
            boot.name = BootObjectName;
            if (boot.GetComponent<Duburi.Game.Boot>() == null) boot.AddComponent<Duburi.Game.Boot>();

            // underwater fog (the game sets colour / density per view at run time)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.fogColor = new Color(0.06f, 0.24f, 0.31f);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                report.Fail("Could not save " + ScenePath);
                return false;
            }
            report.Ok("Scene " + ScenePath + (exists ? " (updated, open)" : " (created, open)"));

            // first enabled scene in the build list, the others kept after it
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s != null && s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
            report.Ok("Build list: " + ScenePath + " is scene 0");
            return true;
        }

        // ---- report ---------------------------------------------------------------------------

        public sealed class Report
        {
            readonly StringBuilder sb = new StringBuilder();
            public int failures, warnings;
            public void Ok(string s) => sb.Append("✓ ").AppendLine(s);
            public void Warn(string s) { warnings++; sb.Append("! ").AppendLine(s); }
            public void Fail(string s) { failures++; sb.Append("✗ ").AppendLine(s); }
            public void Line(string s) => sb.AppendLine(s);
            public override string ToString() => sb.ToString();

            public void Show(string title, string footer)
            {
                string text = sb + "\n" + footer;
                if (failures > 0) Debug.LogWarning(title + "\n" + text);
                else Debug.Log(title + "\n" + text);
                EditorUtility.DisplayDialog(title, text, "OK");
            }
        }
    }
}
