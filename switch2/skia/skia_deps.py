# Fetches only the Skia third-party sources a CPU-raster libSkiaSharp needs, at the revisions DEPS pins.
#   python3 skia_deps.py /deps/skia
import os, re, subprocess, sys

skia = sys.argv[1]
wanted = ['freetype', 'libpng', 'zlib', 'expat', 'wuffs', 'brotli']
deps = open(os.path.join(skia, 'DEPS')).read()
for name in wanted:
    m = re.search(r'^\s*"third_party/externals/%s"\s*:\s*"([^@"]+)@([0-9a-f]+)"' % re.escape(name), deps, re.M)
    assert m, name
    url, rev = m.group(1), m.group(2)
    dest = os.path.join(skia, 'third_party', 'externals', name)
    if not os.path.isdir(os.path.join(dest, '.git')):
        subprocess.run(['git', 'init', '-q', dest], check=True)
        subprocess.run(['git', '-C', dest, 'remote', 'add', 'origin', url], check=True)
    subprocess.run(['git', '-C', dest, 'fetch', '-q', '--depth', '1', 'origin', rev], check=True)
    subprocess.run(['git', '-C', dest, 'checkout', '-q', 'FETCH_HEAD'], check=True)
    head = subprocess.run(['git', '-C', dest, 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
    print('%-8s %s %s' % (name, head[:12], 'ok' if head == rev else 'MISMATCH (pinned %s)' % rev[:12]))
