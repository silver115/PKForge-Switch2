# PKForge for Switch 2

Work on the Switch 2 port. Its design notes and console test findings are kept out of the published source.

## Build

Everything builds in one Docker image (`switch2/toolchain/Dockerfile`). There are two ways to run it.

**On a NAS (in use).** The image runs as a Docker Compose stack, `pkforge-switch2` (`compose.yaml` here), in a folder the PC reaches over a network share. Set `PKF_NAS_STACK` to that folder. From the PC:

```powershell
switch2\toolchain\nas-job.ps1 "<command>"   # mirrors the repo to the NAS, runs <command> in /work/pkforge, prints the log
```

The container runs `runner.sh`, which executes each job file it finds in the stack's `work/jobs` folder. Anyone who can write to that folder can run commands in the container, though not on the NAS itself. The Mono and Skia sources live in the Docker volume `deps`, mounted at `/deps`, because they need a case-sensitive filesystem.

**On WSL2 (fallback).** Under Ubuntu 24.04 on the PC:

```sh
cd /mnt/c/<your path>/pkforge
docker build -t pkforge-switch2 switch2/toolchain
switch2/toolchain/run.sh <command>      # runs <command> in the image, in the repo root
```

`run.sh` mounts the repo at `/work/pkforge` and `~/pkforge-deps` at `/deps`.

**The phase 0 spike.** In the image:

- `sh switch2/runtime/build-mono.sh` builds the Mono fork, with `PKF_RUNTIME_TFM=net10.0 PKF_RUNTIME_DIR=/deps/dotnet_runtime10`.
- `sh switch2/host/build.sh aot` builds `switch2/host/PKForge.nro` in full AOT. Without `aot`, it builds with the Mono interpreter.
- After a build, `make -C switch2/host` relinks other modes: `JIT=1`, `AOT=1 JIT=1`, or `TRACE=1` for all Mono log channels.
- `PKF_CONSOLE_FTP=ftp://<console>:<port> sh switch2/toolchain/console-upload.sh <folder> /` copies a folder to the console's SD card and checks every file's size.

**The headless host.** It runs the Switch 2 app in the image with no console, drives it with a script, and saves screenshots:

```sh
dotnet run --project tools/PKForge.Switch2.Headless -- --saves <dir> --data <dir> --script tools/PKForge.Switch2.Headless/scripts/home.txt --out <dir> [--seed] [--seed-restore-points] [--profile]
```

`--seed` puts test saves in `<saves>`. `--seed-restore-points` leaves the flag file the console takes too. `--profile` prints frame times and where drawing time went. The exit code is 0 when the script passed with no UI errors, 1 when a script line failed, and 2 when app code reported errors.

## Console test

1. Copy the `.nro` to `sdmc:/switch/` (SD card or OunceShell's FTP server), or send it to the homebrew loader's
   netloader: `python switch2/toolchain/netload.py <nro> [--script <name>] [--report] [--backup-saves] [--wait <s>]`.
2. Hold R while starting any game, then pick the app in hbmenu (or open the netloader there).
3. `--script <name>` runs `switch2/app/PKForge.Switch2/scripts/<name>.txt` on the console's real saves (smoke, games,
   pages and box only read); `--report` sends that run's log and results to the PC, then the app closes.
4. Otherwise, copy `sdmc:/switch/PKForge/logs/*` into `switch2/findings/inbox/`. Git ignores that folder because the
   logs hold account IDs.

Phase 2 builds write console saves of Legends: Z-A only, after copying the original to
`sdmc:/switch/PKForge/save-backups/`. `--backup-saves` copies every Pokémon save there, and
`switch2/toolchain/pull-backups.py` downloads the newest copy and checks it.

## Pins

| What | Version |
|---|---|
| Base image | `devkitpro/devkita64:20260219` |
| ounce-devkit | `5e957599bd498643d82e5470aec80da67a12685d` |
| mono-nx | `285b277de16711344126cd90c2a21cfbd0d5d714` |
| exelix11/dotnet_runtime `libnx` | `289cdaa5d288809891e3e2fbac82cd6e0cdcfcd7` |
| PKForge | `c1d12b6d1b0239e57ead0c1e1add1432d24f7e3a` |
| PKHeX / PKHeX-Plugins | `0ef324a7` / `8e80759a` |
| SkiaSharp | `3.116.1` |

Our fork branches, with their patches in `runtime/patches/`:

| Fork | Branch | Head | Patches |
|---|---|---|---|
| dotnet/runtime `v10.0.15` + exelix11's port | `ounce-net10` (.NET 10, in use) | `c463b03b3478a426b4da6cb4f8a94dcd4df66029` | `net10/`: the whole series, `git am` onto `v10.0.15` (exelix11's 15 port commits, then ours) |
| mono/skia (SkiaSharp 3.116.1's pin `c16e913577`) | `ounce` | `af78b51cbae25be86ae59b6271c17a682c929147` | `../../skia/patches/skia-*`: no mmap, no dynamic loader, GPU and JPEG/WebP stubs |
| Skia's zlib (DEPS `3ca9f16`) | `ounce` | `450b5ad14ef8398b0e0983f454a6c0b83d826c68` | `../../skia/patches/zlib-0001`: static ARM CPU features |
| exelix11/dotnet_runtime | `ounce` (.NET 9) | `b1ad8058a512d1b7eeb6b5b054904fdf906ab5e6` | 0001 libounce target, 0002 Release sockets build, 0003 kaiHEN staged JIT, 0004 POSIX TLS destructors |
| exelix11/mono-nx | `ounce` | `aa693e66c57206a8f775c33f68dff9147c2b6c4d` | 0001 unbuffered file log |
