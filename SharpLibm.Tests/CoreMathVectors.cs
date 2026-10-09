using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SharpLibm.Tests
{
    /// <summary>
    /// CoreMathSharp's test vectors (CoreMathSharp.Tests/Binary64|Binary32/*.txt):
    /// tab-separated bit patterns, inputs then outputs, produced by the C
    /// CORE-MATH sources. CORE-MATH rounds correctly, so every result must
    /// match bit for bit (any NaN matches any NaN).
    /// </summary>
    internal static unsafe class CoreMathVectors
    {
        public static int Run(string root)
        {
            string dir64 = Path.Combine(root, "Binary64"), dir32 = Path.Combine(root, "Binary32");
            if (!Directory.Exists(dir64))
            {
                Console.WriteLine("  --   no CoreMathSharp vectors at " + root);
                return 0;
            }
            int failedFunctions = 0;

            foreach (var (file, f) in Unary64())
                failedFunctions += Check(Path.Combine(dir64, file + ".txt"), file, 1, v => Same(f(D(v[0])), D(v[1])));
            foreach (var (file, f) in Binary64())
                failedFunctions += Check(Path.Combine(dir64, file + ".txt"), file, 2, v => Same(f(D(v[0]), D(v[1])), D(v[2])));
            failedFunctions += Check(Path.Combine(dir64, "fusedMultiplyAdd.txt"), "fma", 3, v => Same(Libm.fma(D(v[0]), D(v[1]), D(v[2])), D(v[3])));
            failedFunctions += Check(Path.Combine(dir64, "sincos.txt"), "sincos", 1, v =>
            {
                double s, c; Libm.sincos(D(v[0]), &s, &c);
                return Same(s, D(v[1])) && Same(c, D(v[2]));
            });
            failedFunctions += Check(Path.Combine(dir64, "lgamma.txt"), "lgamma_r", 1, v =>
            {
                int sign; double y = Libm.lgamma_r(D(v[0]), &sign);
                return Same(y, D(v[1])) && (Bits.IsNaN(y) || sign == (int)ulong.Parse(v[2], NumberStyles.HexNumber));
            });

            foreach (var (file, f) in Unary32())
                failedFunctions += Check(Path.Combine(dir32, file + ".txt"), file, 1, v => Same(f(F(v[0])), F(v[1])));
            foreach (var (file, f) in Binary32())
                failedFunctions += Check(Path.Combine(dir32, file + ".txt"), file, 2, v => Same(f(F(v[0]), F(v[1])), F(v[2])));
            failedFunctions += Check(Path.Combine(dir32, "fusedMultiplyAddF.txt"), "fmaf", 3, v => Same(Libm.fmaf(F(v[0]), F(v[1]), F(v[2])), F(v[3])));
            failedFunctions += Check(Path.Combine(dir32, "sincosf.txt"), "sincosf", 1, v =>
            {
                float s, c; Libm.sincosf(F(v[0]), &s, &c);
                return Same(s, F(v[1])) && Same(c, F(v[2]));
            });
            failedFunctions += Check(Path.Combine(dir32, "lgammaf.txt"), "lgammaf_r", 1, v =>
            {
                int sign; float y = Libm.lgammaf_r(F(v[0]), &sign);
                return Same(y, F(v[1])) && (Bits.IsNaN(y) || sign == (int)uint.Parse(v[2], NumberStyles.HexNumber));
            });
            return failedFunctions;
        }

        private static IEnumerable<(string, Func<double, double>)> Unary64() => new (string, Func<double, double>)[]
        {
            ("acos", Libm.acos), ("acosh", Libm.acosh), ("acospi", Libm.acospi), ("asin", Libm.asin), ("asinh", Libm.asinh),
            ("asinpi", Libm.asinpi), ("atan", Libm.atan), ("atanh", Libm.atanh), ("atanpi", Libm.atanpi), ("cbrt", Libm.cbrt),
            ("cos", Libm.cos), ("cosh", Libm.cosh), ("cospi", Libm.cospi), ("erf", Libm.erf), ("erfc", Libm.erfc),
            ("exp", Libm.exp), ("exp10", Libm.exp10), ("exp10m1", Libm.exp10m1), ("exp2", Libm.exp2), ("exp2m1", Libm.exp2m1),
            ("expm1", Libm.expm1), ("log", Libm.log), ("log10", Libm.log10), ("log10p1", Libm.log10p1), ("log1p", Libm.log1p),
            ("log2", Libm.log2), ("log2p1", Libm.log2p1), ("reciprocalSqrt", Libm.rsqrt), ("sin", Libm.sin), ("sinh", Libm.sinh),
            ("sinpi", Libm.sinpi), ("tan", Libm.tan), ("tanh", Libm.tanh), ("tanpi", Libm.tanpi), ("tgamma", Libm.tgamma),
        };

        // Columns are x then y; atan2 takes (y, x).
        private static IEnumerable<(string, Func<double, double, double>)> Binary64() => new (string, Func<double, double, double>)[]
        {
            ("atan2", (x, y) => Libm.atan2(y, x)), ("atan2pi", (x, y) => Libm.atan2pi(y, x)), ("hypot", Libm.hypot), ("pow", Libm.pow),
        };

        private static IEnumerable<(string, Func<float, float>)> Unary32() => new (string, Func<float, float>)[]
        {
            ("acosf", Libm.acosf), ("acoshf", Libm.acoshf), ("acospif", Libm.acospif), ("asinf", Libm.asinf), ("asinhf", Libm.asinhf),
            ("asinpif", Libm.asinpif), ("atanf", Libm.atanf), ("atanhf", Libm.atanhf), ("atanpif", Libm.atanpif), ("cbrtf", Libm.cbrtf),
            ("cosf", Libm.cosf), ("coshf", Libm.coshf), ("cospif", Libm.cospif), ("erff", Libm.erff), ("erfcf", Libm.erfcf),
            ("expf", Libm.expf), ("exp10f", Libm.exp10f), ("exp10m1f", Libm.exp10m1f), ("exp2f", Libm.exp2f), ("exp2m1f", Libm.exp2m1f),
            ("expm1f", Libm.expm1f), ("logf", Libm.logf), ("log10f", Libm.log10f), ("log10p1f", Libm.log10p1f), ("log1pf", Libm.log1pf),
            ("log2f", Libm.log2f), ("log2p1f", Libm.log2p1f), ("reciprocalSqrtF", Libm.rsqrtf), ("sinf", Libm.sinf), ("sinhf", Libm.sinhf),
            ("sinpif", Libm.sinpif), ("tanf", Libm.tanf), ("tanhf", Libm.tanhf), ("tanpif", Libm.tanpif), ("tgammaf", Libm.tgammaf),
        };

        private static IEnumerable<(string, Func<float, float, float>)> Binary32() => new (string, Func<float, float, float>)[]
        {
            ("atan2f", (x, y) => Libm.atan2f(y, x)), ("atan2pif", (x, y) => Libm.atan2pif(y, x)), ("hypotf", Libm.hypotf), ("powf", Libm.powf), ("compoundf", Libm.compoundf),
        };

        private static int Check(string path, string name, int arity, Func<string[], bool> check)
        {
            if (!File.Exists(path)) { Console.WriteLine($"  --   {name}: no vector file"); return 0; }
            int total = 0, failed = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                string[] v = line.Split('\t');
                total++;
                bool ok;
                string error = null;
                try { ok = check(v); }
                catch (Exception e) { ok = false; error = e.GetType().Name + ": " + e.Message; }
                if (!ok && ++failed <= 5) Console.WriteLine($"    {Path.GetFileName(path)}: {line}{(error == null ? "" : "  " + error)}");
            }
            Console.WriteLine($"  {(failed == 0 ? "ok  " : "FAIL")} {name}: {total - failed}/{total}");
            return failed == 0 ? 0 : 1;
        }

        private static double D(string hex) => Bits.Double(ulong.Parse(hex, NumberStyles.HexNumber));
        private static float F(string hex) => Bits.Single(uint.Parse(hex, NumberStyles.HexNumber));

        private static bool Same(double got, double want) => Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);
        private static bool Same(float got, float want) => Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);
    }
}
