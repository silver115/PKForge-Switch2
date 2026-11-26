#!/bin/sh
# Runs a command in the pkforge-switch2 image, with the repo at /work/pkforge and ~/pkforge-deps at /deps.
#   switch2/toolchain/run.sh make -C switch2/hello
set -e
root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$HOME/pkforge-deps"
tty=
[ -t 0 ] && tty=-it
exec docker run --rm $tty -v "$root:/work/pkforge" -v "$HOME/pkforge-deps:/deps" \
    -w /work/pkforge -e PKF_DEPS=/deps pkforge-switch2 "$@"
