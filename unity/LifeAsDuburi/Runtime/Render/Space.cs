// Coordinate frames of the port (PORTING.md "Coordinates").
//
//  * physics: NED (x north, y east, z down), body FRD, quaternions [w, x, y, z] body -> world
//  * the browser renders with three.js (right-handed, y up): three = (y, -z, -x)  (= src/render/course3d.js ned())
//  * Unity is left-handed (x right, y up, z forward)
//
// Every mesh lives under a mirrored root ('World', localScale (1, 1, -1)) and uses the three.js
// positions / rotations / geometry of the JS literally. Cameras, lights and particle systems live in
// Unity world space and are converted with the functions below. Never call Transform.LookAt on
// children of the mirrored root: use Space.LookAt (a port of three's Object3D.lookAt) instead.

using Duburi.Core;
using UnityEngine;

namespace Duburi.Game
{
    public static class Space
    {
        // ---------------------------------------------------------------- three <-> Unity world

        /// three.js position -> Unity world (only valid for a mirrored root at the Unity origin)
        public static Vector3 ToUnity(Vector3 three) => new Vector3(three.x, three.y, -three.z);
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);
        public static Vector3 FromUnity(Vector3 u) => new Vector3(u.x, u.y, -u.z);

        /// three.js direction -> Unity world direction
        public static Vector3 DirToUnity(Vector3 three) => new Vector3(three.x, three.y, -three.z);

        /// three.js world rotation -> Unity world rotation. Also right for cameras: three cameras look
        /// down -Z, Unity cameras down +Z, and the mirror maps one onto the other.
        public static Quaternion RotToUnity(Quaternion q3) => new Quaternion(-q3.x, -q3.y, q3.z, q3.w);
        public static Quaternion RotFromUnity(Quaternion qu) => new Quaternion(-qu.x, -qu.y, qu.z, qu.w);

        // ---------------------------------------------------------------- NED -> three / Unity

        /// course3d.js ned(): NED (north, east, down) -> three (east, up, south)
        public static Vector3 Ned(double x, double y, double z) => new Vector3((float)y, (float)-z, (float)-x);
        public static Vector3 Ned(V3 p) => new Vector3((float)p.y, (float)-p.z, (float)-p.x);

        /// course3d.js nedYaw(): NED heading -> rotation.y of an object built facing -Z
        public static float NedYaw(double h) => (float)-h;

        /// NED -> Unity world directly: (y, -z, x)
        public static Vector3 NedToUnity(double x, double y, double z) => new Vector3((float)y, (float)-z, (float)x);
        public static Vector3 NedToUnity(V3 p) => new Vector3((float)p.y, (float)-p.z, (float)p.x);

        /// main.js nedQuat(): NED body->world quaternion [w, x, y, z] -> three.js quaternion (basis change P q P^T)
        public static Quaternion NedQuat(Quat q) => new Quaternion((float)q.y, (float)-q.z, (float)-q.x, (float)q.w);
        public static Quaternion NedQuat(double w, double x, double y, double z) => new Quaternion((float)y, (float)-z, (float)-x, (float)w);

        /// NED body->world quaternion -> Unity world rotation
        public static Quaternion NedQuatToUnity(Quat q) => new Quaternion((float)-q.y, (float)q.z, (float)-q.x, (float)q.w);

        /// body FRD point -> three model coords (bots3d.js P()): (b[1], -b[2], -b[0])
        public static Vector3 Body(V3 b) => new Vector3((float)b.y, (float)-b.z, (float)-b.x);
        public static Vector3 Body(double x, double y, double z) => new Vector3((float)y, (float)-z, (float)-x);

        // ---------------------------------------------------------------- the mirrored root

        /// Creates a mirrored root (localScale (1, 1, -1)); children use three.js coordinates literally.
        public static Transform CreateWorld(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            var t = go.transform;
            if (parent != null) t.SetParent(parent, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(1f, 1f, -1f);
            return t;
        }

        // ---------------------------------------------------------------- three.js rotation helpers

        /// three Euler (order 'XYZ', radians) -> quaternion (Quaternion.setFromEuler)
        public static Quaternion Euler(float x, float y, float z)
        {
            float c1 = Mathf.Cos(x / 2), c2 = Mathf.Cos(y / 2), c3 = Mathf.Cos(z / 2);
            float s1 = Mathf.Sin(x / 2), s2 = Mathf.Sin(y / 2), s3 = Mathf.Sin(z / 2);
            return new Quaternion(
                s1 * c2 * c3 + c1 * s2 * s3,
                c1 * s2 * c3 - s1 * c2 * s3,
                c1 * c2 * s3 + s1 * s2 * c3,
                c1 * c2 * c3 - s1 * s2 * s3);
        }

        /// three Quaternion.setFromAxisAngle (axis normalised, radians)
        public static Quaternion AxisAngle(Vector3 axis, float angle)
        {
            float h = angle / 2, s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        /// three Quaternion.setFromUnitVectors (both normalised)
        public static Quaternion FromUnitVectors(Vector3 vFrom, Vector3 vTo)
        {
            float r = Vector3.Dot(vFrom, vTo) + 1f;
            float x, y, z, w;
            if (r < 1.1920929e-7f) // Number.EPSILON in float terms
            {
                r = 0;
                if (Mathf.Abs(vFrom.x) > Mathf.Abs(vFrom.z)) { x = -vFrom.y; y = vFrom.x; z = 0; w = r; }
                else { x = 0; y = -vFrom.z; z = vFrom.y; w = r; }
            }
            else
            {
                x = vFrom.y * vTo.z - vFrom.z * vTo.y;
                y = vFrom.z * vTo.x - vFrom.x * vTo.z;
                z = vFrom.x * vTo.y - vFrom.y * vTo.x;
                w = r;
            }
            float l = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (l == 0) return Quaternion.identity;
            return new Quaternion(x / l, y / l, z / l, w / l);
        }

        /// three Matrix4.lookAt(eye, target, up) + Quaternion.setFromRotationMatrix: the rotation whose
        /// +Z points from target to eye.
        public static Quaternion LookRotationThree(Vector3 eye, Vector3 target, Vector3 up)
        {
            Vector3 z = eye - target;
            if (z.sqrMagnitude == 0) z.z = 1;
            z.Normalize();
            Vector3 x = Vector3.Cross(up, z);
            if (x.sqrMagnitude == 0)
            {
                if (Mathf.Abs(up.z) == 1) z.x += 0.0001f; else z.z += 0.0001f;
                z.Normalize();
                x = Vector3.Cross(up, z);
            }
            x.Normalize();
            Vector3 y = Vector3.Cross(z, x);
            return FromBasis(x, y, z);
        }

        /// three Object3D.lookAt for an object whose parent has world rotation parentWorldRot (three
        /// space). Cameras and lights point -Z at the target, everything else +Z.
        public static Quaternion LookAt(Vector3 worldPos, Vector3 target, Vector3 up, bool cameraOrLight, Quaternion parentWorldRot)
        {
            Quaternion q = cameraOrLight ? LookRotationThree(worldPos, target, up) : LookRotationThree(target, worldPos, up);
            return Quaternion.Inverse(parentWorldRot) * q;
        }

        public static Quaternion LookAt(Vector3 worldPos, Vector3 target, bool cameraOrLight = false) =>
            LookAt(worldPos, target, Vector3.up, cameraOrLight, Quaternion.identity);

        /// Quaternion.setFromRotationMatrix for the rotation with columns x, y, z.
        public static Quaternion FromBasis(Vector3 cx, Vector3 cy, Vector3 cz)
        {
            float m11 = cx.x, m12 = cy.x, m13 = cz.x;
            float m21 = cx.y, m22 = cy.y, m23 = cz.y;
            float m31 = cx.z, m32 = cy.z, m33 = cz.z;
            float trace = m11 + m22 + m33, s;
            if (trace > 0)
            {
                s = 0.5f / Mathf.Sqrt(trace + 1.0f);
                return new Quaternion((m32 - m23) * s, (m13 - m31) * s, (m21 - m12) * s, 0.25f / s);
            }
            if (m11 > m22 && m11 > m33)
            {
                s = 2.0f * Mathf.Sqrt(1.0f + m11 - m22 - m33);
                return new Quaternion(0.25f * s, (m12 + m21) / s, (m13 + m31) / s, (m32 - m23) / s);
            }
            if (m22 > m33)
            {
                s = 2.0f * Mathf.Sqrt(1.0f + m22 - m11 - m33);
                return new Quaternion((m12 + m21) / s, 0.25f * s, (m23 + m32) / s, (m13 - m31) / s);
            }
            s = 2.0f * Mathf.Sqrt(1.0f + m33 - m11 - m22);
            return new Quaternion((m13 + m31) / s, (m23 + m32) / s, 0.25f * s, (m21 - m12) / s);
        }

        /// three Quaternion.slerp (shortest path)
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => Quaternion.Slerp(a, b, t);
    }
}
