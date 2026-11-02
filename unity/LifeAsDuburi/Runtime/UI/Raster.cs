// A small anti-aliased rasteriser for the shapes the browser version draws with
// SVG / canvas 2D (controller outline, minimap, icons): signed-area accumulation
// as in font-rs (exact coverage per pixel, non-zero winding by magnitude).
// Coordinates are in pixels with y DOWN (like SVG / canvas); ToTexture flips them.
// Used only while building textures (menus / match start), never per frame.

using System.Collections.Generic;
using UnityEngine;

namespace Duburi.Game
{
    public sealed class Raster
    {
        public readonly int W, H;
        readonly float[] a;      // accumulation buffer (w*h + 2)
        readonly Color[] px;     // straight alpha, row 0 = top
        readonly List<Vector2> tmp = new List<Vector2>(64);

        public Raster(int w, int h, Color clear)
        {
            W = w; H = h;
            a = new float[w * h + 2];
            px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = clear;
        }

        // ---------------------------------------------------------------- coverage
        void Line(Vector2 p0, Vector2 p1)
        {
            // keep everything inside the buffer (shapes are drawn inside it anyway)
            p0.x = Mathf.Clamp(p0.x, 0f, W - 0.001f); p1.x = Mathf.Clamp(p1.x, 0f, W - 0.001f);
            if (Mathf.Abs(p0.y - p1.y) <= 1e-6f) return;
            float dir;
            if (p0.y < p1.y) dir = 1f;
            else { dir = -1f; var t = p0; p0 = p1; p1 = t; }
            float dxdy = (p1.x - p0.x) / (p1.y - p0.y);
            float x = p0.x;
            int y0 = p0.y < 0 ? 0 : (int)p0.y;
            if (p0.y < 0) x -= p0.y * dxdy;
            int yEnd = Mathf.Min(H, Mathf.CeilToInt(p1.y));
            for (int y = y0; y < yEnd; y++)
            {
                int ls = y * W;
                float dy = Mathf.Min(y + 1, p1.y) - Mathf.Max(y, p0.y);
                float xnext = x + dxdy * dy;
                float d = dy * dir;
                float x0 = x < xnext ? x : xnext, x1 = x < xnext ? xnext : x;
                float x0floor = Mathf.Floor(x0);
                int x0i = (int)x0floor;
                float x1ceil = Mathf.Ceil(x1);
                int x1i = (int)x1ceil;
                if (x1i <= x0i + 1)
                {
                    float xmf = 0.5f * (x + xnext) - x0floor;
                    a[ls + x0i] += d - d * xmf;
                    a[ls + x0i + 1] += d * xmf;
                }
                else
                {
                    float s = 1f / (x1 - x0);
                    float x0f = x0 - x0floor;
                    float a0 = 0.5f * s * (1 - x0f) * (1 - x0f);
                    float x1f = x1 - x1ceil + 1;
                    float am = 0.5f * s * x1f * x1f;
                    a[ls + x0i] += d * a0;
                    if (x1i == x0i + 2) a[ls + x0i + 1] += d * (1 - a0 - am);
                    else
                    {
                        float a1 = s * (1.5f - x0f);
                        a[ls + x0i + 1] += d * (a1 - a0);
                        for (int xi = x0i + 2; xi < x1i - 1; xi++) a[ls + xi] += d * s;
                        float a2 = a1 + (x1i - x0i - 3) * s;
                        a[ls + x1i - 1] += d * (1 - a2 - am);
                    }
                    a[ls + x1i] += d * am;
                }
                x = xnext;
            }
        }

        static float SignedArea(List<Vector2> p)
        {
            float s = 0;
            for (int i = 0, n = p.Count; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; s += u.x * v.y - v.x * u.y; }
            return s * 0.5f;
        }

        /// add a closed contour to the current shape; `orient` makes every piece wind the same
        /// way so overlapping stroke pieces add up instead of cancelling
        public void AddContour(List<Vector2> p, bool orient = false)
        {
            int n = p.Count;
            if (n < 3) return;
            bool rev = orient && SignedArea(p) < 0;
            for (int i = 0; i < n; i++)
            {
                var u = p[rev ? n - 1 - i : i];
                var v = p[rev ? (2 * n - 2 - i) % n : (i + 1) % n];
                Line(u, v);
            }
        }

        /// composite the accumulated shape with colour c (source-over) and clear the accumulator
        public void Paint(Color c)
        {
            float acc = 0;
            int n = W * H;
            for (int i = 0; i < n; i++)
            {
                acc += a[i];
                a[i] = 0;
                float cov = Mathf.Min(1f, Mathf.Abs(acc));
                if (cov < 0.002f) continue;
                float sa = c.a * cov;
                Color d = px[i];
                float oa = sa + d.a * (1 - sa);
                if (oa <= 0) continue;
                float k = d.a * (1 - sa);
                px[i] = new Color((c.r * sa + d.r * k) / oa, (c.g * sa + d.g * k) / oa, (c.b * sa + d.b * k) / oa, oa);
            }
            a[n] = 0; a[n + 1] = 0;
        }

        // ---------------------------------------------------------------- shapes
        public static void Circle(List<Vector2> o, float cx, float cy, float r)
        {
            o.Clear();
            int n = Mathf.Clamp(Mathf.CeilToInt(r * 1.6f), 12, 128);
            for (int i = 0; i < n; i++) { float t = i * Mathf.PI * 2 / n; o.Add(new Vector2(cx + Mathf.Cos(t) * r, cy + Mathf.Sin(t) * r)); }
        }

        public static void RoundRect(List<Vector2> o, float x, float y, float w, float h, float r)
        {
            o.Clear();
            r = Mathf.Min(r, Mathf.Min(w, h) / 2);
            int seg = Mathf.Clamp(Mathf.CeilToInt(r * 0.6f), 3, 24);
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= seg; i++) { float t = a0 + i * (Mathf.PI / 2) / seg; o.Add(new Vector2(cx + Mathf.Cos(t) * r, cy + Mathf.Sin(t) * r)); }
            }
            if (r <= 0) { o.Add(new Vector2(x, y)); o.Add(new Vector2(x + w, y)); o.Add(new Vector2(x + w, y + h)); o.Add(new Vector2(x, y + h)); return; }
            Corner(x + w - r, y + r, -Mathf.PI / 2);
            Corner(x + w - r, y + h - r, 0);
            Corner(x + r, y + h - r, Mathf.PI / 2);
            Corner(x + r, y + r, Mathf.PI);
        }

        public void FillPoly(List<Vector2> p, Color c) { AddContour(p); Paint(c); }

        public void FillCircle(float cx, float cy, float r, Color c) { Circle(tmp, cx, cy, r); FillPoly(tmp, c); }

        public void StrokeCircle(float cx, float cy, float r, float width, Color c)
        {
            // ring = outer contour + inner contour wound the other way
            Circle(tmp, cx, cy, r + width / 2);
            AddContour(tmp, true);
            Circle(tmp, cx, cy, Mathf.Max(0, r - width / 2));
            tmp.Reverse();
            AddContourRaw(tmp);
            Paint(c);
        }

        void AddContourRaw(List<Vector2> p)
        {
            // p must already be clockwise-negative (used for holes)
            bool rev = SignedArea(p) > 0;
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var u = p[rev ? n - 1 - i : i];
                var v = p[rev ? (2 * n - 2 - i) % n : (i + 1) % n];
                Line(u, v);
            }
        }

        public void FillRoundRect(float x, float y, float w, float h, float r, Color c) { RoundRect(tmp, x, y, w, h, r); FillPoly(tmp, c); }

        public void StrokeRoundRect(float x, float y, float w, float h, float r, float width, Color c)
        {
            float hw = width / 2;
            RoundRect(tmp, x - hw, y - hw, w + width, h + width, r + hw);
            AddContour(tmp, true);
            RoundRect(tmp, x + hw, y + hw, w - width, h - width, Mathf.Max(0, r - hw));
            AddContourRaw(tmp);
            Paint(c);
        }

        /// stroke a polyline with round joins and caps (width in px)
        public void Stroke(List<Vector2> pts, bool closed, float width, Color c)
        {
            int n = pts.Count;
            if (n < 2) return;
            float hw = width / 2;
            var q = new List<Vector2>(4);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = pts[i], p1 = pts[(i + 1) % n];
                Vector2 d = p1 - p0;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                Vector2 nn = new Vector2(-d.y, d.x) / len * hw;
                q.Clear(); q.Add(p0 + nn); q.Add(p1 + nn); q.Add(p1 - nn); q.Add(p0 - nn);
                AddContour(q, true);
            }
            var circ = new List<Vector2>(32);
            for (int i = 0; i < n; i++) { Circle(circ, pts[i].x, pts[i].y, hw); AddContour(circ, true); }
            Paint(c);
        }

        // ---------------------------------------------------------------- paths (SVG subset)
        /// flattened SVG-like path: MoveTo / LineTo / CubicTo / Close
        public sealed class Path
        {
            public readonly List<Vector2> pts = new List<Vector2>(128);
            Vector2 cur;
            public Path M(float x, float y) { pts.Clear(); cur = new Vector2(x, y); pts.Add(cur); return this; }
            public Path L(float x, float y) { cur = new Vector2(x, y); pts.Add(cur); return this; }
            public Path C(float x1, float y1, float x2, float y2, float x, float y)
            {
                Vector2 p0 = cur, p1 = new Vector2(x1, y1), p2 = new Vector2(x2, y2), p3 = new Vector2(x, y);
                const int N = 20;
                for (int i = 1; i <= N; i++)
                {
                    float t = i / (float)N, u = 1 - t;
                    pts.Add(u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3);
                }
                cur = p3;
                return this;
            }
            public Path Scale(float k) { for (int i = 0; i < pts.Count; i++) pts[i] *= k; return this; }
            public Path Offset(float dx, float dy) { for (int i = 0; i < pts.Count; i++) pts[i] += new Vector2(dx, dy); return this; }
        }

        /// clip everything to a rounded rectangle of the whole image (border-radius on a canvas element)
        public void MaskRoundRect(float r)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - W / 2f) - (W / 2f - r), qy = Mathf.Abs(y + 0.5f - H / 2f) - (H / 2f - r);
                    float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                    float cov = Mathf.Clamp01(0.5f - d);
                    if (cov < 1) { var c = px[y * W + x]; c.a *= cov; px[y * W + x] = c; }
                }
        }

        // ---------------------------------------------------------------- output
        public Texture2D ToTexture(string name)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                int src = y * W, dst = (H - 1 - y) * W;
                for (int x = 0; x < W; x++) outPx[dst + x] = px[src + x];
            }
            tex.SetPixels32(outPx);
            tex.Apply(false, true);
            return tex;
        }

        public Sprite ToSprite(string name, float pixelsPerUnit = 100f)
        {
            var tex = ToTexture(name);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
