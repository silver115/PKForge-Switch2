#!/bin/sh
# Builds switch2/host/PKForge.nro inside the pkforge-switch2 image, from PKF_APP_PROJECT (default: the app,
# switch2/app/PKForge.Switch2; the phase 0 spike is switch2/spike/PKForge.Switch2.Spike/PKForge.Switch2.Spike.csproj):
#   sh switch2/host/build.sh        Mono interpreter: the app's IL and the whole framework in romfs
#   sh switch2/host/build.sh aot    full AOT: ILLink, then mono-aot-cross on every assembly
# Everything it makes stays under switch2/host/build/, which the NAS mirror leaves alone; logs land there too.
# Based on mono-nx's native/aot/build_aot.sh.
set -e
: "${PKF_DEPS:=/deps}"
: "${PKF_RUNTIME_DIR:=$PKF_DEPS/dotnet_runtime10}"
: "${PKF_RUNTIME_TFM:=net10.0}"
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
art=$PKF_RUNTIME_DIR/artifacts
corelib=$art/bin/mono/libnx.arm64.Release
framework=$art/bin/runtime/$PKF_RUNTIME_TFM-libnx-Release-arm64
build=$here/build
mode=${1:-interpreter}
project=$repo/${PKF_APP_PROJECT:-switch2/app/PKForge.Switch2/PKForge.Switch2.csproj}
root=$(basename "$project" .csproj)

rm -rf "$build/publish" "$build/linked" "$build/romfs" "$build/aot"
mkdir -p "$build/romfs" "$build/aot"
dotnet publish "$project" -c Release -p:UseAppHost=false -o "$build/publish" > "$build/publish.log"
# Mono starts the root assembly that aot_config.ini names.
sed "s|^default_assembly = .*|default_assembly = /$root.dll|" "$here/romfs/aot_config.ini" > "$build/romfs/aot_config.ini"
if [ "$root" = PKForge.Switch2.Spike ]; then
    # The spike scene's art: the PKSM chrome, the eight sprites SpikeScene shows, and the font.
    mkdir -p "$build/romfs/art/pksm" "$build/romfs/art/sprites"
    cp "$repo"/src/PKForge.App/Resources/UI/pksm/*.png "$build/romfs/art/pksm/"
    for id in 645 25 149 143 9 658 445 376; do
        cp "$repo/external/PKHeX/PKHeX.Drawing.PokeSprite/Resources/img/Big Pokemon Sprites/b_$id.png" "$build/romfs/art/sprites/"
    done
    cp "$repo/src/PKForge.App/Resources/Fonts/NDS12.ttf" "$build/romfs/art/"
fi
# The licences of everything in the .nro travel with it (switch2/licenses/THIRD-PARTY-NOTICES.md).
mkdir -p "$build/romfs/licenses"
cp "$repo/LICENSE" "$build/romfs/LICENSE"
cp "$repo"/switch2/licenses/* "$build/romfs/licenses/"
# The app's package files (Android's assets) at their MAUI names; FileSystem.OpenAppPackageFileAsync reads /app/.
if [ -d "$build/publish/app" ]; then
    cp -r "$build/publish/app" "$build/romfs/app"
    rm -rf "$build/publish/app"
    # Lossless: optipng only re-encodes, so every image decodes to the same pixels (about 9 % smaller, 2026-11-26).
    find "$build/romfs/app" -name '*.png' -print0 | xargs -0 -P 8 -n 20 optipng -quiet -o2 > "$build/optipng.log" 2>&1 || true
fi

if [ "$mode" = aot ]; then
    # The framework is trimmed; the app's own assemblies (PKForge, PKHeX, the Auto Legality Mod) are kept whole.
    keep=""
    for dll in "$build"/publish/*.dll; do keep="$keep --action copy $(basename "$dll" .dll)"; done
    # CoreLib carries its ILLink descriptors as embedded resources, which ILLink reads by itself.
    # $keep is a word list on purpose.
    # shellcheck disable=SC2086
    # The two feature switches below are the SDK's defaults for trimmed apps. They also drop CoreLib's only
    # unsafe accessors into other assemblies (BinaryFormatter, MetricsEventSource): AOT-compiled, those make
    # CoreLib's AOT module load an assembly that needs CoreLib while CoreLib itself is still loading.
    dotnet "$art/bin/Mono.Linker/Release/$PKF_RUNTIME_TFM/illink.dll" \
        --feature System.Globalization.Invariant true \
        --feature System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization false \
        --feature System.Diagnostics.Tracing.EventSource.IsSupported false \
        --feature System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported false \
        -d "$corelib" -d "$framework" -d "$build/publish" \
        --trim-mode link --action copy $keep \
        -a "$build/publish/$root.dll" -out "$build/linked" > "$build/illink.log" 2>&1

    aot=$art/bin/mono/linux.x64.Release/cross/linux-x64/libnx-arm64/mono-aot-cross
    # Calls into the host's own libraries are linked directly. Any other P/Invoke (OpenSSL's TLS shims, which
    # copied assemblies can reach) resolves at run time and fails with DllNotFoundException, as in the interpreter.
    printf '%s\n' libSystem.Native libSystem.IO.Compression.Native libSystem.Globalization.Native pkfhost \
        > "$build/aot/direct-pinvokes.txt"
    # SkiaSharp's C API: direct calls for each function our CPU-only libSkiaSharp.a defines (no GPU ones).
    aarch64-none-elf-nm "$PKF_DEPS/skia/out/libnx/switch/libSkiaSharp.a" 2>/dev/null \
        | sed -n 's/^[0-9a-f]* T \(\(sk\|gr\|skottie\|sksg\|skresources\)_[A-Za-z0-9_]*\)$/libSkiaSharp!\1/p' \
        | sort -u >> "$build/aot/direct-pinvokes.txt"
    # Dedup, as the .NET SDK does for iOS: each assembly's image leaves out the generic code that assemblies share
    # (dedup-skip), and the image of an empty assembly, aot-instances.dll, holds that code once (dedup-include).
    dotnet build "$here/aot-instances/aot-instances.csproj" -c Release -o "$build/aot-instances" > "$build/aot-instances.log"
    cp "$build/aot-instances/aot-instances.dll" "$build/linked/"
    aot_args=full,static,direct-icalls,direct-pinvoke-lists="$build/aot/direct-pinvokes.txt",tool-prefix=aarch64-none-elf-
    : > "$build/mono_aot.log"
    for dll in "$build"/linked/*.dll; do
        [ "$(basename "$dll")" = aot-instances.dll ] && continue
        # Full AOT takes every trampoline from CoreLib's image; PKHeX's legality code ran out of the
        # default 4096 specific ones ("Ran out of trampolines of type 0"), so CoreLib gets bigger pools.
        pools=
        [ "$(basename "$dll")" = System.Private.CoreLib.dll ] && pools=,ntrampolines=65536,nrgctx-trampolines=16384,nimt-trampolines=2048,ngsharedvt-trampolines=8192,nftnptr-arg-trampolines=8192,nunbox-arbitrary-trampolines=1024,nrgctx-fetch-trampolines=1024
        "$aot" --path="$build/linked" --aot="$aot_args",dedup-skip$pools "$dll" >> "$build/mono_aot.log" 2>&1
    done
    # The container is compiled with every assembly given, so it sees all the code they share. Only its own image is
    # kept from this run: the others' come from the loop above.
    mkdir -p "$build/aot/images"
    cp "$build"/linked/*.o "$build/aot/images/"
    "$aot" --path="$build/linked" --aot="$aot_args",dedup-include=aot-instances.dll \
        "$build/linked/aot-instances.dll" $(ls "$build"/linked/*.dll | grep -v '/aot-instances\.dll$') >> "$build/mono_aot.log" 2>&1
    cp "$build"/aot/images/*.o "$build/linked/"
    grep "Linking symbol:" "$build/mono_aot.log" \
        | sed "s/Linking symbol: '\([^']*\)'\./STATIC_MONO_SYM(\1);/" | awk '!seen[$0]++' > "$build/aot/mono_symbols.h"
    cp "$build"/linked/*.dll "$build/romfs/"   # the IL is still needed for metadata
    make_args=AOT=1
else
    # The fork's framework comes last, so it wins over any framework copy a package brought along.
    cp "$build"/publish/*.dll "$build/romfs/"
    cp "$corelib/System.Private.CoreLib.dll" "$framework"/*.dll "$build/romfs/"
    make_args=
fi

# Clean first: the link does not depend on the Mono archives or the romfs.
make -C "$here" clean > /dev/null
make -C "$here" $make_args PKF_DEPS="$PKF_DEPS" PKF_RUNTIME_DIR="$PKF_RUNTIME_DIR" PKF_RUNTIME_TFM="$PKF_RUNTIME_TFM"
ls -l "$here/PKForge.nro"
du -sh "$build/romfs"
