// Port of src/core/math.js: core math utilities shared by physics, allocation and the ArduSub port.
// Conventions:
//   - Vectors are V3 [x, y, z].
//   - Quaternions are Quat [w, x, y, z] (Hamilton), representing body -> NED rotation
//     (same meaning as ArduPilot's get_quat_body_to_ned()).
//   - Euler angles follow ArduPilot's 3-2-1 convention: roll (x), pitch (y), yaw (z).
//   - 3x3 matrices are double[9], row-major. Dense matrices are double[][] (array of rows).

using System;
using System.Collections.Generic;

namespace Duburi.Core
{
    public static class MathUtil
    {
        public const double DEG = JsMath.PI / 180;
        public const double RAD = 180 / JsMath.PI;
        public const double GRAVITY_MSS = 9.80665; // ArduPilot GRAVITY_MSS

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        // AP_Math.cpp:267 constrain_value_line(): NaN maps to the midpoint (used by the ArduSub port only;
        // the physics keeps plain clamp so a NaN in the plant is not hidden)
        public static double ConstrainFloat(double v, double lo, double hi) => double.IsNaN(v) ? (lo + hi) / 2 : v < lo ? lo : v > hi ? hi : v;
        public static double SafeSqrt(double v) => v > 0 ? Math.Sqrt(v) : 0; // AP_Math safe_sqrt (NaN/negative -> 0)
        public static bool IsZero(double v) => Math.Abs(v) < 1.1920929e-7; // FLT_EPSILON like ArduPilot's is_zero
        public static bool IsPositive(double v) => v >= 1.1920929e-7;
        public static bool IsNegative(double v) => v <= -1.1920929e-7;
        public static double Sq(double v) => v * v;
        public static double Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0; // math.js sign (NaN -> 0), not Math.sign

        public static double WrapPI(double a)
        {
            // wrap to (-pi, pi]
            double r = a % (2 * JsMath.PI);
            if (r > JsMath.PI) r -= 2 * JsMath.PI;
            else if (r <= -JsMath.PI) r += 2 * JsMath.PI;
            return r;
        }
        public static double Wrap2PI(double a)
        {
            double r = a % (2 * JsMath.PI);
            if (r < 0) r += 2 * JsMath.PI;
            return r;
        }
        public static double Wrap180cd(double cd)
        {
            // AP_Math.cpp:157 wrap_180_cd = wrap_360_cd(), minus 36000 if > 18000 -> (-18000, 18000]
            double r = cd % 36000;
            if (r < 0) r += 36000;
            if (r > 18000) r -= 36000;
            return r;
        }
        public static double Wrap360cd(double cd)
        {
            double r = cd % 36000;
            if (r < 0) r += 36000;
            return r;
        }

        // ---------------- vec3 ----------------
        public static V3 Vadd(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 Vsub(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 Vscale(V3 a, double s) => new V3(a.x * s, a.y * s, a.z * s);
        public static double Vdot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Vcross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static double Vlen(V3 a) => JsMath.Hypot(a.x, a.y, a.z);
        public static V3 Vnorm(V3 a)
        {
            double l = Vlen(a);
            return l > 1e-12 ? new V3(a.x / l, a.y / l, a.z / l) : new V3(0, 0, 0);
        }
        public static V3 Vcopy(V3 a) => a;
        public static V3 Vneg(V3 a) => new V3(-a.x, -a.y, -a.z);

        // ---------------- mat3 (row-major arrays of 9) ----------------
        public static V3 M3MulV(double[] m, V3 v) => new V3(
            m[0] * v.x + m[1] * v.y + m[2] * v.z,
            m[3] * v.x + m[4] * v.y + m[5] * v.z,
            m[6] * v.x + m[7] * v.y + m[8] * v.z);

        // transpose(m) * v
        public static V3 M3TMulV(double[] m, V3 v) => new V3(
            m[0] * v.x + m[3] * v.y + m[6] * v.z,
            m[1] * v.x + m[4] * v.y + m[7] * v.z,
            m[2] * v.x + m[5] * v.y + m[8] * v.z);

        public static double[] M3Mul(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return r;
        }
        public static double[] M3Transpose(double[] m) => new[] { m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8] };
        public static double[] M3Inv(double[] m)
        {
            double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-18) throw new InvalidOperationException("singular 3x3 matrix");
            double inv = 1 / det;
            return new[] {
                A * inv, -(b * i - c * h) * inv, (b * f - c * e) * inv,
                B * inv, (a * i - c * g) * inv, -(a * f - c * d) * inv,
                C * inv, -(a * h - b * g) * inv, (a * e - b * d) * inv,
            };
        }
        public static double[] M3Identity() => new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        // ---------------- quaternion ----------------
        public static Quat QIdentity() => new Quat(1, 0, 0, 0);
        public static Quat QMul(Quat a, Quat b) => new Quat(
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w);
        public static Quat QConj(Quat q) => new Quat(q.w, -q.x, -q.y, -q.z);
        public static Quat QInverse(Quat q) => QConj(q); // unit quaternions only
        public static Quat QNormalize(Quat q)
        {
            double n = JsMath.Hypot(q.w, q.x, q.y, q.z);
            if (n < 1e-12) return new Quat(1, 0, 0, 0);
            return new Quat(q.w / n, q.x / n, q.y / n, q.z / n);
        }
        public static V3 QRotate(Quat q, V3 v)
        {
            // rotate vector v by quaternion q: q * v * q^-1
            double w = q.w, x = q.x, y = q.y, z = q.z;
            double ix = w * v.x + y * v.z - z * v.y;
            double iy = w * v.y + z * v.x - x * v.z;
            double iz = w * v.z + x * v.y - y * v.x;
            double iw = -x * v.x - y * v.y - z * v.z;
            return new V3(
                ix * w + iw * -x + iy * -z - iz * -y,
                iy * w + iw * -y + iz * -x - ix * -z,
                iz * w + iw * -z + ix * -y - iy * -x);
        }
        public static V3 QRotateInv(Quat q, V3 v) => QRotate(QConj(q), v);

        // rotation matrix (row-major) equal to rotating by q (body -> ned for attitude)
        public static double[] QToMat(Quat q)
        {
            var m = new double[9];
            QToMat(q, m);
            return m;
        }
        /// QToMat written into an existing double[9] (no garbage)
        public static void QToMat(Quat q, double[] m)
        {
            double w = q.w, x = q.x, y = q.y, z = q.z;
            m[0] = 1 - 2 * (y * y + z * z); m[1] = 2 * (x * y - w * z); m[2] = 2 * (x * z + w * y);
            m[3] = 2 * (x * y + w * z); m[4] = 1 - 2 * (x * x + z * z); m[5] = 2 * (y * z - w * x);
            m[6] = 2 * (x * z - w * y); m[7] = 2 * (y * z + w * x); m[8] = 1 - 2 * (x * x + y * y);
        }

        public static Quat QFromEuler(double roll, double pitch, double yaw)
        {
            // ArduPilot Quaternion::from_euler (3-2-1)
            double cr2 = JsMath.Cos(roll * 0.5), cp2 = JsMath.Cos(pitch * 0.5), cy2 = JsMath.Cos(yaw * 0.5);
            double sr2 = JsMath.Sin(roll * 0.5), sp2 = JsMath.Sin(pitch * 0.5), sy2 = JsMath.Sin(yaw * 0.5);
            return new Quat(
                cr2 * cp2 * cy2 + sr2 * sp2 * sy2,
                sr2 * cp2 * cy2 - cr2 * sp2 * sy2,
                cr2 * sp2 * cy2 + sr2 * cp2 * sy2,
                cr2 * cp2 * sy2 - sr2 * sp2 * cy2);
        }
        /// ArduPilot Quaternion::to_euler -> (roll, pitch, yaw) as V3 (x, y, z)
        public static V3 QToEuler(Quat q)
        {
            double q1 = q.w, q2 = q.x, q3 = q.y, q4 = q.z;
            double roll = JsMath.Atan2(2 * (q1 * q2 + q3 * q4), 1 - 2 * (q2 * q2 + q3 * q3));
            double pitch = JsMath.Asin(Clamp(2 * (q1 * q3 - q4 * q2), -1, 1));
            double yaw = JsMath.Atan2(2 * (q1 * q4 + q2 * q3), 1 - 2 * (q3 * q3 + q4 * q4));
            return new V3(roll, pitch, yaw);
        }
        public static Quat QFromAxisAngleVec(V3 v)
        {
            // ArduPilot Quaternion::from_axis_angle(Vector3f v) where |v| = angle
            // quaternion.cpp:470: is_zero(theta) (FLT_EPSILON) gives the identity
            double theta = Vlen(v);
            if (IsZero(theta)) return new Quat(1, 0, 0, 0);
            return QFromAxisAngle(Vscale(v, 1 / theta), theta);
        }
        public static Quat QFromAxisAngle(V3 axis, double theta)
        {
            if (IsZero(theta)) return new Quat(1, 0, 0, 0);
            double st2 = JsMath.Sin(0.5 * theta);
            return new Quat(JsMath.Cos(0.5 * theta), axis.x * st2, axis.y * st2, axis.z * st2);
        }
        public static V3 QToAxisAngle(Quat q)
        {
            // ArduPilot Quaternion::to_axis_angle -> rotation vector (quaternion.cpp:497: raw (q2,q3,q4) when is_zero(l))
            double l = JsMath.Hypot(q.x, q.y, q.z);
            var v = new V3(q.x, q.y, q.z);
            if (!IsZero(l)) return Vscale(v, WrapPI(2 * JsMath.Atan2(l, q.w)) / l);
            return v;
        }

        // ---------------- dense matrices (row-major, array of rows) ----------------
        public static double[][] Zeros(int r, int c)
        {
            var m = new double[r][];
            for (int i = 0; i < r; i++) m[i] = new double[c];
            return m;
        }
        public static double[][] MatT(double[][] A)
        {
            int r = A.Length, c = A[0].Length;
            var T = Zeros(c, r);
            for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) T[j][i] = A[i][j];
            return T;
        }
        public static double[][] MatMul(double[][] A, double[][] B)
        {
            int r = A.Length, n = B.Length, c = B[0].Length;
            var C = Zeros(r, c);
            for (int i = 0; i < r; i++)
                for (int k = 0; k < n; k++)
                {
                    double a = A[i][k];
                    if (a == 0) continue;
                    double[] Bk = B[k], Ci = C[i];
                    for (int j = 0; j < c; j++) Ci[j] += a * Bk[j];
                }
            return C;
        }
        public static double[] MatVec(double[][] A, double[] x)
        {
            var r = new double[A.Length];
            for (int i = 0; i < A.Length; i++)
            {
                double s = 0;
                double[] row = A[i];
                for (int j = 0; j < row.Length; j++) s = s + row[j] * x[j];
                r[i] = s;
            }
            return r;
        }

        public sealed class SvdResult
        {
            public double[][] U; // m x k
            public double[] S;   // k, descending
            public double[][] V; // n x k
        }

        // One-sided Jacobi SVD. Returns {U (m x k), S (k), V (n x k)} with k = min(m, n),
        // singular values sorted descending. Robust for the small matrices used here.
        public static SvdResult Svd(double[][] A)
        {
            int m = A.Length, n = A[0].Length;
            bool transpose = m < n;
            double[][] M;
            if (transpose) M = MatT(A);
            else { M = new double[m][]; for (int i = 0; i < m; i++) M[i] = (double[])A[i].Clone(); }
            int rows = M.Length, cols = M[0].Length; // rows >= cols
            var V = Zeros(cols, cols);
            for (int i = 0; i < cols; i++) V[i][i] = 1;
            for (int sweep = 0; sweep < 80; sweep++)
            {
                double off = 0;
                for (int p = 0; p < cols - 1; p++)
                {
                    for (int q = p + 1; q < cols; q++)
                    {
                        double alpha = 0, beta = 0, gamma = 0;
                        for (int i = 0; i < rows; i++)
                        {
                            alpha += M[i][p] * M[i][p];
                            beta += M[i][q] * M[i][q];
                            gamma += M[i][p] * M[i][q];
                        }
                        if (Math.Abs(gamma) <= 1e-15 * Math.Sqrt(alpha * beta) || gamma == 0) continue;
                        off = JsMath.Max(off, Math.Abs(gamma) / Math.Sqrt(alpha * beta));
                        double zeta = (beta - alpha) / (2 * gamma);
                        double t = JsMath.Sign(Js.Or(zeta, 1)) / (Math.Abs(zeta) + Math.Sqrt(1 + zeta * zeta));
                        double c = 1 / Math.Sqrt(1 + t * t), s = c * t;
                        for (int i = 0; i < rows; i++)
                        {
                            double mp = M[i][p], mq = M[i][q];
                            M[i][p] = c * mp - s * mq;
                            M[i][q] = s * mp + c * mq;
                        }
                        for (int i = 0; i < cols; i++)
                        {
                            double vp = V[i][p], vq = V[i][q];
                            V[i][p] = c * vp - s * vq;
                            V[i][q] = s * vp + c * vq;
                        }
                    }
                }
                if (off < 1e-14) break;
            }
            var S = new double[cols];
            var U = Zeros(rows, cols);
            for (int j = 0; j < cols; j++)
            {
                double nrm = 0;
                for (int i = 0; i < rows; i++) nrm += M[i][j] * M[i][j];
                nrm = Math.Sqrt(nrm);
                S[j] = nrm;
                for (int i = 0; i < rows; i++) U[i][j] = nrm > 1e-300 ? M[i][j] / nrm : 0;
            }
            // sort descending (stable, like Array.prototype.sort)
            var idx = new List<int>(cols);
            for (int i = 0; i < cols; i++) idx.Add(i);
            Js.StableSort(idx, (a, b) => S[b] - S[a]);
            var Ss = new double[cols];
            for (int i = 0; i < cols; i++) Ss[i] = S[idx[i]];
            var Us = Zeros(rows, cols);
            for (int r = 0; r < rows; r++) for (int i = 0; i < cols; i++) Us[r][i] = U[r][idx[i]];
            var Vs = Zeros(cols, cols);
            for (int r = 0; r < cols; r++) for (int i = 0; i < cols; i++) Vs[r][i] = V[r][idx[i]];
            return transpose ? new SvdResult { U = Vs, S = Ss, V = Us } : new SvdResult { U = Us, S = Ss, V = Vs };
        }

        // Moore-Penrose pseudo-inverse with relative tolerance.
        public static double[][] Pinv(double[][] A, double rtol = 1e-10)
        {
            var svd = Svd(A);
            double[][] U = svd.U, V = svd.V;
            double[] S = svd.S;
            double tol = (S.Length > 0 ? Js.Or(S[0], 0) : 0) * rtol;
            int m = A.Length, n = A[0].Length, k = S.Length;
            var P = Zeros(n, m);
            for (int t = 0; t < k; t++)
            {
                if (S[t] <= tol || S[t] == 0) continue;
                double inv = 1 / S[t];
                for (int i = 0; i < n; i++)
                {
                    double vi = V[i][t] * inv;
                    if (vi == 0) continue;
                    for (int j = 0; j < m; j++) P[i][j] += vi * U[j][t];
                }
            }
            return P;
        }

        public static int RankOf(double[][] A, double rtol = 1e-9)
        {
            double[] S = Svd(A).S;
            double tol = (S.Length > 0 ? Js.Or(S[0], 0) : 0) * rtol;
            int r = 0;
            foreach (double s in S) if (s > tol) r++;
            return r;
        }

        // Solve small dense linear system Ax=b with partial pivoting (A square).
        public static double[] Solve(double[][] A, double[] b)
        {
            int n = A.Length;
            var M = new double[n][];
            for (int i = 0; i < n; i++)
            {
                M[i] = new double[A[i].Length + 1];
                Array.Copy(A[i], M[i], A[i].Length);
                M[i][A[i].Length] = b[i];
            }
            for (int c = 0; c < n; c++)
            {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(M[r][c]) > Math.Abs(M[p][c])) p = r;
                if (Math.Abs(M[p][c]) < 1e-14) throw new InvalidOperationException("singular system");
                var tmp = M[c]; M[c] = M[p]; M[p] = tmp;
                for (int r = c + 1; r < n; r++)
                {
                    double f = M[r][c] / M[c][c];
                    for (int k = c; k <= n; k++) M[r][k] -= f * M[c][k];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = M[r][n];
                for (int k = r + 1; k < n; k++) s -= M[r][k] * x[k];
                x[r] = s / M[r][r];
            }
            return x;
        }

        public static string Fmt(double v, int d = 2)
        {
            if (!Js.IsFinite(v)) return "—";
            return Js.ToFixed(v, d);
        }
    }
}
