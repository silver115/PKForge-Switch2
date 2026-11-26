#!/bin/sh
# Uploads a folder to the console over its FTP server, keeping subfolders, in one curl run:
#   PKF_CONSOLE_FTP=ftp://<console>:<port> sh switch2/toolchain/console-upload.sh <local-dir> <remote-dir>
# Then checks each file's size on the console. Only upload this project's own build outputs.
set -e
: "${PKF_CONSOLE_FTP:?set PKF_CONSOLE_FTP to the console FTP server, e.g. ftp://<console IP>:<port>}"
src=$(cd "$1" && pwd)
dst=${2%/}
list=$(cd "$src" && find . -type f | sed 's|^\./||' | sort)
cfg=$(mktemp)
for f in $list; do printf 'upload-file = "%s/%s"\nurl = "%s%s/%s"\n' "$src" "$f" "$PKF_CONSOLE_FTP" "$dst" "$f"; done > "$cfg"
curl -sS --ftp-create-dirs --retry 2 -K "$cfg"
rm -f "$cfg"
bad=0
for f in $list; do
    want=$(wc -c < "$src/$f" | tr -d ' ')
    got=$(curl -sSI "$PKF_CONSOLE_FTP$dst/$f" | tr -d '\r' | sed -n 's/^Content-Length: //p')
    [ "$got" = "$want" ] || { echo "size mismatch: $dst/$f console=$got local=$want"; bad=1; }
done
echo "uploaded $(echo "$list" | wc -l | tr -d ' ') files to $dst"
exit $bad
