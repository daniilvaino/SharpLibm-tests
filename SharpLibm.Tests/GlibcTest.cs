using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SharpLibm.Tests
{
    /// <summary>
    /// glibc's libm tests (math/ of a glibc checkout):
    /// <list type="bullet">
    /// <item>auto-libm-test-out-&lt;f&gt;: lines "= f mode format inputs : outputs : annotations",
    /// outputs computed by MPFR and correctly rounded to the format;</item>
    /// <item>libm-test-&lt;f&gt;.inc: hand-written TEST_&lt;shape&gt; (f, inputs, outputs, flags[, outputs, flags]×3)
    /// macros, inputs and outputs as C literals, under #if conditions on the format.</item>
    /// </list>
    /// Only round-to-nearest, binary32 and binary64 are used (in the .inc the first output group
    /// is round-to-nearest). Flags, errno and annotations are ignored: "xfail…" marks a known
    /// glibc inaccuracy, but the expected value is still the correctly rounded one, so it is
    /// checked like any other. Every result must match bit for bit, any NaN matching any NaN;
    /// an output written IGNORE (frexp's exponent of inf, llrint out of range) is not checked.
    /// </summary>
    internal static unsafe class GlibcTest
    {
        private sealed class Case
        {
            public string Source;     // file:line
            public string[] In, Out;
            public string Flags = "";
        }

        // IGNORE_ZERO_INF_SIGN / ignore-zero-inf-sign on the current case: a zero or infinite result may have either sign.
        [ThreadStatic] private static bool s_ignoreZeroInfSign;

        private const string Skip = "\0skip";

        // TEST_<shape>: number of inputs and outputs after the function name.
        private static readonly Dictionary<string, (int In, int Out)> Shapes = new()
        {
            ["f_f"] = (1, 1), ["ff_f"] = (2, 1), ["fff_f"] = (3, 1), ["f_f1"] = (1, 2), ["fFF_11"] = (1, 2),
            ["fF_f1"] = (1, 2), ["fI_f1"] = (1, 2), ["ffI_f1"] = (2, 2), ["f_L"] = (1, 1), ["f_l"] = (1, 1),
            ["f_i"] = (1, 1), ["fi_f"] = (2, 1), ["fl_f"] = (2, 1), ["fj_f"] = (2, 1), ["fL_f"] = (2, 1),
        };

        private static readonly string[] Unary =
        {
            "acos", "acosh", "acospi", "asin", "asinh", "asinpi", "atan", "atanh", "atanpi", "cbrt", "cos", "cosh",
            "cospi", "erf", "erfc", "exp", "exp10", "exp10m1", "exp2", "exp2m1", "expm1", "log", "log10", "log10p1",
            "log1p", "log2", "log2p1", "rsqrt", "sin", "sinh", "sinpi", "sqrt", "tan", "tanh", "tanpi", "tgamma",
            "floor", "ceil", "trunc", "round", "rint", "nearbyint", "roundeven", "fabs", "logb",
        };

        private static readonly string[] Binary =
        {
            "atan2", "atan2pi", "hypot", "pow", "fmod", "remainder", "copysign", "fdim", "fmax", "fmin", "nextafter",
        };

        public static int Run(string glibc, params string[] only)
        {
            string math = Path.Combine(glibc, "math");
            if (!Directory.Exists(math)) { Console.WriteLine("  --   no glibc math tests at " + math); return 0; }
            var sw = Stopwatch.StartNew();
            // (C name, bits) -> cases, per source kind
            var auto = new Dictionary<(string, int), List<Case>>();
            var inc = new Dictionary<(string, int), List<Case>>();
            foreach (string file in Directory.GetFiles(math, "auto-libm-test-out-*").OrderBy(f => f, StringComparer.Ordinal))
                LoadAuto(file, auto);
            int unparsedInc = 0;
            foreach (string file in Directory.GetFiles(math, "libm-test-*.inc").OrderBy(f => f, StringComparer.Ordinal))
                foreach (int bits in new[] { 32, 64 })
                    unparsedInc += LoadInc(file, bits, inc);

            bool Want(string name) => only.Length == 0 || only.Contains(name);
            int failedFunctions = 0;
            var names = auto.Keys.Concat(inc.Keys).Distinct().OrderBy(k => k.Item1, StringComparer.Ordinal).ThenBy(k => k.Item2);
            var unsupported = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var key in names)
            {
                var (func, bits) = key;
                string cname = bits == 32 ? func + "f" : func;
                if (!Want(cname) && !Want(func)) continue;
                Func<Case, string> check = Checker(func, bits);
                if (check == null) { unsupported.Add(cname); continue; }
                int total = 0, bad = 0, skipped = 0, unparsed = 0;
                int nAuto = 0, nInc = 0;
                foreach (var (dict, kind) in new[] { (auto, 0), (inc, 1) })
                {
                    if (!dict.TryGetValue(key, out List<Case> cases)) continue;
                    foreach (Case c in cases)
                    {
                        string r;
                        s_ignoreZeroInfSign = c.Flags.Contains("IGNORE_ZERO_INF_SIGN") || c.Flags.Contains("ignore-zero-inf-sign");
                        try { r = check(c); }
                        catch (FormatException e)
                        {
                            if (unparsed++ == 0) Console.WriteLine($"    {c.Source}: unparsed ({e.Message}), e.g. {cname}({string.Join(", ", c.In)})");
                            continue;
                        }
                        catch (Exception e) { r = e.GetType().Name + ": " + e.Message; }
                        if (r == Skip) { skipped++; continue; }
                        total++;
                        if (kind == 0) nAuto++; else nInc++;
                        if (r == null) continue;
                        if (++bad <= 5) Console.WriteLine($"    {c.Source}: {cname}({string.Join(", ", c.In)}) = {r}; want {string.Join(" ", c.Out)}");
                    }
                }
                string extra = (skipped == 0 ? "" : $", {skipped} not applicable") + (unparsed == 0 ? "" : $", {unparsed} unparsed");
                Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} {cname}: {total - bad}/{total} (auto {nAuto}, inc {nInc}{extra})");
                if (bad != 0) failedFunctions++;
            }
            if (unsupported.Count != 0) Console.WriteLine($"  --   not in SharpLibm: {string.Join(" ", unsupported)}");
            if (unparsedInc != 0) Console.WriteLine($"  --   {unparsedInc} .inc test lines of other shapes (complex, narrowing, fromfp, classification) skipped");
            Console.WriteLine($"  glibc: {sw.Elapsed.TotalSeconds:F1} s");
            return failedFunctions;
        }

        // ---- loading ----

        private static void LoadAuto(string file, Dictionary<(string, int), List<Case>> into)
        {
            string fname = Path.GetFileName(file);
            int n = 0;
            foreach (string line in File.ReadLines(file))
            {
                n++;
                if (!line.StartsWith("= ")) continue;
                // "= func tonearest binary64 in... : out... : annotations"
                int a = line.IndexOf(' ', 2), b = line.IndexOf(' ', a + 1), c = line.IndexOf(' ', b + 1);
                if (a < 0 || b < 0 || c < 0) continue;
                if (string.CompareOrdinal(line, a + 1, "tonearest ", 0, 10) != 0) continue;
                string format = line.Substring(b + 1, c - b - 1);
                int bits = format == "binary32" ? 32 : format == "binary64" ? 64 : 0;
                if (bits == 0) continue;
                string func = line.Substring(2, a - 2);
                string[] parts = line.Substring(c + 1).Split(" : ");
                if (parts.Length < 2) continue;
                Add(into, (func, bits), new Case
                {
                    Source = fname + ":" + n,
                    In = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    Out = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    Flags = parts.Length > 2 ? parts[2] : "",
                });
            }
        }

        private static readonly Regex TestLine = new(@"^\s*TEST_(\w+)\s*\((.*)\)\s*,?\s*$");

        private static int LoadInc(string file, int bits, Dictionary<(string, int), List<Case>> into)
        {
            string fname = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);
            var stack = new Stack<(bool parent, bool taken, bool active)>();
            bool active = true;
            int unknown = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith('#'))
                {
                    string d = t.Substring(1).TrimStart();
                    if (d.StartsWith("ifdef") || d.StartsWith("ifndef"))
                    {
                        bool def = Defined(d.Substring(d.StartsWith("ifdef") ? 5 : 6).Trim());
                        bool v = d.StartsWith("ifdef") ? def : !def;
                        stack.Push((active, v, active && v)); active = active && v;
                    }
                    else if (d.StartsWith("if"))
                    {
                        bool v = Eval(d.Substring(2), bits) != 0;
                        stack.Push((active, v, active && v)); active = active && v;
                    }
                    else if (d.StartsWith("elif"))
                    {
                        var (parent, taken, _) = stack.Pop();
                        bool v = !taken && Eval(d.Substring(4), bits) != 0;
                        stack.Push((parent, taken || v, parent && v)); active = parent && v;
                    }
                    else if (d.StartsWith("else"))
                    {
                        var (parent, taken, _) = stack.Pop();
                        stack.Push((parent, true, parent && !taken)); active = parent && !taken;
                    }
                    else if (d.StartsWith("endif"))
                    {
                        active = stack.Pop().parent;
                    }
                    continue;
                }
                if (!active) continue;
                Match m = TestLine.Match(lines[i]);
                if (!m.Success) continue;
                List<string> args = SplitArgs(m.Groups[2].Value);
                if (!Shapes.TryGetValue(m.Groups[1].Value, out var shape)) { unknown++; continue; }
                if (args.Count < 1 + shape.In + shape.Out) { unknown++; continue; }
                // One output group (outputs + flags) for all rounding modes, or four, in the order
                // downward, tonearest, towardzero, upward: then the second is round-to-nearest.
                int group = shape.Out + 1, start = 1 + shape.In;
                if (args.Count >= start + 4 * group) start += group;
                Add(into, (args[0], bits), new Case
                {
                    Source = fname + ":" + (i + 1),
                    In = args.GetRange(1, shape.In).ToArray(),
                    Out = args.GetRange(start, shape.Out).ToArray(),
                    Flags = start + shape.Out < args.Count ? args[start + shape.Out] : "",
                });
            }
            return unknown;
        }

        private static void Add(Dictionary<(string, int), List<Case>> d, (string, int) key, Case c)
        {
            if (!d.TryGetValue(key, out List<Case> list)) d[key] = list = new List<Case>();
            list.Add(c);
        }

        private static List<string> SplitArgs(string s)
        {
            var result = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                else if (s[i] == ',' && depth == 0) { result.Add(s.Substring(start, i - start).Trim()); start = i + 1; }
            }
            result.Add(s.Substring(start).Trim());
            return result;
        }

        // ---- the C preprocessor conditions of the .inc files, and integer arguments ----

        private static bool Defined(string name) => name == "__SUPPORT_SNAN__";

        private static Int128 Symbol(string name, int bits) => name switch
        {
            "MANT_DIG" => bits == 32 ? 24 : 53,
            "MIN_EXP" => bits == 32 ? -125 : -1021,
            "MAX_EXP" => bits == 32 ? 128 : 1024,
            "PAYLOAD_DIG" => bits == 32 ? 22 : 51,
            "LDBL_MANT_DIG" => 64,
            "TEST_COND_binary32" => bits == 32 ? 1 : 0,
            "TEST_COND_binary64" => bits == 64 ? 1 : 0,
            // SharpLibm's lrint/lround return a 64-bit long, so the LP64 cases apply.
            "LONG_MAX" or "LLONG_MAX" => long.MaxValue,
            "LONG_MIN" or "LLONG_MIN" => long.MinValue,
            "INT_MAX" => int.MaxValue,
            "INT_MIN" => int.MinValue,
            "FP_ILOGB0" => Libm.FP_ILOGB0,
            "FP_ILOGBNAN" => Libm.FP_ILOGBNAN,
            _ => 0,   // TEST_FLOATN, TEST_MATHVEC, other formats, HIGH_ORDER_BIT_IS_SET_FOR_SNAN …
        };

        private static Int128 Eval(string expr, int bits)
        {
            var tokens = Regex.Matches(expr, @"0[xX][0-9a-fA-F]+[uUlL]*|\d+[uUlL]*|[A-Za-z_]\w*|&&|\|\||[<>=!]=|[-+*/()<>!,]")
                              .Select(m => m.Value).ToList();
            int pos = 0;
            Int128 v = Or(tokens, ref pos, bits);
            if (pos != tokens.Count) throw new FormatException("bad expression: " + expr);
            return v;
        }

        private static Int128 Or(List<string> t, ref int p, int bits)
        {
            Int128 v = And(t, ref p, bits);
            while (p < t.Count && t[p] == "||") { p++; Int128 w = And(t, ref p, bits); v = v != 0 || w != 0 ? 1 : 0; }
            return v;
        }

        private static Int128 And(List<string> t, ref int p, int bits)
        {
            Int128 v = Cmp(t, ref p, bits);
            while (p < t.Count && t[p] == "&&") { p++; Int128 w = Cmp(t, ref p, bits); v = v != 0 && w != 0 ? 1 : 0; }
            return v;
        }

        private static Int128 Cmp(List<string> t, ref int p, int bits)
        {
            Int128 v = Add(t, ref p, bits);
            while (p < t.Count && t[p] is "<" or ">" or "<=" or ">=" or "==" or "!=")
            {
                string op = t[p++];
                Int128 w = Add(t, ref p, bits);
                bool r = op switch { "<" => v < w, ">" => v > w, "<=" => v <= w, ">=" => v >= w, "==" => v == w, _ => v != w };
                v = r ? 1 : 0;
            }
            return v;
        }

        private static Int128 Add(List<string> t, ref int p, int bits)
        {
            Int128 v = Mul(t, ref p, bits);
            while (p < t.Count && t[p] is "+" or "-")
            {
                string op = t[p++];
                Int128 w = Mul(t, ref p, bits);
                v = op == "+" ? v + w : v - w;
            }
            return v;
        }

        private static Int128 Mul(List<string> t, ref int p, int bits)
        {
            Int128 v = Prim(t, ref p, bits);
            while (p < t.Count && t[p] is "*" or "/")
            {
                string op = t[p++];
                Int128 w = Prim(t, ref p, bits);
                v = op == "*" ? v * w : v / w;
            }
            return v;
        }

        private static Int128 Prim(List<string> t, ref int p, int bits)
        {
            if (p >= t.Count) throw new FormatException("truncated expression");
            string s = t[p++];
            if (s == "-") return -Prim(t, ref p, bits);
            if (s == "+") return Prim(t, ref p, bits);
            if (s == "!") return Prim(t, ref p, bits) == 0 ? 1 : 0;
            if (s == "(")
            {
                Int128 v = Or(t, ref p, bits);
                if (p >= t.Count || t[p++] != ")") throw new FormatException("missing )");
                return v;
            }
            if (char.IsAsciiDigit(s[0]))
            {
                string d = s.TrimEnd('u', 'U', 'l', 'L');
                return d.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? (Int128)UInt128.Parse(d.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                    : Int128.Parse(d, CultureInfo.InvariantCulture);
            }
            if (p < t.Count && t[p] == "(")   // TEST_COMPAT (…), defined (…): skip the call, value 0
            {
                int depth = 0;
                do { if (t[p] == "(") depth++; else if (t[p] == ")") depth--; p++; } while (depth > 0 && p < t.Count);
                return 0;
            }
            if (char.IsLetter(s[0]) || s[0] == '_')
            {
                if (s is "IGNORE") throw new FormatException("IGNORE");
                return Symbol(s, bits);
            }
            throw new FormatException("bad token " + s);
        }

        // An integer argument or result: a literal or a small expression of them.
        private static Int128 Int(string s, int bits)
        {
            if (Regex.IsMatch(s, @"^-?\d+\.\d*$"))   // an integral value written as a floating literal ("0.0")
            {
                double v = double.Parse(s, CultureInfo.InvariantCulture);
                if (v != Math.Floor(v)) throw new FormatException("not an integer " + s);
                return (Int128)v;
            }
            foreach (Match m in Regex.Matches(s, @"\b[A-Za-z_]\w*\b"))
                if (m.Value is not ("INT_MAX" or "INT_MIN" or "LONG_MAX" or "LONG_MIN" or "LLONG_MAX" or "LLONG_MIN"
                    or "FP_ILOGB0" or "FP_ILOGBNAN" or "MANT_DIG" or "MIN_EXP" or "MAX_EXP"))
                    throw new FormatException("unknown integer " + s);
            return Eval(s, bits);
        }

        // ---- checking ----

        private static double D(string s)
        {
            if (!CLiteral.TryParse(s, 64, out ulong b, out _)) throw new FormatException(s);
            return Bits.Double(b);
        }

        private static float F(string s)
        {
            if (!CLiteral.TryParse(s, 32, out ulong b, out _)) throw new FormatException(s);
            return Bits.Single((uint)b);
        }

        private static bool Same(double got, double want) => Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);
        private static bool Same(float got, float want) => Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);
        private static string Hex(double x) => Bits.IsNaN(x) ? "nan" : $"{x:R} (0x{Bits.Of(x):x16})";
        private static string Hex(float x) => Bits.IsNaN(x) ? "nan" : $"{x:R} (0x{Bits.Of(x):x8})";
        private static bool Ignored(string s) => s == "IGNORE";

        private static T Method<T>(string name, params Type[] args) where T : Delegate =>
            (T)typeof(Libm).GetMethod(name, BindingFlags.Public | BindingFlags.Static, args)?.CreateDelegate(typeof(T));

        /// <summary>The check of one case (null: passed, Skip: not applicable, else what was got), or null if SharpLibm lacks the function.</summary>
        private static Func<Case, string> Checker(string func, int bits)
        {
            bool f32 = bits == 32;
            string name = f32 ? func + "f" : func;
            if (Unary.Contains(func))
            {
                if (f32) { var f = Method<Func<float, float>>(name, typeof(float)); if (f == null) return null; return c => R(f(F(c.In[0])), F(c.Out[0])); }
                else { var f = Method<Func<double, double>>(name, typeof(double)); if (f == null) return null; return c => R(f(D(c.In[0])), D(c.Out[0])); }
            }
            if (func is "fmax" or "fmin")
            {
                // glibc follows TS 18661-1: a signalling NaN operand gives a quiet NaN. SharpLibm
                // has no signalling-NaN semantics (no flags); like C99 and musl it ignores any NaN operand.
                Func<Case, string> inner = BinaryChecker(name, f32);
                return c => c.In.Any(a => a.Contains("snan")) ? Skip : inner(c);
            }
            if (Binary.Contains(func)) return BinaryChecker(name, f32);
            switch (func)
            {
                case "fma":
                    if (f32) return c => R(Libm.fmaf(F(c.In[0]), F(c.In[1]), F(c.In[2])), F(c.Out[0]));
                    return c => R(Libm.fma(D(c.In[0]), D(c.In[1]), D(c.In[2])), D(c.Out[0]));
                case "lgamma":
                    if (f32) return c =>
                    {
                        int s; float y = Libm.lgammaf_r(F(c.In[0]), &s); float y2 = Libm.lgammaf(F(c.In[0]));
                        bool ok = Same(y, F(c.Out[0])) && Same(y2, F(c.Out[0])) && (Ignored(c.Out[1]) || Bits.IsNaN(y) || s == Int(c.Out[1], bits));
                        return ok ? null : $"{Hex(y)} sign {s} (lgammaf {Hex(y2)})";
                    };
                    return c =>
                    {
                        int s; double y = Libm.lgamma_r(D(c.In[0]), &s); double y2 = Libm.lgamma(D(c.In[0]));
                        bool ok = Same(y, D(c.Out[0])) && Same(y2, D(c.Out[0])) && (Ignored(c.Out[1]) || Bits.IsNaN(y) || s == Int(c.Out[1], bits));
                        return ok ? null : $"{Hex(y)} sign {s} (lgamma {Hex(y2)})";
                    };
                case "sincos":
                    if (f32) return c =>
                    {
                        float s, co; Libm.sincosf(F(c.In[0]), &s, &co);
                        return Same(s, F(c.Out[0])) && Same(co, F(c.Out[1])) ? null : $"{Hex(s)} {Hex(co)}";
                    };
                    return c =>
                    {
                        double s, co; Libm.sincos(D(c.In[0]), &s, &co);
                        return Same(s, D(c.Out[0])) && Same(co, D(c.Out[1])) ? null : $"{Hex(s)} {Hex(co)}";
                    };
                case "modf":
                    if (f32) return c =>
                    {
                        float i; float fr = Libm.modff(F(c.In[0]), &i);
                        return Same(fr, F(c.Out[0])) && Same(i, F(c.Out[1])) ? null : $"{Hex(fr)} {Hex(i)}";
                    };
                    return c =>
                    {
                        double i; double fr = Libm.modf(D(c.In[0]), &i);
                        return Same(fr, D(c.Out[0])) && Same(i, D(c.Out[1])) ? null : $"{Hex(fr)} {Hex(i)}";
                    };
                case "frexp":
                    if (f32) return c =>
                    {
                        int e; float y = Libm.frexpf(F(c.In[0]), &e);
                        return Same(y, F(c.Out[0])) && (Ignored(c.Out[1]) || e == Int(c.Out[1], bits)) ? null : $"{Hex(y)} {e}";
                    };
                    return c =>
                    {
                        int e; double y = Libm.frexp(D(c.In[0]), &e);
                        return Same(y, D(c.Out[0])) && (Ignored(c.Out[1]) || e == Int(c.Out[1], bits)) ? null : $"{Hex(y)} {e}";
                    };
                case "remquo":   // glibc's RUN_TEST_LOOP_ffI_f1_mod8: the quotient compared modulo 8 (C %, sign kept)
                    if (f32) return c =>
                    {
                        int q; float r = Libm.remquof(F(c.In[0]), F(c.In[1]), &q);
                        return Same(r, F(c.Out[0])) && (Ignored(c.Out[1]) || q % 8 == Int(c.Out[1], bits) % 8) ? null : $"{Hex(r)} {q}";
                    };
                    return c =>
                    {
                        int q; double r = Libm.remquo(D(c.In[0]), D(c.In[1]), &q);
                        return Same(r, D(c.Out[0])) && (Ignored(c.Out[1]) || q % 8 == Int(c.Out[1], bits) % 8) ? null : $"{Hex(r)} {q}";
                    };
                case "llrint": case "llround": case "lrint": case "lround":
                {
                    if (f32) { var f = Method<Func<float, long>>(name, typeof(float)); return c => Ignored(c.Out[0]) ? Skip : RI(f(F(c.In[0])), Int(c.Out[0], bits)); }
                    var g = Method<Func<double, long>>(name, typeof(double));
                    return c => Ignored(c.Out[0]) ? Skip : RI(g(D(c.In[0])), Int(c.Out[0], bits));
                }
                case "ilogb":
                    if (f32) return c => RI(Libm.ilogbf(F(c.In[0])), Int(c.Out[0], bits));
                    return c => RI(Libm.ilogb(D(c.In[0])), Int(c.Out[0], bits));
                case "scalbn":
                    if (f32) return c => { Int128 n = Int(c.In[1], bits); return n < int.MinValue || n > int.MaxValue ? Skip : R(Libm.scalbnf(F(c.In[0]), (int)n), F(c.Out[0])); };
                    return c => { Int128 n = Int(c.In[1], bits); return n < int.MinValue || n > int.MaxValue ? Skip : R(Libm.scalbn(D(c.In[0]), (int)n), D(c.Out[0])); };
                case "scalbln":
                    if (f32) return c => { Int128 n = Int(c.In[1], bits); return n < long.MinValue || n > long.MaxValue ? Skip : R(Libm.scalblnf(F(c.In[0]), (long)n), F(c.Out[0])); };
                    return c => { Int128 n = Int(c.In[1], bits); return n < long.MinValue || n > long.MaxValue ? Skip : R(Libm.scalbln(D(c.In[0]), (long)n), D(c.Out[0])); };
                case "nexttoward":   // y is long double; only cases where it is exact in double apply
                    return c =>
                    {
                        if (!CLiteral.TryParse(c.In[1], 64, out ulong yb, out bool exact) || !exact) return Skip;
                        double y = Bits.Double(yb);
                        return f32 ? R(Libm.nexttowardf(F(c.In[0]), y), F(c.Out[0])) : R(Libm.nexttoward(D(c.In[0]), y), D(c.Out[0]));
                    };
                case "pown":   // pown(x, n) = pow(x, n) whenever n is exact in the format
                    return c =>
                    {
                        Int128 n = Int(c.In[1], bits);
                        Int128 limit = f32 ? 1 << 24 : (Int128)1 << 53;
                        if (n > limit || n < -limit) return Skip;
                        return f32 ? R(Libm.powf(F(c.In[0]), (float)n), F(c.Out[0])) : R(Libm.pow(D(c.In[0]), (double)n), D(c.Out[0]));
                    };
                case "powr":   // powr(x, y) = pow(x, y) for finite x > 0 and finite y; elsewhere their special cases differ
                    return c =>
                    {
                        if (f32)
                        {
                            float x = F(c.In[0]), y = F(c.In[1]);
                            if (!(x > 0) || float.IsInfinity(x) || !float.IsFinite(y)) return Skip;
                            return R(Libm.powf(x, y), F(c.Out[0]));
                        }
                        double xd = D(c.In[0]), yd = D(c.In[1]);
                        if (!(xd > 0) || double.IsInfinity(xd) || !double.IsFinite(yd)) return Skip;
                        return R(Libm.pow(xd, yd), D(c.Out[0]));
                    };
                case "compoundn":   // C23 compoundn / compoundnf, n a long long
                    // compoundnf is correctly rounded (bit for bit); compoundn is
                    // documented as within one ulp (SharpLibm Compound.cs).
                    return c =>
                    {
                        Int128 n = Int(c.In[1], bits);
                        if (n < long.MinValue || n > long.MaxValue) return Skip;
                        return f32 ? R(Libm.compoundnf(F(c.In[0]), (long)n), F(c.Out[0]))
                                   : RUlp(Libm.compoundn(D(c.In[0]), (long)n), D(c.Out[0]), 1);
                    };
            }
            return null;
        }

        private static Func<Case, string> BinaryChecker(string name, bool f32)
        {
            if (f32) { var f = Method<Func<float, float, float>>(name, typeof(float), typeof(float)); if (f == null) return null; return c => R(f(F(c.In[0]), F(c.In[1])), F(c.Out[0])); }
            var g = Method<Func<double, double, double>>(name, typeof(double), typeof(double)); if (g == null) return null;
            return c => R(g(D(c.In[0]), D(c.In[1])), D(c.Out[0]));
        }

        private static string R(double got, double want) =>
            Same(got, want) || (s_ignoreZeroInfSign && (want == 0 || double.IsInfinity(want)) && Math.Abs(got) == Math.Abs(want)) ? null : Hex(got);

        private static string R(float got, float want) =>
            Same(got, want) || (s_ignoreZeroInfSign && (want == 0 || float.IsInfinity(want)) && Math.Abs(got) == Math.Abs(want)) ? null : Hex(got);
        private static string RI(Int128 got, Int128 want) => got == want ? null : got.ToString();

        // Within maxUlp of want, counted in representable doubles (same sign).
        private static string RUlp(double got, double want, long maxUlp)
        {
            if (R(got, want) == null) return null;
            if (double.IsNaN(got) || double.IsNaN(want) || Math.Sign(got) != Math.Sign(want)) return Hex(got);
            long a = BitConverter.DoubleToInt64Bits(Math.Abs(got)), b = BitConverter.DoubleToInt64Bits(Math.Abs(want));
            return Math.Abs(a - b) <= maxUlp ? null : Hex(got) + $" ({Math.Abs(a - b)} ulp)";
        }
    }
}
