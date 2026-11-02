// src/render/graphics.js: image-quality settings shared by the game — a reflection environment for the
// metal / acrylic parts (a sky-over-water gradient per scenario) and sun shadows.
//
// three.js lights a MeshStandardMaterial with  hemisphere / ambient irradiance * albedo / PI  plus the
// PMREM environment * envMapIntensity; Unity has one ambient (RenderSettings, trilight) and reflection
// probes. Ambient sums the scenery's hemisphere light and the environment's diffuse part; the
// environment's specular part is a custom-cubemap ReflectionProbe. Light intensities are three's / PI
// (Unity's light units include the PI that three's BRDF_Lambert divides by).

using Duburi.Core;
using UnityEngine;
using UnityEngine.Rendering;

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
            // three HemisphereLight: irradiance = mix(ground, sky, 0.5 n.y + 0.5) * intensity, diffuse = irradiance / PI
            float k = hemiI / Mathf.PI;
            Color sky = hemiSky * k, ground = hemiGround * k, eq = (hemiSky + hemiGround) * 0.5f * k;
            if (env != null)
            {
                sky += env.up * env.intensity;
                eq += env.side * env.intensity;
                ground += env.down * env.intensity;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky.gamma;
            RenderSettings.ambientEquatorColor = eq.gamma;
            RenderSettings.ambientGroundColor = ground.gamma;
            RenderSettings.ambientIntensity = 1f;
        }
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
        public static Light SetupShadows(Scenery scenery, int size, Arena arena)
        {
            if (scenery == null || scenery.Sun == null) return null;
            var sun = scenery.Sun;
            if (size <= 0)
            {
                sun.shadows = LightShadows.None;
                scenery.SetSunDirection(-scenery.SunDir);
                return null;
            }
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            // sun.position.set(9, 45, 14); sun.target.position.set(0, -arena.depth, 0)
            var from = new Vector3(9, 45, 14);
            var to = new Vector3(0, (float)-arena.depth, 0);
            scenery.SetSunDirection((to - from).normalized);
            return sun;
        }

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
}
