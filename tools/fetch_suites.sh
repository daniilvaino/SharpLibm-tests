#!/bin/bash
# Fetches the libm test suites into suites/. Whole repos for the small ones,
# sparse checkouts (only the test directories) for the large ones. Each suite
# keeps its own licence file; nothing here is redistributed by this repository.
cd "$(dirname "$0")/.." && mkdir -p suites && cd suites

full() {   # full <name> <url>
  [ -d "$1" ] || git clone -q --depth 1 "$2" "$1" || echo "FAILED $1"
  echo "$1 $(git -C "$1" log -1 --format='%h %cs' 2>/dev/null) $2"
}

sparse() { # sparse <name> <url> <dir>...
  local name=$1 url=$2; shift 2
  if [ ! -d "$name" ]; then
    git clone -q --depth 1 --filter=blob:none --sparse "$url" "$name" || { echo "FAILED $name"; return; }
    git -C "$name" sparse-checkout set --no-cone "$@" || echo "FAILED sparse $name"
  fi
  echo "$name $(git -C "$name" log -1 --format='%h %cs' 2>/dev/null) $url [$*]"
}

raw() {    # raw <dir> <file> <url>
  mkdir -p "$1" && curl -sfL "$3" -o "$1/$2" && echo "$1/$2 $(wc -c < "$1/$2") bytes $3" || echo "FAILED $1/$2"
}

full libc-test          https://github.com/kraj/libc-test
full core-math          https://gitlab.inria.fr/core-math/core-math.git
full math_accuracy      https://gitlab.inria.fr/zimmerma/math_accuracy.git
full berkeley-testfloat-3 https://github.com/ucb-bar/berkeley-testfloat-3
sparse optimized-routines https://github.com/ARM-software/optimized-routines '/math/test/' '/LICENSE' '/README*'
sparse compiler-builtins  https://github.com/rust-lang/compiler-builtins '/libm-test/' '/libm/' '/LICENSE*' '/CONTRIBUTING.md'
sparse go                 https://github.com/golang/go '/src/math/*_test.go' '/LICENSE'
sparse freebsd-msun       https://github.com/freebsd/freebsd-src '/lib/msun/tests/' '/COPYRIGHT'
sparse jint               https://github.com/sebastienros/jint '/Jint.Tests/Runtime/MathFdlibmTests.cs' '/LICENSE*'
sparse test262            https://github.com/tc39/test262 '/test/staging/sm/Math/' '/test/built-ins/Math/' '/LICENSE*'
sparse llvm-libc          https://github.com/llvm/llvm-project '/libc/test/src/math/' '/libc/utils/MPFRWrapper/' '/LICENSE.TXT'
sparse glibc              https://github.com/bminor/glibc '/math/auto-libm-test-*' '/math/libm-test-*' '/COPYING.LIB'
sparse openjdk            https://github.com/openjdk/jdk '/test/jdk/java/lang/StrictMath/' '/test/jdk/java/lang/Math/' '/LICENSE'
sparse newlib             https://github.com/mirror/newlib-cygwin '/newlib/libm/test/' '/COPYING.NEWLIB'
sparse crlibm             https://github.com/taschini/crlibm '/tests/' '/COPYING*'
sparse aocl-libm          https://github.com/amd/aocl-libm-ose '/gtests/' '/LICENSE*'
raw v8      ieee754-unittest.cc https://raw.githubusercontent.com/v8/v8/main/test/unittests/base/ieee754-unittest.cc
raw v8      LICENSE             https://raw.githubusercontent.com/v8/v8/main/LICENSE
raw cpython math_testcases.txt  https://raw.githubusercontent.com/python/cpython/main/Lib/test/mathdata/math_testcases.txt
raw cpython cmath_testcases.txt https://raw.githubusercontent.com/python/cpython/main/Lib/test/mathdata/cmath_testcases.txt
raw cpython LICENSE             https://raw.githubusercontent.com/python/cpython/main/LICENSE
# CoreMathSharp's vectors (CORE-MATH C output), laid out as coremathsharp/Binary{32,64}/<f>.txt.
if [ ! -d coremathsharp ]; then
  sparse coremathsharp-src https://github.com/andanteyk/CoreMathSharp '/CoreMathSharp.Tests/Binary32/*.txt' '/CoreMathSharp.Tests/Binary64/*.txt' '/LICENSE.md'
  mkdir -p coremathsharp && cp -r coremathsharp-src/CoreMathSharp.Tests/Binary32 coremathsharp-src/CoreMathSharp.Tests/Binary64 coremathsharp-src/LICENSE.md coremathsharp/ \
    && rm -rf coremathsharp-src
fi
echo DONE
