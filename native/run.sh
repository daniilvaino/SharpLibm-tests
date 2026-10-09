#!/bin/sh
# SharpLibm as a C library: builds it for the host (../OS/SharpLibm/native:
# sharplibm.dll on Windows, sharplibm.so on Linux) and calls it from C. Every
# function is bound at run time (gen_dyn.py: GetProcAddress / dlsym), so none
# can come from the C toolchain's own libm.
#   sh native/run.sh
# Windows: MSYS2 mingw64 gcc, Visual Studio's link.exe with vswhere on PATH.
# Linux: gcc, and the .NET SDK's usual prerequisites (clang).
set -e
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/.." && pwd)
lib=${SHARPLIBM_ROOT:-$root/../OS/SharpLibm}
out=$root/build/native
mkdir -p "$out"
(cd "$lib" && MSBUILDDISABLENODEREUSE=1 dotnet publish native -c Release -o "$out" >/dev/null)
cd "$out"
cp "$here/smoke.c" .
case "$(uname -s)" in
  Linux)
    python3 "$here/gen_dyn.py" linux
    gcc -O1 -fno-builtin -o smoke_dyn smoke_dyn.c -ldl
    ./smoke_dyn ;;
  *)
    "$root/.venv/Scripts/python.exe" "$here/gen_dyn.py"
    PATH=/c/msys64/mingw64/bin:$PATH gcc -O1 -fno-builtin -o smoke_dyn.exe smoke_dyn.c
    ./smoke_dyn.exe ;;
esac
