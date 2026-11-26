#!/bin/sh
# Builds libSkiaSharp.a, CPU raster only, for libounce (Switch 2), inside the pkforge-switch2 image:
#   sh switch2/skia/build-libskiasharp.sh
# From $PKF_DEPS/skia: mono/skia at the commit SkiaSharp v3.116.1 pins, on branch ounce with our Switch patches
# (switch2/skia/patches; the zlib one goes on third_party/externals/zlib). Its third-party sources come from
# `python3 switch2/skia/skia_deps.py $PKF_DEPS/skia`: freetype, libpng, zlib, expat, wuffs, brotli at DEPS revisions.
# Output: $PKF_DEPS/skia/out/libnx/switch/libSkiaSharp.a, one archive holding SkiaSharp's C API and all of Skia.
set -e
: "${PKF_DEPS:=/deps}"
cd "$PKF_DEPS/skia"
inc="$DEVKITPRO/libounce/include"

# No GPU backends, no PDF, no shaping or ICU, no JPEG/WebP/GIF/RAW codecs: the app draws on the CPU and only
# needs PNG and FreeType. Fonts come from files or streams (the empty custom font manager), never fontconfig.
# SK_BUILD_FOR_UNIX: aarch64-none-elf defines neither __linux__ nor __unix__, so Skia would assume macOS.
# _GNU_SOURCE: newlib hides POSIX functions (fileno, pread) under -std=c++17; glibc's g++ defines it by itself.
bin/gn gen out/libnx --args="
  target_os=\"linux\" target_cpu=\"arm64\"
  cc=\"aarch64-none-elf-gcc\" cxx=\"aarch64-none-elf-g++\" ar=\"aarch64-none-elf-ar\"
  is_official_build=true is_debug=false is_static_skiasharp=true skia_enable_tools=false
  skia_enable_ganesh=false skia_enable_graphite=false skia_use_gl=false skia_use_vulkan=false
  skia_enable_pdf=false skia_use_harfbuzz=false skia_use_icu=false skia_use_sfntly=false
  skia_use_piex=false skia_use_dng_sdk=false skia_use_wuffs=false skia_use_libheif=false skia_use_libavif=false
  skia_use_libjpeg_turbo_decode=false skia_use_libjpeg_turbo_encode=false
  skia_use_libwebp_decode=false skia_use_libwebp_encode=false
  skia_use_fontconfig=false skia_enable_fontmgr_custom_empty=true skia_use_freetype=true
  skia_use_expat=true skia_use_system_expat=false skia_use_system_freetype2=false
  skia_use_system_libpng=false skia_use_system_zlib=false
  extra_cflags=[ \"-mcpu=cortex-a78c+crypto\", \"-mtp=soft\", \"-fPIE\", \"-ffunction-sections\", \"-fdata-sections\",
                 \"-D__SWITCH__\", \"-DSK_BUILD_FOR_UNIX\", \"-D_GNU_SOURCE\", \"-DSKIA_C_DLL\", \"-DXML_POOR_ENTROPY\",
                 \"-isystem$inc\" ]
"
ninja -C out/libnx SkiaSharp

# Skia's raster pipeline (src/opts/SkRasterPipeline_opts.h) does nearly all of the app's per-pixel work: blending,
# images, layers, anti-aliased edges. Unless clang compiles it, it is plain scalar code, one pixel at a time
# ("#if !defined(__clang__) #define JUMPER_IS_SCALAR"); the console drew screens 20-50 times slower than a PC.
# SkOpts.cpp, the one arm64 file that holds it, is compiled again by clang with ninja's own command line and
# replaces GCC's copy in the merged archive below. Clang has no -mtp=soft (devkitPro's GCC option for libnx's
# thread pointer); the file declares no thread-locals, so it is dropped. Clang keeps its own builtin headers
# (arm_neon.h) and takes devkitA64's C library and libstdc++ headers.
gcc_version=$(aarch64-none-elf-gcc -dumpversion)
sysroot="$DEVKITPRO/devkitA64/aarch64-none-elf"
opts=$(ninja -C out/libnx -t commands SkiaSharp | grep -m1 -- '-c ../../src/core/SkOpts.cpp ')
[ -n "$opts" ] || { echo "no compile command for src/core/SkOpts.cpp"; exit 1; }
cxx="clang++ --target=aarch64-none-elf --sysroot=$sysroot -nostdinc++"
cxx="$cxx -isystem $sysroot/include/c++/$gcc_version -isystem $sysroot/include/c++/$gcc_version/aarch64-none-elf"
opts=$(echo "$opts" | sed -e "s|^aarch64-none-elf-g++ |$cxx |" -e 's| -mtp=soft||' \
    -e 's|libskia\.SkOpts\.o|libskia.SkOpts.clang.o|g')
(cd out/libnx && sh -c "$opts")

# gn's complete_static_lib leaves out dependencies that are static libraries themselves (Skia, FreeType, ...), so
# every archive the SkiaSharp target built is merged into one, next to ninja's own outputs.
cd out/libnx
mkdir -p switch
{
    echo "create switch/libSkiaSharp.a"
    for lib in *.a; do echo "addlib $lib"; done
    echo "delete libskia.SkOpts.o"
    echo "addmod obj/src/core/libskia.SkOpts.clang.o"
    echo save
    echo end
} | aarch64-none-elf-ar -M
ls -l switch/libSkiaSharp.a
# The clang copy must be the only SkOpts in the archive, with the NEON low-precision stages GCC's copy lacks.
members=$(aarch64-none-elf-ar t switch/libSkiaSharp.a | grep -E '^libskia\.SkOpts(\.clang)?\.o$' | tr '\n' ' ')
lowp=$(aarch64-none-elf-nm -C obj/src/core/libskia.SkOpts.clang.o | grep -c 'neon::lowp::')
echo "raster pipeline: $members($lowp NEON lowp symbols)"
[ "$members" = "libskia.SkOpts.clang.o " ] && [ "$lowp" -gt 0 ] || { echo "SkOpts.cpp is not clang's in the archive"; exit 1; }
echo "sk_ functions: $(aarch64-none-elf-nm switch/libSkiaSharp.a 2>/dev/null | grep -c ' T sk_')"
aarch64-none-elf-nm switch/libSkiaSharp.a 2>/dev/null | grep ' T sk_version_get_milestone'
