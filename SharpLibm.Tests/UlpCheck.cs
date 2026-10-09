using System;
using System.Collections.Generic;

namespace SharpLibm.Tests
{
    /// <summary>
    /// Error in ulp against libc-test's vectors, measured as libc-test does
    /// (src/common/mtest.c, ulperr): (got − want)·2^−eulp(want) + dwant, where
    /// dwant is the vector's own error of the expected value. libc-test
    /// passes |err| < 1.5; a correctly rounded function stays within 0.5, so
    /// that is the bound here.
    /// </summary>
    internal static class UlpCheck
    {
        private const double Bound = 0.5 + 1e-7;   // dwant is a float; allow its rounding

        public static int Run(string math)
        {
            int failed = 0;
            foreach (var (name, f) in Unary64()) failed += Measure(math, name, v => Err(f(D(v[0])), D(v[1]), Dw(v[2])));
            foreach (var (name, f) in Binary64()) failed += Measure(math, name, v => Err(f(D(v[0]), D(v[1])), D(v[2]), Dw(v[3])));
            foreach (var (name, f) in Unary32()) failed += Measure(math, name, v => ErrF(f(F(v[0])), F(v[1]), Dw(v[2])));
            foreach (var (name, f) in Binary32()) failed += Measure(math, name, v => ErrF(f(F(v[0]), F(v[1])), F(v[2]), Dw(v[3])));
            return failed;
        }

        private static IEnumerable<(string, Func<double, double>)> Unary64() => new (string, Func<double, double>)[]
        {
            ("acos", Libm.acos), ("acosh", Libm.acosh), ("asin", Libm.asin), ("asinh", Libm.asinh), ("atan", Libm.atan),
            ("atanh", Libm.atanh), ("cbrt", Libm.cbrt), ("cos", Libm.cos), ("cosh", Libm.cosh), ("erf", Libm.erf),
            ("erfc", Libm.erfc), ("exp", Libm.exp), ("exp10", Libm.exp10), ("exp2", Libm.exp2), ("expm1", Libm.expm1),
            ("lgamma", Libm.lgamma), ("log", Libm.log), ("log10", Libm.log10), ("log1p", Libm.log1p), ("log2", Libm.log2),
            ("sin", Libm.sin), ("sinh", Libm.sinh), ("sqrt", Libm.sqrt), ("tan", Libm.tan), ("tanh", Libm.tanh), ("tgamma", Libm.tgamma),
        };

        private static IEnumerable<(string, Func<double, double, double>)> Binary64() => new (string, Func<double, double, double>)[]
        {
            ("atan2", Libm.atan2), ("hypot", Libm.hypot), ("pow", Libm.pow),
        };

        private static IEnumerable<(string, Func<float, float>)> Unary32() => new (string, Func<float, float>)[]
        {
            ("acosf", Libm.acosf), ("acoshf", Libm.acoshf), ("asinf", Libm.asinf), ("asinhf", Libm.asinhf), ("atanf", Libm.atanf),
            ("atanhf", Libm.atanhf), ("cbrtf", Libm.cbrtf), ("cosf", Libm.cosf), ("coshf", Libm.coshf), ("erff", Libm.erff),
            ("erfcf", Libm.erfcf), ("expf", Libm.expf), ("exp10f", Libm.exp10f), ("exp2f", Libm.exp2f), ("expm1f", Libm.expm1f),
            ("lgammaf", Libm.lgammaf), ("logf", Libm.logf), ("log10f", Libm.log10f), ("log1pf", Libm.log1pf), ("log2f", Libm.log2f),
            ("sinf", Libm.sinf), ("sinhf", Libm.sinhf), ("sqrtf", Libm.sqrtf), ("tanf", Libm.tanf), ("tanhf", Libm.tanhf), ("tgammaf", Libm.tgammaf),
        };

        private static IEnumerable<(string, Func<float, float, float>)> Binary32() => new (string, Func<float, float, float>)[]
        {
            ("atan2f", Libm.atan2f), ("hypotf", Libm.hypotf), ("powf", Libm.powf),
        };

        private static int Measure(string math, string name, Func<string[], double> err)
        {
            List<LibcTest.Vector> vectors = LibcTest.Load(math, name);
            if (vectors.Count == 0) { Console.WriteLine($"  --   {name}: no vectors"); return 0; }
            double worst = 0;
            int over = 0, shown = 0;
            foreach (LibcTest.Vector v in vectors)
            {
                double e;
                try { e = err(v.Fields); }
                catch (Exception ex) { e = double.PositiveInfinity; if (shown++ < 3) Console.WriteLine($"    {v.File}:{v.Line}: {ex.GetType().Name}: {ex.Message}"); }
                double a = Math.Abs(e);
                if (a > worst || double.IsNaN(a)) worst = double.IsNaN(a) ? double.PositiveInfinity : a;
                if (!(a <= Bound))
                {
                    over++;
                    if (shown++ < 3) Console.WriteLine($"    {v.File}:{v.Line}: {string.Join(", ", v.Fields)}  err {e:F3} ulp");
                }
            }
            Console.WriteLine($"  {(over == 0 ? "ok  " : "FAIL")} {name}: {vectors.Count - over}/{vectors.Count} within 0.5 ulp, worst {worst:F3}");
            return over == 0 ? 0 : 1;
        }

        // ulperr from libc-test's mtest.c.
        private static double Err(double got, double want, double dwant)
        {
            if (double.IsNaN(got) && double.IsNaN(want)) return 0;
            if (got == want) return (Bits.Of(got) >> 63) == (Bits.Of(want) >> 63) ? dwant : double.PositiveInfinity;
            if (double.IsInfinity(got)) { got = Math.CopySign(8.98846567431158e307, got); want *= 0.5; }   // 0x1p1023
            return Math.ScaleB(got - want, -Eulp(want)) + dwant;
        }

        private static double ErrF(float got, float want, double dwant)
        {
            if (float.IsNaN(got) && float.IsNaN(want)) return 0;
            if (got == want) return (Bits.Of(got) >> 31) == (Bits.Of(want) >> 31) ? dwant : double.PositiveInfinity;
            if (float.IsInfinity(got)) { got = MathF.CopySign(1.7014118e38f, got); want *= 0.5f; }         // 0x1p127
            return Math.ScaleB((double)got - want, -EulpF(want)) + dwant;
        }

        private static int Eulp(double x)
        {
            int e = (int)(Bits.Of(x) >> 52) & 0x7ff;
            if (e == 0) e++;
            return e - 0x3ff - 52;
        }

        private static int EulpF(float x)
        {
            int e = (int)(Bits.Of(x) >> 23) & 0xff;
            if (e == 0) e++;
            return e - 0x7f - 23;
        }

        private static double D(string s) => HexFloat.ParseDouble(s);
        private static float F(string s) => HexFloat.ParseSingle(s);
        private static double Dw(string s) => HexFloat.ParseDouble(s);
    }
}
