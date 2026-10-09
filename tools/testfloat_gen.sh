#!/bin/bash
# Berkeley TestFloat 3 vectors for the exact IEEE operations SharpLibm implements:
# builds SoftFloat 3 and testfloat_gen (Linux x86-64 GCC; run under WSL:
# `wsl -e bash tools/testfloat_gen.sh`) and writes suites/testfloat-vec/
# <function>_<rounding>_L<level>.txt[.gz]: one case per line, operands, result
# and exception flags as hex bit patterns (the flags are not checked).
#
#   f64/f32_mulAdd     near_even          level 1, and the first FMA_L2 cases of level 2
#   f64/f32_sqrt       near_even          levels 1 and 2
#   f64/f32_roundToInt near_even, minMag, min, max, near_maxMag   levels 1 and 2
#   f64/f32_rem        near_even          levels 1 and 2 (rem is IEEE remainder)
#
# Level 2 of mulAdd is 1.8e11 (f64) and 1.4e10 (f32) cases, too many to keep or
# run: of it, only a prefix is taken. Its cases alternate between three random
# operands and a structured triple whose last operand varies fastest, so the
# prefix holds FMA_L2/2 random triples and structured ones for the first few
# values of the first operand only.
set -e
cd "$(dirname "$0")/.."
FMA_L2=${FMA_L2:-16000000}
SF=suites/berkeley-softfloat-3/build/Linux-x86_64-GCC
TF=suites/berkeley-testfloat-3/build/Linux-x86_64-GCC
[ -f $SF/softfloat.a ] || make -s -C $SF -j8
[ -x $TF/testfloat_gen ] || make -s -C $TF -j8
GEN=$TF/testfloat_gen
OUT=suites/testfloat-vec
mkdir -p $OUT

gen() {   # gen <file> <args...>   (.gz files are compressed)
  local file=$1; shift
  if [ -s "$OUT/$file" ]; then echo "$file exists"; return; fi
  case $file in
    *.gz) "$GEN" "$@" | gzip -1 > "$OUT/$file.tmp" ;;
    *)    "$GEN" "$@" > "$OUT/$file.tmp" ;;
  esac
  mv "$OUT/$file.tmp" "$OUT/$file"
  echo "$file $(wc -c < "$OUT/$file") bytes"
}

for t in f32 f64; do
  gen ${t}_mulAdd_near_even_L1.txt.gz -level 1 -rnear_even ${t}_mulAdd
  # head ends testfloat_gen with SIGPIPE; pipefail would report that as a failure.
  if [ ! -s $OUT/${t}_mulAdd_near_even_L2.txt.gz ]; then
    ("$GEN" -level 2 -rnear_even ${t}_mulAdd || true) | head -n "$FMA_L2" | gzip -1 > $OUT/${t}_mulAdd_near_even_L2.txt.gz.tmp
    mv $OUT/${t}_mulAdd_near_even_L2.txt.gz.tmp $OUT/${t}_mulAdd_near_even_L2.txt.gz
    echo "${t}_mulAdd_near_even_L2.txt.gz $(wc -c < $OUT/${t}_mulAdd_near_even_L2.txt.gz) bytes (first $FMA_L2 cases)"
  fi
  for l in 1 2; do
    gen ${t}_sqrt_near_even_L$l.txt -level $l -rnear_even ${t}_sqrt
    for r in near_even minMag min max near_maxMag; do
      gen ${t}_roundToInt_${r}_L$l.txt -level $l -r$r ${t}_roundToInt
    done
  done
  gen ${t}_rem_near_even_L1.txt -level 1 -rnear_even ${t}_rem
  gen ${t}_rem_near_even_L2.txt.gz -level 2 -rnear_even ${t}_rem
done
echo DONE
