using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SharpLibm.Tests
{
    /// <summary>
    /// The functions SharpLibm does not round correctly — Bessel j0 j1 jn y0 y1 yn
    /// and erfinv, erfcinv (ported from Go's math), with their float forms — measured
    /// in ulp against MPFR (tools/special_ref.py → special-ref/&lt;f&gt;.txt), plus their
    /// special values checked bit for bit against C/glibc semantics.
    ///
    /// Each reference line holds the arguments, the correctly rounded result and
    /// dy = (exact − result)/ulp(result); the error reported is against the exact
    /// value: (got − result)/ulp(result) − dy.
    ///
    /// Two measures per function:
    ///   ulp — relative to the exact value, ulp(result). Near a zero of a Bessel
    ///         function this is unbounded for any algorithm that is not correctly
    ///         rounded there (fdlibm/Go: up to ~1e15 ulp), so it is reported, not bounded.
    ///   amp — relative to the amplitude: |got − exact| / ulp(max(|exact|, A)),
    ///         A = min(1, sqrt(2/(π|x|))) where the function oscillates (|x| from 0.6
    ///         times the first zero for orders 0 and 1, from |n| for jn yn), A = 0
    ///         elsewhere, so there amp = ulp. This is the absolute error in units of the
    ///         function's local size, and it is what the pass bounds apply to.
    /// erfinv/erfcinv have no zeros but 0: their bound is in plain ulp.
    ///
    /// The bounds are what Go's algorithms (fdlibm's, for the Bessel functions) reach
    /// on these inputs with SharpLibm's correctly rounded sin/cos/log/sqrt underneath,
    /// rounded up — a regression guard, not a quality claim. fdlibm-derived libms
    /// (FreeBSD, musl, Go) do not bound the ulp error near the zeros at all; neither
    /// does this port. (glibc's libm-test-ulps, the usual comparison, is not in
    /// suites/glibc.)
    /// </summary>
    internal static class SpecialFunctions
    {
        private enum Kind { Bessel1, BesselN, Inverse }

        private sealed class Spec
        {
            public string Name;
            public Kind Kind;
            public bool Single;
            public Func<int, double, double> F;     // (n, x); n unused for unary
            public double Bound;                     // on amp (Bessel) or ulp (inverse)
        }

        // Bounds: see the class comment. Measured maxima on the references of
        // tools/special_ref.py (≈200k double / ≈45k float inputs per function, a fifth
        // of the Bessel 0/1 inputs within 2^-20 of a zero), max amp (Bessel) or ulp:
        //   j0 3.47  j1 3.67  y0 3.34  y1 3.95 — fdlibm's rational fits plus the
        //                                        cos(2x) cancellation trick
        //   jn 32.9  yn 26.7                   — the three-term recurrence amplifies
        //                                        the j0/j1 (y0/y1) errors with n
        //   erfinv 1.33  erfcinv 4.58          — AS 241 plus one Newton step on erf/erfc;
        //                                        erfcinv's worst is q below 1e-300, where the step is skipped
        //   every float form 0.5 ulp           — correctly rounded on these inputs
        private static IEnumerable<Spec> Specs() => new[]
        {
            D1("j0", Libm.j0, 6),       F1("j0f", Libm.j0f, 1),
            D1("j1", Libm.j1, 6),       F1("j1f", Libm.j1f, 1),
            D1("y0", Libm.y0, 6),       F1("y0f", Libm.y0f, 1),
            D1("y1", Libm.y1, 6),       F1("y1f", Libm.y1f, 1),
            DN("jn", Libm.jn, 48),      FN("jnf", Libm.jnf, 1),
            DN("yn", Libm.yn, 48),      FN("ynf", Libm.ynf, 1),
            DI("erfinv", Libm.erfinv, 2),   FI("erfinvf", Libm.erfinvf, 1),
            DI("erfcinv", Libm.erfcinv, 5), FI("erfcinvf", Libm.erfcinvf, 1),
        };

        private static Spec D1(string n, Func<double, double> f, double b) => new() { Name = n, Kind = Kind.Bessel1, F = (_, x) => f(x), Bound = b };
        private static Spec F1(string n, Func<float, float> f, double b) => new() { Name = n, Kind = Kind.Bessel1, Single = true, F = (_, x) => f((float)x), Bound = b };
        private static Spec DN(string n, Func<int, double, double> f, double b) => new() { Name = n, Kind = Kind.BesselN, F = f, Bound = b };
        private static Spec FN(string n, Func<int, float, float> f, double b) => new() { Name = n, Kind = Kind.BesselN, Single = true, F = (k, x) => f(k, (float)x), Bound = b };
        private static Spec DI(string n, Func<double, double> f, double b) => new() { Name = n, Kind = Kind.Inverse, F = (_, x) => f(x), Bound = b };
        private static Spec FI(string n, Func<float, float> f, double b) => new() { Name = n, Kind = Kind.Inverse, Single = true, F = (_, x) => f((float)x), Bound = b };

        /// <param name="dir">special-ref/</param>
        /// <param name="only">Function names to check; empty for all.</param>
        public static int Run(string dir, params string[] only)
        {
            bool Want(string name) => only.Length == 0 || Array.IndexOf(only, name) >= 0;
            int failed = SpecialValues(Want);
            if (!Directory.Exists(dir)) { Console.WriteLine("  --   no references at " + dir + " (tools/special_ref.py)"); return failed; }
            foreach (Spec s in Specs())
                if (Want(s.Name)) failed += Measure(dir, s);
            return failed;
        }

        private static int Measure(string dir, Spec s)
        {
            string path = Path.Combine(dir, s.Name + ".txt");
            if (!File.Exists(path)) { Console.WriteLine($"  --   {s.Name}: no references"); return 0; }
            var errs = new List<double>();
            double worstUlp = 0, worstAmp = 0;
            string worstUlpAt = "", worstAmpAt = "";
            int overOne = 0, overBound = 0, shown = 0;
            bool hasN = s.Kind == Kind.BesselN;
            foreach (string line in File.ReadLines(path))
            {
                string[] t = line.Split(' ');
                int i = 0;
                int n = hasN ? int.Parse(t[i++], CultureInfo.InvariantCulture) : 0;
                ulong xb = ulong.Parse(t[i++], NumberStyles.HexNumber);
                ulong yb = ulong.Parse(t[i++], NumberStyles.HexNumber);
                double dy = double.Parse(t[i], CultureInfo.InvariantCulture);
                double x = s.Single ? Bits.Single((uint)xb) : Bits.Double(xb);
                double want = s.Single ? Bits.Single((uint)yb) : Bits.Double(yb);
                double got = s.F(n, x);

                double ulp = Err(got, want, dy, s.Single, 0);
                double amp = s.Kind == Kind.Inverse ? ulp : Err(got, want, dy, s.Single, Amplitude(s, n, x));
                double au = Math.Abs(ulp), aa = Math.Abs(amp);
                if (double.IsNaN(au)) au = aa = double.PositiveInfinity;
                errs.Add(au);
                string at = (hasN ? $"{n}, " : "") + Show(x, s.Single);
                if (au > worstUlp) { worstUlp = au; worstUlpAt = $"{at}: got {Show(got, s.Single)}, want {Show(want, s.Single)}"; }
                if (aa > worstAmp) { worstAmp = aa; worstAmpAt = at; }
                if (au > 1) overOne++;
                if (aa > s.Bound)
                {
                    overBound++;
                    if (shown++ < 3) Console.WriteLine($"    {s.Name}({at}) = {Show(got, s.Single)}, want {Show(want, s.Single)}: {ulp:G4} ulp, amp {amp:G4}");
                }
            }
            errs.Sort();
            double median = errs.Count == 0 ? 0 : errs[errs.Count / 2];
            double p99 = errs.Count == 0 ? 0 : errs[(int)(errs.Count * 0.99)];
            string ampPart = s.Kind == Kind.Inverse ? "" : $"; amp max {worstAmp:G4} at ({worstAmpAt})";
            Console.WriteLine($"  {(overBound == 0 ? "ok  " : "FAIL")} {s.Name}: {errs.Count} inputs, ulp median {median:F3} p99 {p99:G4} max {worstUlp:G4}, {overOne} > 1 ulp{ampPart}; bound {s.Bound} {(s.Kind == Kind.Inverse ? "ulp" : "amp")}");
            Console.WriteLine($"         worst ulp: ({worstUlpAt})");
            return overBound == 0 ? 0 : 1;
        }

        // The size the absolute error is measured against where the function oscillates.
        // Orders 0 and 1 from 0.6 times the first zero (j0 2.405, j1 3.832, y0 0.894,
        // y1 2.197), where the value is still of the order of A; jn yn from |n|.
        private static double Amplitude(Spec s, int n, double x)
        {
            double ax = Math.Abs(x);
            double from = s.Kind == Kind.BesselN ? Math.Abs((double)n)
                : 0.6 * (s.Name[0] == 'j' ? (s.Name[1] == '0' ? 2.405 : 3.832) : (s.Name[1] == '0' ? 0.894 : 2.197));
            return ax >= from ? Math.Min(1.0, Math.Sqrt(2 / Math.PI) / Math.Sqrt(ax)) : 0;
        }

        // (got − exact)/ulp(max(|want|, scale)), exact = want + dy·ulp(want). Infinite
        // values are moved to 2^1024 (2^128) as libc-test's ulperr does.
        private static double Err(double got, double want, double dy, bool single, double scale)
        {
            if (double.IsNaN(got) && double.IsNaN(want)) return 0;
            if (double.IsNaN(got) || double.IsNaN(want)) return double.PositiveInfinity;
            if (double.IsInfinity(want) && got == want) return 0;
            double top = single ? Math.ScaleB(1.0, 128) : double.MaxValue;   // 2^1024 overflows; MaxValue + 1 ulp is close enough
            double topUlp = Math.ScaleB(1.0, single ? 104 : 971);
            double g = double.IsInfinity(got) ? Math.CopySign(top, got) : got;
            double w = double.IsInfinity(want) ? Math.CopySign(top, want) : want;
            double uw = double.IsInfinity(want) ? topUlp : Ulp(want, single);
            double unit = Ulp(Math.Max(Math.Abs(want), scale), single);
            if (double.IsInfinity(want)) unit = topUlp;
            return ((g - w) - dy * uw) / unit;     // w + dy·uw itself would round back to w
        }

        private static double Ulp(double v, bool single)
        {
            if (single)
            {
                int e = (int)(Bits.Of((float)v) >> 23) & 0xff;
                return Math.ScaleB(1.0, Math.Max(e, 1) - 127 - 23);
            }
            int ed = (int)(Bits.Of(v) >> 52) & 0x7ff;
            return Math.ScaleB(1.0, Math.Max(ed, 1) - 1023 - 52);
        }

        private static string Show(double x, bool single) =>
            double.IsNaN(x) ? "nan" : single ? $"{(float)x:R} (0x{Bits.Of((float)x):x8})" : $"{x:R} (0x{Bits.Of(x):x16})";

        // ---- special values: C99 Annex F does not cover these functions; the values
        // are POSIX's (y*(±0) = −inf pole, y*(x < 0) NaN) and glibc/fdlibm's signs ----

        private static int SpecialValues(Func<string, bool> want)
        {
            const double Inf = double.PositiveInfinity, NaN = double.NaN, Min = double.Epsilon;
            int bad = 0, total = 0;
            void D(string name, double got, double expect, string what)
            {
                if (!want(name)) return;
                total++;
                bool ok = double.IsNaN(expect) ? double.IsNaN(got) : Bits.Of(got) == Bits.Of(expect);
                if (!ok && ++bad <= 10) Console.WriteLine($"    {name}({what}) = {Show(got, false)}, want {Show(expect, false)}");
            }
            void S(string name, float got, float expect, string what)
            {
                if (!want(name)) return;
                total++;
                bool ok = float.IsNaN(expect) ? float.IsNaN(got) : Bits.Of(got) == Bits.Of(expect);
                if (!ok && ++bad <= 10) Console.WriteLine($"    {name}({what}) = {Show(got, true)}, want {Show(expect, true)}");
            }

            foreach (double z in new[] { 0.0, -0.0 })
            {
                D("j0", Libm.j0(z), 1, $"{z}"); S("j0f", Libm.j0f((float)z), 1, $"{z}");
                D("j1", Libm.j1(z), z, $"{z}"); S("j1f", Libm.j1f((float)z), (float)z, $"{z}");
                D("y0", Libm.y0(z), -Inf, $"{z}"); S("y0f", Libm.y0f((float)z), float.NegativeInfinity, $"{z}");
                D("y1", Libm.y1(z), -Inf, $"{z}"); S("y1f", Libm.y1f((float)z), float.NegativeInfinity, $"{z}");
                D("erfinv", Libm.erfinv(z), z, $"{z}"); S("erfinvf", Libm.erfinvf((float)z), (float)z, $"{z}");
                D("erfcinv", Libm.erfcinv(z), Inf, $"{z}");
            }
            D("j0", Libm.j0(Inf), 0, "inf"); D("j0", Libm.j0(-Inf), 0, "-inf"); D("j0", Libm.j0(NaN), NaN, "nan");
            D("j1", Libm.j1(Inf), 0, "inf"); D("j1", Libm.j1(-Inf), -0.0, "-inf"); D("j1", Libm.j1(NaN), NaN, "nan");
            D("j1", Libm.j1(Min), 0.0, "min subnormal");    // x/2 rounds to even: 0
            D("j1", Libm.j1(2 * Min), Min, "2 min subnormal");
            foreach (string y in new[] { "y0", "y1" })
            {
                Func<double, double> f = y == "y0" ? Libm.y0 : Libm.y1;
                D(y, f(Inf), 0, "inf"); D(y, f(-Inf), NaN, "-inf"); D(y, f(-1), NaN, "-1");
                D(y, f(-Min), NaN, "-min subnormal"); D(y, f(NaN), NaN, "nan");
            }
            D("y1", Libm.y1(Min), -Inf, "min subnormal");   // -2/(pi x) overflows
            S("j0f", Libm.j0f(float.PositiveInfinity), 0, "inf"); S("j1f", Libm.j1f(float.NegativeInfinity), -0.0f, "-inf");
            S("y0f", Libm.y0f(-1), float.NaN, "-1"); S("y1f", Libm.y1f(float.PositiveInfinity), 0, "inf");

            // jn: J(-n, x) = J(n, -x); odd orders carry the sign of x through ±0 and ±inf.
            D("jn", Libm.jn(0, -Inf), 0, "0, -inf"); D("jn", Libm.jn(1, -Inf), -0.0, "1, -inf");
            D("jn", Libm.jn(3, -Inf), -0.0, "3, -inf"); D("jn", Libm.jn(10, -Inf), 0, "10, -inf");
            D("jn", Libm.jn(3, Inf), 0, "3, inf"); D("jn", Libm.jn(-3, Inf), -0.0, "-3, inf");
            D("jn", Libm.jn(3, -0.0), -0.0, "3, -0"); D("jn", Libm.jn(-3, 0.0), -0.0, "-3, 0");
            D("jn", Libm.jn(-3, -0.0), 0.0, "-3, -0"); D("jn", Libm.jn(2, -0.0), 0.0, "2, -0");
            D("jn", Libm.jn(0, 0.0), 1, "0, 0"); D("jn", Libm.jn(-1, 0.0), -0.0, "-1, 0");
            D("jn", Libm.jn(5, NaN), NaN, "5, nan"); D("jn", Libm.jn(40, 1e-300), 0, "40, 1e-300");
            S("jnf", Libm.jnf(3, float.NegativeInfinity), -0.0f, "3, -inf"); S("jnf", Libm.jnf(-1, 0f), -0.0f, "-1, 0");

            // yn: Y(-n, x) = (-1)^n Y(n, x): +inf at 0 and −0 at +inf for odd negative n (glibc).
            D("yn", Libm.yn(0, 0.0), -Inf, "0, 0"); D("yn", Libm.yn(1, -0.0), -Inf, "1, -0");
            D("yn", Libm.yn(2, 0.0), -Inf, "2, 0"); D("yn", Libm.yn(-1, 0.0), Inf, "-1, 0");
            D("yn", Libm.yn(-1, -0.0), Inf, "-1, -0"); D("yn", Libm.yn(-2, 0.0), -Inf, "-2, 0");
            D("yn", Libm.yn(2, -1), NaN, "2, -1"); D("yn", Libm.yn(-1, -Min), NaN, "-1, -min subnormal");
            D("yn", Libm.yn(3, -Inf), NaN, "3, -inf"); D("yn", Libm.yn(2, Inf), 0, "2, inf");
            D("yn", Libm.yn(-1, Inf), -0.0, "-1, inf"); D("yn", Libm.yn(5, NaN), NaN, "5, nan");
            D("yn", Libm.yn(50, 1e-300), -Inf, "50, 1e-300"); D("yn", Libm.yn(-51, 1e-300), Inf, "-51, 1e-300");
            S("ynf", Libm.ynf(-1, 0f), float.PositiveInfinity, "-1, 0"); S("ynf", Libm.ynf(2, -1f), float.NaN, "2, -1");

            // erfinv: ±1 → ±inf, |x| > 1 → NaN. erfcinv: 0 → +inf, 2 → −inf, 1 → +0, outside [0, 2] → NaN.
            D("erfinv", Libm.erfinv(1), Inf, "1"); D("erfinv", Libm.erfinv(-1), -Inf, "-1");
            D("erfinv", Libm.erfinv(1.5), NaN, "1.5"); D("erfinv", Libm.erfinv(-Inf), NaN, "-inf");
            D("erfinv", Libm.erfinv(Inf), NaN, "inf"); D("erfinv", Libm.erfinv(NaN), NaN, "nan");
            D("erfinv", Libm.erfinv(Min), Min, "min subnormal");
            D("erfcinv", Libm.erfcinv(2), -Inf, "2"); D("erfcinv", Libm.erfcinv(1), 0.0, "1");
            D("erfcinv", Libm.erfcinv(-1), NaN, "-1"); D("erfcinv", Libm.erfcinv(2.5), NaN, "2.5");
            D("erfcinv", Libm.erfcinv(Inf), NaN, "inf"); D("erfcinv", Libm.erfcinv(NaN), NaN, "nan");
            S("erfinvf", Libm.erfinvf(1), float.PositiveInfinity, "1"); S("erfinvf", Libm.erfinvf(-2), float.NaN, "-2");
            S("erfcinvf", Libm.erfcinvf(0), float.PositiveInfinity, "0"); S("erfcinvf", Libm.erfcinvf(2), float.NegativeInfinity, "2");
            S("erfcinvf", Libm.erfcinvf(1), 0f, "1");

            if (total > 0) Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} special values: {total - bad}/{total}");
            return bad == 0 ? 0 : 1;
        }
    }
}
