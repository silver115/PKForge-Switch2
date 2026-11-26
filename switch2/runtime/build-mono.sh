#!/bin/sh
# Builds the Mono fork in Release for libounce (Switch 2), inside the pkforge-switch2 image.
#   PKF_RUNTIME_TFM=net9.0 sh switch2/runtime/build-mono.sh
# The steps are exelix11/mono-nx's build_mono.sh with -c Release added. The fork must be checked
# out at $PKF_RUNTIME_DIR with patches/0001-dotnet_runtime-target-libounce.patch applied.
set -e
: "${PKF_DEPS:=/deps}"
: "${PKF_RUNTIME_DIR:=$PKF_DEPS/dotnet_runtime}"
: "${PKF_RUNTIME_TFM:=net9.0}"
export ROOTFS_DIR="$DEVKITPRO"
export MONO_NX_ROOT="$PKF_RUNTIME_DIR"
export ICU_NX_INSTALL_DIR="$PKF_DEPS/mono-nx/icu/libnx"
# NuGet's audit fails restore when a new advisory names a build-time package (2026-11: Pkcs 9.0.6 in
# Crossgen2Tasks); nothing restored here ships in the app, and a pinned source must keep building.
AUDIT=/p:NuGetAudit=false

# ICU is built once; its data file is never shipped (globalization is invariant).
if [ ! -f "$ICU_NX_INSTALL_DIR/lib/libicudata.a" ]; then
    mkdir -p "$ICU_NX_INSTALL_DIR"
    (cd "$PKF_DEPS/mono-nx/icu" && sh ./build_icu.sh)
fi

cd "$PKF_RUNTIME_DIR"
./build.sh --subset mono.runtime+mono.corelib+libs.native+libs.sfx --cross -a arm64 --os libnx -c Release $AUDIT
./build.sh -s mono.aotcross /p:MonoGenerateOffsetsOSGroups=libnx -c Release $AUDIT
# The AOT cross compiler runs on the build host, so it is built without the Switch root.
ROOTFS_DIR= ./build.sh -s mono -c Release $AUDIT /p:AotHostArchitecture=x64 /p:AotHostOS=linux \
    /p:MonoCrossAOTTargetOS=libnx /p:SkipMonoCrossJitConfigure=true \
    /p:BuildMonoAOTCrossCompilerOnly=true /p:BuildMonoAOTCrossCompiler=true

# Where the outputs land (the static libraries stay under obj, as mono-nx's Makefiles expect):
ls -l "artifacts/obj/mono/libnx.arm64.Release/out/lib/libmonosgen-2.0.a" \
      "artifacts/obj/mono/libnx.arm64.Release/mono/mini/libmono-component-debugger-static.a" \
      "artifacts/bin/mono/libnx.arm64.Release/System.Private.CoreLib.dll" \
      "artifacts/bin/runtime/$PKF_RUNTIME_TFM-libnx-Release-arm64/System.Runtime.dll" \
      "artifacts/bin/mono/linux.x64.Release/cross/linux-x64/libnx-arm64/mono-aot-cross"
