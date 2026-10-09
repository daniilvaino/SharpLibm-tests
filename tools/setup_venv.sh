#!/bin/sh
# Python with gmpy2 (MPFR) for tools/wc_ref.py. gmpy2 has Windows wheels for
# CPython up to 3.12/3.13, not for MSYS2's Python.
set -e
root=$(cd "$(dirname "$0")/.." && pwd)
py -3.12 -m venv "$root/.venv"
"$root/.venv/Scripts/python.exe" -m pip install -q gmpy2
"$root/.venv/Scripts/python.exe" -c "import gmpy2; print('gmpy2', gmpy2.version(), 'MPFR', gmpy2.mpfr_version())"
