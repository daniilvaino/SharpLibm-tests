using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpLibm.Tests
{
    /// <summary>
    /// Berkeley TestFloat 3 vectors made by tools/testfloat_gen.sh
    /// (testfloat-vec/&lt;function&gt;_&lt;rounding&gt;_L&lt;level&gt;.txt[.gz]): per line the
    /// operands, the result and the exception flags, all as hex bit patterns.
    /// The result must match bit for bit (any NaN matches any NaN); the flags
    /// are not checked. Every operation here is exact or correctly rounded, so
    /// SoftFloat's result is the only right one.
    /// </summary>
    internal static unsafe class TestFloat
    {
        private delegate bool Check(ulong a, ulong b, ulong c, ulong z);

        public static int Run(string dir, params string[] only)
        {
            if (!Directory.Exists(dir)) { Console.WriteLine("  --   no TestFloat vectors at " + dir + " (tools/testfloat_gen.sh)"); return 0; }
            var sw = Stopwatch.StartNew();
            int failed = 0;
            var files = Directory.GetFiles(dir, "*.txt").Concat(Directory.GetFiles(dir, "*.txt.gz"))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                name = name.Substring(0, name.IndexOf(".txt", StringComparison.Ordinal));
                if (only.Length != 0 && !only.Any(o => name.StartsWith(o, StringComparison.Ordinal))) continue;
                // <f32|f64>_<op>_<rounding>_L<level>
                string[] p = name.Split('_');
                bool f32 = p[0] == "f32";
                string op = p[1], level = p[^1], rounding = string.Join('_', p[2..^1]);
                var checks = Checks(op, rounding, f32);
                if (checks.Count == 0) { Console.WriteLine($"  --   {name}: not mapped"); continue; }
                int arity = op == "mulAdd" ? 3 : op == "rem" ? 2 : 1;
                failed += RunFile(path, name, arity, f32, checks);
            }
            Console.WriteLine($"  TestFloat: {sw.Elapsed.TotalSeconds:F1} s");
            return failed;
        }

        private static List<(string, Check)> Checks(string op, string rounding, bool f32)
        {
            var list = new List<(string, Check)>();
            void U(string name, Func<double, double> d, Func<float, float> f) =>
                list.Add((f32 ? name + "f" : name, f32
                    ? (a, _, _, z) => Same(f(Bits.Single((uint)a)), (uint)z)
                    : (a, _, _, z) => Same(d(Bits.Double(a)), z)));
            switch (op, rounding)
            {
                case ("mulAdd", "near_even"):
                    list.Add((f32 ? "fmaf" : "fma", f32
                        ? (a, b, c, z) => Same(Libm.fmaf(Bits.Single((uint)a), Bits.Single((uint)b), Bits.Single((uint)c)), (uint)z)
                        : (a, b, c, z) => Same(Libm.fma(Bits.Double(a), Bits.Double(b), Bits.Double(c)), z)));
                    break;
                case ("sqrt", "near_even"): U("sqrt", Libm.sqrt, Libm.sqrtf); break;
                case ("roundToInt", "near_even"):
                    U("roundeven", Libm.roundeven, Libm.roundevenf);
                    U("rint", Libm.rint, Libm.rintf);
                    U("nearbyint", Libm.nearbyint, Libm.nearbyintf);
                    break;
                case ("roundToInt", "minMag"): U("trunc", Libm.trunc, Libm.truncf); break;
                case ("roundToInt", "min"): U("floor", Libm.floor, Libm.floorf); break;
                case ("roundToInt", "max"): U("ceil", Libm.ceil, Libm.ceilf); break;
                case ("roundToInt", "near_maxMag"): U("round", Libm.round, Libm.roundf); break;
                case ("rem", "near_even"):
                    list.Add((f32 ? "remainderf" : "remainder", f32
                        ? (a, b, _, z) => Same(Libm.remainderf(Bits.Single((uint)a), Bits.Single((uint)b)), (uint)z)
                        : (a, b, _, z) => Same(Libm.remainder(Bits.Double(a), Bits.Double(b)), z)));
                    list.Add((f32 ? "remquof" : "remquo", f32
                        ? (a, b, _, z) => { int q; return Same(Libm.remquof(Bits.Single((uint)a), Bits.Single((uint)b), &q), (uint)z); }
                        : (a, b, _, z) => { int q; return Same(Libm.remquo(Bits.Double(a), Bits.Double(b), &q), z); }));
                    break;
            }
            return list;
        }

        private const int Batch = 1 << 20;

        private static int RunFile(string path, string name, int arity, bool f32, List<(string Name, Check Fn)> checks)
        {
            var sw = Stopwatch.StartNew();
            Stream s = File.OpenRead(path);
            if (path.EndsWith(".gz", StringComparison.Ordinal)) s = new GZipStream(s, CompressionMode.Decompress);
            using var reader = new StreamReader(s, System.Text.Encoding.ASCII, false, 1 << 20);
            long total = 0;
            var bad = new long[checks.Count];
            var shown = new List<string>();
            var buf = new ulong[Batch * 4];
            while (true)
            {
                int n = 0;
                string line;
                while (n < Batch && (line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    ReadOnlySpan<char> span = line;
                    for (int k = 0; k <= arity; k++)
                    {
                        span = span.TrimStart(' ');
                        int end = span.IndexOf(' ');
                        if (end < 0) end = span.Length;
                        buf[n * 4 + k] = ParseHex(span.Slice(0, end));
                        span = span.Slice(end);
                    }
                    n++;
                }
                if (n == 0) break;
                total += n;
                for (int ci = 0; ci < checks.Count; ci++)
                {
                    Check fn = checks[ci].Fn;
                    string fname = checks[ci].Name;
                    int idx = ci;
                    Parallel.For(0, (n + 4095) / 4096, chunk =>
                    {
                        long local = 0;
                        int end = Math.Min(n, (chunk + 1) * 4096);
                        for (int i = chunk * 4096; i < end; i++)
                        {
                            ulong a = buf[i * 4], b = buf[i * 4 + 1], c = buf[i * 4 + 2], z = buf[i * 4 + arity];
                            bool ok;
                            try { ok = fn(a, b, c, z); } catch (Exception) { ok = false; }
                            if (ok) continue;
                            local++;
                            lock (shown)
                                if (shown.Count < 5)
                                    shown.Add($"    {fname}({string.Join(", ", Enumerable.Range(0, arity).Select(k => Show(buf[i * 4 + k], f32)))}) want {Show(z, f32)}, got {Got(fname, arity, f32, a, b, c)}");
                        }
                        if (local != 0) Interlocked.Add(ref bad[idx], local);
                    });
                }
            }
            foreach (string t in shown) Console.WriteLine(t);
            for (int ci = 0; ci < checks.Count; ci++)
                Console.WriteLine($"  {(bad[ci] == 0 ? "ok  " : "FAIL")} {name} ({checks[ci].Name}): {total - bad[ci]}/{total}  {sw.Elapsed.TotalSeconds:F1} s");
            return bad.Count(x => x != 0);
        }

        private static ulong ParseHex(ReadOnlySpan<char> s)
        {
            ulong v = 0;
            foreach (char ch in s)
            {
                int d = ch <= '9' ? ch - '0' : (ch | 0x20) - 'a' + 10;
                if ((uint)d > 15) throw new FormatException("bad hex " + s.ToString());
                v = (v << 4) | (uint)d;
            }
            return v;
        }

        private static bool Same(double got, ulong want) =>
            Bits.IsNaN(Bits.Double(want)) ? Bits.IsNaN(got) : Bits.Of(got) == want;

        private static bool Same(float got, uint want) =>
            Bits.IsNaN(Bits.Single(want)) ? Bits.IsNaN(got) : Bits.Of(got) == want;

        private static string Show(ulong x, bool f32) =>
            f32 ? $"{Bits.Single((uint)x):R} (0x{x:x8})" : $"{Bits.Double(x):R} (0x{x:x16})";

        // Recomputes a failing result for the message.
        private static string Got(string fname, int arity, bool f32, ulong a, ulong b, ulong c)
        {
            try
            {
                string n = f32 ? fname.Substring(0, fname.Length - 1) : fname;
                Type t = f32 ? typeof(float) : typeof(double);
                if (n == "remquo") return "(remquo)";
                var m = typeof(Libm).GetMethod(fname, Enumerable.Repeat(t, arity).ToArray());
                object[] args = new[] { a, b, c }.Take(arity).Select(x => f32 ? (object)Bits.Single((uint)x) : Bits.Double(x)).ToArray();
                object r = m.Invoke(null, args);
                return f32 ? Show(Bits.Of((float)r), true) : Show(Bits.Of((double)r), false);
            }
            catch (Exception e) { return e.GetType().Name; }
        }
    }
}
