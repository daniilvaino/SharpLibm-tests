using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SharpLibm.Tests
{
    /// <summary>
    /// The binary32 functions against CORE-MATH's C, which is proven correctly
    /// rounded for binary32: every one of the 2^32 inputs for the unary
    /// functions, 2^28 random pairs for the binary ones. The C is loaded from a
    /// DLL built from CORE-MATH's src/binary32 (exports cr_&lt;name&gt;), passed
    /// on the command line; it is not part of this repository.
    /// </summary>
    internal static unsafe class Exhaustive32
    {
        public static int Run(string dll, params string[] only)
        {
            IntPtr lib = NativeLibrary.Load(dll);
            bool Want(string name) => only.Length == 0 || Array.IndexOf(only, name) >= 0;
            int failed = 0;
            foreach (var (name, f) in Unary())
            {
                if (!Want(name)) continue;
                if (!NativeLibrary.TryGetExport(lib, "cr_" + name, out IntPtr c)) { Console.WriteLine($"  --   {name}: no cr_{name}"); continue; }
                failed += Unary(name, f, (delegate* unmanaged<float, float>)c);
            }
            foreach (var (name, f) in Binary())
            {
                if (!Want(name)) continue;
                if (!NativeLibrary.TryGetExport(lib, "cr_" + name, out IntPtr c)) { Console.WriteLine($"  --   {name}: no cr_{name}"); continue; }
                failed += Binary(name, f, (delegate* unmanaged<float, float, float>)c);
            }
            return failed;
        }

        private static IEnumerable<(string, Func<float, float>)> Unary() => new (string, Func<float, float>)[]
        {
            ("acosf", Libm.acosf), ("acoshf", Libm.acoshf), ("acospif", Libm.acospif), ("asinf", Libm.asinf),
            ("asinhf", Libm.asinhf), ("asinpif", Libm.asinpif), ("atanf", Libm.atanf), ("atanhf", Libm.atanhf),
            ("atanpif", Libm.atanpif), ("cbrtf", Libm.cbrtf), ("cosf", Libm.cosf), ("coshf", Libm.coshf),
            ("cospif", Libm.cospif), ("erff", Libm.erff), ("erfcf", Libm.erfcf), ("expf", Libm.expf),
            ("exp10f", Libm.exp10f), ("exp10m1f", Libm.exp10m1f), ("exp2f", Libm.exp2f), ("exp2m1f", Libm.exp2m1f),
            ("expm1f", Libm.expm1f), ("lgammaf", Libm.lgammaf), ("logf", Libm.logf), ("log10f", Libm.log10f),
            ("log10p1f", Libm.log10p1f), ("log1pf", Libm.log1pf), ("log2f", Libm.log2f), ("log2p1f", Libm.log2p1f),
            ("rsqrtf", Libm.rsqrtf), ("sinf", Libm.sinf), ("sinhf", Libm.sinhf), ("sinpif", Libm.sinpif),
            ("tanf", Libm.tanf), ("tanhf", Libm.tanhf), ("tanpif", Libm.tanpif), ("tgammaf", Libm.tgammaf),
        };

        private static IEnumerable<(string, Func<float, float, float>)> Binary() => new (string, Func<float, float, float>)[]
        {
            ("atan2f", Libm.atan2f), ("atan2pif", Libm.atan2pif), ("hypotf", Libm.hypotf), ("powf", Libm.powf), ("compoundf", Libm.compoundf),
        };

        private const int Chunks = 4096;               // 2^32 / 2^20

        private static int Unary(string name, Func<float, float> f, delegate* unmanaged<float, float> c)
        {
            long bad = 0;
            var shown = new List<string>();
            Parallel.For(0, Chunks, chunk =>
            {
                long local = 0;
                uint start = (uint)chunk << 20;
                for (uint i = 0; i < 1u << 20; i++)
                {
                    float x = Bits.Single(start + i);
                    float got = f(x), want = c(x);
                    if (Same(got, want)) continue;
                    local++;
                    lock (shown) if (shown.Count < 5) shown.Add($"    {name}({x:R} (0x{Bits.Of(x):x8})) = {got:R} (0x{Bits.Of(got):x8}), want {want:R} (0x{Bits.Of(want):x8})");
                }
                if (local != 0) Interlocked.Add(ref bad, local);
            });
            foreach (string s in shown) Console.WriteLine(s);
            Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} {name}: {bad} of 2^32 differ");
            return bad == 0 ? 0 : 1;
        }

        private static int Binary(string name, Func<float, float, float> f, delegate* unmanaged<float, float, float> c)
        {
            long bad = 0;
            var shown = new List<string>();
            Parallel.For(0, Chunks, chunk =>
            {
                long local = 0;
                ulong s = 0x9E3779B97F4A7C15ul * (ulong)(chunk + 1);   // splitmix64 per chunk: reproducible
                for (int i = 0; i < 1 << 16; i++)
                {
                    ulong r = Next(ref s);
                    float x = Bits.Single((uint)r), y = Bits.Single((uint)(r >> 32));
                    float got = f(x, y), want = c(x, y);
                    if (Same(got, want)) continue;
                    local++;
                    lock (shown) if (shown.Count < 5) shown.Add($"    {name}({x:R} (0x{Bits.Of(x):x8}), {y:R} (0x{Bits.Of(y):x8})) = {got:R} (0x{Bits.Of(got):x8}), want {want:R} (0x{Bits.Of(want):x8})");
                }
                if (local != 0) Interlocked.Add(ref bad, local);
            });
            foreach (string t in shown) Console.WriteLine(t);
            Console.WriteLine($"  {(bad == 0 ? "ok  " : "FAIL")} {name}: {bad} of 2^28 random pairs differ");
            return bad == 0 ? 0 : 1;
        }

        private static ulong Next(ref ulong s)
        {
            ulong z = s += 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }

        private static bool Same(float got, float want) =>
            Bits.IsNaN(want) ? Bits.IsNaN(got) : Bits.Of(got) == Bits.Of(want);
    }
}
