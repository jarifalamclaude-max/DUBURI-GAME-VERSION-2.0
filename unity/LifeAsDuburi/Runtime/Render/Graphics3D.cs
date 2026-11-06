// src/render/graphics.js: image-quality settings shared by the game — a reflection environment for the
// metal / acrylic parts (a sky-over-water gradient per scenario) and sun shadows.
//
// three.js lights a MeshStandardMaterial with  hemisphere / ambient irradiance * albedo / PI  plus the
// PMREM environment * envMapIntensity; Unity has one ambient (RenderSettings, trilight) and reflection
// probes. Ambient sums the scenery's hemisphere light and the environment's diffuse part; the
// environment's specular part is a custom-cubemap ReflectionProbe. Light intensities are three's / PI
// (Unity's light units include the PI that three's BRDF_Lambert divides by).
//
// Ultra adds LiveReflection: a real-time reflection probe of the arena (rendered by URP one cube face
// per frame, again and again), so the metal, brass, chrome and acrylic parts reflect the real pool or
// seabed, the course and the other vehicles instead of the gradient. Screen-space reflections and ray
// tracing do not exist in URP / WebGL 2; this is the closest real-time reflection they offer.

using Duburi.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Duburi.Game
{
    /// makeEnvironment(): the gradient environment of one look
    public sealed class EnvLight
    {
        public GameObject go;
        public Cubemap cube;
        public float intensity;
        /// cosine-weighted (diffuse) environment radiance for normals up / sideways / down, linear
        public Color up, side, down;

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
            if (cube != null) Object.Destroy(cube);
            go = null; cube = null;
        }
    }

    /// The scene's ambient light: three's HemisphereLight + the environment's diffuse part.
    public static class Ambient
    {
        static Color hemiSky, hemiGround;
        static float hemiI;
        static EnvLight env;

        public static void SetHemisphere(Color skyLinear, Color groundLinear, float intensity)
        {
            hemiSky = skyLinear; hemiGround = groundLinear; hemiI = intensity;
            Apply();
        }

        public static void SetEnvironment(EnvLight e)
        {
            env = e;
            Apply();
        }

        public static void Apply()
        {
            Compute(hemiSky, hemiGround, hemiI, env, out var sky, out var eq, out var ground);
            Set(sky, eq, ground);
        }

        /// the trilight colours (linear) for a hemisphere light (linear colours, three intensity) plus an
        /// environment's diffuse part
        public static void Compute(Color skyLinear, Color groundLinear, float intensity, EnvLight e, out Color sky, out Color eq, out Color ground)
        {
            // three HemisphereLight: irradiance = mix(ground, sky, 0.5 n.y + 0.5) * intensity, diffuse = irradiance / PI
            float k = intensity / Mathf.PI;
            sky = skyLinear * k; ground = groundLinear * k; eq = (skyLinear + groundLinear) * 0.5f * k;
            if (e != null)
            {
                sky += e.up * e.intensity;
                eq += e.side * e.intensity;
                ground += e.down * e.intensity;
            }
        }

        /// RenderSettings trilight ambient from linear colours
        public static void Set(Color skyLinear, Color eqLinear, Color groundLinear)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyLinear.gamma;
            RenderSettings.ambientEquatorColor = eqLinear.gamma;
            RenderSettings.ambientGroundColor = groundLinear.gamma;
            RenderSettings.ambientIntensity = 1f;
        }

        /// the environment currently in the ambient (SceneExtras)
        public static EnvLight Environment => env;
    }

    public static class Graphics3D
    {
        const int CubeSize = 64;

        /// makeEnvironment(renderer, look): gradient sphere (bright surface above, deep water below) as a
        /// reflection probe around 'center' (Unity world) of the given size, and the diffuse part for Ambient.
        public static EnvLight MakeEnvironment(ScenarioLook look, float intensity, Vector3 center, float size)
        {
            Color top = Color.Lerp(Colors.Rgb(look.surface).linear, Color.white, 0.45f);
            Color mid = Colors.Rgb(look.water).linear;
            Color bot = Colors.Rgb(look.fog).linear * 0.25f;
            var e = new EnvLight { intensity = intensity };

            bool half = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf);
            var cube = new Cubemap(CubeSize, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, true) { name = "environment" };
            var px = new Color[CubeSize * CubeSize];
            for (int f = 0; f < 6; f++)
            {
                for (int row = 0; row < CubeSize; row++)
                    for (int col = 0; col < CubeSize; col++)
                    {
                        // face pixel -> direction (cube map convention, row 0 at the top of the face)
                        float sc = (col + 0.5f) / CubeSize * 2 - 1, tc = (row + 0.5f) / CubeSize * 2 - 1;
                        Vector3 d;
                        switch ((CubemapFace)f)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1, -tc, -sc); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1, -tc, sc); break;
                            case CubemapFace.PositiveY: d = new Vector3(sc, 1, tc); break;
                            case CubemapFace.NegativeY: d = new Vector3(sc, -1, -tc); break;
                            case CubemapFace.PositiveZ: d = new Vector3(sc, -tc, 1); break;
                            default: d = new Vector3(-sc, -tc, -1); break;
                        }
                        Color c = Radiance(d.normalized.y, top, mid, bot);
                        px[row * CubeSize + col] = half ? c : c.gamma;
                    }
                cube.SetPixels(px, (CubemapFace)f, 0);
            }
            cube.Apply(true, false);
            e.cube = cube;

            // diffuse (cosine-weighted) radiance for the three ambient directions
            e.up = Irradiance(Vector3.up, top, mid, bot);
            e.side = Irradiance(Vector3.right, top, mid, bot);
            e.down = Irradiance(Vector3.down, top, mid, bot);

            var go = new GameObject("Environment");
            go.transform.position = center;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cube;
            probe.size = new Vector3(size, size, size);
            probe.intensity = intensity;
            probe.boxProjection = false;
            probe.importance = 10;
            probe.blendDistance = 0;
            e.go = go;
            return e;
        }

        // the JS gradient shader: c = y > 0 ? mix(mid, top, y^0.6) : mix(mid, bot, (-y)^0.7), + sun / Snell's window
        static Color Radiance(float y, Color top, Color mid, Color bot)
        {
            Color c = y > 0 ? Color.Lerp(mid, top, Mathf.Pow(y, 0.6f)) : Color.Lerp(mid, bot, Mathf.Pow(-y, 0.7f));
            float s = Mathf.Pow(Mathf.Max(y, 0), 24f) * 3f;
            c += new Color(1f, 0.97f, 0.9f) * s;
            c.a = 1;
            return c;
        }

        static Color Irradiance(Vector3 n, Color top, Color mid, Color bot)
        {
            // (1/PI) * integral of L(d) max(n.d, 0) dw over a lat-long grid
            const int NT = 48, NP = 96;
            Color sum = Color.black;
            float wsum = 0;
            for (int i = 0; i < NT; i++)
            {
                float th = (i + 0.5f) / NT * Mathf.PI;
                float st = Mathf.Sin(th), ct = Mathf.Cos(th);
                for (int j = 0; j < NP; j++)
                {
                    float ph = (j + 0.5f) / NP * Mathf.PI * 2;
                    var d = new Vector3(st * Mathf.Cos(ph), ct, st * Mathf.Sin(ph));
                    float w = Mathf.Max(0, Vector3.Dot(n, d)) * st;
                    if (w <= 0) continue;
                    sum += Radiance(d.y, top, mid, bot) * w;
                    wsum += w;
                }
            }
            Color r = wsum > 0 ? sum / wsum : mid;
            r.a = 1;
            return r;
        }

        /// setupShadows(renderer, scene, sceneryRoot, size, arena): the scenery's sun casts onto the
        /// floor, course and vehicles; high sun, slightly from the south-east, over the arena centre.
        /// Returns the sun, or null when shadows are off (the sun then keeps the scenery's direction).
        /// hq (Ultra): URP's highest soft-shadow filter (7x7 tent) on the sun.
        public static Light SetupShadows(Scenery scenery, int size, Arena arena, bool hq = false)
        {
            if (scenery == null || scenery.Sun == null) return null;
            var sun = scenery.Sun;
            if (size <= 0)
            {
                sun.shadows = LightShadows.None;
                scenery.SetSunDirection(-scenery.SunDir);
                return null;
            }
            sun.shadows = LightShadows.Soft;   // PCFSoftShadowMap
            sun.shadowStrength = 1f;
            // the filter quality is a per-light shader value (no extra variant): High = 7x7 tent
            var ad = sun.GetUniversalAdditionalLightData();
            if (ad != null) ad.softShadowQuality = hq ? SoftShadowQuality.High : SoftShadowQuality.UsePipelineSettings;
            // the floor and everything in the water receive
            if (scenery.root != null)
                foreach (var r in scenery.root.GetComponentsInChildren<MeshRenderer>(true))
                    if (!Mats.IsTransparent(r.sharedMaterial)) r.receiveShadows = true;
            // sun.position.set(9, 45, 14); sun.target.position.set(0, -arena.depth, 0)
            var from = new Vector3(9, 45, 14);
            var to = new Vector3(0, (float)-arena.depth, 0);
            scenery.SetSunDirection((to - from).normalized);
            return sun;
        }

        /// Ultra: a live reflection probe of the arena (null when the level has none)
        public static LiveReflection MakeLiveReflection(ScenarioLook look, Arena arena, bool pool) => LiveReflection.Create(look, arena, pool);

        /// castShadows(obj, cast, receive): opaque meshes cast, everything receives
        public static void CastShadows(GameObject obj, bool cast = true, bool receive = true)
        {
            if (obj == null) return;
            foreach (var r in obj.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = cast && !Mats.IsTransparent(r.sharedMaterial) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = receive;
            }
        }
    }

    /// Ultra's real-time reflections: a ReflectionProbe at the arena centre (Unity world), box-projected
    /// onto the arena (floor, pool walls, surface) and re-rendered all the time, one cube face per frame
    /// (time slicing), so the vehicles' metal and acrylic parts mirror the scene as it is. It takes over
    /// from the gradient environment (importance 10) only after its first complete render, so nothing
    /// ever reflects an empty cube map. Tick() once per frame (SceneExtras.Tick from Views.DrawView).
    public sealed class LiveReflection
    {
        public const int Resolution = 256;
        const int WaitImportance = 0, ReadyImportance = 20;

        public GameObject go;
        public ReflectionProbe probe;
        int renderId = -1, lastFrame = -1;
        bool ready;

        /// the probe has finished its first render and is in use
        public bool Ready => ready;

        LiveReflection() { }

        /// pool: the box stops at the pool walls (0.4 m above the water); open water: it reaches 30 m
        /// past the arena (the fog hides the rest), floor to surface
        public static LiveReflection Create(ScenarioLook look, Arena arena, bool pool)
        {
            arena = arena ?? Course.ARENA;
            float W = (float)arena.width, L = (float)arena.length, D = (float)arena.depth;
            float top = pool ? 0.4f : 0f, margin = pool ? 0f : 30f;
            GraphicsQuality.UseRealtimeReflectionProbes();
            var go = new GameObject("Live reflections");
            go.transform.position = Space.ToUnity(0, -D / 2, 0);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.resolution = Resolution;
            p.hdr = true;
            p.size = new Vector3(W + 2 * margin, D + top, L + 2 * margin);
            p.center = new Vector3(0, top / 2, 0);      // box from the floor (-D) to 'top', probe at -D / 2
            p.boxProjection = true;                      // needs the URP asset's box projection (Set Up Project)
            p.blendDistance = 1f;
            p.importance = WaitImportance;
            p.intensity = 1f;
            // vehicles passing right next to the capture point would fill the whole cube: clip them
            p.nearClipPlane = 1.5f;
            p.farClipPlane = 200f;
            p.shadowDistance = 40f;
            p.clearFlags = ReflectionProbeClearFlags.SolidColor;
            p.backgroundColor = Colors.Rgb(look != null ? look.fog : 0x3aa6c9);
            p.cullingMask = ~(1 << BotThumbs.Layer);
            return new LiveReflection { go = go, probe = p };
        }

        /// once per frame: start the next render when the last one is done
        public void Tick()
        {
            if (probe == null) return;
            int f = Time.frameCount;
            if (f == lastFrame) return;
            lastFrame = f;
            if (renderId >= 0 && !probe.IsFinishedRendering(renderId)) return;
            if (renderId >= 0 && !ready)
            {
                ready = true;
                probe.importance = ReadyImportance;
            }
            renderId = probe.RenderProbe();
        }

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
            go = null; probe = null;
        }
    }
}
