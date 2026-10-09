// Runs a CORE-MATH function on the lines of a wc-ref file and prints the
// lines where it disagrees with the reference.  Built per function:
//   gcc -O2 -DFN=cr_sin -DARITY=1 -DSINGLE=0 driver.c <f>.c -lm
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <inttypes.h>

#if SINGLE
typedef float T;
typedef uint32_t U;
#else
typedef double T;
typedef uint64_t U;
#endif

#if ARITY == 1
T FN(T);
#else
T FN(T, T);
#endif

static T f(U u) { T x; memcpy(&x, &u, sizeof x); return x; }
static U u(T x) { U v; memcpy(&v, &x, sizeof v); return v; }
static int isnan_(T x) { return x != x; }

int main(int argc, char **argv) {
  FILE *in = fopen(argv[1], "r");
  if (!in) { perror(argv[1]); return 2; }
  unsigned long long a, b = 0, want;
  long n = 0, bad = 0;
  char line[256];
  while (fgets(line, sizeof line, in)) {
#if ARITY == 1
    if (sscanf(line, "%llx %llx", &a, &want) != 2) continue;
    T r = FN(f((U)a));
#else
    if (sscanf(line, "%llx %llx %llx", &a, &b, &want) != 3) continue;
    T r = FN(f((U)a), f((U)b));
#endif
    n++;
    if (u(r) == (U)want || (isnan_(r) && isnan_(f((U)want)))) continue;
    if (++bad <= 5)
      printf("    %llx %llx got %llx want %llx\n", a, b, (unsigned long long)u(r), want);
  }
  printf("  %s %s: %ld/%ld\n", bad ? "FAIL" : "ok  ", argv[2], n - bad, n);
  return bad != 0;
}
