// The only place that touches shaders. Materials are clones of the template materials that
// Life as Duburi -> Set Up Project puts in Resources/Duburi/Materials (so the shader variants they need
// are in the build); clones only change keyword-free properties: _BaseColor, _BaseMap (+ _MainTex),
// _EmissionColor (emissive template), _Smoothness, _Metallic, _Cutoff, _Cull and the render queue.
//
// Mat3 describes a three.js material (MeshStandardMaterial / MeshPhysicalMaterial / MeshBasicMaterial /
// LineBasicMaterial / point sprites) with the JS parameters; Mats.Get maps it onto a template:
//   color            -> _BaseColor (three keeps colours linear; Unity's SetColor takes sRGB: converted)
//   roughness        -> _Smoothness = 1 - roughness        metalness -> _Metallic
//   opacity / transparent -> _BaseColor.a + the transparent template      alphaTest -> LitCutout, _Cutoff
//   emissive * emissiveIntensity -> LitEmissive, _EmissionColor            map -> _BaseMap
//   side             -> _Cull for unlit materials; lit two-sided / back-side geometry is built with
//                       explicit back faces instead (Geo.WithBackFaces / Flipped), so lighting is right
// Identical parameters share one material (the JS shares materials the same way).

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Duburi.Game
{
    public enum Side { Front, Back, Double }
    public enum MatKind { Standard, Basic, Line, Points }

    /// three.js material parameters
    public sealed class Mat3
    {
        public MatKind kind = MatKind.Standard;
        public Color color = Color.white;          // linear (three Color)
        public float roughness = 1f, metalness = 0f;
        public Color emissive = Color.black;       // linear
        public float emissiveIntensity = 1f;
        public Texture map;
        public bool transparent;
        public float opacity = 1f;
        public float alphaTest;
        public Side side = Side.Front;
        public bool depthWrite = true;
        public bool vertexColors;                  // per-vertex / per-instance colours (palette texture)
        public bool flatShading;                   // geometry is given face normals
        public bool additive;                      // THREE.AdditiveBlending
        public string name;

        public Mat3 Clone() => (Mat3)MemberwiseClone();

        // ---- constructors in the JS style

        /// new MeshStandardMaterial({ color, roughness, metalness })
        public static Mat3 Std(int hex, float roughness, float metalness = 0f) =>
            new Mat3 { color = Colors.Rgb(hex).linear, roughness = roughness, metalness = metalness };
        public static Mat3 Std(Color linear, float roughness, float metalness = 0f) =>
            new Mat3 { color = linear, roughness = roughness, metalness = metalness };

        /// new MeshBasicMaterial({ color })
        public static Mat3 Basic(int hex) => new Mat3 { kind = MatKind.Basic, color = Colors.Rgb(hex).linear };
        public static Mat3 Basic(Color linear) => new Mat3 { kind = MatKind.Basic, color = linear };

        public Mat3 Emissive(int hex, float intensity = 1f) { emissive = Colors.Rgb(hex).linear; emissiveIntensity = intensity; return this; }
        public Mat3 Transparent(float op, bool depthW = true) { transparent = true; opacity = op; depthWrite = depthW; return this; }
        public Mat3 Double() { side = Side.Double; return this; }
        public Mat3 Map(Texture t) { map = t; return this; }

        public bool IsTransparent => transparent || additive || kind == MatKind.Points;
        public bool IsLit => kind == MatKind.Standard;

        internal string Key(int queueOffset)
        {
            var sb = new StringBuilder(96);
            sb.Append((int)kind).Append('|').Append(Hex(color)).Append('|').Append(roughness.ToString("R")).Append('|').Append(metalness.ToString("R"))
              .Append('|').Append(Hex(emissive)).Append('|').Append(emissiveIntensity.ToString("R"))
              .Append('|').Append(map != null ? map.GetInstanceID() : 0).Append('|').Append(transparent).Append('|').Append(opacity.ToString("R"))
              .Append('|').Append(alphaTest.ToString("R")).Append('|').Append((int)side).Append('|').Append(depthWrite).Append('|').Append(additive)
              .Append('|').Append(queueOffset);
            return sb.ToString();
        }

        static string Hex(Color c) => c.r.ToString("R") + "," + c.g.ToString("R") + "," + c.b.ToString("R") + "," + c.a.ToString("R");
    }

    public static class Mats
    {
        public const string Dir = "Duburi/Materials/";
        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";
        public const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";

        static readonly Dictionary<string, Material> templates = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static readonly HashSet<Material> shared = new HashSet<Material>();

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly int Cull = Shader.PropertyToID("_Cull");

        /// A template by name: Lit, LitTransparent, LitEmissive, LitCutout, Unlit, UnlitTransparent,
        /// UnlitAdditive, Particle, ParticleAdditive.
        public static Material Template(string name)
        {
            if (templates.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Material>(Dir + name);
            if (t == null) t = Fallback(name);
            templates[name] = t;
            return t;
        }

        /// The shared material for these parameters (identical parameters -> the same material).
        public static Material Get(Mat3 m, int queueOffset = 0)
        {
            string key = m.Key(queueOffset);
            if (cache.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = Build(m, queueOffset);
            cache[key] = mat;
            shared.Add(mat);
            return mat;
        }

        /// A material of its own (changed per frame / per camera, or holding a texture of one object).
        public static Material Create(Mat3 m, int queueOffset = 0) => Build(m, queueOffset);

        /// Materials from Get() belong to the cache and are never destroyed with an object.
        public static bool IsShared(Material m) => m != null && shared.Contains(m);

        public static bool IsTransparent(Material m) => m != null && m.renderQueue >= (int)RenderQueue.AlphaTest + 50;

        static string TemplateName(Mat3 m)
        {
            switch (m.kind)
            {
                case MatKind.Points: return m.additive ? "ParticleAdditive" : "Particle";
                case MatKind.Basic:
                case MatKind.Line:
                    return m.additive ? "UnlitAdditive" : m.transparent ? "UnlitTransparent" : "Unlit";
                default:
                    if (m.additive) return "UnlitAdditive";
                    if (m.transparent) return "LitTransparent";
                    if (m.alphaTest > 0) return "LitCutout";
                    if (m.emissive.maxColorComponent * m.emissiveIntensity > 0) return "LitEmissive";
                    return "Lit";
            }
        }

        static Material Build(Mat3 m, int queueOffset)
        {
            var template = Template(TemplateName(m));
            var mat = new Material(template) { name = m.name ?? template.name };
            Color c = m.color;
            c.a = m.IsTransparent ? m.opacity : 1f;
            SetBaseColorLinear(mat, c);
            if (m.map != null) SetMap(mat, m.map);
            if (m.kind == MatKind.Standard && !m.additive)
            {
                if (mat.HasProperty(Smoothness)) mat.SetFloat(Smoothness, 1f - m.roughness);
                if (mat.HasProperty(Metallic)) mat.SetFloat(Metallic, m.metalness);
                if (m.alphaTest > 0 && mat.HasProperty(Cutoff)) mat.SetFloat(Cutoff, m.alphaTest);
                if (mat.HasProperty(EmissionColor))
                {
                    Color e = m.emissive * m.emissiveIntensity;
                    mat.SetColor(EmissionColor, e.maxColorComponent > 0 ? e.gamma : Color.black);
                }
            }
            // unlit / particle materials are drawn from both sides by the shader; lit ones get back faces in the mesh
            if (!m.IsLit || m.additive)
            {
                float cull = m.side == Side.Double ? 0f : m.side == Side.Back ? 1f : 2f;
                if (mat.HasProperty(Cull)) mat.SetFloat(Cull, cull);
            }
            if (queueOffset != 0) mat.renderQueue = template.renderQueue + queueOffset;
            return mat;
        }

        /// _BaseColor from a linear colour (alpha as is).
        public static void SetBaseColorLinear(Material mat, Color linear)
        {
            Color g = linear.gamma;
            g.a = linear.a;
            mat.SetColor(BaseColor, g);
        }

        public static void SetMap(Material mat, Texture tex)
        {
            if (mat.HasProperty(BaseMap)) mat.SetTexture(BaseMap, tex);
            if (mat.HasProperty(MainTex)) mat.SetTexture(MainTex, tex);
        }

        public static void SetMapTiling(Material mat, Vector2 scale, Vector2 offset)
        {
            if (mat.HasProperty(BaseMap)) { mat.SetTextureScale(BaseMap, scale); mat.SetTextureOffset(BaseMap, offset); }
            if (mat.HasProperty(MainTex)) { mat.SetTextureScale(MainTex, scale); mat.SetTextureOffset(MainTex, offset); }
        }

        public static void SetQueue(Material mat, int queue) { if (mat.renderQueue != queue) mat.renderQueue = queue; }

        public static int BaseQueue(Material mat) => mat.shader != null ? mat.shader.renderQueue : mat.renderQueue;

        // ---------------------------------------------------------------- fallback (Editor without Set Up Project)

        static Material Fallback(string name)
        {
            Debug.LogWarning("Life as Duburi: template material Resources/" + Dir + name +
                             " is missing — run Life as Duburi > Set Up Project. Using Shader.Find (works in the Editor only).");
            string shaderName = name.StartsWith("Particle") ? ParticleShader : name.StartsWith("Unlit") ? UnlitShader : LitShader;
            var sh = Shader.Find(shaderName);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null)
            {
                // a build without the templates and without URP shaders: draw with the UI material rather than throw
                Debug.LogError("Life as Duburi: no URP shader in this build — run Life as Duburi > Set Up Project and build again.");
                return new Material(Canvas.GetDefaultCanvasMaterial()) { name = name };
            }
            var m = new Material(sh) { name = name };
            bool transparent = name.Contains("Transparent") || name.Contains("Additive") || name.StartsWith("Particle");
            bool additive = name.Contains("Additive");
            if (transparent)
            {
                SetF(m, "_Surface", 1f);
                SetF(m, "_Blend", additive ? 2f : 0f);
                SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetF(m, "_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_SrcBlendAlpha", (float)BlendMode.One);
                SetF(m, "_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (name == "LitCutout")
            {
                SetF(m, "_AlphaClip", 1f);
                SetF(m, "_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            if (name == "LitEmissive")
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, Color.white);
            }
            return m;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    }

    /// Per-vertex / per-instance colours on materials that have no vertex-colour input (URP Lit):
    /// every distinct colour becomes one texel of a small palette texture, and the mesh UVs point at it.
    public static class Palette
    {
        public static Texture2D Build(List<Color> linearColors, out List<Vector2> uvs)
        {
            var index = new Dictionary<Color32, int>();
            var colors = new List<Color32>();
            var vIdx = new int[linearColors.Count];
            for (int i = 0; i < linearColors.Count; i++)
            {
                Color32 c = linearColors[i].gamma;
                c.a = 255;
                if (!index.TryGetValue(c, out int k)) { k = colors.Count; index[c] = k; colors.Add(c); }
                vIdx[i] = k;
            }
            int n = Mathf.Max(1, colors.Count);
            int w = Mathf.Min(n, 1024), h = (n + w - 1) / w;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = "palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[w * h];
            for (int i = 0; i < colors.Count; i++) px[i] = colors[i];
            tex.SetPixels32(px);
            tex.Apply(false, false);
            uvs = new List<Vector2>(linearColors.Count);
            for (int i = 0; i < vIdx.Length; i++)
            {
                int k = vIdx[i];
                uvs.Add(new Vector2((k % w + 0.5f) / w, (k / w + 0.5f) / h));
            }
            return tex;
        }
    }

    /// Unity objects created for one scene object, destroyed together (the JS dispose()).
    public sealed class Owned
    {
        readonly List<Object> list = new List<Object>();
        readonly HashSet<Object> set = new HashSet<Object>();
        public T Add<T>(T o) where T : Object { if (o != null && set.Add(o)) list.Add(o); return o; }
        public void DestroyAll()
        {
            foreach (var o in list) if (o != null) Object.Destroy(o);
            list.Clear();
            set.Clear();
        }
    }
}
