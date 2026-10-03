#!/bin/bash
# Cross-compile the ReShade add-on on Linux (mingw-w64): needs `apt install mingw-w64`.
set -e
cd "$(dirname "$0")"
mkdir -p out
x86_64-w64-mingw32-g++-posix -std=c++20 -O2 -shared -static -static-libgcc -static-libstdc++ \
  -Icompat -Ithird_party/reshade -o out/SotfPassthrough.addon64 addon_main.cpp compositor.cpp -lkernel32 -luser32
ls -la out
