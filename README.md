# SharpLibm-tests

Tests for [SharpLibm](https://github.com/daniilvaino/SharpLibm), the pure-C# libm.
Kept apart from SharpLibm because the suites they run come under many licences;
this repository holds only the test code and scripts. The suites are cloned
into `suites/`, and the references are generated into it as well. Neither is
committed.

## What is checked

All checks are in round-to-nearest. Exception flags and errno are not checked.

| check | reference | inputs |
|---|---|---|
| exact operations: floor, ceil, trunc, round, rint, nearbyint, fabs, logb, fmod, remainder, remquo, copysign, fdim, fmax, fmin, nextafter, modf, frexp, ilogb, llrint, llround, ldexp, scalbn, scalbln (+ float) | musl libc-test, bit for bit | libc-test vectors |
| transcendental functions within 0.5 ulp | musl libc-test (incl. crlibm-derived sets), libc-test's ulp metric | ≈65 k vectors, 58 functions |
| CoreMathSharp vectors | CORE-MATH C output, bit for bit | 84 sets |
| double worst cases | MPFR (gmpy2), bit for bit | CORE-MATH `.wc` files with the harness's ±x, x↔y variants: ≈30 M inputs, 40 functions |
| float, exhaustive | CORE-MATH C (proven correctly rounded for binary32), bit for bit | all 2^32 inputs, 36 unary functions; 2^28 random pairs, 5 binary |
| glibc libm-test: `auto-libm-test-out-*` (MPFR results) and the hand-written `libm-test-*.inc` | glibc's expected values, bit for bit (`xfail` marks glibc's own inaccuracy, the value is still the correctly rounded one); pown, powr, compoundn through pow / compoundf where they coincide | ≈23 k binary32/binary64 cases, 145 functions |
| Berkeley TestFloat 3: fma, sqrt, roundToInt (trunc, floor, ceil, round, roundeven, rint, nearbyint), rem (remainder, remquo) | SoftFloat 3, bit for bit | levels 1 and 2 (fma: level 1 and the first 16 M of level 2): ≈92 M cases |
| LLVM libc math tests | the tests' explicit expected values; MPFR for the hard-case inputs they check against MPFR at run time | ≈2.4 k cases, 96 functions |
| Bessel j0 j1 jn y0 y1 yn, erfinv erfcinv (+ float) — not correctly rounded | MPFR (gmpy2; erfinv by Newton on MPFR erf/erfc), error in ulp within per-function bounds (`SpecialFunctions.cs`); special values bit for bit | libc-test and glibc inputs + seeded random: ≈1.9 M inputs, 16 functions |

## Setup

SharpLibm is expected at `../OS/SharpLibm`, the submodule of a SharpOS
checkout next to this repository. To use another location, pass
`-p:SharpLibmRoot=<dir>\`.

```sh
sh tools/fetch_suites.sh            # clone the suites into suites/ (~1 GB)
sh tools/setup_venv.sh              # Python 3.12 + gmpy2 (MPFR) in .venv/
.venv/Scripts/python.exe tools/wc_ref.py suites/core-math/src suites/wc-ref   # ≈6 min, 1.1 GB
sh tools/build_f32_dll.sh           # CORE-MATH binary32 → build/corematf.dll (MSYS2 mingw64 gcc)
.venv/Scripts/python.exe tools/special_ref.py suites   # Bessel/erfinv references → suites/special-ref (all cores, ≈1 min on 24, 100 MB)
wsl -e bash tools/testfloat_gen.sh  # build SoftFloat + testfloat_gen (gcc), vectors → suites/testfloat-vec (≈1 min, 1.5 GB)
.venv/Scripts/python.exe -I tools/llvm_extract.py suites/llvm-libc/libc/test/src/math suites/llvm-vec/llvm.txt   # ≈2 s
```

`testfloat_gen.sh` writes one file per function, rounding mode and level,
`<f32|f64>_<op>_<rounding>_L<level>.txt[.gz]`, in TestFloat's format (operands,
result, flags in hex). Level 2 of `mulAdd` has 1.8·10^11 (f64) / 1.4·10^10
(f32) cases; only its first `FMA_L2` (default 16 M) are kept: half random
triples, half structured ones for the first few values of the first operand.

`llvm_extract.py` line format: `<function> <eq|mpfr> <source:line> <args> =>
<results>` (hex bits, integers as `#n`). It prints how many checks it took,
dropped (not round-to-nearest) or could not evaluate (loop variables and the
like).

`special_ref.py` line format: `[n] <x bits> <result bits> <dy>` — hex bits (16
digits double, 8 float), the order `n` in decimal for jn/yn, and
`dy = (exact − result)/ulp(result)` so the runner measures the error against
the exact value. `--verify` checks its erfinv/erfcinv roots against bisection.

## Run

```sh
cd SharpLibm.Tests
dotnet run -c Release -- ../suites                      # libc-test, ulp, vectors, worst cases (≈1 min)
dotnet run -c Release -- ../suites --wc sin pow         # worst cases of some functions only
dotnet run -c Release -- ../suites --f32 ../build/corematf.dll [expf ...]   # exhaustive float (≈6 min)
dotnet run -c Release -- ../suites --special [j0 erfinv ...]  # Bessel, erfinv in ulp (≈3 s; also part of the default run)
dotnet run -c Release -- ../suites --glibc [sin fmaf ...]     # glibc libm-test (≈1 s; also part of the default run)
dotnet run -c Release -- ../suites --llvm [sinf ...]          # LLVM libc vectors (<1 s; also part of the default run)
dotnet run -c Release -- ../suites --testfloat [f64_mulAdd ...]  # TestFloat vectors, by file-name prefix (≈20 s)
```

The function table in SharpLibm's README is generated:
`python tools/readme_table.py ../OS/SharpLibm/README.md`, from three lists in
`build/` — `managed.txt` (public methods of `Libm`) and `exports-win.txt` /
`exports-linux.txt` (the names declared in each build's `sharplibm.h`).
Semantics and accuracy are in the script.

SharpLibm as a C library: `sh native/run.sh` builds it for the host
(NativeAOT NoStdLib, `SharpLibm/native`: `sharplibm.dll` on Windows,
`sharplibm.so` on Linux) and runs `native/smoke.c` against it, every function
bound at run time (`GetProcAddress` / `dlsym`) so none can come from the C
toolchain's own libm.

To check the references themselves against CORE-MATH's C, use gcc under WSL:
`wsl -e sh tools/cwc/run.sh suites/core-math/src suites/wc-ref [function ...]`.
With current CORE-MATH the C matches every line.

## Suites (`tools/fetch_suites.sh`)

| suite | licence | used by the runner |
|---|---|---|
| libc-test (musl) | MIT | yes |
| core-math | MIT | yes: `.wc` worst cases, binary32 C |
| CoreMathSharp vectors | MIT | yes |
| math_accuracy (Zimmermann) | see repository | not yet |
| berkeley-testfloat-3 | BSD-3 | yes: `testfloat_gen` vectors (`tools/testfloat_gen.sh`) |
| berkeley-softfloat-3 | BSD-3 | yes: TestFloat's reference |
| ARM optimized-routines | MIT / Apache-2.0 | not yet |
| Rust compiler-builtins (libm) | MIT / Apache-2.0 | not yet |
| Go `src/math` tests | BSD-3 | not yet |
| FreeBSD msun tests | BSD | not yet |
| Jint fdlibm tests | BSD-2 | not yet |
| test262 Math | BSD-3 | not yet |
| LLVM libc math tests | Apache-2.0 WITH LLVM-exception | yes: explicit values and hard-case inputs (`tools/llvm_extract.py`) |
| glibc libm-test | LGPL-2.1 | yes: `auto-libm-test-out-*`, `libm-test-*.inc` |
| OpenJDK Math/StrictMath tests | GPL-2.0 WITH Classpath-exception | not yet |
| newlib libm tests | various (COPYING.NEWLIB) | not yet |
| crlibm tests | LGPL-2.1 | not yet |
| AMD AOCL-LibM gtests | BSD-3 | not yet |
| V8 ieee754 unittest | BSD-3 | not yet |
| CPython math test cases | PSF-2.0 | not yet |

## Notes

`notes/` holds the accuracy survey of existing libms and the plan for the
port: donors, function mapping, and the CoreMathSharp edits.
