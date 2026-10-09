using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SharpLibm.Tests
{
    /// <summary>
    /// Vectors extracted from LLVM libc's math tests by tools/llvm_extract.py
    /// (llvm-vec/llvm.txt): "function kind source args... =&gt; results", floating
    /// values as hex bit patterns, integers as #n. Kind "eq" is an expected value
    /// written in the test, "mpfr" an input the test checks against MPFR at run
    /// time, with the correctly rounded result computed by the extractor. Every
    /// result must match bit for bit (any NaN matches any NaN).
    /// </summary>
    internal static unsafe class LlvmVectors
    {
        public static int Run(string file, params string[] only)
        {
            if (!File.Exists(file)) { Console.WriteLine("  --   no LLVM libc vectors at " + file + " (tools/llvm_extract.py)"); return 0; }
            var sw = Stopwatch.StartNew();
            var byFunc = new SortedDictionary<string, List<string[]>>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(file))
            {
                if (line.Length == 0) continue;
                string[] t = line.Split(' ');
                if (only.Length != 0 && !only.Contains(t[0])) continue;
                if (!byFunc.TryGetValue(t[0], out var list)) byFunc[t[0]] = list = new List<string[]>();
                list.Add(t);
            }
            int failedFunctions = 0;
            foreach (var (name, cases) in byFunc)
            {
                int bad = 0, nEq = 0, nMpfr = 0;
                foreach (string[] t in cases)
                {
                    int arrow = Array.IndexOf(t, "=>");
                    string[] args = t[3..arrow], want = t[(arrow + 1)..];
                    if (t[1] == "eq") nEq++; else nMpfr++;
                    string got;
                    try { got = Check(name, args, want); }
                    catch (Exception e) { got = e.GetType().Name + ": " + (e.InnerException?.Message ?? e.Message); }
                    if (got == null) continue;
                    if (++bad <= 5) Console.WriteLine($"    {t[2]}: {name}({string.Join(", ", args)}) = {got}, want {string.Join(" ", want)} ({t[1]})");
                }
                Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} {name}: {cases.Count - bad}/{cases.Count} (eq {nEq}, mpfr {nMpfr})");
                if (bad != 0) failedFunctions++;
            }
            Console.WriteLine($"  LLVM libc: {sw.Elapsed.TotalSeconds:F1} s");
            return failedFunctions;
        }

        /// <summary>null if the result matches, else what was got.</summary>
        private static string Check(string name, string[] args, string[] want)
        {
            if (name == "sincos")
            {
                double s, c; Libm.sincos(D(args[0]), &s, &c);
                return SameD(s, want[0]) && SameD(c, want[1]) ? null : $"{Bits.Of(s):x16} {Bits.Of(c):x16}";
            }
            if (name == "sincosf")
            {
                float s, c; Libm.sincosf(F(args[0]), &s, &c);
                return SameF(s, want[0]) && SameF(c, want[1]) ? null : $"{Bits.Of(s):x8} {Bits.Of(c):x8}";
            }
            MethodInfo m = typeof(Libm).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length && x.GetParameters().All(p => !p.ParameterType.IsPointer))
                ?? throw new MissingMethodException("Libm." + name);
            ParameterInfo[] ps = m.GetParameters();
            var values = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Type ty = ps[i].ParameterType;
                string a = args[i];
                if (a.StartsWith('#'))
                {
                    long n = long.Parse(a.Substring(1), CultureInfo.InvariantCulture);
                    values[i] = ty == typeof(int) ? checked((int)n) : ty == typeof(long) ? n : throw new FormatException("integer for " + ty);
                }
                else if (ty == typeof(double)) values[i] = a.Length == 8 ? (double)F(a) : D(a);
                else if (ty == typeof(float)) values[i] = F(a);
                else throw new FormatException("parameter " + ty);
            }
            object r = m.Invoke(null, values);
            string w = want[0];
            switch (r)
            {
                case double d: return SameD(d, w) ? null : $"{Bits.Of(d):x16}";
                case float f: return SameF(f, w) ? null : $"{Bits.Of(f):x8}";
                case int or long:
                    long got = Convert.ToInt64(r);
                    long expected = w switch
                    {
                        "FP_ILOGB0" => Libm.FP_ILOGB0,
                        "FP_ILOGBNAN" => Libm.FP_ILOGBNAN,
                        _ => long.Parse(w.TrimStart('#'), CultureInfo.InvariantCulture),
                    };
                    return got == expected ? null : got.ToString(CultureInfo.InvariantCulture);
            }
            throw new FormatException("result " + r?.GetType());
        }

        private static double D(string hex) => Bits.Double(ulong.Parse(hex, NumberStyles.HexNumber));
        private static float F(string hex) => Bits.Single(uint.Parse(hex, NumberStyles.HexNumber));

        private static bool SameD(double got, string want)
        {
            double w = D(want);
            return Bits.IsNaN(w) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(w);
        }

        private static bool SameF(float got, string want)
        {
            float w = F(want);
            return Bits.IsNaN(w) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(w);
        }
    }
}
