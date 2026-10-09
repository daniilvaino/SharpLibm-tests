"""The function table of SharpLibm's README.

Availability comes from the build, not from this file:
  build/managed.txt         public static methods of SharpLibm.Libm (.NET)
  build/exports-win.txt     declarations in the Windows build's sharplibm.h
  build/exports-linux.txt   declarations in the Linux build's sharplibm.h
Semantics and accuracy are written here, from what SharpLibm-tests measures.
The table replaces the text between the markers in SharpLibm's README.md:
  <!-- functions:begin -->  …  <!-- functions:end -->

    python tools/readme_table.py <SharpLibm README.md>
"""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def names(f):
    return set(open(os.path.join(ROOT, "build", f)).read().split())

managed, win, linux = names("managed.txt"), names("exports-win.txt"), names("exports-linux.txt")

EXACT = "exact"
CR = "CR"
CR64 = CR
CR32 = CR
SCALE = CR   # exact unless the result is subnormal

# (double name, float name, semantics, accuracy double, accuracy float)
F = [
    # rounding and conversion to integer
    ("floor", "floorf", "largest integer ≤ x", EXACT, EXACT),
    ("ceil", "ceilf", "smallest integer ≥ x", EXACT, EXACT),
    ("trunc", "truncf", "integer part, towards zero", EXACT, EXACT),
    ("round", "roundf", "nearest integer, halves away from zero", EXACT, EXACT),
    ("roundeven", "roundevenf", "nearest integer, halves to even", EXACT, EXACT),
    ("rint", "rintf", "nearest integer in the rounding mode (to even)", EXACT, EXACT),
    ("nearbyint", "nearbyintf", "as rint, raising no inexact", EXACT, EXACT),
    ("modf", "modff", "fraction, integer part through a pointer", EXACT, EXACT),
    ("lrint", "lrintf", "rint as C `long` (32-bit on Windows)", EXACT, EXACT),
    ("lround", "lroundf", "round as C `long` (32-bit on Windows)", EXACT, EXACT),
    ("llrint", "llrintf", "rint as `long long`", EXACT, EXACT),
    ("llround", "llroundf", "round as `long long`", EXACT, EXACT),
    # remainders
    ("fmod", "fmodf", "x − n·y, n truncated", EXACT, EXACT),
    ("remainder", "remainderf", "x − n·y, n to nearest even", EXACT, EXACT),
    ("remquo", "remquof", "remainder, low quotient bits through a pointer", EXACT, EXACT),
    # exponent and sign
    ("frexp", "frexpf", "mantissa in [½, 1), exponent through a pointer", EXACT, EXACT),
    ("ldexp", "ldexpf", "x·2ⁿ", SCALE, SCALE),
    ("scalbn", "scalbnf", "x·2ⁿ, n an int", SCALE, SCALE),
    ("scalbln", "scalblnf", "x·2ⁿ, n a C `long`", SCALE, SCALE),
    ("ilogb", "ilogbf", "binary exponent as int", EXACT, EXACT),
    ("logb", "logbf", "binary exponent as floating point", EXACT, EXACT),
    ("nextafter", "nextafterf", "next representable value towards y", EXACT, EXACT),
    ("nexttoward", "nexttowardf", "nextafter with a `long double` y ⁵", EXACT, EXACT),
    ("copysign", "copysignf", "|x| with the sign of y", EXACT, EXACT),
    ("fabs", "fabsf", "|x|", EXACT, EXACT),
    ("fdim", "fdimf", "max(x − y, 0)", CR, CR),
    ("fmax", "fmaxf", "maximum, ignoring a NaN operand", EXACT, EXACT),
    ("fmin", "fminf", "minimum, ignoring a NaN operand", EXACT, EXACT),
    ("fma", "fmaf", "x·y + z rounded once", CR, CR),
    ("sqrt", "sqrtf", "√x (one instruction)", CR, CR),
    ("rsqrt", "rsqrtf", "1/√x", CR64, CR32),
    ("cbrt", "cbrtf", "∛x", CR64, CR32),
    ("hypot", "hypotf", "√(x² + y²) without overflow", CR64, CR),
    # exponentials and logarithms
    ("exp", "expf", "eˣ", CR64, CR32),
    ("exp2", "exp2f", "2ˣ", CR64, CR32),
    ("exp10", "exp10f", "10ˣ", CR64, CR32),
    ("expm1", "expm1f", "eˣ − 1", CR64, CR32),
    ("exp2m1", "exp2m1f", "2ˣ − 1", CR64, CR32),
    ("exp10m1", "exp10m1f", "10ˣ − 1", CR64, CR32),
    ("log", "logf", "ln x", CR64, CR32),
    ("log2", "log2f", "log₂ x", CR64, CR32),
    ("log10", "log10f", "log₁₀ x", CR64, CR32),
    ("log1p", "log1pf", "ln(1 + x)", CR64, CR32),
    ("log2p1", "log2p1f", "log₂(1 + x)", CR64, CR32),
    ("log10p1", "log10p1f", "log₁₀(1 + x)", CR64, CR32),
    ("pow", "powf", "xʸ", CR64, CR),
    ("compoundn", "compoundnf", "(1 + x)ⁿ, n a `long long` (C23)", "≤ 1 ulp", "CR ³"),
    (None, "compoundf", "(1 + x)ʸ, y real (CORE-MATH extension)", None, CR),
    # trigonometric
    ("sin", "sinf", "sin x", CR64, CR32),
    ("cos", "cosf", "cos x", CR64, CR32),
    ("tan", "tanf", "tan x", CR64, CR32),
    ("sincos", "sincosf", "sin x and cos x through pointers", CR64, CR32),
    ("asin", "asinf", "arcsin x", CR64, CR32),
    ("acos", "acosf", "arccos x", CR64, CR32),
    ("atan", "atanf", "arctan x", CR64, CR32),
    ("atan2", "atan2f", "arctan(y/x) in the quadrant of (x, y)", CR64, CR),
    ("sinpi", "sinpif", "sin πx", CR64, CR32),
    ("cospi", "cospif", "cos πx", CR64, CR32),
    ("tanpi", "tanpif", "tan πx", CR64, CR32),
    ("asinpi", "asinpif", "arcsin(x)/π", CR64, CR32),
    ("acospi", "acospif", "arccos(x)/π", CR64, CR32),
    ("atanpi", "atanpif", "arctan(x)/π", CR64, CR32),
    ("atan2pi", "atan2pif", "atan2(y, x)/π", CR64, CR),
    # hyperbolic
    ("sinh", "sinhf", "sinh x", CR64, CR32),
    ("cosh", "coshf", "cosh x", CR64, CR32),
    ("tanh", "tanhf", "tanh x", CR64, CR32),
    ("asinh", "asinhf", "arsinh x", CR64, CR32),
    ("acosh", "acoshf", "arcosh x", CR64, CR32),
    ("atanh", "atanhf", "artanh x", CR64, CR32),
    # special functions
    ("erf", "erff", "error function", CR64, CR32),
    ("erfc", "erfcf", "1 − erf x", CR64, CR32),
    ("tgamma", "tgammaf", "Γ(x)", CR64, CR32),
    ("lgamma", "lgammaf", "ln |Γ(x)|", CR64, CR32),
    ("lgamma_r", "lgammaf_r", "ln |Γ(x)|, sign of Γ through a pointer", CR64, CR32),
    ("erfinv", "erfinvf", "inverse of erf (not ISO C)", "≤ 1.33 ulp", "CR ²"),
    ("erfcinv", "erfcinvf", "inverse of erfc (not ISO C)", "≤ 4.6 ulp ⁴", "CR ²"),
    ("j0", "j0f", "Bessel J₀", "≤ 4 ulp ¹", "CR ²"),
    ("j1", "j1f", "Bessel J₁", "≤ 4 ulp ¹", "CR ²"),
    ("jn", "jnf", "Bessel Jₙ, n an int", "≤ 33 ulp ¹", "CR ²"),
    ("y0", "y0f", "Bessel Y₀", "≤ 4 ulp ¹", "CR ²"),
    ("y1", "y1f", "Bessel Y₁", "≤ 4 ulp ¹", "CR ²"),
    ("yn", "ynf", "Bessel Yₙ, n an int", "≤ 27 ulp ¹", "CR ²"),
]

def mark(names_, where):
    have = [n for n in names_ if n]
    return "✅" if all(n in where for n in have) else ("❌" if not any(n in where for n in have) else "½")

listed = set()
rows = ["| function | semantics | .NET | Windows | Linux | accuracy |", "|---|---|:-:|:-:|:-:|---|"]
for d, f, sem, ad, af in F:
    ns = [n for n in (d, f) if n]
    listed.update(ns)
    fn = ", ".join(f"`{n}`" for n in ns)
    if ad is None:
        acc = af
    elif ad == af:
        acc = ad
    else:
        acc = f"{ad} / {af}"
    cell = sem.replace("|", "\\|")      # a bare | would end the table cell
    rows.append(f"| {fn} | {cell} | {mark(ns, managed)} | {mark(ns, win)} | {mark(ns, linux)} | {acc} |")

missing = (managed | win | linux) - listed
if missing:
    sys.exit("not in the table: " + " ".join(sorted(missing)))

table = "\n".join(rows) + "\n\n" + (
    "**exact** — the result is representable, bit for bit. **CR** — correctly\n"
    "rounded: double against MPFR on CORE-MATH's worst cases (≈30 M inputs),\n"
    "float against CORE-MATH's C on all 2³² inputs (2²⁸ random pairs for two\n"
    "arguments); fma, sqrt, remainder and roundings also against TestFloat;\n"
    "ldexp/scalbn are exact unless the result is subnormal. Two values:\n"
    "double / float.\n\n"
    "¹ Near a zero of the function the error is absolute, in ulp of its local\n"
    "amplitude; relative error there is unbounded, as in every fdlibm-derived\n"
    "libm. ² On every input tested (≈40–50 k against MPFR), not proven.\n"
    "³ Where n is exact in float (|n| ≤ 2²⁴, and larger n with trailing zeros);\n"
    "otherwise through double, rounded once more. ⁴ ≈1 ulp above 1e-300; below,\n"
    "the Newton step is skipped. ⁵ Not exported to C: its `long double` is double\n"
    "with MSVC and 80-bit with gcc and mingw, so no one ABI fits; use nextafter.\n")

readme = sys.argv[1]
text = open(readme, encoding="utf-8").read()
begin, end = "<!-- functions:begin -->", "<!-- functions:end -->"
if begin not in text:
    sys.exit("no markers in " + readme)
pre, rest = text.split(begin, 1)
_, post = rest.split(end, 1)
open(readme, "w", encoding="utf-8", newline="\n").write(pre + begin + "\n" + table + end + post)
print(f"{len(F)} rows, {len(listed)} functions")
