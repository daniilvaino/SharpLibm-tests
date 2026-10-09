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

## Setup

SharpLibm is expected at `../OS/SharpLibm`, the submodule of a SharpOS
checkout next to this repository. To use another location, pass
`-p:SharpLibmRoot=<dir>\`.

```sh
sh tools/fetch_suites.sh            # clone the suites into suites/ (~1 GB)
sh tools/setup_venv.sh              # Python 3.12 + gmpy2 (MPFR) in .venv/
.venv/Scripts/python.exe tools/wc_ref.py suites/core-math/src suites/wc-ref   # ≈6 min, 1.1 GB
sh tools/build_f32_dll.sh           # CORE-MATH binary32 → build/corematf.dll (MSYS2 mingw64 gcc)
```

## Run

```sh
cd SharpLibm.Tests
dotnet run -c Release -- ../suites                      # libc-test, ulp, vectors, worst cases (≈1 min)
dotnet run -c Release -- ../suites --wc sin pow         # worst cases of some functions only
dotnet run -c Release -- ../suites --f32 ../build/corematf.dll [expf ...]   # exhaustive float (≈6 min)
```

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
| berkeley-testfloat-3 | BSD-3 | not yet |
| ARM optimized-routines | MIT / Apache-2.0 | not yet |
| Rust compiler-builtins (libm) | MIT / Apache-2.0 | not yet |
| Go `src/math` tests | BSD-3 | not yet |
| FreeBSD msun tests | BSD | not yet |
| Jint fdlibm tests | BSD-2 | not yet |
| test262 Math | BSD-3 | not yet |
| LLVM libc math tests | Apache-2.0 WITH LLVM-exception | not yet |
| glibc libm-test | LGPL-2.1 | not yet |
| OpenJDK Math/StrictMath tests | GPL-2.0 WITH Classpath-exception | not yet |
| newlib libm tests | various (COPYING.NEWLIB) | not yet |
| crlibm tests | LGPL-2.1 | not yet |
| AMD AOCL-LibM gtests | BSD-3 | not yet |
| V8 ieee754 unittest | BSD-3 | not yet |
| CPython math test cases | PSF-2.0 | not yet |

## Notes

`notes/` holds the accuracy survey of existing libms and the plan for the
port: donors, function mapping, and the CoreMathSharp edits.
