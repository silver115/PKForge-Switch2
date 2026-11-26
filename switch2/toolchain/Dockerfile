# PKForge for Switch 2: the one build image.
# devkitA64 + the ounce devkit (libounce, OunceVK, libkaihen), dotnet/runtime's cross-build
# dependencies (after exelix11/mono-nx's Dockerfile), SDL2 and the .NET 10 SDK.
#   docker build -t pkforge-switch2 switch2/toolchain
FROM devkitpro/devkita64:20260219

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
        cpio build-essential python3 curl wget git lldb llvm liblldb-dev libunwind8 libunwind8-dev \
        gettext libicu-dev liblttng-ust-dev libssl-dev libkrb5-dev ninja-build tzdata \
        clang libclang-dev glslang-tools ca-certificates optipng \
    && rm -rf /var/lib/apt/lists/*

# dotnet/runtime needs CMake 3.20 or later and the base image ships an older one; mono-nx uses 3.22.1.
RUN (apt-get purge -y cmake || true) \
    && wget -q https://cmake.org/files/v3.22/cmake-3.22.1-linux-x86_64.sh \
    && mkdir -p /opt/cmake && sh cmake-3.22.1-linux-x86_64.sh --skip-license --prefix=/opt/cmake \
    && rm cmake-3.22.1-linux-x86_64.sh && ln -sf /opt/cmake/bin/* /usr/local/bin/

# Mono's offsets tool loads libclang.so by that name.
RUN ln -sf "$(find /usr/lib -name 'libclang.so' -print -quit)" /usr/local/lib/libclang.so

ADD https://raw.githubusercontent.com/ouncebrew/ounce-devkit/5e957599bd498643d82e5470aec80da67a12685d/install.sh /tmp/install.sh
RUN sh /tmp/install.sh && rm /tmp/install.sh

RUN dkp-pacman -Q switch-sdl2 >/dev/null 2>&1 || dkp-pacman -S --noconfirm switch-sdl2

# The runtime build fetches its own SDK, so only PKForge's SDK is installed here.
RUN wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH=/opt/devkitpro/devkitA64/bin:/opt/devkitpro/tools/bin:$PATH

WORKDIR /work/pkforge
