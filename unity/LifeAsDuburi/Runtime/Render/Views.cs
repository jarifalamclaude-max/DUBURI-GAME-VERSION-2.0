// The rendering building blocks of src/main.js for the App: sceneExtras (environment, sun shadows,
// bubbles, water colour, turbidity), the soft contact shadow under each vehicle, the view cameras
// (three.js PerspectiveCamera with a pixel viewport rect, top-left origin like the JS), drawView's
// per-camera steps, and the pose of the payload items (syncMeshes).

using Duburi.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Env = Duburi.Core.Environment;

namespace Duburi.Game
{
    /// main.js sceneExtras(scene, look, arena): reflections for metal and acrylic parts, sun shadows,
    /// bubbles, the look's water colour and turbidity. Follows GraphicsQuality changes (shadows) by itself.
    public sealed class SceneExtras
    {
        /// scene.environmentIntensity
        public const float EnvironmentIntensity = 0.55f;
        /// size of the environment's reflection probe box (m, centred on the arena)
        public const float EnvironmentSize = 1000f;

        public EnvLight Env { get; private set; }
        /// the shadow-casting sun (null with shadows off)
        public Light Sun { get; private set; }
        public Bubbles Bubbles { get; private set; }
        public ScenarioLook Look { get; private set; }
        /// new THREE.Color(look.water): linear
        public Color WaterColor { get; private set; }
        /// clamp((look.fogDensity - 0.02) / 0.05, 0, 1)
        public float Turbidity { get; private set; }

        readonly Scenery scenery;
        readonly Arena arena;

        SceneExtras(Scenery scenery, Arena arena)
        {
            this.scenery = scenery;
            this.arena = arena;
        }

        public static SceneExtras Create(Scenery scenery, ScenarioLook look, Arena arena)
        {
            arena = arena ?? Course.ARENA;
            var e = new SceneExtras(scenery, arena);
            e.Env = Graphics3D.MakeEnvironment(look, EnvironmentIntensity, Space.ToUnity(0, (float)-arena.depth / 2, 0), EnvironmentSize);
            Ambient.SetEnvironment(e.Env);
            var q = GraphicsQuality.Spec;
            e.Sun = Graphics3D.SetupShadows(scenery, q.shadows, arena);
            e.Bubbles = new Bubbles(q.bubbles);
            e.Look = look;
            e.WaterColor = Colors.Rgb(look.water).linear;
            e.Turbidity = Mathf.Clamp01(((float)look.fogDensity - 0.02f) / 0.05f);
            GraphicsQuality.Changed += e.OnQuality;
            return e;
        }

        void OnQuality(string id) => ApplyQuality();

        /// the pause menu's quality change: shadows follow the new level (the bubble pool keeps its size)
        public void ApplyQuality()
        {
            Sun = Graphics3D.SetupShadows(scenery, GraphicsQuality.Spec.shadows, arena);
        }

        public void Dispose()
        {
            GraphicsQuality.Changed -= OnQuality;
            if (Ambient.Environment == Env) Ambient.SetEnvironment(null);
            Env?.Dispose();
            Bubbles?.Dispose();
            Env = null; Bubbles = null; Sun = null;
        }
    }

    /// The soft contact shadow (ambient occlusion) under a vehicle: a black disc on the floor whose
    /// opacity falls with the height (main.js start / syncMeshes).
    public sealed class ContactShadow
    {
        readonly GameObject go;
        readonly Material mat;
        readonly Mesh mesh;
        float opacity = -1;

        /// under the mirrored World root
        public ContactShadow(Transform world, BotDesign design)
        {
            var d = design.dims;
            float r = (float)System.Math.Max(d.x, System.Math.Max(d.y, d.z)) * 0.6f;
            mesh = Geo.Circle(r, 24).ToMesh("contact shadow");
            mat = Mats.Create(new Mat3 { kind = MatKind.Basic, color = Color.black, transparent = true, opacity = 0.25f, depthWrite = false, name = "contact shadow" });
            go = new GameObject("contact shadow");
            go.transform.SetParent(world, false);
            go.transform.localRotation = Space.Euler(-Mathf.PI / 2, 0, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public GameObject GameObject => go;

        public bool Visible
        {
            get => go.activeSelf;
            set { if (go.activeSelf != value) go.SetActive(value); }
        }

        /// the vehicle's (interpolated) NED position and the course floor depth (m)
        public void Update(double x, double y, double z, double floor)
        {
            float fl = (float)-floor + 0.012f;
            go.transform.localPosition = new Vector3((float)y, fl, (float)-x);
            float hgt = (float)(floor - z);
            float o = Mathf.Max(0, 0.3f - hgt * 0.05f);
            if (o != opacity)
            {
                opacity = o;
                Mats.SetBaseColorLinear(mat, new Color(0, 0, 0, o));
            }
        }

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
            if (mat != null) Object.Destroy(mat);
            if (mesh != null) Object.Destroy(mesh);
        }
    }

    /// A view camera: three.js PerspectiveCamera(fov, aspect, near, far) in Unity world space, with the
    /// view's pixel rect (top-left origin, like the JS view rects). Pose in three.js coordinates.
    public sealed class ViewCamera
    {
        public readonly Camera camera;
        public readonly UniversalAdditionalCameraData data;
        /// view index 0..3: its post volume layer (Post.FirstLayer + view) and draw order
        public readonly int view;
        Vector3 pos3;
        Quaternion rot3 = Quaternion.identity;
        Rect rect;

        /// new THREE.PerspectiveCamera(fov, 1, near, far) for view 'view'
        public ViewCamera(string name, float fov, float near, float far, int view = 0)
        {
            this.view = Mathf.Clamp(view, 0, Post.MaxViews - 1);
            var go = new GameObject(name);
            camera = go.AddComponent<Camera>();
            camera.fieldOfView = fov;
            camera.nearClipPlane = near;
            camera.farClipPlane = far;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.depth = this.view;
            camera.cullingMask = ~(1 << BotThumbs.Layer);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            data = camera.GetUniversalAdditionalCameraData();
            data.renderShadows = true;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = 1 << (Post.FirstLayer + this.view);
            camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
        }

        /// cam.fov (vertical, degrees)
        public float Fov
        {
            get => camera.fieldOfView;
            set { if (camera.fieldOfView != value) camera.fieldOfView = value; }
        }

        public bool Enabled
        {
            get => camera.enabled;
            set { if (camera.enabled != value) camera.enabled = value; }
        }

        /// the view rect (screen pixels, top-left origin)
        public Rect Rect => rect;

        /// v.rect = rect; cam.aspect = w / h: screen pixels with a top-left origin (screenHeight 0 =
        /// Screen.height)
        public void SetRect(float x, float y, float w, float h, float screenHeight = 0)
        {
            rect = new Rect(x, y, w, h);
            float H = screenHeight > 0 ? screenHeight : Screen.height;
            camera.pixelRect = new Rect(x, H - y - h, w, h);
            camera.ResetAspect();
        }

        /// cam.position (three.js)
        public Vector3 Position
        {
            get => pos3;
            set { pos3 = value; camera.transform.position = Space.ToUnity(value); }
        }

        /// cam.quaternion (three.js)
        public Quaternion Quaternion
        {
            get => rot3;
            set { rot3 = value; camera.transform.rotation = Space.RotToUnity(value); }
        }

        /// position + quaternion in three.js space
        public void SetPose(Vector3 threePos, Quaternion threeQuat)
        {
            Position = threePos;
            Quaternion = threeQuat;
        }

        /// cam.up.set(0, 1, 0); cam.lookAt(target) — three.js space
        public void LookAt(Vector3 target) => LookAt(target, Vector3.up);

        public void LookAt(Vector3 target, Vector3 up) => Quaternion = Space.LookRotationThree(pos3, target, up);

        public void Dispose()
        {
            RenderHooks.Forget(camera);
            if (camera != null) Object.Destroy(camera.gameObject);
        }
    }

    public static class Views
    {
        /// drawView(cam, rect, time): the per-camera steps before Unity renders the view —
        /// scenery.applyForCamera (fog, background, rays, water look), the bubbles' point size and draw
        /// order, and post.render's grade (under water: depth below the surface at the camera). env is
        /// the game's environment (null in the menu: surface at 0). Call it every frame for every view.
        public static void DrawView(ViewCamera cam, Scenery scenery, SceneExtras extras, Post post, Env env)
        {
            if (cam == null) return;
            scenery?.ApplyForCamera(cam.camera);
            float h = cam.camera.pixelHeight * GraphicsQuality.RenderScale;
            if (extras != null && extras.Bubbles != null)
            {
                extras.Bubbles.SetViewportHeight(h);
                extras.Bubbles.ApplyForCamera(cam.camera, h);
            }
            if (post != null)
            {
                Vector3 p = cam.Position;
                float surf = env != null ? (float)env.SurfaceElevation(-p.z, p.x) : 0f;
                post.Apply(cam.camera, cam.view, p.y < surf, Mathf.Max(0, surf - p.y),
                    extras != null ? extras.WaterColor : Colors.Rgb(0x2f9fc4).linear, extras != null ? extras.Turbidity : 0f);
            }
        }

        /// renderView: 'own' (a vehicle) is not drawn by this camera this frame (null: draw everything).
        /// The JS hides your own vehicle around the onboard views' render; Unity renders the cameras
        /// later, so the hiding is per camera (RenderHooks).
        public static void HideFor(ViewCamera cam, BotMesh own)
        {
            if (cam == null) return;
            RenderHooks.State(cam.camera).hide = own != null && own.group != null ? own.Renderers : null;
        }

        /// main.js nedQuat(q, out): NED body->world quaternion -> three.js
        public static Quaternion NedQuat(Quat q) => Space.NedQuat(q);

        /// syncMeshes, payload items: an item mesh (Bots3D.CreateItemMesh, a direct child of the World
        /// root) follows its item — position, a falling torpedo points along its velocity, a held item
        /// takes the holder's attitude.
        public static void PlaceItem(GameObject m, Item it)
        {
            if (m == null || it == null) return;
            var t = m.transform;
            Vector3 p = Space.Ned(it.pos);
            t.localPosition = p;
            if (it.kind == "torpedo" && it.state == "fall")
            {
                double sp = System.Math.Sqrt(it.vel.x * it.vel.x + it.vel.y * it.vel.y + it.vel.z * it.vel.z);
                if (sp > 0.05)
                {
                    var target = p - new Vector3((float)it.vel.y, (float)-it.vel.z, (float)-it.vel.x);
                    t.localRotation = Space.LookAt(p, target);
                }
            }
            else if (it.state == "held" && it.holder != null)
            {
                t.localRotation = Space.NedQuat(it.holder.veh.q);
            }
        }
    }
}
