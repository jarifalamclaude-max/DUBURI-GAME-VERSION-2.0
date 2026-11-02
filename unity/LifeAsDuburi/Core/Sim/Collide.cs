// Port of src/sim/collide.js.
// Contacts between vehicles (a few spheres each), the course elements and the
// arena. Each contact pushes the vehicle out along the surface normal and
// removes the inward velocity (small restitution, Coulomb-like friction), the
// same idea as the simulator's penalty contacts but stiff enough for a game
// step.

using System.Collections.Generic;

namespace Duburi.Core
{
    /// one contact: normal out of the obstacle (world NED) and penetration depth (m)
    public struct ContactHit
    {
        public V3 n;
        public double d;
        public bool floor;
        public Vehicle bot; // the other vehicle for a vehicle-vehicle contact
    }

    public static class Collide
    {
        static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        static readonly List<ContactHit> hits = new List<ContactHit>(16); // scratch (single-threaded)

        // sphere (centre c, radius r) vs obstacle -> { n: normal out of the obstacle, d: depth } or null (false)
        public static bool SphereVs(Obstacle o, V3 c, double r, out ContactHit hit)
        {
            hit = default;
            if (o.type == "cyl")
            {
                double z = Clamp(c.z, o.z0, o.z1);
                double dx = c.x - o.p.x, dy = c.y - o.p.y, dz = c.z - z;
                // closest point on the cylinder surface (side or caps)
                double rh = JsMath.Hypot(dx, dy);
                if (c.z >= o.z0 && c.z <= o.z1)
                {
                    double d = o.r + r - rh;
                    if (d <= 0) return false;
                    hit.n = rh > 1e-9 ? new V3(dx / rh, dy / rh, 0) : new V3(1, 0, 0);
                    hit.d = d;
                    return true;
                }
                double qx = rh > o.r ? o.p.x + (dx / rh) * o.r : c.x, qy = rh > o.r ? o.p.y + (dy / rh) * o.r : c.y;
                double ex = c.x - qx, ey = c.y - qy, L = JsMath.Hypot(ex, ey, dz);
                if (L >= r || L < 1e-9) return false;
                hit.n = new V3(ex / L, ey / L, dz / L);
                hit.d = r - L;
                return true;
            }
            if (o.type == "box")
            {
                double cy = JsMath.Cos(o.yaw), sy = JsMath.Sin(o.yaw);
                double dx = c.x - o.c.x, dy = c.y - o.c.y, dz = c.z - o.c.z;
                double lx = dx * cy + dy * sy, ly = -dx * sy + dy * cy;
                double px = Clamp(lx, -o.h.x, o.h.x), py = Clamp(ly, -o.h.y, o.h.y), pz = Clamp(dz, -o.h.z, o.h.z);
                double ex = lx - px, ey = ly - py, ez = dz - pz;
                double L = JsMath.Hypot(ex, ey, ez);
                if (L >= r) return false;
                if (L < 1e-9)
                {
                    // centre inside: leave through the nearest face
                    double fx = o.h.x - System.Math.Abs(lx), fy = o.h.y - System.Math.Abs(ly), fz = o.h.z - System.Math.Abs(dz);
                    if (fx <= fy && fx <= fz) { ex = Js.Or(JsMath.Sign(lx), 1); ey = 0; ez = 0; L = -fx; }
                    else if (fy <= fz) { ex = 0; ey = Js.Or(JsMath.Sign(ly), 1); ez = 0; L = -fy; }
                    else { ex = 0; ey = 0; ez = Js.Or(JsMath.Sign(dz), 1); L = -fz; }
                    hit.n = new V3(ex * cy - ey * sy, ex * sy + ey * cy, ez);
                    hit.d = r - L;
                    return true;
                }
                ex /= L; ey /= L; ez /= L;
                hit.n = new V3(ex * cy - ey * sy, ex * sy + ey * cy, ez);
                hit.d = r - L;
                return true;
            }
            if (o.type == "board")
            {
                var b = o.board;
                var bl = Course.BoardLocal(b, c);
                double u = bl.u, v = bl.v, n = bl.n;
                double half = b.size / 2, th = 0.012;
                double pu = Clamp(u, -half, half), pv = Clamp(v, -half, half), pn = Clamp(n, -th, th);
                double eu = u - pu, ev = v - pv, en = n - pn;
                double L = JsMath.Hypot(eu, ev, en);
                if (L >= r) return false;
                // back to world: u -> -right, v -> up (-z), n -> dir
                double k = L > 1e-9 ? 1 / L : 0;
                double nu = eu * k, nv = ev * k, nn = L > 1e-9 ? en * k : Js.Or(JsMath.Sign(n), 1);
                hit.n = new V3(-b.right.x * nu + b.dir.x * nn, -b.right.y * nu + b.dir.y * nn, -nv);
                hit.d = r - (L > 1e-9 ? L : 0);
                return true;
            }
            if (o.type == "ring")
            {
                // octagon floating pipe: torus around the vertical axis at the surface
                double dx = c.x - o.p.x, dy = c.y - o.p.y;
                double rh = Js.Or(JsMath.Hypot(dx, dy), 1e-9);
                double qx = o.p.x + (dx / rh) * o.R, qy = o.p.y + (dy / rh) * o.R, qz = o.z;
                double ex = c.x - qx, ey = c.y - qy, ez = c.z - qz, L = JsMath.Hypot(ex, ey, ez);
                if (L >= o.r + r || L < 1e-9) return false;
                hit.n = new V3(ex / L, ey / L, ez / L);
                hit.d = o.r + r - L;
                return true;
            }
            return false;
        }

        /// Resolve contacts for one vehicle. Returns a new set of the obstacle tags touched.
        public static HashSet<string> CollideVehicle(Vehicle veh, Course course, Arena arena, IList<Vehicle> others = null)
        {
            var touched = new HashSet<string>();
            CollideVehicle(veh, course, arena, others, touched);
            return touched;
        }

        /// Resolve contacts for one vehicle. Clears `touched` and fills it with the obstacle
        /// tags touched ("bot" for another vehicle). No garbage.
        public static void CollideVehicle(Vehicle veh, Course course, Arena arena, IList<Vehicle> others, HashSet<string> touched)
        {
            touched.Clear();
            var spheres = veh.d.spheres;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int si = 0; si < spheres.Length; si++)
                {
                    Sphere s = spheres[si];
                    V3 c = veh.ToWorld(s.c);
                    double r = s.r;
                    hits.Clear();
                    var obstacles = course.obstacles;
                    for (int oi = 0; oi < obstacles.Count; oi++)
                    {
                        var o = obstacles[oi];
                        if (SphereVs(o, c, r, out var h)) { hits.Add(h); touched.Add(o.tag); }
                    }
                    // floor and walls (the sea scenarios keep an invisible boundary at the same place)
                    if (c.z + r > arena.depth) hits.Add(new ContactHit { n = new V3(0, 0, -1), d = c.z + r - arena.depth, floor = true });
                    double hx = arena.length / 2, hy = arena.width / 2;
                    if (c.x + r > hx) hits.Add(new ContactHit { n = new V3(-1, 0, 0), d = c.x + r - hx });
                    if (c.x - r < -hx) hits.Add(new ContactHit { n = new V3(1, 0, 0), d = -hx - (c.x - r) });
                    if (c.y + r > hy) hits.Add(new ContactHit { n = new V3(0, -1, 0), d = c.y + r - hy });
                    if (c.y - r < -hy) hits.Add(new ContactHit { n = new V3(0, 1, 0), d = -hy - (c.y - r) });
                    if (c.z - r < -0.6) hits.Add(new ContactHit { n = new V3(0, 0, 1), d = -0.6 - (c.z - r) }); // can't jump out of the water
                    if (others != null)
                    {
                        for (int k = 0; k < others.Count; k++)
                        {
                            var other = others[k];
                            var os = other.d.spheres;
                            for (int j = 0; j < os.Length; j++)
                            {
                                Sphere so = os[j];
                                V3 co = other.ToWorld(so.c);
                                double dx = c.x - co.x, dy = c.y - co.y, dz = c.z - co.z;
                                double L = JsMath.Hypot(dx, dy, dz), rr = r + so.r;
                                if (L < rr && L > 1e-6) { hits.Add(new ContactHit { n = new V3(dx / L, dy / L, dz / L), d = (rr - L) * 0.5, bot = other }); touched.Add("bot"); }
                            }
                        }
                    }
                    for (int k = 0; k < hits.Count; k++) ApplyContact(veh, hits[k], s);
                }
            }
        }

        static void ApplyContact(Vehicle veh, ContactHit h, Sphere s)
        {
            V3 n = h.n;
            double d = JsMath.Min(h.d, 0.12);
            veh.p.x += n.x * d; veh.p.y += n.y * d; veh.p.z += n.z * d;
            // world velocity of the contact point: v + w x r (r in world)
            V3 vw = veh.WorldVel();
            double vn = vw.x * n.x + vw.y * n.y + vw.z * n.z;
            if (vn < 0)
            {
                const double e = 0.25;
                double dv = -(1 + e) * vn;
                V3 nv = new V3(vw.x + n.x * dv, vw.y + n.y * dv, vw.z + n.z * dv);
                // friction on the tangential part
                V3 vt = new V3(
                    nv.x - n.x * (nv.x * n.x + nv.y * n.y + nv.z * n.z),
                    nv.y - n.y * (nv.x * n.x + nv.y * n.y + nv.z * n.z),
                    nv.z - n.z * (nv.x * n.x + nv.y * n.y + nv.z * n.z));
                double f = h.floor ? 0.25 : 0.1;
                nv.x -= vt.x * f; nv.y -= vt.y * f; nv.z -= vt.z * f;
                double[] R = veh.R;
                veh.v = new V3(R[0] * nv.x + R[3] * nv.y + R[6] * nv.z, R[1] * nv.x + R[4] * nv.y + R[7] * nv.z, R[2] * nv.x + R[5] * nv.y + R[8] * nv.z);
                // a knock spins the vehicle a little (torque of the impulse about the CG)
                V3 rb = s.c;
                V3 nb = new V3(R[0] * n.x + R[3] * n.y + R[6] * n.z, R[1] * n.x + R[4] * n.y + R[7] * n.z, R[2] * n.x + R[5] * n.y + R[8] * n.z);
                V3 tq = new V3(rb.y * nb.z - rb.z * nb.y, rb.z * nb.x - rb.x * nb.z, rb.x * nb.y - rb.y * nb.x);
                double k = (dv * veh.mass) * 0.35;
                veh.w.x += (tq.x * k) / veh.It[0];
                veh.w.y += (tq.y * k) / veh.It[1];
                veh.w.z += (tq.z * k) / veh.It[2];
                veh.lastImpact = dv;
            }
        }
    }
}
