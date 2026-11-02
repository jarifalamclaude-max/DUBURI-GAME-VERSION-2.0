// A small three.js-like scene graph (Object3D / Group / Mesh / Line) that the render code builds in
// three.js space exactly like the JS, then turns into GameObjects under the mirrored World root
// (Realize). Building in memory first lets bots3d.js mergeStatic() be ported as is.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Duburi.Game
{
    public sealed class Node
    {
        public string name = "";
        public Vector3 position;
        public Vector3 scale = Vector3.one;
        Vector3 euler;              // three Euler 'XYZ' (radians)
        Quaternion quat = Quaternion.identity;
        public Geo geo;
        public Mat3 mat;
        public int renderOrder;
        public bool visible = true;
        public bool castShadow, receiveShadow;  // three defaults: false
        public bool isGroup;                    // THREE.Group (plain Object3D otherwise, when it has no geometry)
        public Node parent;
        public readonly List<Node> children = new List<Node>();
        /// the GameObject made by Realize
        public Transform transform;

        public static Node Group(string name = "") => new Node { name = name, isGroup = true };
        public static Node Object3D(string name = "") => new Node { name = name };
        public static Node Mesh(Geo g, Mat3 m, string name = "") => new Node { geo = g, mat = m, name = name };
        public static Node Line(Geo g, Mat3 m, string name = "") { g.lines = true; return new Node { geo = g, mat = m, name = name }; }

        // ---------------------------------------------------------------- rotation (three keeps Euler and quaternion in sync)

        public Quaternion quaternion
        {
            get => quat;
            set { quat = value; euler = EulerFromQuaternion(value); }
        }

        public Vector3 rotation
        {
            get => euler;
            set { euler = value; quat = Space.Euler(value.x, value.y, value.z); }
        }

        public float rotX { get => euler.x; set { euler.x = value; quat = Space.Euler(euler.x, euler.y, euler.z); } }
        public float rotY { get => euler.y; set { euler.y = value; quat = Space.Euler(euler.x, euler.y, euler.z); } }
        public float rotZ { get => euler.z; set { euler.z = value; quat = Space.Euler(euler.x, euler.y, euler.z); } }

        public void SetRotation(float x, float y, float z) => rotation = new Vector3(x, y, z);

        /// three Euler.setFromQuaternion (order XYZ)
        static Vector3 EulerFromQuaternion(Quaternion q)
        {
            Matrix4x4 m = Matrix4x4.Rotate(q);
            float m11 = m.m00, m12 = m.m01, m13 = m.m02, m22 = m.m11, m23 = m.m12, m32 = m.m21, m33 = m.m22;
            float y = Mathf.Asin(Mathf.Clamp(m13, -1, 1)), x, z;
            if (Mathf.Abs(m13) < 0.9999999f) { x = Mathf.Atan2(-m23, m33); z = Mathf.Atan2(-m12, m11); }
            else { x = Mathf.Atan2(m32, m22); z = 0; }
            return new Vector3(x, y, z);
        }

        // ---------------------------------------------------------------- hierarchy

        public Node Add(Node c)
        {
            if (c == null) return this;
            c.parent?.children.Remove(c);
            c.parent = this;
            children.Add(c);
            return this;
        }

        public Node Add(params Node[] cs)
        {
            foreach (var c in cs) Add(c);
            return this;
        }

        public void Remove(Node c)
        {
            if (children.Remove(c)) c.parent = null;
        }

        public void Traverse(Action<Node> fn)
        {
            fn(this);
            for (int i = 0; i < children.Count; i++) children[i].Traverse(fn);
        }

        public Matrix4x4 LocalMatrix => Matrix4x4.TRS(position, quat, scale);

        /// matrix to the top of this tree
        public Matrix4x4 WorldMatrix => parent == null ? LocalMatrix : parent.WorldMatrix * LocalMatrix;

        public Quaternion WorldQuaternion => parent == null ? quat : parent.WorldQuaternion * quat;

        public Vector3 WorldPosition => WorldMatrix.MultiplyPoint3x4(Vector3.zero);

        /// three Object3D.lookAt (target in the coordinates of the top of this tree)
        public void LookAt(Vector3 target, bool cameraOrLight = false)
        {
            Quaternion parentRot = parent != null ? parent.WorldQuaternion : Quaternion.identity;
            quaternion = Space.LookAt(WorldPosition, target, Vector3.up, cameraOrLight, parentRot);
        }

        /// three Object3D.attach: re-parent keeping the world transform
        public void Attach(Node o)
        {
            Matrix4x4 m = WorldMatrix.inverse;
            if (o.parent != null) m = m * o.parent.WorldMatrix;
            m = m * o.LocalMatrix;
            o.parent?.children.Remove(o);
            o.position = m.GetColumn(3);
            o.quaternion = m.rotation;
            o.scale = m.lossyScale;
            o.parent = this;
            children.Add(o);
        }

        // ---------------------------------------------------------------- GameObjects

        /// Creates the GameObjects for this tree under parent (use a mirrored root, Space.CreateWorld).
        /// Meshes, palette textures and own materials go to owned.
        public GameObject Realize(Transform parentT, Owned owned, int layer = -1)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? (geo != null ? "mesh" : "group") : name);
            if (layer >= 0) go.layer = layer;
            transform = go.transform;
            transform.SetParent(parentT, false);
            transform.localPosition = position;
            transform.localRotation = quat;
            transform.localScale = scale;
            if (geo != null && mat != null) AddRenderer(go, geo, mat, renderOrder, castShadow, receiveShadow, owned);
            if (!visible) go.SetActive(false);
            for (int i = 0; i < children.Count; i++) children[i].Realize(transform, owned, layer);
            return go;
        }

        /// MeshFilter + MeshRenderer for a three.js mesh / line.
        public static MeshRenderer AddRenderer(GameObject go, Geo geo, Mat3 mat, int renderOrder, bool castShadow, bool receiveShadow, Owned owned)
        {
            Geo g = geo;
            if (mat.IsLit && !mat.additive && !g.lines)
            {
                if (mat.side == Side.Double) g = geo.TwoSided();
                else if (mat.side == Side.Back) g = geo.BackSided();
            }
            Mesh mesh;
            Material um;
            if (mat.vertexColors && g.col != null && g.col.Count == g.Count)
            {
                var tex = owned.Add(Palette.Build(g.col, out var uvs));
                mesh = owned.Add(g.ToMesh(go.name, uvs));
                var m2 = mat.Clone();
                m2.map = tex;
                um = owned.Add(Mats.Create(m2, renderOrder));
            }
            else
            {
                mesh = g.ToMesh(go.name);
                if (!g.shared) owned.Add(mesh);
                um = Mats.Get(mat, renderOrder);
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = um;
            r.shadowCastingMode = castShadow && !mat.IsTransparent ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = receiveShadow;
            return r;
        }
    }
}
