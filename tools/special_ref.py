"""References for SharpLibm's non-correctly-rounded special functions, from MPFR.

Functions: j0 j1 jn y0 y1 yn erfinv erfcinv, and the float forms j0f … erfcinvf.
They are not correctly rounded, so the runner measures their error in ulp;
the references give it the correctly rounded result and how far the exact
value lies from it.

    suites/special-ref/<f>.txt, one input per line:
        unary:   <x bits> <result bits> <dy>
        jn, yn:  <n> <x bits> <result bits> <dy>
    bits in hex (16 digits for double, 8 for float), n decimal, and
    dy = (exact − result) / ulp(result) as a decimal (|dy| <= 0.5), the
    rounding error of the reference itself; the runner adds it back, so the
    reported error is that against the exact value (as libc-test's ulperr).

Inputs: the RN vectors of musl libc-test (sanity/, special/) and glibc's
auto-libm-test-out-* (binary64/binary32 tonearest lines) for the Bessel
functions — the inputs only, the results are recomputed here — then
deterministic random inputs (seeded): Bessel |x| mostly up to 1e3 with some
up to 1e300 and some tiny, points within a few ulp to 2^-20 relative of the
zeros below 1000 (j0 j1 y0 y1), n in [-50, 50] for jn/yn; erfinv over (−1, 1)
with emphasis near ±1 and 0; erfcinv over (0, 2) with emphasis near 0, 1, 2.
Special inputs (NaN, ±inf, ±0, negative x for y*, |x| >= 1 for erfinv) are
left to the runner, which checks them against C/glibc semantics directly.

Bessel references: MPFR's j0 j1 jn y0 y1 yn (correctly rounded) at 128 bits,
rounded to the target format; when the 128-bit value is exactly a midpoint
of the target format, the function is evaluated again directly in the target
format, so the result is the correctly rounded one.

erfinv / erfcinv: MPFR has no inverse; the root of erf(y) = a (a <= 0.5) or
of log erfc(y) = log q (q = 1 − a, or the erfcinv argument, exact) is found by
Newton's method at 256 bits until the step is below 2^-200 relative, then
rounded the same way. `--verify` checks the Newton roots against plain
bisection at 256 bits and against known values.

    python special_ref.py <suites> [function ...]          (≈ 1 min on 24 cores)
    python special_ref.py <suites> --verify
"""
import math, os, random, re, struct, sys
from multiprocessing import Pool
import gmpy2
from gmpy2 import mpfr

D = dict(precision=53, emin=-1073, emax=1024, subnormalize=True, round=gmpy2.RoundToNearest)
F = dict(precision=24, emin=-148, emax=128, subnormalize=True, round=gmpy2.RoundToNearest)
WIDE = dict(emin=-(1 << 24), emax=1 << 24)

def bits64(x): return struct.unpack('<Q', struct.pack('<d', x))[0]
def bits32(x): return struct.unpack('<I', struct.pack('<f', x))[0]
def f32(x): return struct.unpack('<f', struct.pack('<f', x))[0]

def ulp(y, single):
    """ulp of a finite target-format value, as the runner's Eulp."""
    if single:
        e = (bits32(y) >> 23) & 0xff
        return math.ldexp(1.0, max(e, 1) - 127 - 23)
    e = (bits64(y) >> 52) & 0x7ff
    return math.ldexp(1.0, max(e, 1) - 1023 - 52)

def round_ref(v, direct, single):
    """Round a high-precision value v to the target format; (result, dy)."""
    fmt = F if single else D
    p = 24 if single else 53
    with gmpy2.context(**fmt):
        y = float(mpfr(v))
    with gmpy2.context(precision=p + 1, **WIDE):
        tie = mpfr(v) == v
    with gmpy2.context(precision=p, **WIDE):
        tie = tie and mpfr(v) != v
    if tie:
        y = direct()
    if single:
        y = f32(y)
    if math.isinf(y) or math.isnan(y):
        return y, 0.0
    with gmpy2.context(precision=2000, **WIDE):
        dy = float((v - mpfr(y)) / mpfr(ulp(y, single)))
    return y, dy

# ---- Bessel ----

BESSEL1 = {'j0': gmpy2.j0, 'j1': gmpy2.j1, 'y0': gmpy2.y0, 'y1': gmpy2.y1}
BESSELN = {'jn': gmpy2.jn, 'yn': gmpy2.yn}

def bessel_ref(name, n, x, single):
    fmt = F if single else D
    if n is None:
        fn = BESSEL1[name]
        call = lambda: fn(mpfr(x))
    else:
        # MPFR 4.2 aborts on some negative orders with large x (init2.c / jn.c
        # assertions); J(-n,x) = (-1)^n J(n,x) and Y(-n,x) = (-1)^n Y(n,x) exactly.
        fn = BESSELN[name]
        if n < 0 and n % 2 == 1:
            call = lambda: -fn(-n, mpfr(x))
        else:
            call = lambda: fn(abs(n), mpfr(x))
    with gmpy2.context(precision=128, **WIDE):
        v = call()
    def direct():
        with gmpy2.context(**fmt):
            return float(call())
    return round_ref(v, direct, single)

# ---- erfinv / erfcinv ----

P = 256

def erf_central(a):
    """y >= 0 with erf(y) = a, 0 <= a <= 0.5 (mpfr a). Newton from 0: monotone."""
    if a == 0:
        return mpfr(0)
    c = gmpy2.sqrt(gmpy2.const_pi()) / 2
    y = mpfr(0)
    for _ in range(200):
        step = (a - gmpy2.erf(y)) * c * gmpy2.exp(y * y)
        y += step
        if abs(step) <= abs(y) * mpfr(2) ** -200:
            return y
    raise RuntimeError('erf_central did not converge')

def erfc_tail(q):
    """y with erfc(y) = q, 0 < q <= 0.5 (mpfr q). Newton on log erfc (concave)."""
    c = 2 / gmpy2.sqrt(gmpy2.const_pi())
    lq = gmpy2.log(q)
    y = gmpy2.sqrt(-lq) if q < mpfr('0.01') else mpfr('0.47')
    for _ in range(400):
        e = gmpy2.erfc(y)
        g = gmpy2.log(e) - lq
        dg = -c * gmpy2.exp(-y * y) / e
        step = -g / dg
        y += step
        if abs(step) <= abs(y) * mpfr(2) ** -200:
            return y
    raise RuntimeError('erfc_tail did not converge')

def erfinv_exact(x):
    """erfinv of a double x in (-1, 1), at P bits."""
    with gmpy2.context(precision=P, **WIDE):
        a = abs(mpfr(x))
        y = erf_central(a) if a <= mpfr('0.5') else erfc_tail(1 - a)
        return -y if x < 0 else y

def erfcinv_exact(q):
    """erfcinv of a double q in (0, 2), at P bits."""
    with gmpy2.context(precision=P, **WIDE):
        q = mpfr(q)
        if q <= mpfr('0.5'):
            return erfc_tail(q)
        if q >= mpfr('1.5'):
            return -erfc_tail(2 - q)
        a = 1 - q                      # exact: q in (0.5, 1.5)
        y = erf_central(abs(a))
        return -y if a < 0 else y

def erfinv_ref(name, x, single):
    exact = erfinv_exact if name.startswith('erfinv') else erfcinv_exact
    with gmpy2.context(precision=P, **WIDE):
        v = exact(x)
    def direct():
        # A 256-bit value on a midpoint: refine at a much higher precision.
        global P
        old, P = P, 1024
        try:
            with gmpy2.context(precision=P, **WIDE):
                w = exact(x)
            with gmpy2.context(**(F if single else D)):
                return float(mpfr(w))
        finally:
            P = old
    return round_ref(v, direct, single)

# ---- inputs ----

def libc_test_inputs(suites, name, nfields_in):
    """Inputs of the RN lines of libc-test's sanity/special headers."""
    out = []
    for sub in ('sanity', 'special'):
        path = os.path.join(suites, 'libc-test', 'src', 'math', sub, name + '.h')
        if not os.path.exists(path):
            continue
        for line in open(path):
            m = re.match(r'^\s*T\((.*)\)\s*$', line)
            if not m:
                continue
            f = [t.strip() for t in m.group(1).split(',')]
            if f[0] != 'RN':
                continue
            try:
                if nfields_in == 2:      # jn/yn: x, n
                    out.append((int(f[2]), float.fromhex(f[1])))
                else:
                    out.append((None, float.fromhex(f[1])))
            except ValueError:
                pass
    return out

def glibc_inputs(suites, base, single):
    path = os.path.join(suites, 'glibc', 'math', 'auto-libm-test-out-' + base)
    out = []
    if not os.path.exists(path):
        return out
    fmt = 'binary32' if single else 'binary64'
    for line in open(path):
        t = line.split()
        if len(t) < 5 or t[0] != '=' or t[2] != 'tonearest' or t[3] != fmt:
            continue
        try:
            if base in ('jn', 'yn'):
                out.append((int(t[4]), float.fromhex(t[5])))
            else:
                out.append((None, float.fromhex(t[4])))
        except (ValueError, IndexError):
            pass
    return out

def zeros(base, limit=1000.0):
    """Zeros of j0/j1/y0/y1 in (0, limit], to 80 bits (sign changes on a grid, then bisection)."""
    fn = BESSEL1[base]
    zs = []
    with gmpy2.context(precision=80, **WIDE):
        h = 0.25
        x0 = 0.5
        f0 = fn(mpfr(x0))
        while x0 < limit:
            x1 = x0 + h
            f1 = fn(mpfr(x1))
            if (f0 < 0) != (f1 < 0):
                a, b, fa = mpfr(x0), mpfr(x1), f0
                for _ in range(80):
                    m = (a + b) / 2
                    fm = fn(m)
                    if (fm < 0) == (fa < 0):
                        a, fa = m, fm
                    else:
                        b = m
                zs.append(float((a + b) / 2))
            x0, f0 = x1, f1
    return zs

def rnd(single):
    return f32 if single else (lambda v: v)

def random_bessel1(name, count, rng, single):
    base = name.rstrip('f') if single else name
    odd = base in ('j0', 'j1')            # defined for x < 0
    cap = 3.4e38 if single else 1e300
    r = rnd(single)
    zs = zeros(base)
    out = []
    for i in range(count):
        u = rng.random()
        if u < 0.35:
            x = rng.uniform(0, 30)
        elif u < 0.6:
            x = rng.uniform(30, 1000)
        elif u < 0.75:
            x = math.ldexp(1.0, rng.randint(-60, 10)) * rng.uniform(1, 2)
        elif u < 0.8:
            x = math.exp(rng.uniform(math.log(1e3), math.log(cap)))
        else:
            z = rng.choice(zs)
            x = z * (1 + rng.choice((-1, 1)) * math.ldexp(rng.uniform(1, 2), -rng.randint(20, 55)))
        x = r(x)
        if x == 0 or math.isinf(x):
            continue
        if odd and rng.random() < 0.5:
            x = -x
        out.append((None, x))
    return out

def random_besseln(name, count, rng, single):
    base = name.rstrip('f') if single else name
    cap = 3.4e38 if single else 1e300
    r = rnd(single)
    out = []
    for i in range(count):
        n = rng.randint(2, 50) if rng.random() < 0.8 else rng.randint(-50, 1)
        u = rng.random()
        if u < 0.45:
            x = rng.uniform(0, 100)
        elif u < 0.7:
            x = rng.uniform(0, 1000)
        elif u < 0.95:
            x = math.ldexp(1.0, rng.randint(-40, 10)) * rng.uniform(1, 2)
        else:
            x = math.exp(rng.uniform(math.log(1e3), math.log(cap)))
        x = r(x)
        if x == 0 or math.isinf(x):
            continue
        if base == 'jn' and rng.random() < 0.5:
            x = -x
        out.append((n, x))
    return out

def random_erfinv(count, rng, single):
    r = rnd(single)
    out = []
    for i in range(count):
        u = rng.random()
        if u < 0.4:
            x = rng.uniform(-1, 1)
        elif u < 0.7:       # near ±1: 1 - 2^-k
            x = 1 - math.ldexp(rng.uniform(1, 2), -rng.randint(2, 24 if single else 53))
        elif u < 0.9:       # near 0
            x = math.ldexp(rng.uniform(1, 2), -rng.randint(1, 140 if single else 1074))
        else:               # around the switch at 0.85 and the tail split
            x = rng.uniform(0.8, 0.9) if rng.random() < 0.5 else 1 - math.exp(-rng.uniform(20, 30))
        x = r(x)
        if rng.random() < 0.5:
            x = -x
        if x == 0 or abs(x) >= 1:
            continue
        out.append((None, x))
    return out

def random_erfcinv(count, rng, single):
    r = rnd(single)
    tiny = 149 if single else 1074
    out = []
    for i in range(count):
        u = rng.random()
        if u < 0.35:
            x = rng.uniform(0, 2)
        elif u < 0.65:      # near 0, down to subnormals
            x = math.ldexp(rng.uniform(1, 2), -rng.randint(1, tiny))
        elif u < 0.85:      # near 2
            x = 2 - math.ldexp(rng.uniform(1, 2), -rng.randint(2, 24 if single else 53))
        else:               # near 1
            x = 1 + rng.choice((-1, 1)) * math.ldexp(rng.uniform(1, 2), -rng.randint(1, 60))
        x = r(x)
        if x <= 0 or x >= 2:
            continue
        out.append((None, x))
    return out

# name: (base, single, kind)
FUNCS = {}
for b in ('j0', 'j1', 'y0', 'y1'):
    FUNCS[b] = (b, False, 'b1'); FUNCS[b + 'f'] = (b, True, 'b1')
for b in ('jn', 'yn'):
    FUNCS[b] = (b, False, 'bn'); FUNCS[b + 'f'] = (b, True, 'bn')
for b in ('erfinv', 'erfcinv'):
    FUNCS[b] = (b, False, 'ei'); FUNCS[b + 'f'] = (b, True, 'ei')

COUNT_D = 200_000
COUNT_F = 50_000

def inputs(suites, name):
    base, single, kind = FUNCS[name]
    rng = random.Random(f'sharplibm-special-{name}')
    count = COUNT_F if single else COUNT_D
    if kind == 'ei':
        rand = random_erfinv(count, rng, single) if base == 'erfinv' else random_erfcinv(count, rng, single)
        return rand
    fixed = libc_test_inputs(suites, name, 2 if kind == 'bn' else 1) + glibc_inputs(suites, base, single)
    rand = random_bessel1(name, count, rng, single) if kind == 'b1' else random_besseln(name, count, rng, single)
    return fixed + rand

def keep(name, n, x):
    base, single, kind = FUNCS[name]
    if math.isnan(x) or math.isinf(x) or x == 0:
        return False
    if base[0] == 'y' and x < 0:
        return False
    if single and f32(x) != x:
        return False
    return True

def work(job):
    name, chunk = job
    base, single, kind = FUNCS[name]
    w = 8 if single else 16
    b = bits32 if single else bits64
    lines = []
    for n, x in chunk:
        if kind == 'ei':
            y, dy = erfinv_ref(base, x, single)
        else:
            y, dy = bessel_ref(base, n, x, single)
        head = f'{n} ' if n is not None else ''
        lines.append(f'{head}{b(x):0{w}x} {b(y):0{w}x} {dy:.6f}')
    return lines

def run(suites, out, names, pool):
    for name in names:
        seen, items = set(), []
        for n, x in inputs(suites, name):
            if not keep(name, n, x):
                continue
            k = (n, bits64(x))
            if k in seen:
                continue
            seen.add(k)
            items.append((n, x))
        chunks = [(name, items[i:i + 2000]) for i in range(0, len(items), 2000)]
        with open(os.path.join(out, name + '.txt'), 'w') as o:
            for lines in pool.imap(work, chunks):
                o.write('\n'.join(lines) + '\n')
        print(f'{name}: {len(items)} references', flush=True)

# ---- --verify ----

def bisect_erf(target, lo, hi, use_erfc):
    """Plain bisection at P bits: erf(y) = target, or erfc(y) = target."""
    with gmpy2.context(precision=P, **WIDE):
        lo, hi, t = mpfr(lo), mpfr(hi), mpfr(target)
        for _ in range(P + 1100):       # down to the subnormal range from [0, 1]
            m = (lo + hi) / 2
            v = gmpy2.erfc(m) if use_erfc else gmpy2.erf(m)
            if (v > t) != use_erfc:     # erf increasing, erfc decreasing
                hi = m
            else:
                lo = m
        return (lo + hi) / 2

def verify():
    rng = random.Random(7)
    worst = 0.0
    xs = [0.5, 0.25, 1e-300, 5e-324, 0.85, 0.9999, 1 - 2 ** -53, -0.3]
    xs += [rng.uniform(-1, 1) for _ in range(60)] + [1 - math.ldexp(1, -rng.randint(2, 53)) for _ in range(30)]
    for x in xs:
        v = erfinv_exact(x)
        a = abs(x)
        if a <= 0.5:
            w = bisect_erf(a, 0, 1, False)
        else:
            w = bisect_erf(1 - a, 0, 30, True)
        with gmpy2.context(precision=P, **WIDE):
            if x < 0:
                w = -w
            rel = abs((v - w) / w)
        if rel > 2 ** -190:
            print(f'  erfinv({x!r}): Newton {float(v)!r}, bisection {float(w)!r}')
        worst = max(worst, float(rel))
    qs = [1e-300, 5e-324, 1e-10, 0.3, 0.7, 1.0, 1.3, 1.9999999, 2 - 2 ** -52]
    for q in qs:
        v = erfcinv_exact(q)
        w = bisect_erf(q if q <= 1 else 2 - q, 0, 30, True)
        with gmpy2.context(precision=P, **WIDE):
            if q > 1:
                w = -w
            rel = abs((v - w) / w) if v != 0 else abs(w)   # erfcinv(1) = 0: absolute
        if rel > 2 ** -190:
            print(f'  erfcinv({q!r}): Newton {float(v)!r}, bisection {float(w)!r}')
        worst = max(worst, float(rel))
    print(f'Newton vs bisection, worst relative difference: {worst:.3e} (2^{math.log2(worst) if worst else -math.inf:.1f})')
    # Known values: erfinv(1/2) = 0.4769362762044698733814183536431305598089697490594706447038826959...
    known = mpfr('0.4769362762044698733814183536431305598089697490594706447038826959', 256)
    with gmpy2.context(precision=P, **WIDE):
        print('erfinv(0.5) - known:', float(erfinv_exact(0.5) - known))
        # erf(erfinv(x)) = x round trip
        for x in (0.1, 0.999999, -0.75):
            print(f'erf(erfinv({x})) - {x}:', float(gmpy2.erf(erfinv_exact(x)) - mpfr(x)))
        print('erfc(erfcinv(1e-300)) / 1e-300 - 1:', float(gmpy2.erfc(erfcinv_exact(1e-300)) / mpfr(1e-300) - 1))

if __name__ == '__main__':
    suites = sys.argv[1]
    if '--verify' in sys.argv[2:]:
        verify()
        sys.exit(0)
    out = os.path.join(suites, 'special-ref')
    os.makedirs(out, exist_ok=True)
    names = sys.argv[2:] or list(FUNCS)
    with Pool() as pool:
        run(suites, out, names, pool)
