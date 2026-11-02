// Small value types for the JS arrays the simulation uses:
//   V2   = [x, y]          (horizontal positions / directions of the course, NED)
//   V3   = [x, y, z]       (vectors; NED in the world, FRD in the body)
//   Quat = [w, x, y, z]    (Hamilton quaternion, body -> NED, the JS order)
// They are structs (no garbage); the indexer gives the JS index access (v[0], q[3]).
// Arithmetic is written out per component in the same order as the JS so results
// are bit-identical.

using System;
using System.Globalization;

namespace Duburi.Core
{
    [Serializable]
    public struct V2 : IEquatable<V2>
    {
        public double x, y;
        public V2(double x, double y) { this.x = x; this.y = y; }

        public double this[int i]
        {
            get { if (i == 0) return x; if (i == 1) return y; throw new IndexOutOfRangeException(); }
            set { if (i == 0) x = value; else if (i == 1) y = value; else throw new IndexOutOfRangeException(); }
        }

        public bool Equals(V2 o) => x == o.x && y == o.y;
        public override bool Equals(object obj) => obj is V2 o && Equals(o);
        public override int GetHashCode() => x.GetHashCode() * 31 + y.GetHashCode();
        public override string ToString() => "[" + x.ToString("R", CultureInfo.InvariantCulture) + ", " + y.ToString("R", CultureInfo.InvariantCulture) + "]";
    }

    [Serializable]
    public struct V3 : IEquatable<V3>
    {
        public double x, y, z;
        public V3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static readonly V3 Zero = new V3(0, 0, 0);

        public double this[int i]
        {
            get { if (i == 0) return x; if (i == 1) return y; if (i == 2) return z; throw new IndexOutOfRangeException(); }
            set { if (i == 0) x = value; else if (i == 1) y = value; else if (i == 2) z = value; else throw new IndexOutOfRangeException(); }
        }

        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator -(V3 a) => new V3(-a.x, -a.y, -a.z);
        public static V3 operator *(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);

        public bool Equals(V3 o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object obj) => obj is V3 o && Equals(o);
        public override int GetHashCode() => (x.GetHashCode() * 31 + y.GetHashCode()) * 31 + z.GetHashCode();
        public override string ToString() => "[" + x.ToString("R", CultureInfo.InvariantCulture) + ", " + y.ToString("R", CultureInfo.InvariantCulture) + ", " + z.ToString("R", CultureInfo.InvariantCulture) + "]";
    }

    [Serializable]
    public struct Quat : IEquatable<Quat>
    {
        public double w, x, y, z;
        public Quat(double w, double x, double y, double z) { this.w = w; this.x = x; this.y = y; this.z = z; }
        public static readonly Quat Identity = new Quat(1, 0, 0, 0);

        /// JS index order: [0] = w, [1] = x, [2] = y, [3] = z
        public double this[int i]
        {
            get { switch (i) { case 0: return w; case 1: return x; case 2: return y; case 3: return z; } throw new IndexOutOfRangeException(); }
            set { switch (i) { case 0: w = value; break; case 1: x = value; break; case 2: y = value; break; case 3: z = value; break; default: throw new IndexOutOfRangeException(); } }
        }

        public bool Equals(Quat o) => w == o.w && x == o.x && y == o.y && z == o.z;
        public override bool Equals(object obj) => obj is Quat o && Equals(o);
        public override int GetHashCode() => ((w.GetHashCode() * 31 + x.GetHashCode()) * 31 + y.GetHashCode()) * 31 + z.GetHashCode();
        public override string ToString() => "[" + w.ToString("R", CultureInfo.InvariantCulture) + ", " + x.ToString("R", CultureInfo.InvariantCulture) + ", " + y.ToString("R", CultureInfo.InvariantCulture) + ", " + z.ToString("R", CultureInfo.InvariantCulture) + "]";
    }
}
