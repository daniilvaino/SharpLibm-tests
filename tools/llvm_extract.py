"""Vectors from LLVM libc's math tests (libc/test/src/math, smoke/, exhaustive/).

Most of LLVM libc's checks compare against MPFR at run time over loops of
generated inputs; those cannot be reused as data. What is extracted:

  eq    checks with an explicit expected value, EXPECT_FP_EQ(expected, f(args))
        and the like (also EXPECT_EQ for the integer-valued functions), where
        the expected value and the arguments are constant expressions: literals,
        FPBits(bits).get_val(), the FPTest constants (inf, aNaN, min_denormal …),
        casts and simple arithmetic, and locals initialised from those. The
        templated headers (CeilTest.h …) are expanded for every *_test.cpp that
        instantiates them.
  mpfr  inputs given explicitly but checked against MPFR in the test
        (EXPECT_MPFR_MATCH(op, x, f(x), tol) with constant x, the INPUTS[] /
        VALUES[] arrays of hard cases, SDCOMP26094_VALUES): the expected value
        is computed here with MPFR (gmpy2), correctly rounded to nearest, as
        tools/wc_ref.py does.

Only round-to-nearest checks are kept: checks under ForceRoundingMode, in
loops over rounding modes, the *_ROUNDING_{UPWARD,DOWNWARD,TOWARD_ZERO} forms,
and `if constexpr` blocks are dropped. Of #ifdef LIBC_MATH_HAS_SKIP_ACCURATE_PASS
the accurate branch is kept. Functions SharpLibm does not have (f16, bf16,
f128, long double, narrowing) are ignored.

Output, one vector per line (bits in hex, integers as #n):
    <function> <eq|mpfr> <source:line> <args...> => <results...>

    python llvm_extract.py <llvm-project>/libc/test/src/math <out-file>
"""
import os, re, struct, sys
from fractions import Fraction

import gmpy2
from gmpy2 import mpfr

sys.dont_write_bytecode = True   # no __pycache__ next to the scripts
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wc_ref  # noqa: E402  (MPFR helpers)
from wc_ref import D, F, direct, derived  # noqa: E402

# ---------------------------------------------------------------- SharpLibm's functions
# signature letters: d = the floating type, i = int, l = long, D = double (nexttoward's y)
SIG = {}
for n in ('acos acosh acospi asin asinh asinpi atan atanh atanpi cbrt cos cosh cospi erf erfc exp exp10 '
          'exp10m1 exp2 exp2m1 expm1 log log10 log10p1 log1p log2 log2p1 rsqrt sin sinh sinpi sqrt tan tanh '
          'tanpi tgamma lgamma floor ceil trunc round rint nearbyint roundeven fabs logb').split():
    SIG[n] = ('d', 'd')
for n in 'atan2 atan2pi hypot pow fmod remainder copysign fdim fmax fmin nextafter'.split():
    SIG[n] = ('dd', 'd')
SIG.update({'fma': ('ddd', 'd'), 'ilogb': ('d', 'i'), 'llrint': ('d', 'l'), 'llround': ('d', 'l'),
            'lrint': ('d', 'l'), 'lround': ('d', 'l'), 'ldexp': ('di', 'd'), 'scalbn': ('di', 'd'),
            'scalbln': ('dl', 'd'), 'nexttoward': ('dD', 'd'), 'sincos': ('d', 'dd')})
NO_FLOAT = {'compound'}           # (compoundf exists, but takes (float, float); not in LLVM's tests)


def lookup(name):
    """(base name, bits) of a SharpLibm function, or None."""
    if name in SIG:
        return name, 64
    if name.endswith('f') and name[:-1] in SIG:
        return name[:-1], 32
    return None


# ---------------------------------------------------------------- references (MPFR)
def ctx(bits):
    return D if bits == 64 else F


def ref_unary(base, bits, x):
    c = ctx(bits)
    table = {
        'acos': gmpy2.acos, 'acosh': gmpy2.acosh, 'asin': gmpy2.asin, 'asinh': gmpy2.asinh, 'atan': gmpy2.atan,
        'atanh': gmpy2.atanh, 'cbrt': gmpy2.cbrt, 'cos': gmpy2.cos, 'cosh': gmpy2.cosh, 'erf': gmpy2.erf,
        'erfc': gmpy2.erfc, 'exp': gmpy2.exp, 'exp10': gmpy2.exp10, 'exp2': gmpy2.exp2, 'expm1': gmpy2.expm1,
        'log': gmpy2.log, 'log10': gmpy2.log10, 'log1p': gmpy2.log1p, 'log2': gmpy2.log2, 'sin': gmpy2.sin,
        'sinh': gmpy2.sinh, 'tan': gmpy2.tan, 'tanh': gmpy2.tanh, 'tgamma': gmpy2.gamma, 'sqrt': gmpy2.sqrt,
        'lgamma': lambda v: gmpy2.lgamma(v)[0],
    }
    if base in table:
        return direct(c, table[base], x)
    if base == 'rsqrt':
        return wc_ref.math.copysign(wc_ref.math.inf, x) if x == 0 else direct(c, gmpy2.rec_sqrt, x)
    if not wc_ref.math.isfinite(x):
        raise ValueError('special input of a derived function')
    if base == 'sinpi': return wc_ref.sinpi(c, x)
    if base == 'cospi': return wc_ref.cospi(c, x)
    if base == 'tanpi': return wc_ref.tanpi(c, x)
    if base == 'asinpi': return derived(c, lambda v: gmpy2.asin(v) / wc_ref.pi(), x)
    if base == 'acospi': return derived(c, lambda v: gmpy2.acos(v) / wc_ref.pi(), x)
    if base == 'atanpi': return derived(c, lambda v: gmpy2.atan(v) / wc_ref.pi(), x)
    if x == 0 and base in ('exp2m1', 'exp10m1', 'log2p1', 'log10p1'):
        return x
    if base == 'exp2m1': return derived(c, lambda v: gmpy2.exp2(v) - 1, x)
    if base == 'exp10m1': return derived(c, lambda v: gmpy2.exp10(v) - 1, x)
    if base == 'log2p1': return derived(c, lambda v: gmpy2.log2(1 + v), x)
    if base == 'log10p1': return derived(c, lambda v: gmpy2.log10(1 + v), x)
    raise KeyError(base)


def ref(base, bits, args):
    """Correctly rounded results (as Python floats) for constant inputs, or None."""
    if any(isinstance(a, float) and a != a for a in args):
        return None
    c = ctx(bits)
    try:
        if base == 'sincos':
            return [ref_unary('sin', bits, args[0]), ref_unary('cos', bits, args[0])]
        if base == 'fma':
            return [direct(c, gmpy2.fma, *args)]
        if base == 'hypot':
            return [direct(c, gmpy2.hypot, *args)]
        if base == 'pow':
            return [direct(c, lambda a, b: a ** b, *args)]
        if base == 'atan2':
            return [direct(c, gmpy2.atan2, *args)]
        if base == 'atan2pi':
            if not all(wc_ref.math.isfinite(a) for a in args):
                return None
            return [derived(c, lambda a, b: gmpy2.atan2(a, b) / wc_ref.pi(), *args)]
        if SIG.get(base) == ('d', 'd'):
            return [ref_unary(base, bits, args[0])]
    except (KeyError, ValueError, OverflowError, ZeroDivisionError):
        return None
    return None


# ---------------------------------------------------------------- values
def to_bits(x, bits):
    if x != x:
        return 0x7fc00000 if bits == 32 else 0x7ff8000000000000
    return struct.unpack('<I', struct.pack('<f', x))[0] if bits == 32 else struct.unpack('<Q', struct.pack('<d', x))[0]


def from_bits(b, bits):
    return struct.unpack('<f', struct.pack('<I', b & 0xffffffff))[0] if bits == 32 else \
        struct.unpack('<d', struct.pack('<Q', b & 0xffffffffffffffff))[0]


class FV:
    """A floating value of the test's format, kept as its bit pattern (NaN payloads and sNaN survive)."""
    __slots__ = ('b',)

    def __init__(self, b): self.b = b


class Bits:
    """An FPBits object."""
    __slots__ = ('b',)

    def __init__(self, b): self.b = b


class Fail(Exception):
    pass


def rnd(x, bits):
    """Rounds a Fraction or int to the format (to nearest, subnormals), as a C conversion does."""
    with gmpy2.context(**ctx(bits)):
        return to_bits(float(mpfr(gmpy2.mpq(x.numerator, x.denominator)) if isinstance(x, Fraction) else mpfr(x)), bits)


def fval(v, bits):
    if isinstance(v, FV): return from_bits(v.b, bits)
    if isinstance(v, int): return from_bits(rnd(v, bits), bits)
    raise Fail('not a float')


def as_fv(v, bits):
    if isinstance(v, FV): return v
    if isinstance(v, int): return FV(rnd(v, bits))
    if isinstance(v, Fraction): return FV(rnd(v, bits))
    raise Fail('not a float: %r' % (v,))


def special(name, bits):
    p, emax = (24, 127) if bits == 32 else (53, 1023)
    ebits = 8 if bits == 32 else 11
    sign = 1 << (bits - 1)
    inf = ((1 << ebits) - 1) << (p - 1)
    t = {
        'zero': 0, 'inf': inf, 'aNaN': inf | (1 << (p - 2)), 'sNaN': inf | (1 << (p - 3)),
        'min_normal': 1 << (p - 1), 'max_normal': inf - 1, 'min_denormal': 1, 'max_denormal': (1 << (p - 1)) - 1,
        'one': (((1 << (ebits - 1)) - 1) << (p - 1)),
    }
    neg = name.startswith('neg_')
    base = name[4:] if neg else name
    if base not in t:
        return None
    return t[base] | (sign if neg else 0)


# ---------------------------------------------------------------- expressions
TOKEN = re.compile(r"""
    (?P<hexf>0[xX][0-9a-fA-F']*\.?[0-9a-fA-F']*[pP][-+]?\d+[fFlL]?)
  | (?P<decf>(?:\d[\d']*\.\d*|\.\d+)(?:[eE][-+]?\d+)?[fFlL]?|\d[\d']*[eE][-+]?\d+[fFlL]?)
  | (?P<int>0[xX][0-9a-fA-F']+[uUlL]*|\d[\d']*[uUlL]*)
  | (?P<id>[A-Za-z_]\w*(?:\s*::\s*[A-Za-z_]\w*)*)
  | (?P<op>->|<<|>>|::|[-+*/(),.<>&!~\[\]{}|^%?:=])
  | (?P<ws>\s+)
""", re.X)

FLOAT_TYPES = {'T', 'InType', 'OutType', 'FloatType', 'F', 'float', 'double', 'InFloatType', 'OutFloatType'}
INT_TYPES = {'StorageType', 'OutStorageType', 'InStorageType', 'UIntType', 'int', 'long', 'unsigned',
             'uint32_t', 'uint64_t', 'int32_t', 'int64_t', 'IntType', 'I', 'long long', 'size_t'}


def tokenize(s):
    out, pos = [], 0
    while pos < len(s):
        m = TOKEN.match(s, pos)
        if not m:
            raise Fail('token at ' + s[pos:pos + 10])
        pos = m.end()
        if m.lastgroup != 'ws':
            out.append((m.lastgroup, re.sub(r'\s+', '', m.group())))
    return out


def lit_fraction(text):
    t = text.replace("'", '')
    if t[:2].lower() == '0x':
        t = t.rstrip('fFlL')
        m = re.fullmatch(r'0[xX]([0-9a-fA-F]*)\.?([0-9a-fA-F]*)[pP]([-+]?\d+)', t)
        ip, fp, e = m.group(1), m.group(2), int(m.group(3))
        v = Fraction(int((ip + fp) or '0', 16)) * Fraction(2) ** (e - 4 * len(fp))
        return v
    return Fraction(t.rstrip('fFlL'))


class Parser:
    def __init__(self, toks, bits, env, func_name=None):
        self.t, self.p, self.bits, self.env = toks, 0, bits, env

    def peek(self, k=0):
        return self.t[self.p + k] if self.p + k < len(self.t) else (None, None)

    def take(self, val=None):
        tok = self.peek()
        if tok[0] is None or (val is not None and tok[1] != val):
            raise Fail('expected %s got %s' % (val, tok[1]))
        self.p += 1
        return tok

    def done(self):
        return self.p == len(self.t)

    # arithmetic in the format (C does it in the type of the operands)
    def arith(self, op, a, b):
        if isinstance(a, int) and isinstance(b, int):
            if op == '+': return a + b
            if op == '-': return a - b
            if op == '*': return a * b
            if op == '/': return Fraction(a, b) if a % b else a // b
            if op == '<<': return a << b
            if op == '>>': return a >> b
            if op == '|': return a | b
            if op == '&': return a & b
        if isinstance(a, Fraction) or isinstance(b, Fraction):
            if isinstance(a, FV) or isinstance(b, FV):
                a, b = as_fv(a, self.bits), as_fv(b, self.bits)
            else:
                return {'+': a + b, '-': a - b, '*': a * b, '/': Fraction(a) / b}[op]
        x, y = fval(a, self.bits), fval(b, self.bits)
        with gmpy2.context(**ctx(self.bits)):
            mx, my = mpfr(x), mpfr(y)
            r = {'+': lambda: mx + my, '-': lambda: mx - my, '*': lambda: mx * my, '/': lambda: mx / my}[op]()
            return FV(to_bits(float(r), self.bits))

    def expr(self):
        v = self.shift()
        while self.peek()[1] in ('|', '&'):
            op = self.take()[1]
            v = self.arith(op, v, self.shift())
        return v

    def shift(self):
        v = self.add()
        while self.peek()[1] in ('<<', '>>'):
            op = self.take()[1]
            v = self.arith(op, v, self.add())
        return v

    def add(self):
        v = self.mul()
        while self.peek()[1] in ('+', '-'):
            op = self.take()[1]
            v = self.arith(op, v, self.mul())
        return v

    def mul(self):
        v = self.unary()
        while self.peek()[1] in ('*', '/'):
            op = self.take()[1]
            v = self.arith(op, v, self.unary())
        return v

    def unary(self):
        k, s = self.peek()
        if s == '-':
            self.take()
            v = self.unary()
            if isinstance(v, FV): return FV(v.b ^ (1 << (self.bits - 1)))
            if isinstance(v, Fraction) and v == 0: return FV(1 << (self.bits - 1))   # -0.0
            if isinstance(v, (int, Fraction)): return -v
            raise Fail('negate')
        if s == '+':
            self.take()
            return self.unary()
        if s == '&':
            raise Fail('address-of')
        return self.postfix()

    def postfix(self):
        v = self.primary()
        while self.peek()[1] == '.':
            self.take()
            name = self.take()[1]
            self.take('(')
            args = self.args()
            if isinstance(v, Bits) and name == 'get_val' and not args:
                v = FV(v.b)
            elif isinstance(v, Bits) and name == 'uintval' and not args:
                v = v.b
            else:
                raise Fail('method ' + name)
        return v

    def args(self):
        out = []
        if self.peek()[1] == ')':
            self.take()
            return out
        while True:
            out.append(self.expr())
            if self.take()[1] == ')':
                return out

    def template_arg(self):
        # <T>, <float>, <double>, <uint32_t> …
        self.take('<')
        depth, parts = 1, []
        while depth:
            k, s = self.take()
            if s == '<': depth += 1
            elif s == '>': depth -= 1
            if depth: parts.append(s)
        return ' '.join(parts)

    def fpbits_ctor(self):
        if self.peek()[1] == '<':
            self.template_arg()
        if self.peek()[1] == '::':
            self.take()
            return self.fpbits_static(self.take()[1])
        self.take('(')
        args = self.args()
        if len(args) != 1:
            raise Fail('FPBits args')
        a = args[0]
        if isinstance(a, int): return Bits(a)
        if isinstance(a, FV): return Bits(a.b)
        raise Fail('FPBits arg')

    def fpbits_static(self, name):
        self.take('(')
        args = self.args()
        neg = bool(args) and args[0] == 'NEG'
        b = {'inf': 'inf', 'quiet_nan': 'aNaN', 'signaling_nan': 'sNaN', 'zero': 'zero', 'one': 'one',
             'min_normal': 'min_normal', 'max_normal': 'max_normal', 'min_subnormal': 'min_denormal',
             'max_subnormal': 'max_denormal'}.get(name)
        if b is None:
            raise Fail('FPBits::' + name)
        return Bits(special(('neg_' if neg else '') + b, self.bits))

    def primary(self):
        k, s = self.take()
        if k == 'hexf' or k == 'decf':
            return lit_fraction(s)
        if k == 'int':
            t = s.replace("'", '').rstrip('uUlL')
            return int(t, 16) if t[:2].lower() == '0x' else int(t, 8) if len(t) > 1 and t[0] == '0' else int(t)
        if s == '(':
            v = self.expr()
            self.take(')')
            return v
        if k != 'id':
            raise Fail('token ' + s)
        name = s.split('::')[-1]
        full = s
        if name in ('NEG', 'POS') and 'Sign' in full:
            return name
        if name in ('in', 'out') and self.peek()[1] == '.':
            self.take('.')
            return self.primary()
        if name in ('FPBits', 'OutFPBits', 'InFPBits', 'FPBits_t'):
            return self.fpbits_ctor()
        parts = full.split('::')
        if len(parts) >= 2 and parts[-2] in ('FPBits', 'OutFPBits', 'InFPBits'):   # FPBits::inf(…) as one token
            return self.fpbits_static(name)
        if name in FLOAT_TYPES and self.peek()[1] == '(':
            self.take('(')
            a = self.args()
            if len(a) != 1: raise Fail('cast')
            return as_fv(a[0], self.bits)
        if name in INT_TYPES and self.peek()[1] == '(':
            self.take('(')
            a = self.args()
            if len(a) != 1 or not isinstance(a[0], int): raise Fail('int cast')
            return a[0]
        if name in ('static_cast', 'cast', 'bit_cast'):
            ty = self.template_arg()
            self.take('(')
            a = self.args()
            if len(a) != 1: raise Fail('cast')
            if name == 'bit_cast':
                if ty in FLOAT_TYPES and isinstance(a[0], int): return FV(a[0])
                raise Fail('bit_cast')
            if ty in FLOAT_TYPES: return as_fv(a[0], self.bits)
            if ty in INT_TYPES and isinstance(a[0], int): return a[0]
            raise Fail('cast to ' + ty)
        if name in ('INT_MAX', 'INT_MIN', 'LONG_MAX', 'LONG_MIN', 'LLONG_MAX', 'LLONG_MIN'):
            return {'INT_MAX': 2**31 - 1, 'INT_MIN': -2**31, 'LONG_MAX': 2**63 - 1, 'LONG_MIN': -2**63,
                    'LLONG_MAX': 2**63 - 1, 'LLONG_MIN': -2**63}[name]
        if name in ('FP_ILOGB0', 'FP_ILOGBNAN'):
            return name
        if 'IntLogbConstants' in full and self.peek()[1] == '<':   # IntLogbConstants<T>::FP_LOGB0 …
            self.template_arg()
            if self.peek()[1] == '::': self.take()
            k2, member = self.take()
            if member == 'FP_LOGB0': return 'FP_ILOGB0'
            if member == 'FP_LOGBNAN': return 'FP_ILOGBNAN'
            if member == 'T_MAX': return 2**31 - 1
            raise Fail('IntLogbConstants ' + member)
        sp = special(name, self.bits)
        if sp is not None and self.peek()[1] != '(':
            return FV(sp)
        if name in self.env and self.peek()[1] != '(':
            return self.env[name]
        raise Fail('unknown ' + full)


def evaluate(text, bits, env):
    p = Parser(tokenize(text), bits, env)
    v = p.expr()
    if not p.done():
        raise Fail('trailing ' + str(p.peek()))
    return v


# ---------------------------------------------------------------- source scanning
def preprocess(text):
    """Drops #define bodies and the LIBC_MATH_HAS_SKIP_ACCURATE_PASS branch; keeps every other branch."""
    out, stack = [], []
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if s.startswith('#'):
            d = s[1:].strip()
            while lines[i].rstrip().endswith('\\') and i + 1 < len(lines):
                i += 1
            if d.startswith('if'):
                # the inaccurate fast path, and flush-to-zero / denormals-are-zero modes
                off = any(m in d for m in ('LIBC_MATH_HAS_SKIP_ACCURATE_PASS', 'LIBC_TEST_FTZ_DAZ'))
                skip_if = off and (d.startswith('ifdef') or d.startswith('if '))
                keep_if = off and d.startswith('ifndef')
                stack.append(('skip' if skip_if else 'keep' if keep_if else 'both', True))
            elif d.startswith('else') or d.startswith('elif'):
                kind, _ = stack.pop()
                stack.append((kind, False))
            elif d.startswith('endif'):
                stack.pop()
            out.append('')
            i += 1
            continue
        active = True
        for kind, first in stack:
            if kind == 'skip' and first: active = False
            if kind == 'keep' and not first: active = False
        out.append(line if active else '')
        i += 1
    return '\n'.join(out)


def strip_comments(text):
    text = re.sub(r'/\*.*?\*/', lambda m: re.sub(r'[^\n]', ' ', m.group()), text, flags=re.S)
    return re.sub(r'//[^\n]*', '', text)


def statements(text):
    """Yields (statement text, line, skip) with brace-scoped round-to-nearest tracking."""
    stack = [False]
    buf, start_line, line = [], 1, 1
    depth = 0
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            buf.append(text[i:j + 1]); i = j + 1; continue
        if c == "'" and not (buf and re.match(r'[0-9a-fA-F]', buf[-1][-1:] or '')):
            j = text.find("'", i + 1)
            buf.append(text[i:j + 1]); i = j + 1; continue
        if c == '\n':
            line += 1
        if c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
        if depth == 0 and c in ';{}':
            stmt = ''.join(buf).strip()
            if c == '{':
                # an initializer list "= {" is part of the statement, not a block
                if re.search(r'(=|\breturn)\s*$', stmt) or re.search(r'\]\s*$', stmt) or stmt.endswith(','):
                    j, d = i, 0
                    while j < n:
                        if text[j] == '{': d += 1
                        elif text[j] == '}':
                            d -= 1
                            if d == 0: break
                        elif text[j] == '\n': line += 1
                        j += 1
                    buf.append(text[i:j + 1]); i = j + 1; continue
                trig = triggers(stmt)
                yield stmt, start_line, stack[-1], '{'
                stack.append(stack[-1] or trig)
            elif c == '}':
                if stmt:
                    yield stmt, start_line, stack[-1], ';'
                if len(stack) > 1: stack.pop()
                yield '', line, stack[-1], '}'
            else:
                if not is_check(stmt) and triggers(stmt):
                    stack[-1] = True
                yield stmt, start_line, stack[-1], ';'
            buf, start_line = [], line
            i += 1
            continue
        if not buf and c.isspace():
            i += 1
            start_line = line
            continue
        buf.append(c)
        i += 1


def is_check(stmt):
    return re.match(r'(EXPECT|ASSERT)_', stmt) is not None


def triggers(stmt):
    if re.search(r'\bif\s+constexpr\b', stmt): return True
    if any(w in stmt for w in ('ForceRoundingMode', 'fesetround', 'set_round', 'ModifyMXCSR')): return True
    if 'ROUNDING_MODES' in stmt or 'rounding_mode' in stmt.lower() and 'for' in stmt: return True
    if re.search(r'RoundingMode::(Upward|Downward|TowardZero)|FE_(UPWARD|DOWNWARD|TOWARDZERO)', stmt): return True
    return False


def split_args(s):
    out, depth, cur = [], 0, []
    for c in s:
        if c in '([{': depth += 1
        elif c in ')]}': depth -= 1
        if c == ',' and depth == 0:
            out.append(''.join(cur).strip()); cur = []
        else:
            cur.append(c)
    if cur: out.append(''.join(cur).strip())
    return out


def macro_call(stmt):
    m = re.match(r'(\w+)\s*\((.*)\)\s*$', stmt, re.S)
    if not m: return None
    return m.group(1), split_args(m.group(2))


CALL = re.compile(r'^(?:LIBC_NAMESPACE\s*::\s*)?(\w+)\s*\((.*)\)$', re.S)


class Out:
    def __init__(self):
        self.rows = []
        self.seen = set()
        self.stats = {}
        self.why = {}   # why checks could not be evaluated

    def stat(self, src, key):
        d = self.stats.setdefault(src, {})
        d[key] = d.get(key, 0) + 1

    def add(self, fname, kind, src, args, results):
        key = (fname, tuple(args))
        if key in self.seen:
            self.stat(src.split(':')[0], 'duplicate')
            return
        self.seen.add(key)
        self.rows.append(f'{fname} {kind} {src} {" ".join(args)} => {" ".join(results)}')
        self.stat(src.split(':')[0], kind)


def fmt_arg(v, letter, bits):
    if letter in 'il':
        if not isinstance(v, int): raise Fail('int arg')
        return f'#{v}'
    if letter == 'D':   # nexttoward's long double y, kept only when exact in double
        if isinstance(v, FV) and bits == 64: return f'{v.b:016x}'
        if isinstance(v, (int, Fraction)):
            with gmpy2.context(**D):
                x = mpfr(gmpy2.mpq(Fraction(v).numerator, Fraction(v).denominator))
            if Fraction(float(x)) != Fraction(v): raise Fail('inexact y')
            return f'{to_bits(float(x), 64):016x}'
        if isinstance(v, FV): return f'{to_bits(from_bits(v.b, 32), 64):016x}'
        raise Fail('y')
    fv = as_fv(v, bits)
    return f'{fv.b:0{bits // 4}x}'


def call_parts(call, bound):
    """(C name, arg texts) of a call to a SharpLibm function, or None."""
    m = CALL.match(call.strip())
    if not m: return None
    name = m.group(1)
    if name == 'func' and bound: name = bound
    if lookup(name) is None: return None
    return name, split_args(m.group(2))


def emit_check(out, src, bound, mac, args, env, fbits):
    nm = mac.upper()
    if any(w in nm for w in ('UPWARD', 'DOWNWARD', 'TOWARD_ZERO')):
        return
    if 'MPFR_MATCH' in nm:
        # (op, input, call, tolerance[, rounding])
        if len(args) < 3 or any(re.search(r'RoundingMode::(Upward|Downward|TowardZero)', a) for a in args[3:]):
            return
        cp = call_parts(args[2], bound)
        if cp is None: return
        emit(out, src, cp, None, env)
        return
    if re.search(r'FP_IS_NAN$', nm) and args:
        cp = call_parts(args[0], bound)
        if cp: emit(out, src, cp, 'NAN', env)
        return
    if not re.fullmatch(r'(EXPECT|ASSERT)_(EQ|FP_EQ|FP_EQ_ALL_ROUNDING|FP_EQ_ROUNDING_NEAREST|FP_EQ_ROUNDING_MODE'
                        r'|FP_EQ_WITH_EXCEPTION|FP_EQ_WITH_EXCEPTION_ALL_ROUNDING|EQ_ALL_ROUNDING)', nm) or len(args) < 2:
        return
    if 'ROUNDING_MODE' in nm and not any('Nearest' in a for a in args[2:]):
        return
    cp = call_parts(args[1], bound)
    exp_text = args[0]
    if cp is None:
        cp = call_parts(args[0], bound)
        exp_text = args[1]
    if cp is None: return
    emit(out, src, cp, exp_text, env)


def emit(out, src, cp, exp_text, env):
    name, arg_texts = cp
    base, bits = lookup(name)
    sig_in, sig_out = SIG[base]
    try:
        if len(arg_texts) != len(sig_in): raise Fail('arity')
        vals = [evaluate(a, bits, env) for a in arg_texts]
        args = [fmt_arg(v, l, bits) for v, l in zip(vals, sig_in)]
        if exp_text is None or base == 'sincos':
            fl = [fval(as_fv(v, bits), bits) if l == 'd' else v for v, l in zip(vals, sig_in)]
            r = ref(base, bits, fl)
            if r is None: raise Fail('no reference')
            out.add(name, 'mpfr', src, args, [f'{to_bits(x, bits):0{bits // 4}x}' for x in r])
            return
        if exp_text == 'NAN':
            res = [f'{special("aNaN", bits):0{bits // 4}x}']
        elif sig_out in ('i', 'l'):
            v = evaluate(exp_text, bits, env)
            if isinstance(v, FV):   # OutType(n) in a header shared with the floating logb
                f = from_bits(v.b, bits)
                if f == f and abs(f) != float('inf') and f == int(f): v = int(f)
            if v in ('FP_ILOGB0', 'FP_ILOGBNAN'): res = [v]
            elif isinstance(v, int): res = [f'#{v}']
            else: raise Fail('int result ' + src)
        else:
            res = [fmt_arg(evaluate(exp_text, bits, env), 'd', bits)]
        out.add(name, 'eq', src, args, res)
        # Cross-check against MPFR where it has the function: a disagreement is reported, the source's value kept.
        fl = [fval(as_fv(v, bits), bits) if l == 'd' else v for v, l in zip(vals, sig_in)]
        r = ref(base, bits, fl) if sig_out == 'd' else None
        if r is not None and res[0] != 'NAN':
            want = int(res[0], 16)
            got = to_bits(r[0], bits)
            nan = lambda b: (b & ((1 << (bits - 1)) - 1)) > (0x7f800000 if bits == 32 else 0x7ff0000000000000)
            if got != want and not (nan(got) and nan(want)):
                print(f'  note: {src}: {name}({", ".join(args)}) source says {res[0]}, MPFR {got:0{bits // 4}x}')
    except (Fail, ZeroDivisionError, OverflowError, ValueError, TypeError) as e:
        out.stat(src.split(':')[0], 'unevaluable')
        reason = re.sub(r'\d+', 'N', str(e))[:50]
        out.why[reason] = out.why.get(reason, 0) + 1


DECL = re.compile(r'^(?:(?:static|constexpr|const|volatile)\s+)*(\w+)\s+(\w+)\s*=\s*(.+)$', re.S)
ARRAY = re.compile(r'^(?:(?:static|constexpr|const)\s+)*([\w:<>]+)\s+(\w+)\s*\[[^\]]*\]\s*=\s*\{(.*)\}$', re.S)
ARRAY2 = re.compile(r'^(?:(?:static|constexpr|const)\s+)*[\w:]*array<(\w+),\s*\d+>\s+(\w+)\s*\{(.*)\}$', re.S)


def scan(path, rel, out, bound=None, bits_hint=None):
    """Extracts from one file; bound is the function `func` stands for in a templated header."""
    text = strip_comments(preprocess(open(path, encoding='utf-8', errors='replace').read()))
    main = bound or re.sub(r'_test\.cpp$', '', os.path.basename(path))
    main = main if lookup(main) else None
    envs = [{}]
    for stmt, line, skip, kind in statements(text):
        if kind == '{':
            envs.append(dict(envs[-1]))
            continue
        if kind == '}':
            if len(envs) > 1: envs.pop()
            continue
        if not stmt: continue
        src = f'{rel}:{line}'
        env = envs[-1]
        if is_check(stmt):
            if skip: out.stat(rel, 'not round-to-nearest'); continue
            mc = macro_call(stmt)
            if mc: emit_check(out, src, bound, mc[0], mc[1], env, None)
            continue
        if main is None:
            continue
        base, bits = lookup(main)
        # Arrays of inputs: hard cases checked against MPFR in the test.
        m = ARRAY.match(stmt) or ARRAY2.match(stmt)
        if m and base in REF_OK:
            ty, body = m.group(1), m.group(3)
            arity = len(SIG[base][0])
            items = split_args(body)
            for it in items:
                it = it.strip()
                if not it: continue
                parts = split_args(it[1:-1]) if it.startswith('{') else [it]
                if len(parts) != arity: continue
                try:
                    vals = []
                    for ptxt in parts:
                        v = evaluate(ptxt, bits, env)
                        if isinstance(v, int) and ty in ('uint32_t', 'uint64_t', 'StorageType'):
                            v = FV(v)
                        vals.append(as_fv(v, bits))
                    emit_values(out, src, main, vals, bits)
                except Fail:
                    out.stat(rel, 'unevaluable')
            continue
        m = DECL.match(stmt)
        if m and not stmt.startswith(('return', 'for', 'if', 'while')):
            ty, name, rhs = m.groups()
            try:
                v = evaluate(rhs, bits, env)
                if ty in FLOAT_TYPES or ty in ('auto',) and isinstance(v, FV):
                    v = as_fv(v, bits)
                env[name] = v
            except Fail:
                env.pop(name, None)
            continue
        # anything else that assigns or mutates a known local forgets it
        for name in list(env):
            if re.search(r'\b' + name + r'\s*(?:[-+*/|&]?=(?!=)|\+\+|--|\.set_)', stmt) or re.search(r'(\+\+|--)\s*' + name + r'\b', stmt):
                env.pop(name, None)
        # SDCOMP26094_VALUES loops
        if 'SDCOMP26094_VALUES' in stmt and base in REF_OK:
            for v in SDCOMP:
                emit_values(out, src, main, [FV(v)], bits)


REF_OK = {b for b in SIG if b not in ('floor', 'ceil', 'trunc', 'round', 'rint', 'nearbyint', 'roundeven', 'fabs',
                                       'logb', 'fmod', 'remainder', 'copysign', 'fdim', 'fmax', 'fmin', 'nextafter',
                                       'ilogb', 'llrint', 'llround', 'lrint', 'lround', 'ldexp', 'scalbn', 'scalbln',
                                       'nexttoward')}
SDCOMP = []


def emit_values(out, src, name, vals, bits):
    base, _ = lookup(name)
    fl = [from_bits(v.b, bits) for v in vals]
    r = ref(base, bits, fl)
    if r is None:
        out.stat(src.split(':')[0], 'no reference')
        return
    out.add(name, 'mpfr', src, [f'{v.b:0{bits // 4}x}' for v in vals], [f'{to_bits(x, bits):0{bits // 4}x}' for x in r])


def main():
    root, dst = sys.argv[1], sys.argv[2]
    sd = open(os.path.join(root, 'sdcomp26094.h')).read()
    SDCOMP.extend(int(x, 16) for x in re.findall(r'0x[0-9a-fA-F]+', sd.split('SDCOMP26094_VALUES')[1]))
    out = Out()
    files = []
    for sub in ('', 'smoke', 'exhaustive'):
        d = os.path.join(root, sub)
        for f in sorted(os.listdir(d)):
            if f.endswith('_test.cpp'):
                files.append((os.path.join(d, f), os.path.join(sub, f).replace('\\', '/')))
    used_headers = 0
    for path, rel in files:
        name = os.path.basename(path)[:-9]
        if lookup(name) is None:
            continue
        scan(path, rel, out)
        # templated headers it instantiates
        text = open(path, encoding='utf-8', errors='replace').read()
        for h in re.findall(r'#include\s+"(\w+Test\.h)"', text):
            hp = os.path.join(os.path.dirname(path), h)
            if os.path.exists(hp):
                scan(hp, os.path.join(os.path.dirname(rel), h).replace('\\', '/').lstrip('/') + f'[{name}]', out, bound=name)
                used_headers += 1
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    with open(dst, 'w', newline='\n') as f:
        f.write('\n'.join(out.rows) + '\n')
    total = {}
    for src, d in out.stats.items():
        for k, v in d.items():
            total[k] = total.get(k, 0) + v
    funcs = {}
    for r in out.rows:
        funcs[r.split(' ')[0]] = funcs.get(r.split(' ')[0], 0) + 1
    print(f'{len(out.rows)} vectors, {len(funcs)} functions, {used_headers} header instantiations; ' +
          ', '.join(f'{k} {v}' for k, v in sorted(total.items())))
    print(' '.join(f'{k}:{v}' for k, v in sorted(funcs.items())))
    if os.environ.get('LLVM_WHY'):
        for k, v in sorted(out.why.items(), key=lambda kv: -kv[1])[:25]:
            print(f'  {v:5} {k}')


if __name__ == '__main__':
    main()
