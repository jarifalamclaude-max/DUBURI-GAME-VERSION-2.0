// UI kit for the uGUI port of src/ui/style.css: the CSS colours, the two font
// families, runtime-generated sprites (rounded panels with 9-slice borders,
// circles, shadows, gradients, the symbol icons the fonts do not have) and
// small builders for the elements every screen uses (boxes, text, rich text
// with inline keys / buttons, buttons with hover / focus skins).
// Units: one canvas unit = one CSS pixel (see UiRoot for the scaling).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Duburi.Game
{
    /// colours of style.css (:root and the literal colours of the rules)
    public static class Css
    {
        public static Color Rgba(int r, int g, int b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);
        public static Color White(float a) => new Color(1, 1, 1, a);

        public static readonly Color bg = Colors.Hex("#04131c");
        public static readonly Color panel = Rgba(6, 24, 36, 0.92f);
        public static readonly Color line = Rgba(160, 220, 255, 0.18f);
        public static readonly Color text = Colors.Hex("#e3f1f7");
        public static readonly Color mut = Colors.Hex("#8fb3c4");
        public static readonly Color acc = Colors.Hex("#ffb547");
        public static readonly Color accHover = Colors.Hex("#ffc66e");
        public static readonly Color blue = Colors.Hex("#3aa0e0");
        public static readonly Color good = Colors.Hex("#59d98e");
        public static readonly Color bad = Colors.Hex("#ff5d5d");
        public static readonly Color warm = Colors.Hex("#ffd38c");  // .mi.warn, .rules em, .chosen, .toast.warn
        public static readonly Color dark = Colors.Hex("#1c1200");  // text on the amber primary button
        public static readonly Color hud = Rgba(3, 16, 24, 0.66f);  // HUD boxes
        public static readonly Color field = Colors.Hex("#0b2738"); // select / input
        public static readonly Color pale = Colors.Hex("#cfe6f0");  // .title-screen .tag / .press
        public static readonly Color blurb = Colors.Hex("#c7dde7");

        public static string Hex(Color c) => Colors.ToHex(c);
    }

    public enum UiFont { Regular, Medium, SemiBold, Bold, Mono, MonoMedium }

    public static partial class Ui
    {
        public const int Layer = 5; // "UI"
        const float K = 2f;         // sprite pixels per canvas unit

        // ---------------------------------------------------------------- fonts
        static readonly Font[] fonts = new Font[6];
        static readonly string[] fontFiles = { "SairaSemiCondensed-Regular", "SairaSemiCondensed-Medium", "SairaSemiCondensed-SemiBold", "SairaSemiCondensed-Bold", "IBMPlexMono-Regular", "IBMPlexMono-Medium" };

        public static Font GetFont(UiFont f)
        {
            int i = (int)f;
            if (fonts[i] == null)
            {
                fonts[i] = Resources.Load<Font>("Duburi/Fonts/" + fontFiles[i]);
                if (fonts[i] == null)
                {
                    Debug.LogWarning("Life as Duburi: font Resources/Duburi/Fonts/" + fontFiles[i] + " is missing; using LegacyRuntime.ttf");
                    fonts[i] = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            return fonts[i];
        }

        // ---------------------------------------------------------------- nodes
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = Layer };
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        /// place a rect by its top-left corner (y down from the parent's top-left), CSS style
        public static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// anchor + pivot at the same normalised point (0,0 = bottom-left) with an offset
        public static void Anchor(RectTransform rt, Vector2 at, Vector2 pos, Vector2? size = null)
        {
            rt.anchorMin = rt.anchorMax = at;
            rt.pivot = at;
            rt.anchoredPosition = pos;
            if (size.HasValue) rt.sizeDelta = size.Value;
        }

        /// the component, added if missing (no ?? on Unity objects: GetComponent gives a fake null in the Editor)
        public static T Get<T>(Component c) where T : Component => c.TryGetComponent(out T t) ? t : c.gameObject.AddComponent<T>();

        public static LayoutElement LE(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1, float minW = -1, float minH = -1)
        {
            var le = Get<LayoutElement>(c);
            le.preferredWidth = prefW; le.preferredHeight = prefH;
            le.flexibleWidth = flexW; le.flexibleHeight = flexH;
            le.minWidth = minW; le.minHeight = minH;
            return le;
        }

        public static void Ignore(Component c) { var le = Get<LayoutElement>(c); le.ignoreLayout = true; }

        public static VerticalLayoutGroup VBox(Component c, RectOffset pad = null, float gap = 0, TextAnchor align = TextAnchor.UpperLeft, bool stretch = true)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.padding = pad ?? new RectOffset();
            g.spacing = gap;
            g.childAlignment = align;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = stretch; g.childForceExpandHeight = false;
            g.childScaleWidth = false; g.childScaleHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HBox(Component c, RectOffset pad = null, float gap = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.padding = pad ?? new RectOffset();
            g.spacing = gap;
            g.childAlignment = align;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            return g;
        }

        public static ContentSizeFitter Fit(Component c, bool w = true, bool h = true)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = w ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.verticalFit = h ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return f;
        }

        public static RectOffset Pad(int t, int r, int b, int l) => new RectOffset(l, r, t, b);
        public static RectOffset Pad(int v, int h) => new RectOffset(h, h, v, v);
        public static RectOffset Pad(int a) => new RectOffset(a, a, a, a);

        public static CanvasGroup Group(Component c, float alpha = 1, bool blocks = true)
        {
            var g = Get<CanvasGroup>(c);
            g.alpha = alpha; g.blocksRaycasts = blocks; g.interactable = blocks;
            return g;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        // ---------------------------------------------------------------- graphics
        public static Image Img(Component c, Sprite sprite, Color color, bool raycast = false)
        {
            var im = Get<Image>(c);
            im.sprite = sprite;
            im.color = color;
            im.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            im.raycastTarget = raycast;
            return im;
        }

        /// child image filling its parent (ignored by layout groups)
        public static Image Backing(Transform parent, string name, Sprite sprite, Color color, float inset = 0)
        {
            var rt = Node(name, parent);
            Ignore(rt);
            Stretch(rt, inset, inset, inset, inset);
            rt.SetAsFirstSibling();
            return Img(rt, sprite, color);
        }

        public static RawImage Raw(Component c, Texture tex, Color? color = null)
        {
            var im = c.gameObject.AddComponent<RawImage>();
            im.texture = tex;
            im.color = color ?? Color.white;
            im.raycastTarget = false;
            return im;
        }

        /// a CSS box: background with border-radius and a 1px border (border colour clear = none)
        public static RectTransform Box(Transform parent, string name, Color bgc, float radius, Color borderColor, float border = 1, bool raycast = false)
        {
            var rt = Node(name, parent);
            Img(rt, Rounded(radius), bgc, raycast);
            if (borderColor.a > 0 && border > 0) Backing(rt, "border", Rounded(radius, border), borderColor);
            return rt;
        }

        /// soft drop shadow behind a box (box-shadow: 0 dy blur color)
        public static Image DropShadow(RectTransform box, float radius, float blur, float dy, Color c)
        {
            var rt = Node("shadow", box);
            Ignore(rt);
            float m = blur * 0.5f;
            Stretch(rt, -m, -m + dy, -m, -m - dy);
            rt.SetAsFirstSibling();
            var im = Img(rt, ShadowSprite(radius, m), c);
            return im;
        }

        // ---------------------------------------------------------------- text
        public static Text Label(Transform parent, string s, float size, Color color, UiFont font = UiFont.Regular, TextAnchor align = TextAnchor.UpperLeft, bool wrap = true, string name = "text")
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = GetFont(font);
            t.fontSize = Mathf.Max(1, Mathf.RoundToInt(size));
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = s ?? "";
            return t;
        }

        public static Shadow TextShadow(Graphic g, Color c, float dy, float dx = 0)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = c;
            s.effectDistance = new Vector2(dx, -dy);
            return s;
        }

        /// rich text colour tag for uGUI Text
        public static string Col(string s, Color c) => "<color=" + Css.Hex(c) + (c.a < 1 ? ((int)(c.a * 255)).ToString("x2") : "") + ">" + s + "</color>";
        public static string B(string s) => "<b>" + s + "</b>";

        /// a horizontal line under / through a text (text-decoration)
        public static Image Rule(Transform parent, Color c, float thickness, float yFrac)
        {
            var rt = Node("rule", parent);
            Ignore(rt);
            rt.anchorMin = new Vector2(0, yFrac); rt.anchorMax = new Vector2(1, yFrac);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0, thickness);
            rt.anchoredPosition = Vector2.zero;
            return Img(rt, null, c);
        }

        // ---------------------------------------------------------------- symbols the fonts lack
        // Saira / IBM Plex Mono have no ✕ ○ □ △ ● ▶ ◀ ▲ ▼ ⏸ ▮ ▯ 🏆: they are drawn as icons
        public static string IconKey(string s, int i, out int len)
        {
            len = 1;
            char c = s[i];
            switch (c)
            {
                case '✕': return "cross";
                case '○': return "ring";
                case '□': return "square";
                case '△': return "tri";
                case '●': return "disc";
                case '▶': return "right";
                case '◀': return "left";
                case '▲': return "up";
                case '▼': return "down";
                case '⏸': return "pause";
                case '▮': return "pipOn";
                case '▯': return "pipOff";
            }
            if (c == '\uD83C' && i + 1 < s.Length && s[i + 1] == '\uDFC6') { len = 2; return "trophy"; }
            return null;
        }

        public static bool HasIcons(string s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.Length; i++) if (IconKey(s, i, out _) != null) return true;
            return false;
        }

        /// an icon image sized for text of `size` px (keeps the glyph's place in a line)
        public static Image IconImage(Transform parent, string key, float size, Color color)
        {
            var rt = Node("icon-" + key, parent);
            bool pip = key == "pipOn" || key == "pipOff";
            float h = size * (pip ? 0.8f : key == "trophy" ? 1.05f : 0.72f);
            float w = pip ? size * 0.45f : h;
            var im = Img(rt, Icon(key), key == "trophy" ? Color.white : color);
            im.preserveAspect = !pip;
            LE(rt, w, h, 0, 0, w, h);
            rt.sizeDelta = new Vector2(w, h);
            return im;
        }

        // ---------------------------------------------------------------- sprites
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// rounded rectangle (stroke = 0: filled; band 1 / 2: only the top / left `bandW` units of it,
        /// for border-top / border-left in another colour). White, 9-sliced: tint with Image.color.
        public static Sprite Rounded(float radius, float stroke = 0, int band = 0, float bandW = 0)
        {
            if (radius <= 0 && stroke <= 0 && band == 0) return null; // plain rect: Image without sprite
            string key = "r" + radius + "/" + stroke + "/" + band + "/" + bandW;
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            float R = Mathf.Max(0.5f, radius * K);
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(R, stroke * K, bandW * K)));
            int size = 2 * n + 4;
            float half = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - R), qy = Mathf.Abs(y + 0.5f - half) - (half - R);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - R;
                    float cov = Mathf.Clamp01(0.5f - d);
                    if (stroke > 0) cov -= Mathf.Clamp01(0.5f - (d + stroke * K));
                    // y here counts from the bottom of the texture: the top band is the last rows
                    if (band == 1) cov *= Mathf.Clamp01(bandW * K - (size - 1 - y));
                    else if (band == 2) cov *= Mathf.Clamp01(bandW * K - x);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(cov) * 255));
                }
            sp = MakeSprite(px, size, size, new Vector4(n, n, n, n), key);
            sprites[key] = sp;
            return sp;
        }

        /// circle of diameter d (a rounded rect drawn at d x d)
        public static Sprite Circle(float d) => Rounded(d / 2);
        public static Sprite Ring(float d, float w) => Rounded(d / 2, w);

        /// blurred rounded rectangle for box-shadow (m = blur margin in units)
        public static Sprite ShadowSprite(float radius, float m)
        {
            string key = "s" + radius + "/" + m;
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            float R = Mathf.Max(0.5f, radius * K), M = Mathf.Max(1, m * K);
            int n = Mathf.CeilToInt(R + 2 * M);
            int size = 2 * n + 4;
            float half = size / 2f, inner = half - 2 * M; // shape half size inside the margin
            float sigma = M * 0.55f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (inner - R), qy = Mathf.Abs(y + 0.5f - half) - (inner - R);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - R + M;
                    float a = d <= 0 ? 1 : Mathf.Exp(-(d * d) / (2 * sigma * sigma));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                }
            sp = MakeSprite(px, size, size, new Vector4(n, n, n, n), key);
            sprites[key] = sp;
            return sp;
        }

        /// .bar i: a 6-unit pill with linear-gradient(90deg, colour, #fff8)
        public static Sprite BarSprite(Color c)
        {
            string key = "bar" + Css.Hex(c);
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            const int w = 64, h = 12, r = 6;
            var px = new Color32[w * h];
            var end = new Color(1, 1, 1, 0x88 / 255f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - w / 2f) - (w / 2f - r), qy = Mathf.Abs(y + 0.5f - h / 2f) - (h / 2f - r);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                    Color col = Color.Lerp(c, end, x / (float)(w - 1));
                    col.a *= Mathf.Clamp01(0.5f - d);
                    px[y * w + x] = col;
                }
            sp = MakeSprite(px, w, h, new Vector4(r, 0, r, 0), key);
            sprites[key] = sp;
            return sp;
        }

        static Sprite MakeSprite(Color32[] px, int w, int h, Vector4 border, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * K, 0, SpriteMeshType.FullRect, border);
        }

        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        /// texture from a colour function of (u, v) in 0..1, v = 0 at the TOP (CSS gradients)
        public static Texture2D Gradient(string key, int w, int h, Func<float, float, Color> f)
        {
            if (textures.TryGetValue(key, out var tex) && tex != null) return tex;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(h - 1 - y) * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            textures[key] = tex;
            return tex;
        }

        /// CSS linear-gradient stops (positions 0..1) evaluated at t
        public static Color Stops(float t, Color[] c, float[] p)
        {
            if (t <= p[0]) return c[0];
            for (int i = 1; i < p.Length; i++)
                if (t <= p[i]) return Color.Lerp(c[i - 1], c[i], (t - p[i - 1]) / Mathf.Max(1e-5f, p[i] - p[i - 1]));
            return c[c.Length - 1];
        }

        // icons: white shapes on 64 x 64 (tinted by Image.color), the trophy in colour
        public static Sprite Icon(string key)
        {
            string k = "icon-" + key;
            if (sprites.TryGetValue(k, out var sp)) return sp;
            var r = new Raster(64, 64, new Color(1, 1, 1, 0));
            var w = Color.white;
            var pts = new List<Vector2>();
            void P(params float[] xy) { pts.Clear(); for (int i = 0; i < xy.Length; i += 2) pts.Add(new Vector2(xy[i], xy[i + 1])); }
            switch (key)
            {
                case "cross": P(13, 13, 51, 51); r.Stroke(pts, false, 7, w); P(51, 13, 13, 51); r.Stroke(pts, false, 7, w); break;
                case "ring": r.StrokeCircle(32, 32, 23, 6, w); break;
                case "square": r.StrokeRoundRect(12, 12, 40, 40, 3, 6, w); break;
                case "tri": P(32, 9, 55, 51, 9, 51); r.Stroke(pts, true, 6, w); break;
                case "disc": r.FillCircle(32, 32, 24, w); break;
                case "right": P(14, 8, 56, 32, 14, 56); r.FillPoly(pts, w); break;
                case "left": P(50, 8, 8, 32, 50, 56); r.FillPoly(pts, w); break;
                case "up": P(32, 6, 58, 58, 6, 58); r.FillPoly(pts, w); break;
                case "down": P(6, 6, 58, 6, 32, 58); r.FillPoly(pts, w); break;
                case "pause": r.FillRoundRect(14, 8, 13, 48, 2, w); r.FillRoundRect(37, 8, 13, 48, 2, w); break;
                case "pipOn": r.FillRoundRect(10, 3, 44, 58, 3, w); break;
                case "pipOff": r.StrokeRoundRect(14, 7, 36, 50, 3, 7, w); break;
                case "check": P(12, 34, 26, 48, 52, 16); r.Stroke(pts, false, 7, w); break;
                case "caret": P(16, 24, 32, 40, 48, 24); r.Stroke(pts, false, 6, w); break;
                case "trophy":
                {
                    var gold = Colors.Hex("#ffc83d");
                    r.StrokeCircle(14, 20, 8, 4.5f, gold);
                    r.StrokeCircle(50, 20, 8, 4.5f, gold);
                    var path = new Raster.Path().M(14, 8).L(50, 8).C(50, 34, 42, 42, 32, 42).C(22, 42, 14, 34, 14, 8);
                    r.FillPoly(path.pts, gold);
                    r.FillRoundRect(28.5f, 40, 7, 10, 1, gold);
                    r.FillRoundRect(17, 49, 30, 9, 2, Colors.Hex("#d89a1c"));
                    break;
                }
                default: r.FillCircle(32, 32, 20, w); break;
            }
            sp = r.ToSprite(k, 100f);
            sprites[k] = sp;
            return sp;
        }

        /// a closed / open polyline drawn into a sprite (SVG strokes / fills that are not icons);
        /// points in viewBox units, `scale` sprite pixels per unit, origin of the viewBox at `origin`
        public static Sprite PolySprite(string key, int w, int h, float scale, Vector2 origin, float[] xy, bool fill, bool closed, float width, Color color)
        {
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            var r = new Raster(w, h, new Color(color.r, color.g, color.b, 0));
            var pts = new List<Vector2>();
            for (int i = 0; i < xy.Length; i += 2) pts.Add(new Vector2((xy[i] + origin.x) * scale, (xy[i + 1] + origin.y) * scale));
            if (fill) r.FillPoly(pts, color); else r.Stroke(pts, closed, width * scale, color);
            sp = r.ToSprite(key, 100f * scale);
            sprites[key] = sp;
            return sp;
        }
    }

    /// hover / focus looks of a button (the CSS :hover, .focus and .on rules). `padShift` moves the
    /// box and its content right while focused (.mi.focus { transform: translateX(6px) }).
    public sealed class Skin : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image fill, border, outline;
        public Graphic label;
        public Color fillN, fillH, fillF, borderN, borderH, borderF, textN, textH, textF;
        public bool hoverLikeFocus;       // .mi / .bind: hover looks like focus
        public LayoutGroup group;         // padShift moves its padding
        public int padShift;
        RectOffset basePad;
        bool hover, focus, shifted;

        public bool Focus { get => focus; set { if (focus == value) return; focus = value; Apply(); } }
        public bool Hover => hover;

        public void Init(Image f, Image b, Graphic l, Color fN, Color fH, Color bN, Color bH)
        {
            fill = f; border = b; label = l;
            fillN = fN; fillH = fH; fillF = fH;
            borderN = bN; borderH = bH; borderF = bH;
            textN = textH = textF = l != null ? l.color : Color.white;
            Apply();
        }

        public void Apply()
        {
            bool hot = focus || (hoverLikeFocus && hover);
            if (fill != null) fill.color = hot ? fillF : hover ? fillH : fillN;
            if (border != null) border.color = hot ? borderF : hover ? borderH : borderN;
            if (label != null) label.color = hot ? textF : hover ? textH : textN;
            if (outline != null) outline.enabled = focus;
            if (group != null && group.padding != null && padShift != 0 && hot != shifted)
            {
                shifted = hot;
                if (basePad == null) basePad = new RectOffset(group.padding.left, group.padding.right, group.padding.top, group.padding.bottom);
                int d = hot ? padShift : 0;
                group.padding = new RectOffset(basePad.left + d, basePad.right - d, basePad.top, basePad.bottom);
                foreach (var im in new[] { fill, border })
                    if (im != null && im.transform != transform) { var rt = im.rectTransform; rt.offsetMin = new Vector2(d, rt.offsetMin.y); rt.offsetMax = new Vector2(d, rt.offsetMax.y); }
            }
        }

        public void OnPointerEnter(PointerEventData e) { hover = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { hover = false; Apply(); }
        void OnDisable() { hover = false; }
    }

    /// small animations of style.css: blink, toast pop / fade-out, self-removal (unscaled time)
    public sealed class Anim : MonoBehaviour
    {
        public CanvasGroup group;
        public float blinkPeriod;          // @keyframes blink: 50% { opacity: 0.45 }
        public float popTime = -1;         // @keyframes pop 0.25s: scale 0.8 -> 1, opacity 0 -> 1
        public float fadeAt = -1, fadeDur = 0.5f, fadeRise; // .out: opacity 0 (+ translateY)
        public float destroyAt = -1;
        public RectTransform move;
        float t0;
        Vector2 basePos;

        bool baseSet;

        void OnEnable() { t0 = Time.unscaledTime; }

        public static Anim On(Component c, bool blocksRaycasts = true)
        {
            var a = c.gameObject.AddComponent<Anim>();
            a.group = Ui.Group(c, 1, blocksRaycasts);
            return a;
        }

        public void FadeOutIn(float delay, float dur, float rise = 0)
        {
            fadeAt = Time.unscaledTime - t0 + delay; fadeDur = dur; fadeRise = rise;
        }

        void Update()
        {
            // a MonoBehaviour update outside the App's frame: an error is reported with its place, and the
            // animation stops instead of throwing every frame
            try { Step(); }
            catch (Exception e) { Faults.Report("ui animation '" + name + "'", e); enabled = false; }
        }

        void Step()
        {
            float t = Time.unscaledTime - t0;
            if (move != null && !baseSet) { basePos = move.anchoredPosition; baseSet = true; }
            float alpha = 1;
            if (blinkPeriod > 0) alpha = 1 - 0.55f * (0.5f - 0.5f * Mathf.Cos(t / blinkPeriod * Mathf.PI * 2));
            if (popTime > 0 && t < popTime)
            {
                float k = 1 - (1 - t / popTime) * (1 - t / popTime); // ease-out
                alpha *= k;
                transform.localScale = Vector3.one * (0.8f + 0.2f * k);
            }
            else if (popTime > 0) transform.localScale = Vector3.one;
            if (fadeAt >= 0 && t >= fadeAt)
            {
                float k = Mathf.Clamp01((t - fadeAt) / fadeDur);
                alpha *= 1 - k;
                if (move != null && fadeRise != 0) move.anchoredPosition = basePos + new Vector2(0, fadeRise * k);
            }
            if (group != null) group.alpha = alpha;
            if (destroyAt >= 0 && t >= destroyAt) { gameObject.SetActive(false); Destroy(gameObject); }
        }
    }
}
