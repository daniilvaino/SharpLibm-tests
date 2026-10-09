#!/bin/sh
# CORE-MATH's binary32 functions as a Windows DLL (exports cr_<name>f), the
# reference for the exhaustive float check. MSYS2 mingw64 gcc. -mfma is
# required: without it gcc calls mingw's fmaf, which is not correctly rounded.
#   sh tools/build_f32_dll.sh   ->  build/corematf.dll
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$root/build"
export PATH=/c/msys64/mingw64/bin:$PATH
cd "$root/suites/core-math/src/binary32"
gcc -O2 -msse4.1 -mfma -shared -static-libgcc -o "$root/build/corematf.dll" \
  $(ls */*f.c | grep -v -E "support|_mpfr|check|worst|tst|sincos") -lm
echo "$root/build/corematf.dll"
