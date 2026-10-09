// Calls sharplibm.dll from C, comparing bit patterns with known correctly
// rounded results (MPFR / CORE-MATH).
#include <stdio.h>
#include <stdint.h>
#include <string.h>

double sin(double), exp(double), log(double), pow(double, double), atan2(double, double);
double fma(double, double, double), cbrt(double), sqrt(double), fmod(double, double);
double frexp(double, int *), modf(double, double *), remquo(double, double, int *);
double lgamma_r(double, int *), j0(double), erfinv(double), tgamma(double);
void sincos(double, double *, double *);
float sinf(float), powf(float, float), fmaf(float, float, float);
long lrint(double), lround(double);
double scalbln(double, long), compoundn(double, long long);
float compoundnf(float, long long), compoundf(float, float);

static uint64_t b(double x) { uint64_t u; memcpy(&u, &x, 8); return u; }
static uint32_t bf(float x) { uint32_t u; memcpy(&u, &x, 4); return u; }
static int fails;
#define CHECK(name, cond) do { int ok = (cond); printf("  %s %s\n", ok ? "ok  " : "FAIL", name); fails += !ok; } while (0)

int main(void) {
    CHECK("sin(1)", b(sin(1.0)) == 0x3FEAED548F090CEEull);
    CHECK("exp(1)", b(exp(1.0)) == 0x4005BF0A8B145769ull);
    CHECK("log(10)", b(log(10.0)) == 0x40026BB1BBB55516ull);
    CHECK("pow(2, 0.5)", b(pow(2.0, 0.5)) == 0x3FF6A09E667F3BCDull);
    CHECK("pow(0.84027…, 0.98712…) (was 0.62 in CoreMathSharp)",
          b(pow(0x1.ae37ed1670326p-1, 0x1.f967df66a202p-1)) == 0x3feaf2f1054bc4b4ull);
    CHECK("atan2(1, 1)", b(atan2(1.0, 1.0)) == 0x3FE921FB54442D18ull);
    CHECK("fma(0.1, 10, -1) rounds once", b(fma(0.1, 10.0, -1.0)) == 0x3C90000000000000ull);
    CHECK("cbrt(27), sqrt(2)", cbrt(27.0) == 3.0 && sqrt(2.0) == 1.4142135623730951);
    CHECK("fmod(5.979e200, 1.1258…)", fmod(5.9790119248836734e+200, 1.1258465975523544) == 0.6447968302508578);

    int e; double m = frexp(48.0, &e);
    CHECK("frexp(48) = 0.75 * 2^6 (int* out)", m == 0.75 && e == 6);
    double ip; double fr = modf(-3.25, &ip);
    CHECK("modf(-3.25) = -0.25, -3 (double* out)", fr == -0.25 && ip == -3.0);
    int q; double r = remquo(10.0, 3.0, &q);
    CHECK("remquo(10, 3) = 1, q≡3", r == 1.0 && (q & 7) == 3);
    int sg; double lg = lgamma_r(-0.5, &sg);
    CHECK("lgamma_r(-0.5): sign -1", sg == -1 && lg > 1.26 && lg < 1.27);
    double s, c; sincos(0.5, &s, &c);
    CHECK("sincos(0.5) == sin, cos", b(s) == b(sin(0.5)) && c > 0.877 && c < 0.878);
    CHECK("tgamma(5) = 24", tgamma(5.0) == 24.0);
    CHECK("j0(0) = 1, erfinv(0.5)", j0(0.0) == 1.0 && erfinv(0.5) > 0.4769 && erfinv(0.5) < 0.4770);

    CHECK("sinf(1)", bf(sinf(1.0f)) == 0x3f576aa4u);
    CHECK("powf(2, 10) = 1024", powf(2.0f, 10.0f) == 1024.0f);
    CHECK("fmaf subnormal (TestFloat case)", bf(fmaf(0x1.000002p-24f, -0x1.fffffcp-127f, 0x1.fffffcp-127f)) == 0x007fffffu);

    // C's long: 32 bits on Windows, 64 on Linux — a negative long argument
    // and long results must survive the ABI.
    CHECK("lrint(2.5) = 2, lround(2.5) = 3, lrint(-7.5) = -8 (C long)", lrint(2.5) == 2 && lround(2.5) == 3 && lrint(-7.5) == -8);
    CHECK("scalbln(1, 10) = 1024, scalbln(1, -1074) = min subnormal (C long)",
          scalbln(1.0, 10) == 1024.0 && b(scalbln(1.0, -1074)) == 1);
    CHECK("compoundn(-0.5, -2) = 4, compoundnf(0.5, 3) = 3.375", compoundn(-0.5, -2) == 4.0 && compoundnf(0.5f, 3) == 3.375f);
    CHECK("compoundnf(-1.5, 0) is NaN, compoundn(-1, -1) = +inf",
          compoundnf(-1.5f, 0) != compoundnf(-1.5f, 0) && compoundn(-1.0, -1) > 1e308);
    CHECK("compoundf(0.5, 0.5) = sqrt(1.5) (real exponent)", bf(compoundf(0.5f, 0.5f)) == 0x3f9cc471u);

    printf(fails ? "%d failed\n" : "all passed\n", fails);
    return fails != 0;
}
