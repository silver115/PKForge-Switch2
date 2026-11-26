#!/bin/sh
# Builds the Mono interpreter smoke test (phase 0, Task 3) and packs an SD card layout:
#   build/mono-smoke-sd.zip -> /mono/ (runtime, config), /switch/mono-smoke.nro, /switch/PKForge/mono-smoke/example.dll
#   PKF_RUNTIME_DIR=/deps/dotnet_runtime PKF_RUNTIME_TFM=net9.0 sh build.sh   builds the .NET 9 one (.NET 10 by default)
set -e
: "${PKF_DEPS:=/deps}"
: "${PKF_RUNTIME_DIR:=$PKF_DEPS/dotnet_runtime10}"
: "${PKF_RUNTIME_TFM:=net10.0}"
tfm=$PKF_RUNTIME_TFM
here=$(cd "$(dirname "$0")" && pwd)
rt=$PKF_RUNTIME_DIR/artifacts

# Clean first: the link does not depend on the Mono archives or the Makefile's LIBS.
make -C "$here" clean > /dev/null
make -C "$here" PKF_DEPS="$PKF_DEPS" PKF_RUNTIME_DIR="$PKF_RUNTIME_DIR" PKF_RUNTIME_TFM="$tfm"
mkdir -p "$here/build"
# The example is retargeted in a copy: restore ignores a TargetFramework given on the command line.
rm -rf "$here/build/example-src" && mkdir -p "$here/build/example-src"
cp "$PKF_DEPS/mono-nx/managed/example/example.cs" "$here/build/example-src/"
sed "s|<TargetFramework>[^<]*</TargetFramework>|<TargetFramework>$tfm</TargetFramework>|" \
    "$PKF_DEPS/mono-nx/managed/example/example.csproj" > "$here/build/example-src/example.csproj"
dotnet build "$here/build/example-src/example.csproj" -c Release -o "$here/build/example" > "$here/build/example-build.log"

sd=$here/build/sd
rm -rf "$sd"
mkdir -p "$sd/mono/lib_$tfm" "$sd/mono/framework_$tfm" "$sd/mono/etc" "$sd/switch/PKForge/mono-smoke"
cp "$rt"/bin/mono/libnx.arm64.Release/*.dll "$sd/mono/lib_$tfm/"
cp "$rt"/bin/runtime/$tfm-libnx-Release-arm64/*.dll "$sd/mono/framework_$tfm/"
cp "$PKF_DEPS"/mono-nx/icu/libnx/share/icu/77.1/icudt77l.dat "$sd/mono/etc/"
sed "s/net9\.0/$tfm/g" "$here/config.ini" > "$sd/mono/config.ini"   # the runtime folders carry the TFM
cp "$here/build/example/example.dll" "$sd/switch/PKForge/mono-smoke/"
cp "$here/mono-smoke.nro" "$sd/switch/mono-smoke.nro"
(cd "$sd" && rm -f ../mono-smoke-sd.zip && python3 -m zipfile -c ../mono-smoke-sd.zip mono switch)
ls -l "$here/mono-smoke.nro" "$here/build/mono-smoke-sd.zip"
