// C# port of the fdlibm functions in V8's src/base/ieee754.cc (V8 12.4, the
// engine of Node 22 and of the browsers the JavaScript game runs in). JavaScript's
// Math.sin / cos / tan / exp / log / atan / atan2 / asin / tanh / pow are these
// functions, so with them the C# simulation gives the same bits as the browser.
// System.Math calls the platform C library instead (glibc, musl on WebGL, the
// MSVC runtime, ...) whose last bit differs now and then.
//
// ====================================================
// Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
//
// Developed at SunSoft, a Sun Microsystems, Inc. business.
// Permission to use, copy, modify, and distribute this
// software is freely granted, provided that this notice
// is preserved.
// ====================================================
// Modified by the V8 project authors (BSD licence, see the V8 source).

using System;

namespace Duburi.Core
{
    public static class Fdlibm
    {
        // ---- word access (EXTRACT_WORDS / GET_HIGH_WORD / ... of the C code)
        static int Hi(double d) => (int)(BitConverter.DoubleToInt64Bits(d) >> 32);
        static uint Lo(double d) => unchecked((uint)BitConverter.DoubleToInt64Bits(d));
        static double Words(int hi, uint lo) => BitConverter.Int64BitsToDouble(((long)hi << 32) | lo);
        static double SetHi(double d, int hi) => Words(hi, Lo(d));
        static double SetLo(double d, uint lo) => Words(Hi(d), lo);

        // fdlibm s_scalbn.c: x * 2^n, exact (correctly rounded when the result is subnormal)
        public static double Scalbn(double x, int n)
        {
            const double two54 = 1.80143985094819840000e+16, twom54 = 5.55111512312578270212e-17, huge = 1.0e+300, tiny = 1.0e-300;
            int hx = Hi(x);
            uint lx = Lo(x);
            int k = (hx & 0x7ff00000) >> 20;
            if (k == 0)
            {
                if ((lx | (uint)(hx & 0x7fffffff)) == 0) return x; // +-0
                x *= two54;
                hx = Hi(x);
                k = ((hx & 0x7ff00000) >> 20) - 54;
                if (n < -50000) return tiny * x; // underflow
            }
            if (k == 0x7ff) return x + x; // NaN or Inf
            k = k + n;
            if (k > 0x7fe) return huge * CopySign(huge, x); // overflow
            if (k > 0) return SetHi(x, unchecked((int)((uint)hx & 0x800fffff)) | (k << 20)); // normal result
            if (k <= -54)
            {
                if (n > 50000) return huge * CopySign(huge, x); // overflow
                return tiny * CopySign(tiny, x); // underflow
            }
            k += 54; // subnormal result
            x = SetHi(x, unchecked((int)((uint)hx & 0x800fffff)) | (k << 20));
            return x * twom54;
        }

        static double CopySign(double mag, double sgn) =>
            BitConverter.Int64BitsToDouble((BitConverter.DoubleToInt64Bits(mag) & 0x7FFFFFFFFFFFFFFF) | (BitConverter.DoubleToInt64Bits(sgn) & unchecked((long)0x8000000000000000)));

        // ------------------------------------------------------------ argument reduction
        static readonly int[] two_over_pi = {
            0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62, 0x95993C,
            0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A, 0x424DD2, 0xE00649,
            0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129, 0xA73EE8, 0x8235F5, 0x2EBB44,
            0x84E99C, 0x7026B4, 0x5F7E41, 0x3991D6, 0x398353, 0x39F49C, 0x845F8B,
            0xBDF928, 0x3B1FF8, 0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D,
            0x367ECF, 0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5,
            0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08, 0x560330,
            0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3, 0x91615E, 0xE61B08,
            0x659985, 0x5F14A0, 0x68408D, 0xFFD880, 0x4D7327, 0x310606, 0x1556CA,
            0x73A8C9, 0x60E27B, 0xC08C6B,
        };

        static readonly int[] npio2_hw = {
            0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C,
            0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C,
            0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A,
            0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
            0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB,
            0x404858EB, 0x404921FB,
        };

        // __ieee754_rem_pio2: the remainder of x rem pi/2 in y0 + y1, returns n
        static int RemPio2(double x, out double y0, out double y1)
        {
            const double
                zero = 0.00000000000000000000e+00,
                half = 5.00000000000000000000e-01,
                two24 = 1.67772160000000000000e+07,
                invpio2 = 6.36619772367581382433e-01,
                pio2_1 = 1.57079632673412561417e+00,
                pio2_1t = 6.07710050650619224932e-11,
                pio2_2 = 6.07710050630396597660e-11,
                pio2_2t = 2.02226624879595063154e-21,
                pio2_3 = 2.02226624871116645580e-21,
                pio2_3t = 8.47842766036889956997e-32;

            double z = 0, w, t, r, fn;
            int e0, i, j, nx, n, ix, hx;

            hx = Hi(x);
            ix = hx & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) { y0 = x; y1 = 0; return 0; } // |x| ~<= pi/4
            if (ix < 0x4002D97C)
            { // |x| < 3pi/4, special case with n=+-1
                if (hx > 0)
                {
                    z = x - pio2_1;
                    if (ix != 0x3FF921FB) { y0 = z - pio2_1t; y1 = (z - y0) - pio2_1t; }
                    else { z -= pio2_2; y0 = z - pio2_2t; y1 = (z - y0) - pio2_2t; }
                    return 1;
                }
                else
                {
                    z = x + pio2_1;
                    if (ix != 0x3FF921FB) { y0 = z + pio2_1t; y1 = (z - y0) + pio2_1t; }
                    else { z += pio2_2; y0 = z + pio2_2t; y1 = (z - y0) + pio2_2t; }
                    return -1;
                }
            }
            if (ix <= 0x413921FB)
            { // |x| ~<= 2^19*(pi/2), medium size
                t = Math.Abs(x);
                n = (int)(t * invpio2 + half);
                fn = n;
                r = t - fn * pio2_1;
                w = fn * pio2_1t; // 1st round good to 85 bit
                if (n < 32 && ix != npio2_hw[n - 1])
                {
                    y0 = r - w; // quick check no cancellation
                }
                else
                {
                    j = ix >> 20;
                    y0 = r - w;
                    int high = Hi(y0);
                    i = j - ((high >> 20) & 0x7FF);
                    if (i > 16)
                    { // 2nd iteration needed, good to 118
                        t = r;
                        w = fn * pio2_2;
                        r = t - w;
                        w = fn * pio2_2t - ((t - r) - w);
                        y0 = r - w;
                        high = Hi(y0);
                        i = j - ((high >> 20) & 0x7FF);
                        if (i > 49)
                        { // 3rd iteration need, 151 bits acc
                            t = r;
                            w = fn * pio2_3;
                            r = t - w;
                            w = fn * pio2_3t - ((t - r) - w);
                            y0 = r - w;
                        }
                    }
                }
                y1 = (r - y0) - w;
                if (hx < 0) { y0 = -y0; y1 = -y1; return -n; }
                return n;
            }
            // all other (large) arguments
            if (ix >= 0x7FF00000) { y0 = y1 = x - x; return 0; } // inf or NaN
            // set z = scalbn(|x|,ilogb(x)-23)
            z = SetLo(z, Lo(x));
            e0 = (ix >> 20) - 1046;
            z = SetHi(z, ix - (int)((uint)e0 << 20));
            var tx = new double[3];
            for (i = 0; i < 2; i++)
            {
                tx[i] = (int)z;
                z = (z - tx[i]) * two24;
            }
            tx[2] = z;
            nx = 3;
            while (tx[nx - 1] == zero) nx--; // skip zero term
            var y = new double[3];
            n = KernelRemPio2(tx, y, e0, nx, 2, two_over_pi);
            if (hx < 0) { y0 = -y[0]; y1 = -y[1]; return -n; }
            y0 = y[0]; y1 = y[1];
            return n;
        }

        static readonly int[] init_jk = { 2, 3, 4, 6 };
        static readonly double[] PIo2 = {
            1.57079625129699707031e+00, 7.54978941586159635335e-08, 5.39030252995776476554e-15,
            3.28200341580791294123e-22, 1.27065575308067607349e-29, 1.22933308981111328932e-36,
            2.73370053816464559624e-44, 2.16741683877804819444e-51,
        };

        static int KernelRemPio2(double[] x, double[] y, int e0, int nx, int prec, int[] ipio2)
        {
            const double zero = 0.0, one = 1.0, two24 = 1.67772160000000000000e+07, twon24 = 5.96046447753906250000e-08;
            int jz, jx, jv, jp, jk, carry, n, i, j, k, m, q0, ih;
            var iq = new int[20];
            double z, fw;
            var f = new double[20];
            var fq = new double[20];
            var q = new double[20];

            jk = init_jk[prec];
            jp = jk;

            jx = nx - 1;
            jv = (e0 - 3) / 24;
            if (jv < 0) jv = 0;
            q0 = e0 - 24 * (jv + 1);

            j = jv - jx;
            m = jx + jk;
            for (i = 0; i <= m; i++, j++) f[i] = (j < 0) ? zero : (double)ipio2[j];

            for (i = 0; i <= jk; i++)
            {
                for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j];
                q[i] = fw;
            }

            jz = jk;
            while (true)
            { // recompute:
                for (i = 0, j = jz, z = q[jz]; j > 0; i++, j--)
                {
                    fw = (int)(twon24 * z);
                    iq[i] = (int)(z - two24 * fw);
                    z = q[j - 1] + fw;
                }

                z = Scalbn(z, q0);
                z -= 8.0 * Math.Floor(z * 0.125);
                n = (int)z;
                z -= n;
                ih = 0;
                if (q0 > 0)
                {
                    i = (iq[jz - 1] >> (24 - q0));
                    n += i;
                    iq[jz - 1] -= i << (24 - q0);
                    ih = iq[jz - 1] >> (23 - q0);
                }
                else if (q0 == 0)
                {
                    ih = iq[jz - 1] >> 23;
                }
                else if (z >= 0.5)
                {
                    ih = 2;
                }

                if (ih > 0)
                {
                    n += 1;
                    carry = 0;
                    for (i = 0; i < jz; i++)
                    {
                        j = iq[i];
                        if (carry == 0)
                        {
                            if (j != 0) { carry = 1; iq[i] = 0x1000000 - j; }
                        }
                        else
                        {
                            iq[i] = 0xFFFFFF - j;
                        }
                    }
                    if (q0 > 0)
                    {
                        switch (q0)
                        {
                            case 1: iq[jz - 1] &= 0x7FFFFF; break;
                            case 2: iq[jz - 1] &= 0x3FFFFF; break;
                        }
                    }
                    if (ih == 2)
                    {
                        z = one - z;
                        if (carry != 0) z -= Scalbn(one, q0);
                    }
                }

                if (z == zero)
                {
                    j = 0;
                    for (i = jz - 1; i >= jk; i--) j |= iq[i];
                    if (j == 0)
                    { // need recomputation
                        for (k = 1; jk >= k && iq[jk - k] == 0; k++) { }
                        for (i = jz + 1; i <= jz + k; i++)
                        {
                            f[jx + i] = ipio2[jv + i];
                            for (j = 0, fw = 0.0; j <= jx; j++) fw += x[j] * f[jx + i - j];
                            q[i] = fw;
                        }
                        jz += k;
                        continue;
                    }
                }
                break;
            }

            // chop off zero terms
            if (z == 0.0)
            {
                jz -= 1;
                q0 -= 24;
                while (iq[jz] == 0) { jz--; q0 -= 24; }
            }
            else
            { // break z into 24-bit if necessary
                z = Scalbn(z, -q0);
                if (z >= two24)
                {
                    fw = (int)(twon24 * z);
                    iq[jz] = (int)(z - two24 * fw);
                    jz += 1;
                    q0 += 24;
                    iq[jz] = (int)fw;
                }
                else
                {
                    iq[jz] = (int)z;
                }
            }

            // convert integer "bit" chunk to floating-point value
            fw = Scalbn(one, q0);
            for (i = jz; i >= 0; i--)
            {
                q[i] = fw * iq[i];
                fw *= twon24;
            }

            // compute PIo2[0,...,jp]*q[jz,...,0]
            for (i = jz; i >= 0; i--)
            {
                for (fw = 0.0, k = 0; k <= jp && k <= jz - i; k++) fw += PIo2[k] * q[i + k];
                fq[jz - i] = fw;
            }

            // compress fq[] into y[]
            switch (prec)
            {
                case 0:
                    fw = 0.0;
                    for (i = jz; i >= 0; i--) fw += fq[i];
                    y[0] = (ih == 0) ? fw : -fw;
                    break;
                case 1:
                case 2:
                    fw = 0.0;
                    for (i = jz; i >= 0; i--) fw += fq[i];
                    y[0] = (ih == 0) ? fw : -fw;
                    fw = fq[0] - fw;
                    for (i = 1; i <= jz; i++) fw += fq[i];
                    y[1] = (ih == 0) ? fw : -fw;
                    break;
                case 3:
                    for (i = jz; i > 0; i--)
                    {
                        fw = fq[i - 1] + fq[i];
                        fq[i] += fq[i - 1] - fw;
                        fq[i - 1] = fw;
                    }
                    for (i = jz; i > 1; i--)
                    {
                        fw = fq[i - 1] + fq[i];
                        fq[i] += fq[i - 1] - fw;
                        fq[i - 1] = fw;
                    }
                    for (fw = 0.0, i = jz; i >= 2; i--) fw += fq[i];
                    if (ih == 0) { y[0] = fq[0]; y[1] = fq[1]; y[2] = fw; }
                    else { y[0] = -fq[0]; y[1] = -fq[1]; y[2] = -fw; }
                    break;
            }
            return n & 7;
        }

        // ------------------------------------------------------------ kernels on [-pi/4, pi/4]
        static double KernelCos(double x, double y)
        {
            const double
                one = 1.00000000000000000000e+00,
                C1 = 4.16666666666666019037e-02,
                C2 = -1.38888888888741095749e-03,
                C3 = 2.48015872894767294178e-05,
                C4 = -2.75573143513906633035e-07,
                C5 = 2.08757232129817482790e-09,
                C6 = -1.13596475577881948265e-11;
            double a, iz, z, r, qx;
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000)
            { // if x < 2**27
                if ((int)x == 0) return one;
            }
            z = x * x;
            r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
            if (ix < 0x3FD33333) return one - (0.5 * z - (z * r - x * y)); // |x| < 0.3
            if (ix > 0x3FE90000) qx = 0.28125; // x > 0.78125
            else qx = Words(ix - 0x00200000, 0); // x/4
            iz = 0.5 * z - qx;
            a = one - qx;
            return a - (iz - (z * r - x * y));
        }

        static double KernelSin(double x, double y, int iy)
        {
            const double
                half = 5.00000000000000000000e-01,
                S1 = -1.66666666666666324348e-01,
                S2 = 8.33333333332248946124e-03,
                S3 = -1.98412698298579493134e-04,
                S4 = 2.75573137070700676789e-06,
                S5 = -2.50507602534068634195e-08,
                S6 = 1.58969099521155010221e-10;
            double z, r, v;
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix < 0x3E400000)
            { // |x| < 2**-27
                if ((int)x == 0) return x;
            }
            z = x * x;
            v = z * x;
            r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
            if (iy == 0) return x + v * (S1 + z * r);
            return x - ((z * (half * y - v * r) - y) - v * S1);
        }

        static readonly double[] T = {
            3.33333333333334091986e-01, 1.33333333333201242699e-01, 5.39682539762260521377e-02,
            2.18694882948595424599e-02, 8.86323982359930005737e-03, 3.59207910759131235356e-03,
            1.45620945432529025516e-03, 5.88041240820264096874e-04, 2.46463134818469906812e-04,
            7.81794442939557092300e-05, 7.14072491382608190305e-05, -1.85586374855275456654e-05,
            2.59073051863633712884e-05,
        };

        static double KernelTan(double x, double y, int iy)
        {
            const double one = 1.00000000000000000000e+00, pio4 = 7.85398163397448278999e-01, pio4lo = 3.06161699786838301793e-17;
            double z, r, v, w, s;
            int hx = Hi(x);
            int ix = hx & 0x7FFFFFFF;
            if (ix < 0x3E300000)
            { // x < 2**-28
                if ((int)x == 0)
                {
                    uint low = Lo(x);
                    if (((uint)ix | low | (uint)(iy + 1)) == 0)
                    {
                        return one / Math.Abs(x);
                    }
                    else
                    {
                        if (iy == 1) return x;
                        // compute -1 / (x+y) carefully
                        double a, t;
                        z = w = x + y;
                        z = SetLo(z, 0);
                        v = y - (z - x);
                        t = a = -one / w;
                        t = SetLo(t, 0);
                        s = one + t * z;
                        return t + a * (s + t * v);
                    }
                }
            }
            if (ix >= 0x3FE59428)
            { // |x| >= 0.6744
                if (hx < 0) { x = -x; y = -y; }
                z = pio4 - x;
                w = pio4lo - y;
                x = z + w;
                y = 0.0;
            }
            z = x * x;
            w = z * z;
            r = T[1] + w * (T[3] + w * (T[5] + w * (T[7] + w * (T[9] + w * T[11]))));
            v = z * (T[2] + w * (T[4] + w * (T[6] + w * (T[8] + w * (T[10] + w * T[12])))));
            s = z * x;
            r = y + z * (s * (r + v) + y);
            r += T[0] * s;
            w = x + r;
            if (ix >= 0x3FE59428)
            {
                v = iy;
                return (1 - ((hx >> 30) & 2)) * (v - 2.0 * (x - (w * w / (w + v) - r)));
            }
            if (iy == 1) return w;
            {
                double a, t;
                z = w;
                z = SetLo(z, 0);
                v = r - (z - x); // z+v = r+x
                t = a = -1.0 / w; // a = -1.0/w
                t = SetLo(t, 0);
                s = 1.0 + t * z;
                return t + a * (s + t * v);
            }
        }

        // ------------------------------------------------------------ public functions
        public static double Sin(double x)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return KernelSin(x, 0.0, 0);
            if (ix >= 0x7FF00000) return x - x;
            int n = RemPio2(x, out double y0, out double y1);
            switch (n & 3)
            {
                case 0: return KernelSin(y0, y1, 1);
                case 1: return KernelCos(y0, y1);
                case 2: return -KernelSin(y0, y1, 1);
                default: return -KernelCos(y0, y1);
            }
        }

        public static double Cos(double x)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return KernelCos(x, 0.0);
            if (ix >= 0x7FF00000) return x - x;
            int n = RemPio2(x, out double y0, out double y1);
            switch (n & 3)
            {
                case 0: return KernelCos(y0, y1);
                case 1: return -KernelSin(y0, y1, 1);
                case 2: return -KernelCos(y0, y1);
                default: return KernelSin(y0, y1, 1);
            }
        }

        public static double Tan(double x)
        {
            int ix = Hi(x) & 0x7FFFFFFF;
            if (ix <= 0x3FE921FB) return KernelTan(x, 0.0, 1);
            if (ix >= 0x7FF00000) return x - x;
            int n = RemPio2(x, out double y0, out double y1);
            return KernelTan(y0, y1, 1 - ((n & 1) << 1));
        }

        public static double Asin(double x)
        {
            const double
                one = 1.00000000000000000000e+00,
                huge = 1.000e+300,
                pio2_hi = 1.57079632679489655800e+00,
                pio2_lo = 6.12323399573676603587e-17,
                pio4_hi = 7.85398163397448278999e-01,
                pS0 = 1.66666666666666657415e-01,
                pS1 = -3.25565818622400915405e-01,
                pS2 = 2.01212532134862925881e-01,
                pS3 = -4.00555345006794114027e-02,
                pS4 = 7.91534994289814532176e-04,
                pS5 = 3.47933107596021167570e-05,
                qS1 = -2.40339491173441421878e+00,
                qS2 = 2.02094576023350569471e+00,
                qS3 = -6.88283971605453293030e-01,
                qS4 = 7.70381505559019352791e-02;
            double t = 0, w, p, q, c, r, s;
            int hx = Hi(x);
            int ix = hx & 0x7FFFFFFF;
            if (ix >= 0x3FF00000)
            { // |x|>= 1
                uint lx = Lo(x);
                if (((uint)(ix - 0x3FF00000) | lx) == 0) return x * pio2_hi + x * pio2_lo; // asin(1)=+-pi/2
                return double.NaN; // asin(|x|>1) is NaN
            }
            else if (ix < 0x3FE00000)
            { // |x|<0.5
                if (ix < 0x3E400000)
                {
                    if (huge + x > one) return x;
                }
                else
                {
                    t = x * x;
                }
                p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
                q = one + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
                w = p / q;
                return x + x * w;
            }
            // 1> |x|>= 0.5
            w = one - Math.Abs(x);
            t = w * 0.5;
            p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
            q = one + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
            s = Math.Sqrt(t);
            if (ix >= 0x3FEF3333)
            { // if |x| > 0.975
                w = p / q;
                t = pio2_hi - (2.0 * (s + s * w) - pio2_lo);
            }
            else
            {
                w = s;
                w = SetLo(w, 0);
                c = (t - w * w) / (s + w);
                r = p / q;
                p = 2.0 * s * r - (pio2_lo - 2.0 * c);
                q = pio4_hi - 2.0 * w;
                t = pio4_hi - (p - q);
            }
            return hx > 0 ? t : -t;
        }

        static readonly double[] atanhi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
        static readonly double[] atanlo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
        static readonly double[] aT = {
            3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01,
            -1.11111104054623557880e-01, 9.09088713343650656196e-02, -7.69187620504482999495e-02,
            6.66107313738753120669e-02, -5.83357013379057348645e-02, 4.97687799461593236017e-02,
            -3.65315727442169155270e-02, 1.62858201153657823623e-02,
        };

        public static double Atan(double x)
        {
            const double one = 1.0, huge = 1.0e300;
            double w, s1, s2, z;
            int ix, hx, id;
            hx = Hi(x);
            ix = hx & 0x7FFFFFFF;
            if (ix >= 0x44100000)
            { // if |x| >= 2^66
                uint low = Lo(x);
                if (ix > 0x7FF00000 || (ix == 0x7FF00000 && (low != 0))) return x + x; // NaN
                if (hx > 0) return atanhi[3] + atanlo[3];
                return -atanhi[3] - atanlo[3];
            }
            if (ix < 0x3FDC0000)
            { // |x| < 0.4375
                if (ix < 0x3E400000)
                { // |x| < 2^-27
                    if (huge + x > one) return x;
                }
                id = -1;
            }
            else
            {
                x = Math.Abs(x);
                if (ix < 0x3FF30000)
                { // |x| < 1.1875
                    if (ix < 0x3FE60000) { id = 0; x = (2.0 * x - one) / (2.0 + x); }
                    else { id = 1; x = (x - one) / (x + one); }
                }
                else
                {
                    if (ix < 0x40038000) { id = 2; x = (x - 1.5) / (one + 1.5 * x); }
                    else { id = 3; x = -1.0 / x; }
                }
            }
            z = x * x;
            w = z * z;
            s1 = z * (aT[0] + w * (aT[2] + w * (aT[4] + w * (aT[6] + w * (aT[8] + w * aT[10])))));
            s2 = w * (aT[1] + w * (aT[3] + w * (aT[5] + w * (aT[7] + w * aT[9]))));
            if (id < 0) return x - x * (s1 + s2);
            z = atanhi[id] - ((x * (s1 + s2) - atanlo[id]) - x);
            return (hx < 0) ? -z : z;
        }

        public static double Atan2(double y, double x)
        {
            const double tiny = 1.0e-300, zero = 0.0,
                pi_o_4 = 7.8539816339744827900E-01,
                pi_o_2 = 1.5707963267948965580E+00,
                pi = 3.1415926535897931160E+00,
                pi_lo = 1.2246467991473531772E-16;
            double z;
            int k, m, hx, hy, ix, iy;
            uint lx, ly;
            hx = Hi(x); lx = Lo(x);
            ix = hx & 0x7FFFFFFF;
            hy = Hi(y); ly = Lo(y);
            iy = hy & 0x7FFFFFFF;
            if (((uint)ix | ((lx | unchecked((uint)-(int)lx)) >> 31)) > 0x7FF00000u ||
                ((uint)iy | ((ly | unchecked((uint)-(int)ly)) >> 31)) > 0x7FF00000u)
                return x + y; // x or y is NaN
            if ((unchecked((uint)(hx - 0x3FF00000)) | lx) == 0) return Atan(y); // x=1.0
            m = ((hy >> 31) & 1) | ((hx >> 30) & 2); // 2*sign(x)+sign(y)

            if (((uint)iy | ly) == 0)
            { // when y = 0
                switch (m)
                {
                    case 0:
                    case 1: return y;
                    case 2: return pi + tiny;
                    case 3: return -pi - tiny;
                }
            }
            if (((uint)ix | lx) == 0) return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny; // x = 0

            if (ix == 0x7FF00000)
            { // x is INF
                if (iy == 0x7FF00000)
                {
                    switch (m)
                    {
                        case 0: return pi_o_4 + tiny;
                        case 1: return -pi_o_4 - tiny;
                        case 2: return 3.0 * pi_o_4 + tiny;
                        case 3: return -3.0 * pi_o_4 - tiny;
                    }
                }
                else
                {
                    switch (m)
                    {
                        case 0: return zero;
                        case 1: return -zero;
                        case 2: return pi + tiny;
                        case 3: return -pi - tiny;
                    }
                }
            }
            if (iy == 0x7FF00000) return (hy < 0) ? -pi_o_2 - tiny : pi_o_2 + tiny; // y is INF

            k = (iy - ix) >> 20;
            if (k > 60)
            { // |y/x| > 2**60
                z = pi_o_2 + 0.5 * pi_lo;
                m &= 1;
            }
            else if (hx < 0 && k < -60)
            {
                z = 0.0; // 0 > |y|/x > -2**-60
            }
            else
            {
                z = Atan(Math.Abs(y / x));
            }
            switch (m)
            {
                case 0: return z;
                case 1: return -z;
                case 2: return pi - (z - pi_lo);
                default: return (z - pi_lo) - pi;
            }
        }

        public static double Exp(double x)
        {
            const double one = 1.0,
                o_threshold = 7.09782712893383973096e+02,
                u_threshold = -7.45133219101941108420e+02,
                invln2 = 1.44269504088896338700e+00,
                P1 = 1.66666666666666019037e-01,
                P2 = -2.77777777770155933842e-03,
                P3 = 6.61375632143793436117e-05,
                P4 = -1.65339022054652515390e-06,
                P5 = 4.13813679705723846039e-08,
                E = 2.718281828459045,
                huge = 1.0e+300,
                twom1000 = 9.33263618503218878990e-302,
                two1023 = 8.988465674311579539e307;
            double y, hi = 0.0, lo = 0.0, c, t, twopk;
            int k = 0, xsb;
            uint hx = unchecked((uint)Hi(x));
            xsb = (int)((hx >> 31) & 1);
            hx &= 0x7FFFFFFF;

            if (hx >= 0x40862E42)
            { // |x|>=709.78...
                if (hx >= 0x7FF00000)
                {
                    uint lx = Lo(x);
                    if (((hx & 0xFFFFF) | lx) != 0) return x + x; // NaN
                    return (xsb == 0) ? x : 0.0; // exp(+-inf)={inf,0}
                }
                if (x > o_threshold) return huge * huge; // overflow
                if (x < u_threshold) return twom1000 * twom1000; // underflow
            }

            if (hx > 0x3FD62E42)
            { // |x| > 0.5 ln2
                if (hx < 0x3FF0A2B2)
                { // and |x| < 1.5 ln2
                    if (x == 1.0) return E;
                    hi = x - (xsb == 0 ? 6.93147180369123816490e-01 : -6.93147180369123816490e-01);
                    lo = xsb == 0 ? 1.90821492927058770002e-10 : -1.90821492927058770002e-10;
                    k = 1 - xsb - xsb;
                }
                else
                {
                    k = (int)(invln2 * x + (xsb == 0 ? 0.5 : -0.5));
                    t = k;
                    hi = x - t * 6.93147180369123816490e-01; // t*ln2HI is exact here
                    lo = t * 1.90821492927058770002e-10;
                }
                x = hi - lo;
            }
            else if (hx < 0x3E300000)
            { // when |x|<2**-28
                if (huge + x > one) return one + x;
            }
            else
            {
                k = 0;
            }

            t = x * x;
            if (k >= -1021) twopk = Words(unchecked(0x3FF00000 + (int)((uint)k << 20)), 0);
            else twopk = Words(unchecked(0x3FF00000 + (int)((uint)(k + 1000) << 20)), 0);
            c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            if (k == 0) return one - ((x * c) / (c - 2.0) - x);
            y = one - ((lo - (x * c) / (2.0 - c)) - hi);
            if (k >= -1021)
            {
                if (k == 1024) return y * 2.0 * two1023;
                return y * twopk;
            }
            return y * twopk * twom1000;
        }

        public static double Log(double x)
        {
            const double
                ln2_hi = 6.93147180369123816490e-01,
                ln2_lo = 1.90821492927058770002e-10,
                two54 = 1.80143985094819840000e+16,
                Lg1 = 6.666666666666735130e-01,
                Lg2 = 3.999999999940941908e-01,
                Lg3 = 2.857142874366239149e-01,
                Lg4 = 2.222219843214978396e-01,
                Lg5 = 1.818357216161805012e-01,
                Lg6 = 1.531383769920937332e-01,
                Lg7 = 1.479819860511658591e-01,
                zero = 0.0;
            double hfsq, f, s, z, R, w, t1, t2, dk;
            int k, hx, i, j;
            uint lx;
            hx = Hi(x); lx = Lo(x);
            k = 0;
            if (hx < 0x00100000)
            { // x < 2**-1022
                if (((uint)(hx & 0x7FFFFFFF) | lx) == 0) return double.NegativeInfinity; // log(+-0)=-inf
                if (hx < 0) return double.NaN; // log(-#) = NaN
                k -= 54;
                x *= two54; // subnormal number, scale up x
                hx = Hi(x);
            }
            if (hx >= 0x7FF00000) return x + x;
            k += (hx >> 20) - 1023;
            hx &= 0x000FFFFF;
            i = (hx + 0x95F64) & 0x100000;
            x = SetHi(x, hx | (i ^ 0x3FF00000)); // normalize x or x/2
            k += (i >> 20);
            f = x - 1.0;
            if ((0x000FFFFF & (2 + hx)) < 3)
            { // -2**-20 <= f < 2**-20
                if (f == zero)
                {
                    if (k == 0) return zero;
                    dk = k;
                    return dk * ln2_hi + dk * ln2_lo;
                }
                R = f * f * (0.5 - 0.33333333333333333 * f);
                if (k == 0) return f - R;
                dk = k;
                return dk * ln2_hi - ((R - dk * ln2_lo) - f);
            }
            s = f / (2.0 + f);
            dk = k;
            z = s * s;
            i = hx - 0x6147A;
            w = z * z;
            j = 0x6B851 - hx;
            t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
            t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
            i |= j;
            R = t2 + t1;
            if (i > 0)
            {
                hfsq = 0.5 * f * f;
                if (k == 0) return f - (hfsq - s * (hfsq + R));
                return dk * ln2_hi - ((hfsq - (s * (hfsq + R) + dk * ln2_lo)) - f);
            }
            if (k == 0) return f - s * (f - R);
            return dk * ln2_hi - ((s * (f - R) - dk * ln2_lo) - f);
        }

        public static double Expm1(double x)
        {
            const double one = 1.0, tiny = 1.0e-300,
                o_threshold = 7.09782712893383973096e+02,
                ln2_hi = 6.93147180369123816490e-01,
                ln2_lo = 1.90821492927058770002e-10,
                invln2 = 1.44269504088896338700e+00,
                Q1 = -3.33333333333331316428e-02,
                Q2 = 1.58730158725481460165e-03,
                Q3 = -7.93650757867487942473e-05,
                Q4 = 4.00821782732936239552e-06,
                Q5 = -2.01099218183624371326e-07,
                huge = 1.0e+300;
            double y, hi, lo, c = 0, t, e, hxs, hfx, r1, twopk;
            int k, xsb;
            uint hx = unchecked((uint)Hi(x));
            xsb = unchecked((int)(hx & 0x80000000)); // sign bit of x
            hx &= 0x7FFFFFFF;

            if (hx >= 0x4043687A)
            { // |x|>=56*ln2
                if (hx >= 0x40862E42)
                { // |x|>=709.78...
                    if (hx >= 0x7FF00000)
                    {
                        uint low = Lo(x);
                        if (((hx & 0xFFFFF) | low) != 0) return x + x; // NaN
                        return (xsb == 0) ? x : -1.0; // exp(+-inf)={inf,-1}
                    }
                    if (x > o_threshold) return huge * huge; // overflow
                }
                if (xsb != 0)
                { // x < -56*ln2, return -1.0 with inexact
                    if (x + tiny < 0.0) return tiny - one;
                }
            }

            if (hx > 0x3FD62E42)
            { // |x| > 0.5 ln2
                if (hx < 0x3FF0A2B2)
                { // and |x| < 1.5 ln2
                    if (xsb == 0) { hi = x - ln2_hi; lo = ln2_lo; k = 1; }
                    else { hi = x + ln2_hi; lo = -ln2_lo; k = -1; }
                }
                else
                {
                    k = (int)(invln2 * x + ((xsb == 0) ? 0.5 : -0.5));
                    t = k;
                    hi = x - t * ln2_hi; // t*ln2_hi is exact here
                    lo = t * ln2_lo;
                }
                x = hi - lo;
                c = (hi - x) - lo;
            }
            else if (hx < 0x3C900000)
            { // when |x|<2**-54, return x
                t = huge + x;
                return x - (t - (huge + x));
            }
            else
            {
                k = 0;
            }

            hfx = 0.5 * x;
            hxs = x * hfx;
            r1 = one + hxs * (Q1 + hxs * (Q2 + hxs * (Q3 + hxs * (Q4 + hxs * Q5))));
            t = 3.0 - r1 * hfx;
            e = hxs * ((r1 - t) / (6.0 - x * t));
            if (k == 0) return x - (x * e - hxs); // c is 0
            twopk = Words(unchecked(0x3FF00000 + (int)((uint)k << 20)), 0); // 2^k
            e = (x * (e - c) - c);
            e -= hxs;
            if (k == -1) return 0.5 * (x - e) - 0.5;
            if (k == 1)
            {
                if (x < -0.25) return -2.0 * (e - (x + 0.5));
                return one + 2.0 * (x - e);
            }
            if (k <= -2 || k > 56)
            { // suffice to return exp(x)-1
                y = one - (e - x);
                if (k == 1024) y = y * 2.0 * 8.98846567431158e+307;
                else y = y * twopk;
                return y - one;
            }
            t = one;
            if (k < 20)
            {
                t = SetHi(t, 0x3FF00000 - (0x200000 >> k)); // t=1-2^-k
                y = t - (e - x);
                y = y * twopk;
            }
            else
            {
                t = SetHi(t, ((0x3FF - k) << 20)); // 2^-k
                y = x - (e + t);
                y += one;
                y = y * twopk;
            }
            return y;
        }

        public static double Tanh(double x)
        {
            const double tiny = 1.0e-300, one = 1.0, two = 2.0, huge = 1.0e300;
            double t, z;
            int jx = Hi(x);
            int ix = jx & 0x7FFFFFFF;
            if (ix >= 0x7FF00000)
            { // x is INF or NaN
                if (jx >= 0) return one / x + one; // tanh(+-inf)=+-1
                return one / x - one; // tanh(NaN) = NaN
            }
            if (ix < 0x40360000)
            { // |x| < 22
                if (ix < 0x3E300000)
                { // |x|<2**-28
                    if (huge + x > one) return x; // tanh(tiny) = tiny with inexact
                }
                if (ix >= 0x3FF00000)
                { // |x|>=1
                    t = Expm1(two * Math.Abs(x));
                    z = one - two / (t + two);
                }
                else
                {
                    t = Expm1(-two * Math.Abs(x));
                    z = -t / (t + two);
                }
            }
            else
            { // |x| >= 22, return +-1
                z = one - tiny;
            }
            return (jx >= 0) ? z : -z;
        }

        public static double Pow(double x, double y)
        {
            const double
                zero = 0.0, one = 1.0, two = 2.0,
                two53 = 9007199254740992.0,
                huge = 1.0e300, tiny = 1.0e-300,
                L1 = 5.99999999999994648725e-01,
                L2 = 4.28571428578550184252e-01,
                L3 = 3.33333329818377432918e-01,
                L4 = 2.72728123808534006489e-01,
                L5 = 2.30660745775561754067e-01,
                L6 = 2.06975017800338417784e-01,
                P1 = 1.66666666666666019037e-01,
                P2 = -2.77777777770155933842e-03,
                P3 = 6.61375632143793436117e-05,
                P4 = -1.65339022054652515390e-06,
                P5 = 4.13813679705723846039e-08,
                lg2 = 6.93147180559945286227e-01,
                lg2_h = 6.93147182464599609375e-01,
                lg2_l = -1.90465429995776804525e-09,
                ovt = 8.0085662595372944372e-0017,
                cp = 9.61796693925975554329e-01,
                cp_h = 9.61796700954437255859e-01,
                cp_l = -7.02846165095275826516e-09,
                ivln2 = 1.44269504088896338700e+00,
                ivln2_h = 1.44269502162933349609e+00,
                ivln2_l = 1.92596299112661746887e-08;
            double z, ax, z_h, z_l, p_h, p_l;
            double y1, t1, t2, r, s, t, u, v, w;
            int i, j, k, yisint, n;
            int hx, hy, ix, iy;
            uint lx, ly;

            hx = Hi(x); lx = Lo(x);
            hy = Hi(y); ly = Lo(y);
            ix = hx & 0x7fffffff;
            iy = hy & 0x7fffffff;

            if (((uint)iy | ly) == 0) return one; // y==zero: x**0 = 1

            // +-NaN return x+y
            if (ix > 0x7ff00000 || ((ix == 0x7ff00000) && (lx != 0)) || iy > 0x7ff00000 || ((iy == 0x7ff00000) && (ly != 0)))
                return x + y;

            // determine if y is an odd int when x < 0: 0 not an integer, 1 odd, 2 even
            yisint = 0;
            if (hx < 0)
            {
                if (iy >= 0x43400000) yisint = 2;
                else if (iy >= 0x3ff00000)
                {
                    k = (iy >> 20) - 0x3ff;
                    if (k > 20)
                    {
                        j = (int)(ly >> (52 - k));
                        if ((j << (52 - k)) == unchecked((int)ly)) yisint = 2 - (j & 1);
                    }
                    else if (ly == 0)
                    {
                        j = iy >> (20 - k);
                        if ((j << (20 - k)) == iy) yisint = 2 - (j & 1);
                    }
                }
            }

            // special value of y
            if (ly == 0)
            {
                if (iy == 0x7ff00000)
                { // y is +-inf
                    if ((unchecked((uint)(ix - 0x3ff00000)) | lx) == 0) return y - y; // inf**+-1 is NaN
                    if (ix >= 0x3ff00000) return (hy >= 0) ? y : zero; // (|x|>1)**+-inf = inf,0
                    return (hy < 0) ? -y : zero; // (|x|<1)**-,+inf = inf,0
                }
                if (iy == 0x3ff00000) return hy < 0 ? one / x : x; // y is +-1
                if (hy == 0x40000000) return x * x; // y is 2
                if (hy == 0x3fe00000)
                { // y is 0.5
                    if (hx >= 0) return Math.Sqrt(x);
                }
            }

            ax = Math.Abs(x);
            // special value of x
            if (lx == 0)
            {
                if (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000)
                {
                    z = ax; // x is +-0,+-inf,+-1
                    if (hy < 0) z = one / z;
                    if (hx < 0)
                    {
                        if (((ix - 0x3ff00000) | yisint) == 0) z = double.NaN; // (-1)**non-int is NaN
                        else if (yisint == 1) z = -z; // (x<0)**odd = -(|x|**odd)
                    }
                    return z;
                }
            }

            n = (hx >> 31) + 1;

            if ((n | yisint) == 0) return double.NaN; // (x<0)**(non-int) is NaN

            s = one; // sign of result
            if ((n | (yisint - 1)) == 0) s = -one; // (-ve)**(odd int)

            if (iy > 0x41e00000)
            { // |y| > 2**31
                if (iy > 0x43f00000)
                { // |y| > 2**64, must o/uflow
                    if (ix <= 0x3fefffff) return (hy < 0) ? huge * huge : tiny * tiny;
                    if (ix >= 0x3ff00000) return (hy > 0) ? huge * huge : tiny * tiny;
                }
                // over/underflow if x is not close to one
                if (ix < 0x3fefffff) return (hy < 0) ? s * huge * huge : s * tiny * tiny;
                if (ix > 0x3ff00000) return (hy > 0) ? s * huge * huge : s * tiny * tiny;
                // now |1-x| is tiny <= 2**-20
                t = ax - one;
                w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
                u = ivln2_h * t;
                v = t * ivln2_l - w * ivln2;
                t1 = u + v;
                t1 = SetLo(t1, 0);
                t2 = v - (t1 - u);
            }
            else
            {
                double ss, s2, s_h, s_l, t_h, t_l;
                n = 0;
                // take care subnormal number
                if (ix < 0x00100000)
                {
                    ax *= two53;
                    n -= 53;
                    ix = Hi(ax);
                }
                n += ((ix) >> 20) - 0x3ff;
                j = ix & 0x000fffff;
                // determine interval
                ix = j | 0x3ff00000; // normalize ix
                if (j <= 0x3988E) k = 0; // |x|<sqrt(3/2)
                else if (j < 0xBB67A) k = 1; // |x|<sqrt(3)
                else
                {
                    k = 0;
                    n += 1;
                    ix -= 0x00100000;
                }
                ax = SetHi(ax, ix);

                double bp = k == 0 ? 1.0 : 1.5;
                double dp_h = k == 0 ? 0.0 : 5.84962487220764160156e-01;
                double dp_l = k == 0 ? 0.0 : 1.35003920212974897128e-08;

                // compute ss = s_h+s_l = (x-1)/(x+1) or (x-1.5)/(x+1.5)
                u = ax - bp;
                v = one / (ax + bp);
                ss = u * v;
                s_h = ss;
                s_h = SetLo(s_h, 0);
                // t_h=ax+bp[k] High
                t_h = zero;
                t_h = SetHi(t_h, ((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18));
                t_l = ax - (t_h - bp);
                s_l = v * ((u - s_h * t_h) - s_h * t_l);
                // compute log(ax)
                s2 = ss * ss;
                r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
                r += s_l * (s_h + ss);
                s2 = s_h * s_h;
                t_h = 3.0 + s2 + r;
                t_h = SetLo(t_h, 0);
                t_l = r - ((t_h - 3.0) - s2);
                // u+v = ss*(1+...)
                u = s_h * t_h;
                v = s_l * t_h + t_l * ss;
                // 2/(3log2)*(ss+...)
                p_h = u + v;
                p_h = SetLo(p_h, 0);
                p_l = v - (p_h - u);
                z_h = cp_h * p_h; // cp_h+cp_l = 2/(3*log2)
                z_l = cp_l * p_h + p_l * cp + dp_l;
                // log2(ax) = (ss+..)*2/(3*log2) = n + dp_h + z_h + z_l
                t = n;
                t1 = (((z_h + z_l) + dp_h) + t);
                t1 = SetLo(t1, 0);
                t2 = z_l - (((t1 - t) - dp_h) - z_h);
            }

            // split up y into y1+y2 and compute (y1+y2)*(t1+t2)
            y1 = y;
            y1 = SetLo(y1, 0);
            p_l = (y - y1) * t1 + y * t2;
            p_h = y1 * t1;
            z = p_l + p_h;
            j = Hi(z);
            i = unchecked((int)Lo(z));
            if (j >= 0x40900000)
            { // z >= 1024
                if (((j - 0x40900000) | i) != 0) return s * huge * huge; // overflow
                if (p_l + ovt > z - p_h) return s * huge * huge; // overflow
            }
            else if ((j & 0x7fffffff) >= 0x4090cc00)
            { // z <= -1075
                if ((unchecked(j - (int)0xc090cc00) | i) != 0) return s * tiny * tiny; // underflow
                if (p_l <= z - p_h) return s * tiny * tiny; // underflow
            }
            // compute 2**(p_h+p_l)
            i = j & 0x7fffffff;
            k = (i >> 20) - 0x3ff;
            n = 0;
            if (i > 0x3fe00000)
            { // if |z| > 0.5, set n = [z+0.5]
                n = j + (0x00100000 >> (k + 1));
                k = ((n & 0x7fffffff) >> 20) - 0x3ff; // new k for n
                t = zero;
                t = SetHi(t, n & ~(0x000fffff >> k));
                n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
                if (j < 0) n = -n;
                p_h -= t;
            }
            t = p_l + p_h;
            t = SetLo(t, 0);
            u = t * lg2_h;
            v = (p_l - (t - p_h)) * lg2 + t * lg2_l;
            z = u + v;
            w = v - (z - u);
            t = z * z;
            t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
            r = (z * t1) / ((t1 - two) - (w + z * w));
            z = one - (r - z);
            j = Hi(z);
            j += unchecked((int)((uint)n << 20));
            if ((j >> 20) <= 0)
            {
                z = Scalbn(z, n); // subnormal output
            }
            else
            {
                int tmp = Hi(z);
                z = SetHi(z, tmp + unchecked((int)((uint)n << 20)));
            }
            return s * z;
        }
    }
}
