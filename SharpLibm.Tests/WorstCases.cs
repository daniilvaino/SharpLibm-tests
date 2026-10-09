using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SharpLibm.Tests
{
    /// <summary>
    /// CORE-MATH's hardest-to-round inputs (the .wc files) against MPFR's
    /// correctly rounded results. Each line of &lt;dir&gt;/&lt;name&gt;.txt holds the
    /// argument bits and the expected result bits in hex; the result must
    /// match bit for bit (any NaN matches any NaN).
    /// </summary>
    internal static class WorstCases
    {
        /// <param name="only">Function names to check; empty for all.</param>
        public static int Run(string dir, params string[] only)
        {
            if (!Directory.Exists(dir)) { Console.WriteLine("  --   no references at " + dir); return 0; }
            bool Want(string name) => only.Length == 0 || Array.IndexOf(only, name) >= 0;
            int failed = 0;
            foreach (var (name, f) in Unary64()) if (Want(name)) failed += Check(dir, name, 1, (a, _) => Bits.Of(f(Bits.Double(a))), true);
            foreach (var (name, f) in Binary64()) if (Want(name)) failed += Check(dir, name, 2, (a, b) => Bits.Of(f(Bits.Double(a), Bits.Double(b))), true);
            foreach (var (name, f) in Binary32()) if (Want(name)) failed += Check(dir, name, 2, (a, b) => Bits.Of(f(Bits.Single((uint)a), Bits.Single((uint)b))), false);
            return failed;
        }

        private static IEnumerable<(string, Func<double, double>)> Unary64() => new (string, Func<double, double>)[]
        {
            ("acos", Libm.acos), ("acosh", Libm.acosh), ("asin", Libm.asin), ("asinh", Libm.asinh), ("atan", Libm.atan),
            ("atanh", Libm.atanh), ("cbrt", Libm.cbrt), ("cos", Libm.cos), ("cosh", Libm.cosh), ("erf", Libm.erf),
            ("erfc", Libm.erfc), ("exp", Libm.exp), ("exp10", Libm.exp10), ("exp2", Libm.exp2), ("expm1", Libm.expm1),
            ("log", Libm.log), ("log10", Libm.log10), ("log1p", Libm.log1p), ("log2", Libm.log2),
            ("sin", Libm.sin), ("sinh", Libm.sinh), ("tan", Libm.tan), ("tanh", Libm.tanh), ("tgamma", Libm.tgamma),
            ("lgamma", Libm.lgamma), ("rsqrt", Libm.rsqrt),
            ("sinpi", Libm.sinpi), ("cospi", Libm.cospi), ("tanpi", Libm.tanpi),
            ("asinpi", Libm.asinpi), ("acospi", Libm.acospi), ("atanpi", Libm.atanpi),
            ("exp2m1", Libm.exp2m1), ("exp10m1", Libm.exp10m1), ("log2p1", Libm.log2p1), ("log10p1", Libm.log10p1),
        };

        private static IEnumerable<(string, Func<double, double, double>)> Binary64() => new (string, Func<double, double, double>)[]
        {
            ("atan2", Libm.atan2), ("atan2pi", Libm.atan2pi), ("hypot", Libm.hypot), ("pow", Libm.pow),
        };

        private static IEnumerable<(string, Func<float, float, float>)> Binary32() => new (string, Func<float, float, float>)[]
        {
            ("atan2f", Libm.atan2f), ("atan2pif", Libm.atan2pif), ("hypotf", Libm.hypotf), ("powf", Libm.powf), ("compoundf", Libm.compoundf),
        };

        private static int Check(string dir, string name, int arity, Func<ulong, ulong, ulong> f, bool dbl)
        {
            string path = Path.Combine(dir, name + ".txt");
            if (!File.Exists(path)) { Console.WriteLine($"  --   {name}: no references"); return 0; }
            int total = 0, bad = 0;
            foreach (string line in File.ReadLines(path))
            {
                string[] t = line.Split(' ');
                ulong a = ulong.Parse(t[0], NumberStyles.HexNumber);
                ulong b = arity == 2 ? ulong.Parse(t[1], NumberStyles.HexNumber) : 0;
                ulong want = ulong.Parse(t[arity], NumberStyles.HexNumber);
                total++;
                ulong got;
                string error = null;
                try { got = f(a, b); }
                catch (Exception ex) { got = ~want; error = ex.GetType().Name + ": " + ex.Message; }
                if (got == want || (IsNaN(got, dbl) && IsNaN(want, dbl))) continue;
                if (++bad <= 5)
                {
                    string args = arity == 2 ? $"{Show(a, dbl)}, {Show(b, dbl)}" : Show(a, dbl);
                    Console.WriteLine($"    {name}({args}) = {error ?? Show(got, dbl)}, want {Show(want, dbl)}");
                }
            }
            Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} {name}: {total - bad}/{total}");
            return bad == 0 ? 0 : 1;
        }

        private static bool IsNaN(ulong x, bool dbl) =>
            dbl ? Bits.IsNaN(Bits.Double(x)) : Bits.IsNaN(Bits.Single((uint)x));

        private static string Show(ulong x, bool dbl) =>
            dbl ? $"{Bits.Double(x):R} (0x{x:x16})" : $"{Bits.Single((uint)x):R} (0x{x:x8})";
    }
}
