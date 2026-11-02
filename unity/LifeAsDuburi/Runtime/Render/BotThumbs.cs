// main.js thumb / renderThumb: a 240x150 picture of a vehicle in a player colour for the menus,
// rendered once per bot + colour and cached. Its own little scene: the bot on layer 29 far below the
// world (y = -5000, outside every scene probe), a camera that sees only layer 29, a directional light
// that lights only layer 29, the hemisphere ambient and the pool environment of the JS thumbnail scene.
// While it renders, the sceneries' suns are switched off and RenderSettings ambient / fog are the
// thumbnail's; everything is restored right after. The background stays transparent (alpha 0): the
// camera renders in LDR without post-processing (URP's HDR target and post pass would drop alpha),
// so the JS thumbnail renderer's ACES tone mapping is not applied.

using System.Collections.Generic;
using Duburi.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Duburi.Game
{
    public static class BotThumbs
    {
        public const int Layer = 29;
        public const int Width = 240, Height = 150;
        static readonly Vector3 Origin = new Vector3(0, -5000, 0);

        static readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
        static readonly List<Light> suns = new List<Light>();
        static GameObject root;
        static Transform world;
        static Camera cam;
        static Light light;
        static EnvLight env;

        /// thumb(bot, color): the cached thumbnail (rendered on the first call, again if the GPU lost it)
        public static Texture Get(string botId, Color color)
        {
            string key = botId + "|" + Colors.ToHex(color);
            if (cache.TryGetValue(key, out var rt) && rt != null && rt.IsCreated()) return rt;
            if (rt == null)
            {
                var desc = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24)
                {
                    sRGB = true,
                    msaaSamples = 4, // antialias: true
                    useMipMap = false,
                };
                rt = new RenderTexture(desc) { name = "thumb " + key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                cache[key] = rt;
            }
            if (!rt.IsCreated()) rt.Create();
            Render(botId, color, rt);
            return rt;
        }

        static void Setup()
        {
            if (root != null) return;
            root = new GameObject("bot thumbnails") { layer = Layer };
            root.transform.position = Origin;
            world = Space.CreateWorld("thumb world", root.transform);
            world.gameObject.layer = Layer;

            // new THREE.PerspectiveCamera(32, 240 / 150, 0.01, 50)
            var cgo = new GameObject("thumb camera") { layer = Layer };
            cgo.transform.SetParent(root.transform, false);
            cam = cgo.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); // alpha: true
            cam.fieldOfView = 32;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50;
            cam.aspect = (float)Width / Height;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.useOcclusionCulling = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.volumeLayerMask = 0;

            // new THREE.DirectionalLight(0xffffff, 2.2), position (2, 3, 2), aimed at the origin
            var lgo = new GameObject("thumb light") { layer = Layer };
            lgo.transform.SetParent(root.transform, false);
            light = lgo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 2.2f / Mathf.PI;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
            light.enabled = false;
            lgo.transform.rotation = Quaternion.LookRotation(Space.DirToUnity(-new Vector3(2, 3, 2).normalized), Vector3.up);

            // thumbScene.environment = makeEnvironment(pool look), environmentIntensity 0.6
            env = Graphics3D.MakeEnvironment(Scenarios.Get("pool").look, 0.6f, Origin, 20f);
            env.go.transform.SetParent(root.transform, true);
        }

        static void Render(string botId, Color color, RenderTexture rt)
        {
            var design = Bots.Get(botId, Bots.BOTS["duburi"]);
            Setup();
            var bm = Bots3D.CreateBotMesh(design, color, Art.Logo, world, Layer);
            var d = design.dims;
            float L = (float)System.Math.Max(d.x, System.Math.Max(d.y, d.z));
            var eye = new Vector3(L * 1.7f, L * 0.9f, L * 1.5f);
            // thumbCam.position.set(...); thumbCam.lookAt(0, 0, 0) — three.js space under the thumb root
            cam.transform.position = Origin + Space.ToUnity(eye);
            cam.transform.rotation = Space.RotToUnity(Space.LookRotationThree(eye, Vector3.zero, Vector3.up));
            cam.targetTexture = rt;

            // the thumbnail scene's lighting, no fog
            var mode = RenderSettings.ambientMode;
            Color aSky = RenderSettings.ambientSkyColor, aEq = RenderSettings.ambientEquatorColor, aGround = RenderSettings.ambientGroundColor;
            float aI = RenderSettings.ambientIntensity;
            bool fog = RenderSettings.fog;
            suns.Clear();
            foreach (var s in Scenery.Live)
                if (s.Sun != null && s.Sun.enabled) { suns.Add(s.Sun); s.Sun.enabled = false; }
            // new THREE.HemisphereLight(0xdff4ff, 0x203040, 1.6) + the environment's diffuse part
            Ambient.Compute(Colors.Rgb(0xdff4ff).linear, Colors.Rgb(0x203040).linear, 1.6f, env, out var sky, out var eq, out var ground);
            Ambient.Set(sky, eq, ground);
            RenderSettings.fog = false;
            light.enabled = true;
            try
            {
                cam.Render();
            }
            finally
            {
                light.enabled = false;
                foreach (var s in suns) if (s != null) s.enabled = true;
                suns.Clear();
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientSkyColor = aSky;
                RenderSettings.ambientEquatorColor = aEq;
                RenderSettings.ambientGroundColor = aGround;
                RenderSettings.ambientIntensity = aI;
                RenderSettings.fog = fog;
                cam.targetTexture = null;
                if (bm.group != null) bm.group.SetActive(false);
                bm.Dispose();
            }
        }

        /// releases every cached thumbnail and the thumbnail scene
        public static void Clear()
        {
            foreach (var rt in cache.Values) if (rt != null) { rt.Release(); Object.Destroy(rt); }
            cache.Clear();
            env?.Dispose();
            env = null;
            if (root != null) Object.Destroy(root);
            root = null; world = null; cam = null; light = null;
        }

        // Enter Play Mode without a domain reload keeps statics: start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache.Clear();
            suns.Clear();
            root = null; world = null; cam = null; light = null; env = null;
        }
    }
}
