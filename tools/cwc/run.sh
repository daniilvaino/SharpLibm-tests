#!/bin/sh
# Builds each CORE-MATH function from <src> and checks it against <refs>.
#   sh run.sh <core-math/src> <wc-ref dir> [function ...]
src=$1; refs=$2; shift 2
here=$(dirname "$0")
out=/tmp/cwc; mkdir -p $out
two="atan2 atan2pi hypot pow atan2f atan2pif hypotf powf compoundf"
single="atan2f atan2pif hypotf powf compoundf"
names=${*:-$(ls $refs | sed 's/\.txt$//')}
for f in $names; do
  arity=1; for t in $two; do [ $f = $t ] && arity=2; done
  s=0; dir=binary64; for t in $single; do [ $f = $t ] && s=1 && dir=binary32; done
  d=$f; [ $s = 1 ] && d=${f%f}; c=$src/$dir/$d/$f.c
  [ -f $c ] || { echo "  --   $f: no $c"; continue; }
  gcc -O2 -I$src/$dir/$d -DFN=cr_$f -DARITY=$arity -DSINGLE=$s -o $out/$f $here/driver.c $c -lm 2>$out/$f.err \
    || { echo "  --   $f: build failed"; head -3 $out/$f.err; continue; }
  $out/$f $refs/$f.txt $f
done
