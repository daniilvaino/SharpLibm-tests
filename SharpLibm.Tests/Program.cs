using System;
using System.Collections.Generic;
using System.IO;

namespace SharpLibm.Tests
{
    internal static unsafe class Program
    {
        private static int s_failedFunctions;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: SharpLibm.Tests <tests-root> [--wc function ...]   (the directory holding libc-test/, …)");
                return 2;
            }
            if (args.Length > 1 && args[1] == "--wc")
                return WorstCases.Run(Path.Combine(args[0], "wc-ref"), args[2..]) == 0 ? 0 : 1;
            if (args.Length > 2 && args[1] == "--f32")      // --f32 <CORE-MATH binary32 dll> [function ...]
                return Exhaustive32.Run(args[2], args[3..]) == 0 ? 0 : 1;
            string math = Path.Combine(args[0], "libc-test", "src", "math");
            if (!Directory.Exists(math))
            {
                Console.Error.WriteLine("no libc-test math vectors at " + math);
                return 2;
            }

            Console.WriteLine("libc-test, exact operations:");
            ExactOperations(math);

            Console.WriteLine("libc-test, transcendental functions, error in ulp (correctly rounded: <= 0.5):");
            s_failedFunctions += UlpCheck.Run(math);

            Console.WriteLine("CoreMathSharp vectors (CORE-MATH C output), bit for bit:");
            s_failedFunctions += CoreMathVectors.Run(Path.Combine(args[0], "coremathsharp"));

            Console.WriteLine("CORE-MATH worst cases (.wc) against MPFR, bit for bit:");
            s_failedFunctions += WorstCases.Run(Path.Combine(args[0], "wc-ref"));

            Console.WriteLine(s_failedFunctions == 0 ? "all passed" : s_failedFunctions + " function(s) failed");
            return s_failedFunctions == 0 ? 0 : 1;
        }

        // Functions whose result is exact: every vector must match bit for bit
        // (any NaN matches any NaN).
        private static void ExactOperations(string math)
        {
            D_D(math, "floor", Libm.floor); F_F(math, "floorf", Libm.floorf);
            D_D(math, "ceil", Libm.ceil); F_F(math, "ceilf", Libm.ceilf);
            D_D(math, "trunc", Libm.trunc); F_F(math, "truncf", Libm.truncf);
            D_D(math, "round", Libm.round); F_F(math, "roundf", Libm.roundf);
            D_D(math, "rint", Libm.rint); F_F(math, "rintf", Libm.rintf);
            D_D(math, "nearbyint", Libm.nearbyint); F_F(math, "nearbyintf", Libm.nearbyintf);
            D_D(math, "fabs", Libm.fabs); F_F(math, "fabsf", Libm.fabsf);
            D_D(math, "logb", Libm.logb); F_F(math, "logbf", Libm.logbf);

            DD_D(math, "fmod", Libm.fmod); FF_F(math, "fmodf", Libm.fmodf);
            DD_D(math, "remainder", Libm.remainder); FF_F(math, "remainderf", Libm.remainderf);
            DD_D(math, "copysign", Libm.copysign); FF_F(math, "copysignf", Libm.copysignf);
            DD_D(math, "fdim", Libm.fdim); FF_F(math, "fdimf", Libm.fdimf);
            DD_D(math, "fmax", Libm.fmax); FF_F(math, "fmaxf", Libm.fmaxf);
            DD_D(math, "fmin", Libm.fmin); FF_F(math, "fminf", Libm.fminf);
            DD_D(math, "nextafter", Libm.nextafter); FF_F(math, "nextafterf", Libm.nextafterf);

            Modf(math);
            Frexp(math);
            Remquo(math);

            D_I(math, "ilogb", x => Libm.ilogb(x), exactInt: true);
            F_I(math, "ilogbf", x => Libm.ilogbf(x), exactInt: true);
            D_I(math, "llrint", Libm.llrint, exactInt: false);
            F_I(math, "llrintf", Libm.llrintf, exactInt: false);
            D_I(math, "llround", Libm.llround, exactInt: false);
            F_I(math, "llroundf", Libm.llroundf, exactInt: false);

            DI_D(math, "ldexp", Libm.ldexp); FI_F(math, "ldexpf", Libm.ldexpf);
            DI_D(math, "scalbn", Libm.scalbn); FI_F(math, "scalbnf", Libm.scalbnf);
            DI_D(math, "scalbln", (x, n) => Libm.scalbln(x, n)); FI_F(math, "scalblnf", (x, n) => Libm.scalblnf(x, n));
        }

        // ---- shapes ----

        private static void D_D(string math, string name, Func<double, double> f) =>
            Run(math, name, v => Same(f(D(v[0])), D(v[1])) ? null : $"{name}({v[0]}) = {Hex(f(D(v[0])))}, want {v[1]}");

        private static void F_F(string math, string name, Func<float, float> f) =>
            Run(math, name, v => Same(f(F(v[0])), F(v[1])) ? null : $"{name}({v[0]}) = {Hex(f(F(v[0])))}, want {v[1]}");

        private static void DD_D(string math, string name, Func<double, double, double> f) =>
            Run(math, name, v => Same(f(D(v[0]), D(v[1])), D(v[2])) ? null : $"{name}({v[0]}, {v[1]}) = {Hex(f(D(v[0]), D(v[1])))}, want {v[2]}");

        private static void FF_F(string math, string name, Func<float, float, float> f) =>
            Run(math, name, v => Same(f(F(v[0]), F(v[1])), F(v[2])) ? null : $"{name}({v[0]}, {v[1]}) = {Hex(f(F(v[0]), F(v[1])))}, want {v[2]}");

        // An out-of-range result is unspecified in C (the vector flags INVALID): skip it unless exact.
        private static void D_I(string math, string name, Func<double, long> f, bool exactInt) =>
            Run(math, name, v =>
            {
                if (!exactInt && v[^1].Contains("INVALID")) return null;
                long got = f(D(v[0])), want = LibcTest.ParseInt(v[1]);
                return got == want ? null : $"{name}({v[0]}) = {got}, want {want}";
            });

        private static void F_I(string math, string name, Func<float, long> f, bool exactInt) =>
            Run(math, name, v =>
            {
                if (!exactInt && v[^1].Contains("INVALID")) return null;
                long got = f(F(v[0])), want = LibcTest.ParseInt(v[1]);
                return got == want ? null : $"{name}({v[0]}) = {got}, want {want}";
            });

        private static void DI_D(string math, string name, Func<double, int, double> f) =>
            Run(math, name, v =>
            {
                double got = f(D(v[0]), (int)LibcTest.ParseInt(v[1]));
                return Same(got, D(v[2])) ? null : $"{name}({v[0]}, {v[1]}) = {Hex(got)}, want {v[2]}";
            });

        private static void FI_F(string math, string name, Func<float, int, float> f) =>
            Run(math, name, v =>
            {
                float got = f(F(v[0]), (int)LibcTest.ParseInt(v[1]));
                return Same(got, F(v[2])) ? null : $"{name}({v[0]}, {v[1]}) = {Hex(got)}, want {v[2]}";
            });

        // modf: x, frac, dfrac, int, dint, flags
        private static void Modf(string math)
        {
            Run(math, "modf", v =>
            {
                double i; double frac = Libm.modf(D(v[0]), &i);
                return Same(frac, D(v[1])) && Same(i, D(v[3])) ? null : $"modf({v[0]}) = {Hex(frac)}, {Hex(i)}; want {v[1]}, {v[3]}";
            });
            Run(math, "modff", v =>
            {
                float i; float frac = Libm.modff(F(v[0]), &i);
                return Same(frac, F(v[1])) && Same(i, F(v[3])) ? null : $"modff({v[0]}) = {Hex(frac)}, {Hex(i)}; want {v[1]}, {v[3]}";
            });
        }

        // frexp: x, y, dy, exponent, flags
        private static void Frexp(string math)
        {
            Run(math, "frexp", v =>
            {
                int e; double y = Libm.frexp(D(v[0]), &e);
                long want = LibcTest.ParseInt(v[3]);
                return Same(y, D(v[1])) && (Bits.IsNaN(y) || e == want) ? null : $"frexp({v[0]}) = {Hex(y)}, {e}; want {v[1]}, {want}";
            });
            Run(math, "frexpf", v =>
            {
                int e; float y = Libm.frexpf(F(v[0]), &e);
                long want = LibcTest.ParseInt(v[3]);
                return Same(y, F(v[1])) && (Bits.IsNaN(y) || e == want) ? null : $"frexpf({v[0]}) = {Hex(y)}, {e}; want {v[1]}, {want}";
            });
        }

        // remquo: x, y, r, dr, quotient, flags — the quotient's sign and low 3 bits must match (C99 7.12.10.3).
        private static void Remquo(string math)
        {
            Run(math, "remquo", v =>
            {
                int q; double r = Libm.remquo(D(v[0]), D(v[1]), &q);
                long want = LibcTest.ParseInt(v[4]);
                bool ok = Same(r, D(v[2])) && (Bits.IsNaN(r) || ((q & 7) == (want & 7) && (q < 0) == (want < 0)));
                return ok ? null : $"remquo({v[0]}, {v[1]}) = {Hex(r)}, {q}; want {v[2]}, {want}";
            });
            Run(math, "remquof", v =>
            {
                int q; float r = Libm.remquof(F(v[0]), F(v[1]), &q);
                long want = LibcTest.ParseInt(v[4]);
                bool ok = Same(r, F(v[2])) && (Bits.IsNaN(r) || ((q & 7) == (want & 7) && (q < 0) == (want < 0)));
                return ok ? null : $"remquof({v[0]}, {v[1]}) = {Hex(r)}, {q}; want {v[2]}, {want}";
            });
        }

        // ---- running and comparing ----

        private static void Run(string math, string name, Func<string[], string> check)
        {
            List<LibcTest.Vector> vectors = LibcTest.Load(math, name);
            int failed = 0;
            foreach (LibcTest.Vector v in vectors)
            {
                string failure = check(v.Fields);
                if (failure == null) continue;
                if (++failed <= 5) Console.WriteLine($"    {v.File}:{v.Line}: {failure}");
            }
            if (vectors.Count == 0) Console.WriteLine($"  --   {name}: no vectors");
            else Console.WriteLine($"  {(failed == 0 ? "ok  " : "FAIL")} {name}: {vectors.Count - failed}/{vectors.Count}");
            if (failed != 0) s_failedFunctions++;
        }

        private static double D(string s) => HexFloat.ParseDouble(s);
        private static float F(string s) => HexFloat.ParseSingle(s);

        private static bool Same(double got, double want) =>
            Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);

        private static bool Same(float got, float want) =>
            Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);

        private static string Hex(double x) => Bits.IsNaN(x) ? "nan" : $"{x:R} (0x{Bits.Of(x):x16})";
        private static string Hex(float x) => Bits.IsNaN(x) ? "nan" : $"{x:R} (0x{Bits.Of(x):x8})";
    }
}
