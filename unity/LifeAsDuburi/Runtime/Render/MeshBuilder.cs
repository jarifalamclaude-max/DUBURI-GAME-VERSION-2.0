// three.js geometries as Unity meshes, built in three.js space with the same parameters, vertex
// order, normals and UVs as three r16x (src/geometries/*.js): BoxGeometry, CylinderGeometry (open /
// partial), ConeGeometry, SphereGeometry (partial), TorusGeometry, CircleGeometry, PlaneGeometry,
// RingGeometry, LatheGeometry, CapsuleGeometry, IcosahedronGeometry, ExtrudeGeometry (Shape / Path with
// holes, Earcut triangulation, bevels) and BufferGeometryUtils.mergeGeometries.
// Meshes are used under the mirrored World root (Space.cs), which keeps three's winding front-facing.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Duburi.Game
{
    /// A three.js BufferGeometry: position / normal / uv (+ optional linear vertex colours), indexed or not.
    public sealed class Geo
    {
        public List<Vector3> pos = new List<Vector3>();
        public List<Vector3> nrm = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<Color> col;          // linear colours (three 'color' attribute), optional
        public List<int> idx;            // null = non-indexed
        public bool lines;               // LineSegments / Line: pairs of indices
        public bool shared;              // never destroyed with an object (item geometry, caches)
        Mesh mesh;
        Geo twoSided, backSided;         // derived geometry for lit DoubleSide / BackSide materials

        /// WithBackFaces / Flipped, built once per geometry
        public Geo TwoSided() { if (twoSided == null) { twoSided = WithBackFaces(); twoSided.shared = shared; } return twoSided; }
        public Geo BackSided() { if (backSided == null) { backSided = Flipped(); backSided.shared = shared; } return backSided; }

        public int Count => pos.Count;

        public Geo Clone()
        {
            var g = new Geo { lines = lines };
            g.pos.AddRange(pos); g.nrm.AddRange(nrm); g.uv.AddRange(uv);
            if (col != null) g.col = new List<Color>(col);
            if (idx != null) g.idx = new List<int>(idx);
            return g;
        }

        // ---------------------------------------------------------------- transforms (BufferGeometry.applyMatrix4 & co.)

        public Geo ApplyMatrix(Matrix4x4 m)
        {
            Matrix4x4 n = m.inverse.transpose;
            for (int i = 0; i < pos.Count; i++) pos[i] = m.MultiplyPoint3x4(pos[i]);
            for (int i = 0; i < nrm.Count; i++) nrm[i] = Norm(n.MultiplyVector(nrm[i]));
            mesh = null;
            return this;
        }

        public Geo RotateX(float a) => ApplyMatrix(RotX(a));
        public Geo RotateY(float a) => ApplyMatrix(RotY(a));
        public Geo RotateZ(float a) => ApplyMatrix(RotZ(a));
        public Geo Translate(float x, float y, float z)
        {
            var d = new Vector3(x, y, z);
            for (int i = 0; i < pos.Count; i++) pos[i] += d;
            mesh = null;
            return this;
        }
        public Geo Scale(float x, float y, float z) => ApplyMatrix(Matrix4x4.Scale(new Vector3(x, y, z)));

        public static Matrix4x4 RotX(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m11 = c; m.m12 = -s; m.m21 = s; m.m22 = c;
            return m;
        }
        public static Matrix4x4 RotY(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c;
            return m;
        }
        public static Matrix4x4 RotZ(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity;
            m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c;
            return m;
        }

        static Vector3 Norm(Vector3 v)
        {
            float l = v.magnitude;
            return l > 0 ? v / l : v;
        }

        // ---------------------------------------------------------------- attribute helpers

        public Geo ToNonIndexed()
        {
            if (idx == null) return Clone();
            var g = new Geo { lines = lines };
            bool hasCol = col != null;
            if (hasCol) g.col = new List<Color>(idx.Count);
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                g.pos.Add(pos[i]);
                if (nrm.Count > 0) g.nrm.Add(nrm[i]);
                if (uv.Count > 0) g.uv.Add(uv[i]);
                if (hasCol) g.col.Add(col[i]);
            }
            return g;
        }

        /// BufferGeometry.computeVertexNormals: area-weighted vertex normals when indexed, face normals when not.
        public Geo ComputeVertexNormals()
        {
            nrm.Clear();
            for (int i = 0; i < pos.Count; i++) nrm.Add(Vector3.zero);
            if (idx != null)
            {
                for (int k = 0; k + 2 < idx.Count; k += 3)
                {
                    int a = idx[k], b = idx[k + 1], c = idx[k + 2];
                    Vector3 cb = Vector3.Cross(pos[c] - pos[b], pos[a] - pos[b]);
                    nrm[a] += cb; nrm[b] += cb; nrm[c] += cb;
                }
            }
            else
            {
                for (int k = 0; k + 2 < pos.Count; k += 3)
                {
                    Vector3 cb = Vector3.Cross(pos[k + 2] - pos[k + 1], pos[k] - pos[k + 1]);
                    nrm[k] = cb; nrm[k + 1] = cb; nrm[k + 2] = cb;
                }
            }
            for (int i = 0; i < nrm.Count; i++) nrm[i] = Norm(nrm[i]);
            mesh = null;
            return this;
        }

        /// Flat shading: a non-indexed copy where every triangle has its own vertices and face normal
        /// (three's toNonIndexed() + computeVertexNormals()). Use for machined, hard-edged parts.
        public Geo Faceted()
        {
            var g = ToNonIndexed();
            g.ComputeVertexNormals();
            g.EnsureUV();
            return g;
        }

        public Geo NormalizeNormals()
        {
            for (int i = 0; i < nrm.Count; i++) nrm[i] = Norm(nrm[i]);
            return this;
        }

        public Geo EnsureUV()
        {
            while (uv.Count < pos.Count) uv.Add(Vector2.zero);
            return this;
        }

        /// scenery.js scaleUV
        public Geo ScaleUV(float su, float sv)
        {
            for (int i = 0; i < uv.Count; i++) uv[i] = new Vector2(uv[i].x * su, uv[i].y * sv);
            mesh = null;
            return this;
        }

        /// scenery.js colored(): one colour on every vertex (hex as in the JS, stored linear)
        public Geo Colored(int hex)
        {
            Color c = Colors.Rgb(hex).linear;
            col = new List<Color>(pos.Count);
            for (int i = 0; i < pos.Count; i++) col.Add(c);
            return this;
        }

        public Geo Colored(Color linear)
        {
            col = new List<Color>(pos.Count);
            for (int i = 0; i < pos.Count; i++) col.Add(linear);
            return this;
        }

        /// The same geometry seen from both sides (three DoubleSide with lighting): the back faces
        /// (reversed winding, flipped normals) first, then the front faces.
        public Geo WithBackFaces()
        {
            var g = new Geo { lines = lines };
            int n = pos.Count;
            for (int pass = 0; pass < 2; pass++)
            {
                float s = pass == 0 ? -1 : 1;
                g.pos.AddRange(pos);
                for (int i = 0; i < n; i++) g.nrm.Add(i < nrm.Count ? nrm[i] * s : Vector3.zero);
                for (int i = 0; i < n; i++) g.uv.Add(i < uv.Count ? uv[i] : Vector2.zero);
                if (col != null) { if (g.col == null) g.col = new List<Color>(2 * n); g.col.AddRange(col); }
            }
            g.idx = new List<int>();
            if (idx != null)
            {
                for (int k = 0; k + 2 < idx.Count; k += 3) { g.idx.Add(idx[k + 2]); g.idx.Add(idx[k + 1]); g.idx.Add(idx[k]); }
                for (int k = 0; k < idx.Count; k++) g.idx.Add(idx[k] + n);
            }
            else
            {
                for (int k = 0; k + 2 < n; k += 3) { g.idx.Add(k + 2); g.idx.Add(k + 1); g.idx.Add(k); }
                for (int k = 0; k < n; k++) g.idx.Add(k + n);
            }
            return g;
        }

        /// three BackSide: only the inside (reversed winding, flipped normals).
        public Geo Flipped()
        {
            var g = Clone();
            for (int i = 0; i < g.nrm.Count; i++) g.nrm[i] = -g.nrm[i];
            if (g.idx != null)
            {
                for (int k = 0; k + 2 < g.idx.Count; k += 3) { int t = g.idx[k]; g.idx[k] = g.idx[k + 2]; g.idx[k + 2] = t; }
            }
            else
            {
                for (int k = 0; k + 2 < g.pos.Count; k += 3)
                {
                    Swap(g.pos, k, k + 2); Swap(g.nrm, k, k + 2); Swap(g.uv, k, k + 2);
                    if (g.col != null) Swap(g.col, k, k + 2);
                }
            }
            return g;
        }

        static void Swap<T>(List<T> l, int a, int b) { if (b >= l.Count) return; T t = l[a]; l[a] = l[b]; l[b] = t; }

        /// BufferGeometryUtils.mergeGeometries (no groups): indexed when every part is indexed.
        public static Geo Merge(IList<Geo> parts)
        {
            if (parts == null || parts.Count == 0) return null;
            bool indexed = true, hasCol = true, isLines = parts[0].lines;
            foreach (var p in parts) { if (p.idx == null) indexed = false; if (p.col == null) hasCol = false; }
            var g = new Geo { lines = isLines };
            if (hasCol) g.col = new List<Color>();
            if (indexed) g.idx = new List<int>();
            foreach (var p in parts)
            {
                int o = g.pos.Count;
                g.pos.AddRange(p.pos);
                for (int i = 0; i < p.pos.Count; i++) g.nrm.Add(i < p.nrm.Count ? p.nrm[i] : Vector3.zero);
                for (int i = 0; i < p.pos.Count; i++) g.uv.Add(i < p.uv.Count ? p.uv[i] : Vector2.zero);
                if (hasCol) g.col.AddRange(p.col);
                if (indexed) foreach (int i in p.idx) g.idx.Add(i + o);
            }
            return g;
        }

        /// scenery.js merge(): every part non-indexed, normals computed where missing, colour kept
        /// only when every part has it.
        public static Geo MergeNonIndexed(IList<Geo> parts)
        {
            var list = new List<Geo>(parts.Count);
            foreach (var p in parts)
            {
                var q = p.idx != null ? p.ToNonIndexed() : p.Clone();
                if (q.nrm.Count < q.pos.Count) q.ComputeVertexNormals();
                list.Add(q);
            }
            return Merge(list);
        }

        /// Copies of g placed with each matrix (an InstancedMesh baked into one mesh). Instance
        /// colours (linear, multiplied with the vertex colour like three's instanceColor) are optional.
        public static Geo MergeInstances(Geo g, IList<Matrix4x4> mats, IList<Color> colors)
        {
            var parts = new List<Geo>(mats.Count);
            for (int i = 0; i < mats.Count; i++)
            {
                var c = g.Clone().ApplyMatrix(mats[i]);
                if (colors != null)
                {
                    Color k = colors[i];
                    if (c.col == null) c.Colored(k);
                    else for (int v = 0; v < c.col.Count; v++) c.col[v] = c.col[v] * k;
                }
                parts.Add(c);
            }
            return Merge(parts);
        }

        // ---------------------------------------------------------------- Unity mesh

        /// The Unity mesh of this geometry (built once; the geometry is used as is, in three space).
        /// uvOverride replaces the UVs (palette colours).
        public Mesh ToMesh(string name = "geo", List<Vector2> uvOverride = null)
        {
            if (mesh != null && uvOverride == null) return mesh;
            var m = new Mesh { name = name };
            int n = pos.Count;
            if (n > 65535 || (idx != null && idx.Count > 0 && n > 65535)) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(pos);
            if (nrm.Count == n) m.SetNormals(nrm);
            var u = uvOverride ?? uv;
            if (u.Count == n) m.SetUVs(0, u);
            if (lines)
            {
                if (idx != null) m.SetIndices(idx, MeshTopology.Lines, 0);
                else m.SetIndices(Seq(n - (n & 1)), MeshTopology.Lines, 0);
            }
            else
            {
                if (idx != null) m.SetTriangles(idx, 0);
                else m.SetTriangles(Seq(n - n % 3), 0);
            }
            m.RecalculateBounds();
            if (uvOverride == null) { mesh = m; if (shared) SharedMeshes.Add(m); }
            return m;
        }

        public void ForgetMesh() { mesh = null; }

        /// meshes of shared geometry (never destroyed with an object)
        public static readonly HashSet<Mesh> SharedMeshes = new HashSet<Mesh>();

        static List<int> Seq(int n)
        {
            var l = new List<int>(n);
            for (int i = 0; i < n; i++) l.Add(i);
            return l;
        }

        // ================================================================ three.js geometries

        /// BoxGeometry(width, height, depth) with 1 segment per side
        public static Geo Box(float width = 1, float height = 1, float depth = 1)
        {
            var g = new Geo { idx = new List<int>() };
            int numberOfVertices = 0;
            void BuildPlane(int u, int v, int w, float udir, float vdir, float wd, float ht, float dp)
            {
                float widthHalf = wd / 2, heightHalf = ht / 2, depthHalf = dp / 2;
                for (int iy = 0; iy < 2; iy++)
                {
                    float y = iy * ht - heightHalf;
                    for (int ix = 0; ix < 2; ix++)
                    {
                        float x = ix * wd - widthHalf;
                        var vec = new Vector3();
                        vec[u] = x * udir; vec[v] = y * vdir; vec[w] = depthHalf;
                        g.pos.Add(vec);
                        var nn = new Vector3();
                        nn[w] = dp > 0 ? 1 : -1;
                        g.nrm.Add(nn);
                        g.uv.Add(new Vector2(ix, 1 - iy));
                    }
                }
                int a = numberOfVertices, b = numberOfVertices + 2, c = numberOfVertices + 3, d = numberOfVertices + 1;
                g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                numberOfVertices += 4;
            }
            const int X = 0, Y = 1, Z = 2;
            BuildPlane(Z, Y, X, -1, -1, depth, height, width);   // px
            BuildPlane(Z, Y, X, 1, -1, depth, height, -width);   // nx
            BuildPlane(X, Z, Y, 1, 1, width, depth, height);     // py
            BuildPlane(X, Z, Y, 1, -1, width, depth, -height);   // ny
            BuildPlane(X, Y, Z, 1, -1, width, height, depth);    // pz
            BuildPlane(X, Y, Z, -1, -1, width, height, -depth);  // nz
            return g;
        }

        /// CylinderGeometry(radiusTop, radiusBottom, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength)
        public static Geo Cylinder(float radiusTop = 1, float radiusBottom = 1, float height = 1, int radialSegments = 32,
            int heightSegments = 1, bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            var g = new Geo { idx = new List<int>() };
            int index = 0;
            var indexArray = new List<int[]>();
            float halfHeight = height / 2;
            double slope = (radiusBottom - radiusTop) / (double)height;
            for (int y = 0; y <= heightSegments; y++)
            {
                var row = new int[radialSegments + 1];
                double v = y / (double)heightSegments;
                double radius = v * (radiusBottom - radiusTop) + radiusTop;
                for (int x = 0; x <= radialSegments; x++)
                {
                    double u = x / (double)radialSegments;
                    double theta = u * thetaLength + thetaStart;
                    double sin = Math.Sin(theta), cos = Math.Cos(theta);
                    g.pos.Add(new Vector3((float)(radius * sin), (float)(-v * height + halfHeight), (float)(radius * cos)));
                    g.nrm.Add(Norm(new Vector3((float)sin, (float)slope, (float)cos)));
                    g.uv.Add(new Vector2((float)u, (float)(1 - v)));
                    row[x] = index++;
                }
                indexArray.Add(row);
            }
            for (int x = 0; x < radialSegments; x++)
                for (int y = 0; y < heightSegments; y++)
                {
                    int a = indexArray[y][x], b = indexArray[y + 1][x], c = indexArray[y + 1][x + 1], d = indexArray[y][x + 1];
                    if (radiusTop > 0) { g.idx.Add(a); g.idx.Add(b); g.idx.Add(d); }
                    if (radiusBottom > 0) { g.idx.Add(b); g.idx.Add(c); g.idx.Add(d); }
                }
            if (!openEnded)
            {
                if (radiusTop > 0) Cap(true);
                if (radiusBottom > 0) Cap(false);
            }
            return g;

            void Cap(bool top)
            {
                int centerIndexStart = index;
                float radius = top ? radiusTop : radiusBottom;
                float sign = top ? 1 : -1;
                for (int x = 1; x <= radialSegments; x++)
                {
                    g.pos.Add(new Vector3(0, halfHeight * sign, 0));
                    g.nrm.Add(new Vector3(0, sign, 0));
                    g.uv.Add(new Vector2(0.5f, 0.5f));
                    index++;
                }
                int centerIndexEnd = index;
                for (int x = 0; x <= radialSegments; x++)
                {
                    double u = x / (double)radialSegments;
                    double theta = u * thetaLength + thetaStart;
                    double cos = Math.Cos(theta), sin = Math.Sin(theta);
                    g.pos.Add(new Vector3((float)(radius * sin), halfHeight * sign, (float)(radius * cos)));
                    g.nrm.Add(new Vector3(0, sign, 0));
                    g.uv.Add(new Vector2((float)(cos * 0.5 + 0.5), (float)(sin * 0.5 * sign + 0.5)));
                    index++;
                }
                for (int x = 0; x < radialSegments; x++)
                {
                    int c = centerIndexStart + x, i = centerIndexEnd + x;
                    if (top) { g.idx.Add(i); g.idx.Add(i + 1); g.idx.Add(c); }
                    else { g.idx.Add(i + 1); g.idx.Add(i); g.idx.Add(c); }
                }
            }
        }

        /// Regular n-sided prism around +Y, centred, with flat (faceted) sides and caps. 'apothem' is the
        /// centre-to-face distance; one face is centred on +Z (for n = 8 also on ±X and the diagonals).
        public static Geo Prism(int sides, float apothem, float height, bool openEnded = false)
        {
            sides = Math.Max(3, sides);
            float r = apothem / Mathf.Cos(Mathf.PI / sides);
            return Cylinder(r, r, height, sides, 1, openEnded, Mathf.PI / sides).Faceted();
        }

        /// Thick-walled tube around +Y, centred (a ring / flange / bushing): outer and inner walls plus
        /// flat annular ends, with hard edges between them.
        public static Geo Tube(float innerRadius, float outerRadius, float height, int radialSegments = 32)
        {
            float h = height / 2;
            return Merge(new[]
            {
                Cylinder(outerRadius, outerRadius, height, radialSegments, 1, true),
                Cylinder(innerRadius, innerRadius, height, radialSegments, 1, true).Flipped(),
                Ring(innerRadius, outerRadius, radialSegments).RotateX(-Mathf.PI / 2).Translate(0, h, 0),
                Ring(innerRadius, outerRadius, radialSegments).RotateX(Mathf.PI / 2).Translate(0, -h, 0),
            });
        }

        /// ConeGeometry(radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength)
        public static Geo Cone(float radius = 1, float height = 1, int radialSegments = 32, int heightSegments = 1,
            bool openEnded = false, float thetaStart = 0, float thetaLength = Mathf.PI * 2) =>
            Cylinder(0, radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength);

        /// SphereGeometry(radius, widthSegments, heightSegments, phiStart, phiLength, thetaStart, thetaLength)
        public static Geo Sphere(float radius = 1, int widthSegments = 32, int heightSegments = 16, float phiStart = 0,
            float phiLength = Mathf.PI * 2, float thetaStart = 0, float thetaLength = Mathf.PI)
        {
            widthSegments = Math.Max(3, widthSegments);
            heightSegments = Math.Max(2, heightSegments);
            double thetaEnd = Math.Min((double)thetaStart + thetaLength, Math.PI);
            // the JS compares thetaEnd with Math.PI exactly; float parameters of PI land within 1e-6
            bool endsAtPole = Math.Abs(thetaEnd - Math.PI) < 1e-6;
            var g = new Geo { idx = new List<int>() };
            int index = 0;
            var grid = new List<int[]>();
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var row = new int[widthSegments + 1];
                double v = iy / (double)heightSegments;
                double uOffset = 0;
                if (iy == 0 && thetaStart == 0) uOffset = 0.5 / widthSegments;
                else if (iy == heightSegments && endsAtPole) uOffset = -0.5 / widthSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    double u = ix / (double)widthSegments;
                    double th = thetaStart + v * thetaLength, ph = phiStart + u * phiLength;
                    var p = new Vector3((float)(-radius * Math.Cos(ph) * Math.Sin(th)), (float)(radius * Math.Cos(th)), (float)(radius * Math.Sin(ph) * Math.Sin(th)));
                    g.pos.Add(p);
                    g.nrm.Add(Norm(p));
                    g.uv.Add(new Vector2((float)(u + uOffset), (float)(1 - v)));
                    row[ix] = index++;
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < heightSegments; iy++)
                for (int ix = 0; ix < widthSegments; ix++)
                {
                    int a = grid[iy][ix + 1], b = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                    if (iy != 0 || thetaStart > 0) { g.idx.Add(a); g.idx.Add(b); g.idx.Add(d); }
                    if (iy != heightSegments - 1 || !endsAtPole) { g.idx.Add(b); g.idx.Add(c); g.idx.Add(d); }
                }
            return g;
        }

        /// TorusGeometry(radius, tube, radialSegments, tubularSegments, arc)
        public static Geo Torus(float radius = 1, float tube = 0.4f, int radialSegments = 12, int tubularSegments = 48, float arc = Mathf.PI * 2)
        {
            var g = new Geo { idx = new List<int>() };
            for (int j = 0; j <= radialSegments; j++)
                for (int i = 0; i <= tubularSegments; i++)
                {
                    double u = i / (double)tubularSegments * arc;
                    double v = j / (double)radialSegments * Math.PI * 2;
                    var p = new Vector3((float)((radius + tube * Math.Cos(v)) * Math.Cos(u)), (float)((radius + tube * Math.Cos(v)) * Math.Sin(u)), (float)(tube * Math.Sin(v)));
                    g.pos.Add(p);
                    var c = new Vector3((float)(radius * Math.Cos(u)), (float)(radius * Math.Sin(u)), 0);
                    g.nrm.Add(Norm(p - c));
                    g.uv.Add(new Vector2(i / (float)tubularSegments, j / (float)radialSegments));
                }
            for (int j = 1; j <= radialSegments; j++)
                for (int i = 1; i <= tubularSegments; i++)
                {
                    int a = (tubularSegments + 1) * j + i - 1, b = (tubularSegments + 1) * (j - 1) + i - 1;
                    int c = (tubularSegments + 1) * (j - 1) + i, d = (tubularSegments + 1) * j + i;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            return g;
        }

        /// CircleGeometry(radius, segments, thetaStart, thetaLength) in the XY plane facing +Z
        public static Geo Circle(float radius = 1, int segments = 32, float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            segments = Math.Max(3, segments);
            var g = new Geo { idx = new List<int>() };
            g.pos.Add(Vector3.zero); g.nrm.Add(Vector3.forward); g.uv.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                double seg = thetaStart + s / (double)segments * thetaLength;
                float x = (float)(radius * Math.Cos(seg)), y = (float)(radius * Math.Sin(seg));
                g.pos.Add(new Vector3(x, y, 0));
                g.nrm.Add(Vector3.forward);
                g.uv.Add(new Vector2((x / radius + 1) / 2, (y / radius + 1) / 2));
            }
            for (int i = 1; i <= segments; i++) { g.idx.Add(i); g.idx.Add(i + 1); g.idx.Add(0); }
            return g;
        }

        /// PlaneGeometry(width, height, widthSegments, heightSegments) in the XY plane facing +Z
        public static Geo Plane(float width = 1, float height = 1, int widthSegments = 1, int heightSegments = 1)
        {
            var g = new Geo { idx = new List<int>() };
            float wh = width / 2, hh = height / 2;
            int gridX = widthSegments, gridY = heightSegments, gridX1 = gridX + 1, gridY1 = gridY + 1;
            float sw = width / gridX, sh = height / gridY;
            g.pos.Capacity = gridX1 * gridY1; g.nrm.Capacity = gridX1 * gridY1; g.uv.Capacity = gridX1 * gridY1;
            for (int iy = 0; iy < gridY1; iy++)
            {
                float y = iy * sh - hh;
                for (int ix = 0; ix < gridX1; ix++)
                {
                    float x = ix * sw - wh;
                    g.pos.Add(new Vector3(x, -y, 0));
                    g.nrm.Add(Vector3.forward);
                    g.uv.Add(new Vector2(ix / (float)gridX, 1 - iy / (float)gridY));
                }
            }
            g.idx.Capacity = gridX * gridY * 6;
            for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = ix + gridX1 * iy, b = ix + gridX1 * (iy + 1), c = ix + 1 + gridX1 * (iy + 1), d = ix + 1 + gridX1 * iy;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            return g;
        }

        /// RingGeometry(innerRadius, outerRadius, thetaSegments, phiSegments, thetaStart, thetaLength)
        public static Geo Ring(float innerRadius = 0.5f, float outerRadius = 1, int thetaSegments = 32, int phiSegments = 1,
            float thetaStart = 0, float thetaLength = Mathf.PI * 2)
        {
            thetaSegments = Math.Max(3, thetaSegments);
            phiSegments = Math.Max(1, phiSegments);
            var g = new Geo { idx = new List<int>() };
            double radius = innerRadius, step = (outerRadius - innerRadius) / (double)phiSegments;
            for (int j = 0; j <= phiSegments; j++)
            {
                for (int i = 0; i <= thetaSegments; i++)
                {
                    double seg = thetaStart + i / (double)thetaSegments * thetaLength;
                    float x = (float)(radius * Math.Cos(seg)), y = (float)(radius * Math.Sin(seg));
                    g.pos.Add(new Vector3(x, y, 0));
                    g.nrm.Add(Vector3.forward);
                    g.uv.Add(new Vector2((x / outerRadius + 1) / 2, (y / outerRadius + 1) / 2));
                }
                radius += step;
            }
            for (int j = 0; j < phiSegments; j++)
            {
                int level = j * (thetaSegments + 1);
                for (int i = 0; i < thetaSegments; i++)
                {
                    int s = i + level, a = s, b = s + thetaSegments + 1, c = s + thetaSegments + 2, d = s + 1;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(b); g.idx.Add(c); g.idx.Add(d);
                }
            }
            return g;
        }

        /// LatheGeometry(points, segments, phiStart, phiLength) around +Y
        public static Geo Lathe(IList<Vector2> points, int segments = 12, float phiStart = 0, float phiLength = Mathf.PI * 2)
        {
            phiLength = Mathf.Clamp(phiLength, 0, Mathf.PI * 2);
            var g = new Geo { idx = new List<int>() };
            int n = points.Count;
            var init = new Vector3[n];
            Vector3 prev = Vector3.zero, cur;
            for (int j = 0; j < n; j++)
            {
                if (j == 0)
                {
                    float dx = points[1].x - points[0].x, dy = points[1].y - points[0].y;
                    var nn = new Vector3(dy, -dx, 0);
                    prev = nn;
                    init[j] = Norm(nn);
                }
                else if (j == n - 1) init[j] = prev;
                else
                {
                    float dx = points[j + 1].x - points[j].x, dy = points[j + 1].y - points[j].y;
                    var nn = new Vector3(dy, -dx, 0);
                    cur = nn;
                    init[j] = Norm(nn + prev);
                    prev = cur;
                }
            }
            double inv = 1.0 / segments;
            for (int i = 0; i <= segments; i++)
            {
                double phi = phiStart + i * inv * phiLength;
                float sin = (float)Math.Sin(phi), cos = (float)Math.Cos(phi);
                for (int j = 0; j < n; j++)
                {
                    g.pos.Add(new Vector3(points[j].x * sin, points[j].y, points[j].x * cos));
                    g.uv.Add(new Vector2(i / (float)segments, j / (float)(n - 1)));
                    g.nrm.Add(new Vector3(init[j].x * sin, init[j].y, init[j].x * cos));
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < n - 1; j++)
                {
                    int b0 = j + i * n, a = b0, b = b0 + n, c = b0 + n + 1, d = b0 + 1;
                    g.idx.Add(a); g.idx.Add(b); g.idx.Add(d);
                    g.idx.Add(c); g.idx.Add(d); g.idx.Add(b);
                }
            return g;
        }

        /// CapsuleGeometry(radius, length, capSegments, radialSegments): a lathe of two arcs (three r16x)
        public static Geo Capsule(float radius = 1, float length = 1, int capSegments = 4, int radialSegments = 8)
        {
            var path = new Path2();
            path.Absarc(0, -length / 2.0, radius, Math.PI * 1.5, 0, false);
            path.Absarc(0, length / 2.0, radius, 0, Math.PI * 0.5, false);
            var pts = path.GetPoints(capSegments);
            var v = new List<Vector2>(pts.Count);
            foreach (var p in pts) v.Add(new Vector2((float)p.x, (float)p.y));
            return Lathe(v, radialSegments);
        }

        /// IcosahedronGeometry(radius, detail): non-indexed positions with smooth normals (no UVs:
        /// the port only uses it for the rocks, which have no texture)
        public static Geo Icosahedron(float radius = 1, int detail = 0)
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            double[] vtx = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
            int[] ind = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            var buf = new List<Vector3d>();
            Vector3d V(int i) => new Vector3d(vtx[i * 3], vtx[i * 3 + 1], vtx[i * 3 + 2]);
            for (int i = 0; i < ind.Length; i += 3)
            {
                Vector3d a = V(ind[i]), b = V(ind[i + 1]), c = V(ind[i + 2]);
                int cols = detail + 1;
                var v = new Vector3d[cols + 1][];
                for (int ii = 0; ii <= cols; ii++)
                {
                    v[ii] = new Vector3d[cols - ii + 1];
                    Vector3d aj = Vector3d.Lerp(a, c, ii / (double)cols), bj = Vector3d.Lerp(b, c, ii / (double)cols);
                    int rows = cols - ii;
                    for (int j = 0; j <= rows; j++)
                        v[ii][j] = (j == 0 && ii == cols) ? aj : Vector3d.Lerp(aj, bj, j / (double)rows);
                }
                for (int ii = 0; ii < cols; ii++)
                    for (int j = 0; j < 2 * (cols - ii) - 1; j++)
                    {
                        int k = j / 2;
                        if (j % 2 == 0) { buf.Add(v[ii][k + 1]); buf.Add(v[ii + 1][k]); buf.Add(v[ii][k]); }
                        else { buf.Add(v[ii][k + 1]); buf.Add(v[ii + 1][k + 1]); buf.Add(v[ii + 1][k]); }
                    }
            }
            var g = new Geo();
            foreach (var p in buf)
            {
                var q = p.Normalized() * radius;
                g.pos.Add(q.ToVector3());
                g.nrm.Add(p.Normalized().ToVector3());
            }
            if (detail == 0) g.ComputeVertexNormals();
            return g;
        }

        /// ExtrudeGeometry(shape, options) without extrudePath: non-indexed with face normals
        /// (three's computeVertexNormals on a non-indexed geometry). UVs are left at 0 (no extruded
        /// part of the game has a texture).
        public static Geo Extrude(Shape2 shape, ExtrudeOptions o)
        {
            int curveSegments = o.curveSegments, steps = o.steps;
            double depth = o.depth;
            bool bevelEnabled = o.bevelEnabled;
            double bevelThickness = o.bevelThickness, bevelSize = o.bevelSize ?? (o.bevelThickness - 0.1), bevelOffset = o.bevelOffset;
            int bevelSegments = o.bevelSegments;
            if (!bevelEnabled) { bevelSegments = 0; bevelThickness = 0; bevelSize = 0; bevelOffset = 0; }

            var vertices = shape.GetPoints(curveSegments);
            var holes = shape.GetPointsHoles(curveSegments);
            bool reverse = !ShapeUtil.IsClockWise(vertices);
            if (reverse)
            {
                vertices.Reverse();
                for (int h = 0; h < holes.Count; h++) if (ShapeUtil.IsClockWise(holes[h])) holes[h].Reverse();
            }
            var faces = ShapeUtil.TriangulateShape(vertices, holes);
            var contour = vertices;
            var all = new List<P2>(vertices);
            foreach (var hole in holes) all.AddRange(hole);
            int vlen = all.Count, flen = faces.Count / 3;

            var placeholder = new List<Vector3d>();
            void Vtx(double x, double y, double z) => placeholder.Add(new Vector3d(x, y, z));
            P2 ScalePt2(P2 pt, P2 vec, double size) => new P2(pt.x + vec.x * size, pt.y + vec.y * size);

            var contourMovements = new P2[contour.Count];
            for (int i = 0, il = contour.Count, j = il - 1, k = i + 1; i < il; i++, j++, k++)
            {
                if (j == il) j = 0;
                if (k == il) k = 0;
                contourMovements[i] = BevelVec(contour[i], contour[j], contour[k]);
            }
            var holesMovements = new List<P2[]>();
            var verticesMovements = new List<P2>(contourMovements);
            foreach (var ahole in holes)
            {
                var m = new P2[ahole.Count];
                for (int i = 0, il = ahole.Count, j = il - 1, k = i + 1; i < il; i++, j++, k++)
                {
                    if (j == il) j = 0;
                    if (k == il) k = 0;
                    m[i] = BevelVec(ahole[i], ahole[j], ahole[k]);
                }
                holesMovements.Add(m);
                verticesMovements.AddRange(m);
            }
            for (int b = 0; b < bevelSegments; b++)
            {
                double t = b / (double)bevelSegments;
                double z = bevelThickness * Math.Cos(t * Math.PI / 2);
                double bs0 = bevelSize * Math.Sin(t * Math.PI / 2) + bevelOffset;
                for (int i = 0; i < contour.Count; i++) { var v = ScalePt2(contour[i], contourMovements[i], bs0); Vtx(v.x, v.y, -z); }
                for (int h = 0; h < holes.Count; h++)
                    for (int i = 0; i < holes[h].Count; i++) { var v = ScalePt2(holes[h][i], holesMovements[h][i], bs0); Vtx(v.x, v.y, -z); }
            }
            double bs = bevelSize + bevelOffset;
            for (int i = 0; i < vlen; i++) { var v = bevelEnabled ? ScalePt2(all[i], verticesMovements[i], bs) : all[i]; Vtx(v.x, v.y, 0); }
            for (int s = 1; s <= steps; s++)
                for (int i = 0; i < vlen; i++) { var v = bevelEnabled ? ScalePt2(all[i], verticesMovements[i], bs) : all[i]; Vtx(v.x, v.y, depth / steps * s); }
            for (int b = bevelSegments - 1; b >= 0; b--)
            {
                double t = b / (double)bevelSegments;
                double z = bevelThickness * Math.Cos(t * Math.PI / 2);
                double bs0 = bevelSize * Math.Sin(t * Math.PI / 2) + bevelOffset;
                for (int i = 0; i < contour.Count; i++) { var v = ScalePt2(contour[i], contourMovements[i], bs0); Vtx(v.x, v.y, depth + z); }
                for (int h = 0; h < holes.Count; h++)
                    for (int i = 0; i < holes[h].Count; i++) { var v = ScalePt2(holes[h][i], holesMovements[h][i], bs0); Vtx(v.x, v.y, depth + z); }
            }

            var g = new Geo();
            void Add(int i) => g.pos.Add(placeholder[i].ToVector3());
            void F3(int a, int b2, int c) { Add(a); Add(b2); Add(c); }
            void F4(int a, int b2, int c, int d) { Add(a); Add(b2); Add(d); Add(b2); Add(c); Add(d); }

            // lids
            if (bevelEnabled)
            {
                int offset = 0;
                for (int i = 0; i < flen; i++) F3(faces[i * 3 + 2] + offset, faces[i * 3 + 1] + offset, faces[i * 3] + offset);
                offset = vlen * (steps + bevelSegments * 2);
                for (int i = 0; i < flen; i++) F3(faces[i * 3] + offset, faces[i * 3 + 1] + offset, faces[i * 3 + 2] + offset);
            }
            else
            {
                for (int i = 0; i < flen; i++) F3(faces[i * 3 + 2], faces[i * 3 + 1], faces[i * 3]);
                for (int i = 0; i < flen; i++) F3(faces[i * 3] + vlen * steps, faces[i * 3 + 1] + vlen * steps, faces[i * 3 + 2] + vlen * steps);
            }
            // side walls
            int layeroffset = 0;
            void Sidewalls(List<P2> c)
            {
                int i = c.Count;
                while (--i >= 0)
                {
                    int j = i, k = i - 1;
                    if (k < 0) k = c.Count - 1;
                    for (int s = 0, sl = steps + bevelSegments * 2; s < sl; s++)
                    {
                        int slen1 = vlen * s, slen2 = vlen * (s + 1);
                        F4(layeroffset + j + slen1, layeroffset + k + slen1, layeroffset + k + slen2, layeroffset + j + slen2);
                    }
                }
            }
            Sidewalls(contour);
            layeroffset += contour.Count;
            foreach (var ahole in holes) { Sidewalls(ahole); layeroffset += ahole.Count; }
            g.EnsureUV();
            g.ComputeVertexNormals();
            return g;
        }

        static P2 BevelVec(P2 inPt, P2 inPrev, P2 inNext)
        {
            double vtx, vty, shrink;
            double vpx = inPt.x - inPrev.x, vpy = inPt.y - inPrev.y;
            double vnx = inNext.x - inPt.x, vny = inNext.y - inPt.y;
            double vpl2 = vpx * vpx + vpy * vpy;
            double collinear0 = vpx * vny - vpy * vnx;
            if (Math.Abs(collinear0) > 2.220446049250313e-16) // Number.EPSILON
            {
                double vpl = Math.Sqrt(vpl2), vnl = Math.Sqrt(vnx * vnx + vny * vny);
                double ppx = inPrev.x - vpy / vpl, ppy = inPrev.y + vpx / vpl;
                double pnx = inNext.x - vny / vnl, pny = inNext.y + vnx / vnl;
                double sf = ((pnx - ppx) * vny - (pny - ppy) * vnx) / (vpx * vny - vpy * vnx);
                vtx = ppx + vpx * sf - inPt.x;
                vty = ppy + vpy * sf - inPt.y;
                double l2 = vtx * vtx + vty * vty;
                if (l2 <= 2) return new P2(vtx, vty);
                shrink = Math.Sqrt(l2 / 2);
            }
            else
            {
                const double EPS = 2.220446049250313e-16;
                bool eq = false;
                if (vpx > EPS) { if (vnx > EPS) eq = true; }
                else if (vpx < -EPS) { if (vnx < -EPS) eq = true; }
                else if (Math.Sign(vpy) == Math.Sign(vny)) eq = true;
                if (eq) { vtx = -vpy; vty = vpx; shrink = Math.Sqrt(vpl2); }
                else { vtx = vpx; vty = vpy; shrink = Math.Sqrt(vpl2 / 2); }
            }
            return new P2(vtx / shrink, vty / shrink);
        }
    }

    public sealed class ExtrudeOptions
    {
        public int curveSegments = 12, steps = 1, bevelSegments = 3;
        public double depth = 1, bevelThickness = 0.2, bevelOffset = 0;
        public double? bevelSize;     // null = bevelThickness - 0.1 (three's default)
        public bool bevelEnabled = true;
    }

    /// double-precision 2D point (three Vector2 in JS numbers)
    public struct P2
    {
        public double x, y;
        public P2(double x, double y) { this.x = x; this.y = y; }
        public bool Equals(P2 o) => x == o.x && y == o.y;
    }

    /// double-precision 3D point for geometry construction
    public struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3d Lerp(Vector3d a, Vector3d b, double t) => new Vector3d(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public Vector3d Normalized() { double l = Math.Sqrt(x * x + y * y + z * z); if (l == 0) l = 1; return new Vector3d(x / l, y / l, z / l); }
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
    }

    // ==================================================================== Path / Shape (three extras/core)

    /// three Path: lines, quadratic curves and arcs; GetPoints like CurvePath.getPoints.
    public class Path2
    {
        abstract class Curve
        {
            public abstract P2 GetPoint(double t);
            public abstract int Resolution(int divisions);
        }
        sealed class LineC : Curve
        {
            readonly P2 a, b;
            public LineC(P2 a, P2 b) { this.a = a; this.b = b; }
            public override P2 GetPoint(double t) => t == 1 ? b : new P2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
            public override int Resolution(int d) => 1;
        }
        sealed class QuadC : Curve
        {
            readonly P2 p0, p1, p2;
            public QuadC(P2 a, P2 b, P2 c) { p0 = a; p1 = b; p2 = c; }
            static double Q(double t, double a, double b, double c) { double k = 1 - t; return k * k * a + 2 * (1 - t) * t * b + t * t * c; }
            public override P2 GetPoint(double t) => new P2(Q(t, p0.x, p1.x, p2.x), Q(t, p0.y, p1.y, p2.y));
            public override int Resolution(int d) => d;
        }
        sealed class EllipseC : Curve
        {
            readonly double ax, ay, rx, ry, a0, a1;
            readonly bool cw;
            public EllipseC(double x, double y, double rx, double ry, double s, double e, bool cw) { ax = x; ay = y; this.rx = rx; this.ry = ry; a0 = s; a1 = e; this.cw = cw; }
            public override P2 GetPoint(double t)
            {
                const double twoPi = Math.PI * 2, EPS = 2.220446049250313e-16;
                double delta = a1 - a0;
                bool same = Math.Abs(delta) < EPS;
                while (delta < 0) delta += twoPi;
                while (delta > twoPi) delta -= twoPi;
                if (delta < EPS) delta = same ? 0 : twoPi;
                if (cw && !same) delta = delta == twoPi ? -twoPi : delta - twoPi;
                double angle = a0 + t * delta;
                return new P2(ax + rx * Math.Cos(angle), ay + ry * Math.Sin(angle));
            }
            public override int Resolution(int d) => d * 2;
        }

        readonly List<Curve> curves = new List<Curve>();
        P2 current;

        public Path2 MoveTo(double x, double y) { current = new P2(x, y); return this; }
        public Path2 LineTo(double x, double y) { curves.Add(new LineC(current, new P2(x, y))); current = new P2(x, y); return this; }
        public Path2 QuadraticCurveTo(double cx, double cy, double x, double y) { curves.Add(new QuadC(current, new P2(cx, cy), new P2(x, y))); current = new P2(x, y); return this; }
        public Path2 Absarc(double x, double y, double r, double a0, double a1, bool clockwise = false)
        {
            var c = new EllipseC(x, y, r, r, a0, a1, clockwise);
            if (curves.Count > 0)
            {
                var first = c.GetPoint(0);
                if (!first.Equals(current)) LineTo(first.x, first.y);
            }
            curves.Add(c);
            current = c.GetPoint(1);
            return this;
        }
        public Path2 ClosePath()
        {
            if (curves.Count == 0) return this;
            P2 s = curves[0].GetPoint(0), e = curves[curves.Count - 1].GetPoint(1);
            if (!s.Equals(e)) curves.Add(new LineC(e, s));
            return this;
        }

        public List<P2> GetPoints(int divisions = 12)
        {
            var pts = new List<P2>();
            bool has = false;
            P2 last = default;
            foreach (var c in curves)
            {
                int res = c.Resolution(divisions);
                for (int d = 0; d <= res; d++)
                {
                    var p = c.GetPoint(d / (double)res);
                    if (has && last.Equals(p)) continue;
                    pts.Add(p);
                    last = p;
                    has = true;
                }
            }
            return pts;
        }
    }

    public sealed class Shape2 : Path2
    {
        public readonly List<Path2> holes = new List<Path2>();
        public List<List<P2>> GetPointsHoles(int divisions)
        {
            var l = new List<List<P2>>(holes.Count);
            foreach (var h in holes) l.Add(h.GetPoints(divisions));
            return l;
        }
    }

    // ==================================================================== ShapeUtils + Earcut

    public static class ShapeUtil
    {
        public static double Area(List<P2> c)
        {
            int n = c.Count;
            double a = 0;
            for (int p = n - 1, q = 0; q < n; p = q++) a += c[p].x * c[q].y - c[q].x * c[p].y;
            return a * 0.5;
        }

        public static bool IsClockWise(List<P2> pts) => Area(pts) < 0;

        /// ShapeUtils.triangulateShape: flat list of triangle indices into contour + holes
        public static List<int> TriangulateShape(List<P2> contour, List<List<P2>> holes)
        {
            RemoveDupEndPts(contour);
            var data = new List<double>();
            foreach (var p in contour) { data.Add(p.x); data.Add(p.y); }
            var holeIndices = new List<int>();
            int holeIndex = contour.Count;
            foreach (var h in holes) RemoveDupEndPts(h);
            foreach (var h in holes)
            {
                holeIndices.Add(holeIndex);
                holeIndex += h.Count;
                foreach (var p in h) { data.Add(p.x); data.Add(p.y); }
            }
            return Earcut.Triangulate(data, holeIndices);
        }

        static void RemoveDupEndPts(List<P2> pts)
        {
            int l = pts.Count;
            if (l > 2 && pts[l - 1].Equals(pts[0])) pts.RemoveAt(l - 1);
        }
    }

    /// three's Earcut (mapbox earcut 2.2). The z-order hashing branch (used above 80 points) is
    /// replaced by the plain ear test, which gives a valid triangulation of the same polygon.
    public static class Earcut
    {
        sealed class Node
        {
            public int i; public double x, y;
            public Node prev, next;
            public bool steiner;
            public Node(int i, double x, double y) { this.i = i; this.x = x; this.y = y; }
        }

        public static List<int> Triangulate(List<double> data, List<int> holeIndices)
        {
            const int dim = 2;
            bool hasHoles = holeIndices != null && holeIndices.Count > 0;
            int outerLen = hasHoles ? holeIndices[0] * dim : data.Count;
            Node outer = LinkedList(data, 0, outerLen, true);
            var tris = new List<int>();
            if (outer == null || outer.next == outer.prev) return tris;
            if (hasHoles) outer = EliminateHoles(data, holeIndices, outer);
            EarcutLinked(outer, tris, 0);
            return tris;
        }

        static Node LinkedList(List<double> data, int start, int end, bool clockwise)
        {
            Node last = null;
            if (clockwise == (SignedArea(data, start, end) > 0))
                for (int i = start; i < end; i += 2) last = InsertNode(i, data[i], data[i + 1], last);
            else
                for (int i = end - 2; i >= start; i -= 2) last = InsertNode(i, data[i], data[i + 1], last);
            if (last != null && Equals(last, last.next)) { RemoveNode(last); last = last.next; }
            return last;
        }

        static Node FilterPoints(Node start, Node end = null)
        {
            if (start == null) return start;
            if (end == null) end = start;
            Node p = start;
            bool again;
            do
            {
                again = false;
                if (!p.steiner && (Equals(p, p.next) || Area(p.prev, p, p.next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.prev;
                    if (p == p.next) break;
                    again = true;
                }
                else p = p.next;
            } while (again || p != end);
            return end;
        }

        static void EarcutLinked(Node ear, List<int> tris, int pass)
        {
            if (ear == null) return;
            Node stop = ear;
            while (ear.prev != ear.next)
            {
                Node prev = ear.prev, next = ear.next;
                if (IsEar(ear))
                {
                    tris.Add(prev.i / 2); tris.Add(ear.i / 2); tris.Add(next.i / 2);
                    RemoveNode(ear);
                    ear = next.next;
                    stop = next.next;
                    continue;
                }
                ear = next;
                if (ear == stop)
                {
                    if (pass == 0) EarcutLinked(FilterPoints(ear), tris, 1);
                    else if (pass == 1) { ear = CureLocalIntersections(FilterPoints(ear), tris); EarcutLinked(ear, tris, 2); }
                    else if (pass == 2) SplitEarcut(ear, tris);
                    break;
                }
            }
        }

        static bool IsEar(Node ear)
        {
            Node a = ear.prev, b = ear, c = ear.next;
            if (Area(a, b, c) >= 0) return false;
            double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;
            double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy));
            double x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
            Node p = c.next;
            while (p != a)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && PointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.next;
            }
            return true;
        }

        static Node CureLocalIntersections(Node start, List<int> tris)
        {
            Node p = start;
            do
            {
                Node a = p.prev, b = p.next.next;
                if (!Equals(a, b) && Intersects(a, p, p.next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    tris.Add(a.i / 2); tris.Add(p.i / 2); tris.Add(b.i / 2);
                    RemoveNode(p);
                    RemoveNode(p.next);
                    p = start = b;
                }
                p = p.next;
            } while (p != start);
            return FilterPoints(p);
        }

        static void SplitEarcut(Node start, List<int> tris)
        {
            Node a = start;
            do
            {
                Node b = a.next.next;
                while (b != a.prev)
                {
                    if (a.i != b.i && IsValidDiagonal(a, b))
                    {
                        Node c = SplitPolygon(a, b);
                        a = FilterPoints(a, a.next);
                        c = FilterPoints(c, c.next);
                        EarcutLinked(a, tris, 0);
                        EarcutLinked(c, tris, 0);
                        return;
                    }
                    b = b.next;
                }
                a = a.next;
            } while (a != start);
        }

        static Node EliminateHoles(List<double> data, List<int> holeIndices, Node outer)
        {
            var queue = new List<Node>();
            for (int i = 0, len = holeIndices.Count; i < len; i++)
            {
                int start = holeIndices[i] * 2, end = i < len - 1 ? holeIndices[i + 1] * 2 : data.Count;
                Node list = LinkedList(data, start, end, false);
                if (list == list.next) list.steiner = true;
                queue.Add(GetLeftmost(list));
            }
            queue.Sort((a, b) => a.x.CompareTo(b.x));
            foreach (var h in queue) outer = EliminateHole(h, outer);
            return outer;
        }

        static Node EliminateHole(Node hole, Node outer)
        {
            Node bridge = FindHoleBridge(hole, outer);
            if (bridge == null) return outer;
            Node bridgeReverse = SplitPolygon(bridge, hole);
            FilterPoints(bridgeReverse, bridgeReverse.next);
            return FilterPoints(bridge, bridge.next);
        }

        static Node FindHoleBridge(Node hole, Node outer)
        {
            Node p = outer, m = null;
            double qx = double.NegativeInfinity, hx = hole.x, hy = hole.y;
            do
            {
                if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
                {
                    double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.x < p.next.x ? p : p.next;
                        if (x == hx) return m;
                    }
                }
                p = p.next;
            } while (p != outer);
            if (m == null) return null;
            Node stop = m;
            double mx = m.x, my = m.y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.x && p.x >= mx && hx != p.x && PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
                {
                    double tan = Math.Abs(hy - p.y) / (hx - p.x);
                    if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }
                p = p.next;
            } while (p != stop);
            return m;
        }

        static bool SectorContainsSector(Node m, Node p) => Area(m.prev, m, p.prev) < 0 && Area(p.next, m, m.next) < 0;

        static Node GetLeftmost(Node start)
        {
            Node p = start, left = start;
            do
            {
                if (p.x < left.x || (p.x == left.x && p.y < left.y)) left = p;
                p = p.next;
            } while (p != start);
            return left;
        }

        static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
            (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
            (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
            (bx - px) * (cy - py) >= (cx - px) * (by - py);

        static bool IsValidDiagonal(Node a, Node b) =>
            a.next.i != b.i && a.prev.i != b.i && !IntersectsPolygon(a, b) &&
            (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
             (Area(a.prev, a, b.prev) != 0 || Area(a, b.prev, b) != 0) ||
             Equals(a, b) && Area(a.prev, a, a.next) > 0 && Area(b.prev, b, b.next) > 0);

        static double Area(Node p, Node q, Node r) => (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        static bool Equals(Node a, Node b) => a.x == b.x && a.y == b.y;

        static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
            return false;
        }

        static bool OnSegment(Node p, Node q, Node r) =>
            q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);

        static int Sign(double n) => n > 0 ? 1 : n < 0 ? -1 : 0;

        static bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (p.i != a.i && p.next.i != a.i && p.i != b.i && p.next.i != b.i && Intersects(p, p.next, a, b)) return true;
                p = p.next;
            } while (p != a);
            return false;
        }

        static bool LocallyInside(Node a, Node b) =>
            Area(a.prev, a, a.next) < 0
                ? Area(a, b, a.next) >= 0 && Area(a, a.prev, b) >= 0
                : Area(a, b, a.prev) < 0 || Area(a, a.next, b) < 0;

        static bool MiddleInside(Node a, Node b)
        {
            Node p = a;
            bool inside = false;
            double px = (a.x + b.x) / 2, py = (a.y + b.y) / 2;
            do
            {
                if (((p.y > py) != (p.next.y > py)) && p.next.y != p.y && (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x))
                    inside = !inside;
                p = p.next;
            } while (p != a);
            return inside;
        }

        static Node SplitPolygon(Node a, Node b)
        {
            Node a2 = new Node(a.i, a.x, a.y), b2 = new Node(b.i, b.x, b.y), an = a.next, bp = b.prev;
            a.next = b; b.prev = a;
            a2.next = an; an.prev = a2;
            b2.next = a2; a2.prev = b2;
            bp.next = b2; b2.prev = bp;
            return b2;
        }

        static Node InsertNode(int i, double x, double y, Node last)
        {
            var p = new Node(i, x, y);
            if (last == null) { p.prev = p; p.next = p; }
            else { p.next = last.next; p.prev = last; last.next.prev = p; last.next = p; }
            return p;
        }

        static void RemoveNode(Node p) { p.next.prev = p.prev; p.prev.next = p.next; }

        static double SignedArea(List<double> data, int start, int end)
        {
            double sum = 0;
            for (int i = start, j = end - 2; i < end; i += 2) { sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]); j = i; }
            return sum;
        }
    }
}
