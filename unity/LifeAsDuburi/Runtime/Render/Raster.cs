// A small CPU stand-in for the browser's 2D canvas, for the textures the JS draws at run time
// (pool tiles, sand / gravel / rock, sprites, the torpedo board print, text labels, animal drawings).
// Colours are canvas colours (sRGB, straight alpha, row 0 at the top); composition is source-over or
// destination-out like the canvas. Fills use the non-zero rule with 4x vertical / exact horizontal
// anti-aliasing; strokes are filled outlines (butt caps, round joins); text uses a built-in stroke font
// (bold sans capitals and digits) in place of the browser's 'bold Arial'.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Duburi.Game
{
    public sealed class Raster
    {
        public readonly int W, H;
        readonly float[] r, g, b, a;
        public enum Op { SourceOver, DestinationOut }
        public Op composite = Op.SourceOver;

        // current transform: x' = sx * x + tx, y' = sy * y + ty (translate / scale only, like the JS uses)
        float sx = 1, sy = 1, tx, ty;
        readonly Stack<float[]> saved = new Stack<float[]>();

        readonly List<List<Vector2>> path = new List<List<Vector2>>();
        List<Vector2> sub;
        float[] cov;
        const int SS = 4; // vertical sub-scanlines per pixel

        public Raster(int w, int h)
        {
            W = w; H = h;
            r = new float[w * h]; g = new float[w * h]; b = new float[w * h]; a = new float[w * h];
            cov = new float[w + 2];
        }

        // ---------------------------------------------------------------- state

        public void Save() => saved.Push(new[] { sx, sy, tx, ty, (float)composite });
        public void Restore()
        {
            if (saved.Count == 0) return;
            var s = saved.Pop();
            sx = s[0]; sy = s[1]; tx = s[2]; ty = s[3]; composite = (Op)(int)s[4];
        }
        public void Translate(float x, float y) { tx += sx * x; ty += sy * y; }
        public void Scale(float x, float y) { sx *= x; sy *= y; }

        Vector2 T(float x, float y) => new Vector2(sx * x + tx, sy * y + ty);

        // ---------------------------------------------------------------- pixels

        /// scenery.js paintPixels: fn(u, v, rgb 0..255) for every pixel, opaque
        public void Paint(Action<float, float, float[]> fn)
        {
            var rgb = new float[3];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    fn(x / (float)W, y / (float)H, rgb);
                    int i = y * W + x;
                    r[i] = Byte(rgb[0]) / 255f; g[i] = Byte(rgb[1]) / 255f; b[i] = Byte(rgb[2]) / 255f; a[i] = 1;
                }
        }

        /// Uint8ClampedArray conversion: clamp, round half to even
        static int Byte(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)Math.Round(v, MidpointRounding.ToEven);

        public void SetPixel(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            r[i] = c.r; g[i] = c.g; b[i] = c.b; a[i] = c.a;
        }

        void Blend(int i, Color c, float cover)
        {
            float sa = c.a * cover;
            if (sa <= 0) return;
            if (composite == Op.DestinationOut) { a[i] *= 1 - sa; return; }
            float da = a[i], oa = sa + da * (1 - sa);
            if (oa <= 0) { r[i] = g[i] = b[i] = a[i] = 0; return; }
            float k = da * (1 - sa);
            r[i] = (c.r * sa + r[i] * k) / oa;
            g[i] = (c.g * sa + g[i] * k) / oa;
            b[i] = (c.b * sa + b[i] * k) / oa;
            a[i] = oa;
        }

        // ---------------------------------------------------------------- rectangles

        public void FillRect(float x, float y, float w, float h, Color c)
        {
            BeginPath();
            Rect(x, y, w, h);
            Fill(c);
        }

        public void StrokeRect(float x, float y, float w, float h, float lineWidth, Color c)
        {
            BeginPath();
            Rect(x, y, w, h);
            Stroke(c, lineWidth, false);
        }

        public void Clear(Color c)
        {
            for (int i = 0; i < r.Length; i++) { r[i] = c.r; g[i] = c.g; b[i] = c.b; a[i] = c.a; }
        }

        // ---------------------------------------------------------------- paths

        public void BeginPath() { path.Clear(); sub = null; }
        public void MoveTo(float x, float y) { sub = new List<Vector2> { T(x, y) }; path.Add(sub); }
        public void LineTo(float x, float y) { if (sub == null) MoveTo(x, y); else sub.Add(T(x, y)); }
        public void ClosePath() { if (sub != null && sub.Count > 0) { var f = sub[0]; sub.Add(f); sub = new List<Vector2> { f }; path.Add(sub); } }

        public void QuadraticCurveTo(float cx, float cy, float x, float y)
        {
            if (sub == null) MoveTo(cx, cy);
            Vector2 p0 = sub[sub.Count - 1], p1 = T(cx, cy), p2 = T(x, y);
            int n = Mathf.Clamp((int)((p0 - p1).magnitude + (p1 - p2).magnitude) / 3, 4, 64);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, k = 1 - t;
                sub.Add(k * k * p0 + 2 * k * t * p1 + t * t * p2);
            }
        }

        public void Rect(float x, float y, float w, float h)
        {
            MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h);
            ClosePath();
        }

        /// arc(cx, cy, r, a0, a1, anticlockwise) like the canvas (angles in radians, y down)
        public void Arc(float cx, float cy, float rad, float a0, float a1, bool anticlockwise = false)
        {
            float d = a1 - a0;
            const float TAU = Mathf.PI * 2;
            if (!anticlockwise) { if (d >= TAU) d = TAU; else { d %= TAU; if (d < 0) d += TAU; } }
            else { if (-d >= TAU) d = -TAU; else { d %= TAU; if (d > 0) d -= TAU; } }
            float pr = rad * Mathf.Max(Mathf.Abs(sx), Mathf.Abs(sy));
            int n = Mathf.Clamp((int)(Mathf.Abs(d) * pr / 2f), 8, 256);
            for (int i = 0; i <= n; i++)
            {
                float t = a0 + d * i / n;
                float x = cx + rad * Mathf.Cos(t), y = cy + rad * Mathf.Sin(t);
                if (i == 0 && sub == null) MoveTo(x, y); else LineTo(x, y);
            }
        }

        /// fill the current path (non-zero winding, sub-paths implicitly closed)
        public void Fill(Color c) => FillPolys(path, c);

        void FillPolys(List<List<Vector2>> polys, Color c)
        {
            float ymin = float.MaxValue, ymax = float.MinValue, xmin = float.MaxValue, xmax = float.MinValue;
            foreach (var p in polys)
                foreach (var v in p)
                {
                    if (v.y < ymin) ymin = v.y; if (v.y > ymax) ymax = v.y;
                    if (v.x < xmin) xmin = v.x; if (v.x > xmax) xmax = v.x;
                }
            if (ymin > ymax) return;
            int y0 = Mathf.Max(0, Mathf.FloorToInt(ymin)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(ymax));
            int x0 = Mathf.Max(0, Mathf.FloorToInt(xmin)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(xmax));
            if (x0 > x1) return;
            var xs = new List<KeyValuePair<float, int>>(64);
            for (int row = y0; row <= y1; row++)
            {
                Array.Clear(cov, x0, x1 - x0 + 2);
                for (int s = 0; s < SS; s++)
                {
                    float syl = row + (s + 0.5f) / SS;
                    xs.Clear();
                    foreach (var p in polys)
                    {
                        int n = p.Count;
                        if (n < 2) continue;
                        for (int i = 0; i < n; i++)
                        {
                            Vector2 e0 = p[i], e1 = p[(i + 1) % n];
                            if (e0.y == e1.y) continue;
                            bool down = e1.y > e0.y;
                            if (down ? (syl >= e0.y && syl < e1.y) : (syl >= e1.y && syl < e0.y))
                            {
                                float x = e0.x + (syl - e0.y) * (e1.x - e0.x) / (e1.y - e0.y);
                                xs.Add(new KeyValuePair<float, int>(x, down ? 1 : -1));
                            }
                        }
                    }
                    if (xs.Count < 2) continue;
                    xs.Sort((p, q) => p.Key.CompareTo(q.Key));
                    int wind = 0;
                    for (int k = 0; k < xs.Count - 1; k++)
                    {
                        wind += xs[k].Value;
                        if (wind != 0) Span(xs[k].Key, xs[k + 1].Key, 1f / SS);
                    }
                }
                for (int x = x0; x <= x1; x++)
                {
                    float cv = cov[x];
                    if (cv > 0) Blend(row * W + x, c, cv > 1 ? 1 : cv);
                }
            }
        }

        void Span(float xa, float xb, float w)
        {
            if (xb <= 0 || xa >= W) return;
            if (xa < 0) xa = 0;
            if (xb > W) xb = W;
            int pa = (int)xa, pb = Mathf.Min(W - 1, (int)xb);
            if (pa == pb) { cov[pa] += (xb - xa) * w; return; }
            cov[pa] += (pa + 1 - xa) * w;
            for (int p = pa + 1; p < pb; p++) cov[p] += w;
            if (pb < W) cov[pb] += (xb - pb) * w;
        }

        /// stroke the current path: butt caps, round joins (lineJoin 'round'); closed rects close
        public void Stroke(Color c, float lineWidth, bool roundJoins = true)
        {
            float hw = lineWidth * 0.5f * (Mathf.Abs(sx) + Mathf.Abs(sy)) * 0.5f;
            var polys = new List<List<Vector2>>();
            foreach (var p in path)
            {
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    Vector2 p0 = p[i], p1 = p[i + 1], d = p1 - p0;
                    float l = d.magnitude;
                    if (l < 1e-6f) continue;
                    Vector2 n = new Vector2(-d.y, d.x) / l * hw;
                    // square extension at the joins of closed rectangles (miter of a right angle)
                    Vector2 ext = roundJoins ? Vector2.zero : d / l * hw;
                    Vector2 a0 = p0 - ext, a1 = p1 + ext;
                    if (i == 0 && !roundJoins && !Closed(p)) a0 = p0;
                    if (i + 2 == p.Count && !roundJoins && !Closed(p)) a1 = p1;
                    polys.Add(Oriented(new List<Vector2> { a0 + n, a1 + n, a1 - n, a0 - n }));
                    if (roundJoins && i > 0) polys.Add(Oriented(CirclePoly(p0, hw)));
                }
            }
            FillPolys(polys, c);
        }

        static bool Closed(List<Vector2> p) => p.Count > 2 && (p[0] - p[p.Count - 1]).sqrMagnitude < 1e-8f;

        static List<Vector2> CirclePoly(Vector2 c, float rad)
        {
            int n = Mathf.Clamp((int)(rad * 2), 8, 48);
            var l = new List<Vector2>(n);
            for (int i = 0; i < n; i++) { float t = i * Mathf.PI * 2 / n; l.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * rad); }
            return l;
        }

        static List<Vector2> Oriented(List<Vector2> p)
        {
            float s = 0;
            for (int i = 0; i < p.Count; i++) { var q0 = p[i]; var q1 = p[(i + 1) % p.Count]; s += q0.x * q1.y - q1.x * q0.y; }
            if (s < 0) p.Reverse();
            return p;
        }

        // ---------------------------------------------------------------- gradients, images, text

        /// fill the whole canvas with a radial gradient (createRadialGradient(cx, cy, 0, cx, cy, r0))
        public void RadialGradient(float cx, float cy, float rad, float[] stops, Color[] colors)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / rad;
                    Blend(y * W + x, Gradient(d, stops, colors), 1);
                }
        }

        static Color Gradient(float t, float[] stops, Color[] colors)
        {
            if (t <= stops[0]) return colors[0];
            for (int i = 1; i < stops.Length; i++)
                if (t <= stops[i]) return Color.Lerp(colors[i - 1], colors[i], (t - stops[i - 1]) / (stops[i] - stops[i - 1]));
            return colors[colors.Length - 1];
        }

        /// drawImage(img, x, y, w, h) with an opaque image (any texture; read through the GPU)
        public void DrawImage(Texture src, int x, int y, int w, int h)
        {
            var px = ReadTexture(src, w, h);
            if (px == null) return;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    // Texture rows go bottom-up, canvas rows top-down
                    Color32 c = px[(h - 1 - j) * w + i];
                    Blend((y + j) * W + x + i, new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f), 1);
                }
        }

        /// The texture resized to w x h, as sRGB pixels (bottom row first), via a GPU blit (the
        /// source does not need to be readable).
        public static Color32[] ReadTexture(Texture src, int w, int h)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            return px;
        }

        /// fillText(text, cx, cy) with textAlign 'center', textBaseline 'middle' in a bold sans of
        /// the given pixel size
        public void FillText(string text, float cx, float cy, float fontPx, Color c)
        {
            float em = fontPx, adv = 0;
            foreach (char ch in text) adv += StrokeFont.Advance(ch) * em;
            float x = cx - adv / 2;
            float capH = 0.716f * em; // Arial cap height
            float baseY = cy + capH / 2;
            float lw = 0.15f * em;
            foreach (char ch in text)
            {
                var strokes = StrokeFont.Glyph(ch);
                if (strokes != null)
                {
                    BeginPath();
                    float gw = StrokeFont.Advance(ch) * em;
                    float inset = lw * 0.5f + 0.06f * em;
                    foreach (var st in strokes)
                    {
                        for (int i = 0; i < st.Length; i += 2)
                        {
                            float gx = x + inset + st[i] * (gw - 2 * inset), gy = baseY - lw * 0.5f - st[i + 1] * (capH - lw);
                            if (i == 0) MoveTo(gx, gy); else LineTo(gx, gy);
                        }
                    }
                    StrokeRoundCaps(c, lw);
                }
                x += StrokeFont.Advance(ch) * em;
            }
        }

        // strokes with round caps and joins (the stroke font)
        void StrokeRoundCaps(Color c, float lineWidth)
        {
            float hw = lineWidth * 0.5f;
            var polys = new List<List<Vector2>>();
            foreach (var p in path)
            {
                for (int i = 0; i < p.Count; i++)
                {
                    polys.Add(Oriented(CirclePoly(p[i], hw)));
                    if (i + 1 < p.Count)
                    {
                        Vector2 p0 = p[i], p1 = p[i + 1], d = p1 - p0;
                        float l = d.magnitude;
                        if (l < 1e-6f) continue;
                        Vector2 n = new Vector2(-d.y, d.x) / l * hw;
                        polys.Add(Oriented(new List<Vector2> { p0 + n, p1 + n, p1 - n, p0 - n }));
                    }
                }
            }
            FillPolys(polys, c);
        }

        // ---------------------------------------------------------------- output

        /// Texture2D (sRGB) of the canvas; three CanvasTexture flipY: canvas top = v 1
        public Texture2D ToTexture(string name, bool mipmaps = true, TextureWrapMode wrap = TextureWrapMode.Clamp, int aniso = 4, bool srgb = true)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, mipmaps, !srgb)
            {
                name = name, wrapMode = wrap, anisoLevel = aniso,
                filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
            };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int src = y * W, dst = (H - 1 - y) * W;
                for (int x = 0; x < W; x++)
                {
                    int i = src + x;
                    px[dst + x] = new Color32(To8(r[i]), To8(g[i]), To8(b[i]), To8(a[i]));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(mipmaps, false);
            return tex;
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// canvas colour from CSS: '#rgb', '#rrggbb' or 'rgba(r,g,b,a)'
        public static Color Css(string s)
        {
            if (s.StartsWith("rgba(") || s.StartsWith("rgb("))
            {
                var p = s.Substring(s.IndexOf('(') + 1).TrimEnd(')').Split(',');
                float A = p.Length > 3 ? float.Parse(p[3].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 1;
                return new Color(int.Parse(p[0].Trim()) / 255f, int.Parse(p[1].Trim()) / 255f, int.Parse(p[2].Trim()) / 255f, A);
            }
            return Colors.Hex(s);
        }
    }

    /// Bold sans capitals and digits as stroke paths in a unit box (x 0..1 across the glyph, y 0..1
    /// from the baseline to the cap height). Stands in for the browser's 'bold Arial' in labels.
    public static class StrokeFont
    {
        static Dictionary<char, float[][]> glyphs;

        public static float Advance(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case ' ': return 0.28f;
                case 'I': return 0.3f;
                case 'M': case 'W': return 0.84f;
                case 'O': case 'Q': case 'G': case 'C': case 'D': return 0.78f;
                case 'F': case 'T': case 'J': case 'L': return 0.61f;
                case 'E': case 'P': case 'S': case 'Z': case 'K': return 0.67f;
                default: return char.IsDigit(c) ? 0.56f : 0.72f;
            }
        }

        public static float[][] Glyph(char c)
        {
            if (glyphs == null) Build();
            return glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : null;
        }

        static float[] Arc(float cx, float cy, float rx, float ry, float a0, float a1, int n = 12)
        {
            var l = new float[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                float t = (a0 + (a1 - a0) * i / n) * Mathf.Deg2Rad;
                l[i * 2] = cx + rx * Mathf.Cos(t);
                l[i * 2 + 1] = cy + ry * Mathf.Sin(t);
            }
            return l;
        }

        static float[] Cat(params float[][] parts)
        {
            var l = new List<float>();
            foreach (var p in parts) l.AddRange(p);
            return l.ToArray();
        }

        static float[] P(params float[] xy) => xy;

        static void Build()
        {
            glyphs = new Dictionary<char, float[][]>
            {
                ['A'] = new[] { P(0, 0, 0.5f, 1, 1, 0), P(0.2f, 0.32f, 0.8f, 0.32f) },
                ['B'] = new[] { Cat(P(0, 0, 0, 1, 0.6f, 1), Arc(0.6f, 0.75f, 0.32f, 0.25f, 90, -90), P(0, 0.5f)), Cat(P(0.6f, 0.5f), Arc(0.6f, 0.25f, 0.4f, 0.25f, 90, -90), P(0, 0)) },
                ['C'] = new[] { Arc(0.55f, 0.5f, 0.5f, 0.5f, 45, 315, 20) },
                ['D'] = new[] { Cat(P(0, 0, 0, 1, 0.45f, 1), Arc(0.45f, 0.5f, 0.55f, 0.5f, 90, -90, 16), P(0, 0)) },
                ['E'] = new[] { P(1, 1, 0, 1, 0, 0, 1, 0), P(0, 0.52f, 0.85f, 0.52f) },
                ['F'] = new[] { P(1, 1, 0, 1, 0, 0), P(0, 0.52f, 0.85f, 0.52f) },
                ['G'] = new[] { Cat(Arc(0.55f, 0.5f, 0.5f, 0.5f, 45, 360, 20), P(1, 0.45f, 0.6f, 0.45f)) },
                ['H'] = new[] { P(0, 0, 0, 1), P(1, 0, 1, 1), P(0, 0.52f, 1, 0.52f) },
                ['I'] = new[] { P(0.5f, 0, 0.5f, 1) },
                ['J'] = new[] { Cat(P(1, 1, 1, 0.3f), Arc(0.5f, 0.3f, 0.5f, 0.3f, 0, -180, 10)) },
                ['K'] = new[] { P(0, 0, 0, 1), P(1, 1, 0, 0.4f), P(0.3f, 0.6f, 1, 0) },
                ['L'] = new[] { P(0, 1, 0, 0, 1, 0) },
                ['M'] = new[] { P(0, 0, 0, 1, 0.5f, 0.2f, 1, 1, 1, 0) },
                ['N'] = new[] { P(0, 0, 0, 1, 1, 0, 1, 1) },
                ['O'] = new[] { Arc(0.5f, 0.5f, 0.5f, 0.5f, 0, 360, 24) },
                ['P'] = new[] { Cat(P(0, 0, 0, 1, 0.55f, 1), Arc(0.55f, 0.74f, 0.45f, 0.26f, 90, -90, 12), P(0, 0.48f)) },
                ['Q'] = new[] { Arc(0.5f, 0.5f, 0.5f, 0.5f, 0, 360, 24), P(0.6f, 0.25f, 1, -0.05f) },
                ['R'] = new[] { Cat(P(0, 0, 0, 1, 0.55f, 1), Arc(0.55f, 0.74f, 0.45f, 0.26f, 90, -90, 12), P(0, 0.48f)), P(0.5f, 0.48f, 1, 0) },
                ['S'] = new[] { Cat(Arc(0.5f, 0.75f, 0.47f, 0.25f, 20, 270, 12), Arc(0.5f, 0.25f, 0.5f, 0.25f, 90, -160, 12)) },
                ['T'] = new[] { P(0, 1, 1, 1), P(0.5f, 1, 0.5f, 0) },
                ['U'] = new[] { Cat(P(0, 1, 0, 0.35f), Arc(0.5f, 0.35f, 0.5f, 0.35f, 180, 360, 14), P(1, 1)) },
                ['V'] = new[] { P(0, 1, 0.5f, 0, 1, 1) },
                ['W'] = new[] { P(0, 1, 0.25f, 0, 0.5f, 0.8f, 0.75f, 0, 1, 1) },
                ['X'] = new[] { P(0, 1, 1, 0), P(0, 0, 1, 1) },
                ['Y'] = new[] { P(0, 1, 0.5f, 0.5f, 1, 1), P(0.5f, 0.5f, 0.5f, 0) },
                ['Z'] = new[] { P(0, 1, 1, 1, 0, 0, 1, 0) },
                ['0'] = new[] { Arc(0.5f, 0.5f, 0.5f, 0.5f, 0, 360, 24) },
                ['1'] = new[] { P(0.2f, 0.8f, 0.6f, 1, 0.6f, 0) },
                ['2'] = new[] { Cat(Arc(0.5f, 0.7f, 0.5f, 0.3f, 160, -20, 10), P(0, 0, 1, 0)) },
                ['3'] = new[] { Cat(Arc(0.5f, 0.75f, 0.45f, 0.25f, 150, -90, 10), Arc(0.5f, 0.25f, 0.5f, 0.25f, 90, -150, 10)) },
                ['4'] = new[] { P(0.75f, 0, 0.75f, 1, 0, 0.3f, 1, 0.3f) },
                ['5'] = new[] { Cat(P(0.95f, 1, 0.1f, 1, 0.05f, 0.55f), Arc(0.5f, 0.3f, 0.5f, 0.3f, 130, -150, 12)) },
                ['6'] = new[] { Cat(Arc(0.55f, 0.6f, 0.45f, 0.4f, 60, 180, 8), Arc(0.5f, 0.3f, 0.5f, 0.3f, 180, 540, 20)) },
                ['7'] = new[] { P(0, 1, 1, 1, 0.35f, 0) },
                ['8'] = new[] { Arc(0.5f, 0.75f, 0.42f, 0.25f, -90, 270, 16), Arc(0.5f, 0.25f, 0.5f, 0.25f, 90, 450, 16) },
                ['9'] = new[] { Cat(Arc(0.5f, 0.7f, 0.5f, 0.3f, 0, 360, 20), Arc(0.45f, 0.4f, 0.55f, 0.4f, 0, -120, 8)) },
                ['-'] = new[] { P(0.1f, 0.45f, 0.9f, 0.45f) },
            };
        }
    }
}
