"""Reference results for CORE-MATH's worst-case inputs (.wc), from MPFR.

For every input line of core-math/src/binary{64,32}/<f>/<f>.wc this writes the
correctly rounded result (round to nearest), computed with MPFR through gmpy2:
    tests/wc-ref/<file>.txt:   <arg bits> [<arg bits>] <result bits>   (hex)
Functions MPFR has are evaluated directly in a context of the target format
(precision, exponent range, subnormals), which MPFR rounds correctly. The
others (π-scaled, *m1 / *p1) are evaluated with a working precision large
enough for the input and rounded once to the target format.

    python wc_ref.py <core-math/src> <out-dir> [function ...]
"""
import math, os, re, struct, sys
import gmpy2
from gmpy2 import mpfr

D = dict(precision=53, emin=-1073, emax=1024, subnormalize=True, round=gmpy2.RoundToNearest)
F = dict(precision=24, emin=-148, emax=128, subnormalize=True, round=gmpy2.RoundToNearest)

def bits64(x): return struct.unpack('<Q', struct.pack('<d', x))[0]
def bits32(x): return struct.unpack('<I', struct.pack('<f', x))[0]

def parse(tok):
    tok = tok.strip()
    try:
        return float.fromhex(tok)
    except ValueError:
        return float(tok)

def wide(*args):
    """Working precision for derived functions: enough to cover the input's scale."""
    e = 0
    for a in args:
        if a != 0 and math.isfinite(a):
            e = max(e, abs(math.frexp(a)[1]))
    return 256 + 2 * e

def derived(fmt, fn, *args):
    with gmpy2.context(precision=wide(*args), emin=-100000, emax=100000):
        v = fn(*[mpfr(a) for a in args])
    with gmpy2.context(**fmt):
        return float(mpfr(v))

def direct(fmt, fn, *args):
    with gmpy2.context(**fmt):
        return float(fn(*[mpfr(a) for a in args]))

pi = lambda: gmpy2.const_pi()

def frac2(x, period):
    """x mod period, exactly (x is a double, so 1100 bits hold the result)."""
    with gmpy2.context(precision=1100, emin=-100000, emax=100000):
        return float(gmpy2.fmod(mpfr(x), period))

# π-scaled trigonometry with IEEE 754-2019 §9.2.1 exact cases.
def sinpi(fmt, x):
    r = frac2(x, 2)
    if r == int(r):
        return math.copysign(0.0, x)
    return derived(fmt, lambda v: gmpy2.sin(pi() * v), r)

def cospi(fmt, x):
    r = frac2(abs(x), 2)
    if r == int(r):
        return 1.0 if r == 0 else -1.0
    if r in (0.5, 1.5):
        return 0.0
    return derived(fmt, lambda v: gmpy2.cos(pi() * v), r)

def tanpi(fmt, x):
    r = frac2(abs(x), 2)
    s = math.copysign(1.0, x)
    if r == 0:
        return s * 0.0
    if r == 1:
        return -s * 0.0
    if r in (0.5, 1.5):
        return s * math.inf if r == 0.5 else -s * math.inf
    return s * derived(fmt, lambda v: gmpy2.tan(pi() * v), r)

def zero_keeps(f):
    """f(±0) = ±0 for the odd *m1 / *p1 functions."""
    return lambda x: x if x == 0 else f(x)

def compound(x, y):
    if x < -1:
        return math.nan
    if y == 0:
        return 1.0
    if x == -1:
        return direct(F, lambda a, b: a ** b, 0.0, y)
    return derived(F, lambda a, b: (1 + a) ** b, x, y)
DOUBLE = {
    # file: (arity, reference)
    'acos': (1, lambda x: direct(D, gmpy2.acos, x)),
    'acosh': (1, lambda x: direct(D, gmpy2.acosh, x)),
    'asin': (1, lambda x: direct(D, gmpy2.asin, x)),
    'asinh': (1, lambda x: direct(D, gmpy2.asinh, x)),
    'atan': (1, lambda x: direct(D, gmpy2.atan, x)),
    'atanh': (1, lambda x: direct(D, gmpy2.atanh, x)),
    'cbrt': (1, lambda x: direct(D, gmpy2.cbrt, x)),
    'cos': (1, lambda x: direct(D, gmpy2.cos, x)),
    'cosh': (1, lambda x: direct(D, gmpy2.cosh, x)),
    'erf': (1, lambda x: direct(D, gmpy2.erf, x)),
    'erfc': (1, lambda x: direct(D, gmpy2.erfc, x)),
    'exp': (1, lambda x: direct(D, gmpy2.exp, x)),
    'exp10': (1, lambda x: direct(D, gmpy2.exp10, x)),
    'exp2': (1, lambda x: direct(D, gmpy2.exp2, x)),
    'expm1': (1, lambda x: direct(D, gmpy2.expm1, x)),
    'log': (1, lambda x: direct(D, gmpy2.log, x)),
    'log10': (1, lambda x: direct(D, gmpy2.log10, x)),
    'log1p': (1, lambda x: direct(D, gmpy2.log1p, x)),
    'log2': (1, lambda x: direct(D, gmpy2.log2, x)),
    'sin': (1, lambda x: direct(D, gmpy2.sin, x)),
    'sinh': (1, lambda x: direct(D, gmpy2.sinh, x)),
    'tan': (1, lambda x: direct(D, gmpy2.tan, x)),
    'tanh': (1, lambda x: direct(D, gmpy2.tanh, x)),
    'tgamma': (1, lambda x: direct(D, gmpy2.gamma, x)),
    'lgamma': (1, lambda x: direct(D, lambda v: gmpy2.lgamma(v)[0], x)),
    'rsqrt': (1, lambda x: math.copysign(math.inf, x) if x == 0 else direct(D, gmpy2.rec_sqrt, x)),
    'atan2': (2, lambda y, x: direct(D, gmpy2.atan2, y, x)),
    'hypot': (2, lambda x, y: direct(D, gmpy2.hypot, x, y)),
    'pow': (2, lambda x, y: direct(D, lambda a, b: a ** b, x, y)),
    'sinpi': (1, lambda x: sinpi(D, x)),
    'cospi': (1, lambda x: cospi(D, x)),
    'tanpi': (1, lambda x: tanpi(D, x)),
    'asinpi': (1, lambda x: derived(D, lambda v: gmpy2.asin(v) / pi(), x)),
    'acospi': (1, lambda x: derived(D, lambda v: gmpy2.acos(v) / pi(), x)),
    'atanpi': (1, lambda x: derived(D, lambda v: gmpy2.atan(v) / pi(), x)),
    'atan2pi': (2, lambda y, x: derived(D, lambda a, b: gmpy2.atan2(a, b) / pi(), y, x)),
    'exp2m1': (1, zero_keeps(lambda x: derived(D, lambda v: gmpy2.exp2(v) - 1, x))),
    'exp10m1': (1, zero_keeps(lambda x: derived(D, lambda v: gmpy2.exp10(v) - 1, x))),
    'log2p1': (1, zero_keeps(lambda x: derived(D, lambda v: gmpy2.log2(1 + v), x))),
    'log10p1': (1, zero_keeps(lambda x: derived(D, lambda v: gmpy2.log10(1 + v), x))),
}
SINGLE = {
    'atan2f': (2, lambda y, x: direct(F, gmpy2.atan2, y, x)),
    'atan2pif': (2, lambda y, x: derived(F, lambda a, b: gmpy2.atan2(a, b) / pi(), y, x)),
    'hypotf': (2, lambda x, y: direct(F, gmpy2.hypot, x, y)),
    'powf': (2, lambda x, y: direct(F, lambda a, b: a ** b, x, y)),
    'compoundf': (2, compound),
}

def find(src, name):
    for root, _, files in os.walk(src):
        if name + '.wc' in files:
            return os.path.join(root, name + '.wc')
    return None

def flags(path):
    """The variants CORE-MATH's check_worst*.c derives from each line (function_under_test.h)."""
    h = os.path.join(os.path.dirname(path), 'function_under_test.h')
    if not os.path.exists(h):
        return set()
    text = open(h).read()
    return {f for f in ('WORST_SYMMETRIC', 'WORST_SYMMETRIC_X', 'WORST_SYMMETRIC_Y', 'WORST_SWAP')
            if re.search(r'^\s*#\s*define\s+' + f + r'\b', text, re.M)}

def variants(args, fl):
    out = [tuple(args)]
    if len(args) == 1:
        if 'WORST_SYMMETRIC' in fl:
            out.append((-args[0],))
    else:
        if 'WORST_SWAP' in fl:
            out.append((args[1], args[0]))
        if 'WORST_SYMMETRIC_X' in fl:
            out += [(-a, b) for a, b in out]
        if 'WORST_SYMMETRIC_Y' in fl:
            out += [(a, -b) for a, b in out]
    return out

def run(src, out, name, arity, ref, single):
    path = find(src, name)
    if path is None:
        print(name, 'no .wc'); return
    fl = flags(path)
    dst = os.path.join(out, name + '.txt')
    n = skipped = 0
    seen = set()
    b = bits32 if single else bits64
    w = 8 if single else 16
    with open(path) as f, open(dst, 'w') as o:
        for line in f:
            line = line.split('#')[0].strip()
            if not line:
                continue
            toks = line.split(',')
            if len(toks) != arity or any('nan' in t.lower() for t in toks):
                skipped += 1
                continue
            try:
                args = [parse(t) for t in toks]
            except ValueError:
                skipped += 1
                continue
            if single:
                args = [struct.unpack('<f', struct.pack('<f', a))[0] for a in args]
            for v in variants(args, fl):
                key = tuple(b(a) for a in v)
                if key in seen:
                    continue
                seen.add(key)
                try:
                    y = ref(*v)
                except (ValueError, OverflowError, ZeroDivisionError):
                    skipped += 1
                    continue
                o.write(' '.join(f'{k:0{w}x}' for k in key) + f' {b(y):0{w}x}\n')
                n += 1
    print(f'{name}: {n} references, {skipped} skipped, variants {sorted(fl)}', flush=True)

if __name__ == '__main__':
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    want = set(sys.argv[3:])
    for table, single in ((DOUBLE, False), (SINGLE, True)):
        for name, (arity, ref) in table.items():
            if not want or name in want:
                run(src, out, name, arity, ref, single)
