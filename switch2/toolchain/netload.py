"""
Sends a .nro to the console's homebrew loader (hbmenu's netloader, in nxlink's protocol), which saves and starts it.

    python switch2/toolchain/netload.py <file.nro> [--script smoke] [--report] [--address <console IP>]

--script runs a test script from switch2/app/PKForge.Switch2/scripts/ in place of a person, then quits. --report has the
app send this PC its report when it quits (switch2/host/source/run_report.c): this prints it and exits 1 if the script
failed; that run then closes the whole loader app, so the loader is started again from HOME. Without --report a run
goes back to the loader's menu, where the netloader is opened again; its results stay in sdmc:/switch/PKForge/logs/.
--address defaults to the PKF_CONSOLE_ADDRESS environment variable.
"""
import argparse
import os
import socket
import struct
import sys
import time
import zlib

CHUNK = 16384     # the loader inflates into a buffer this size: no chunk may be larger
LOG_PORT = 28771  # NXLINK_CLIENT_PORT: where a netloaded app connects back
REFUSED = {-1: "the loader could not create the file", -2: "not enough space", -3: "not enough memory"}


def recv_int(sock):
    data = b""
    while len(data) < 4:
        part = sock.recv(4 - len(data))
        if not part:
            raise ConnectionError("the loader closed the connection")
        data += part
    return struct.unpack("<i", data)[0]


def main():
    parser = argparse.ArgumentParser(description="Netload a .nro to the console's homebrew loader.")
    parser.add_argument("nro")
    parser.add_argument("--address", default=os.environ.get("PKF_CONSOLE_ADDRESS"),
                        required="PKF_CONSOLE_ADDRESS" not in os.environ, help="the console's IP address")
    parser.add_argument("--port", type=int, default=28280)
    # shortcut: the live log starts the console's sockets while Mono runs, and PKForge then dies on Home (Mono's
    # "Cannot transition thread ... DO_BLOCKING"); use it once that is fixed. --report sends after Mono has finished.
    parser.add_argument("--log", action="store_true", help="print the app's log until it closes (crashes PKForge for now)")
    parser.add_argument("--script", help="run this test script (switch2/app/PKForge.Switch2/scripts/<name>.txt), then quit")
    parser.add_argument("--backup-saves", action="store_true",
                        help="copy every Pokémon save to sdmc:/switch/PKForge/save-backups, then quit (pull-backups.py fetches it)")
    parser.add_argument("--report", action="store_true", help="print the run's report when it quits; exit 1 if its script failed")
    parser.add_argument("--run-timeout", type=float, default=600, help="seconds to wait for the run's report")
    parser.add_argument("--wait", type=float, default=120, help="seconds to keep trying while the loader is not listening")
    args = parser.parse_args()

    with open(args.nro, "rb") as file:
        data = file.read()
    name = os.path.basename(args.nro)
    listener = None
    if args.log or args.report:
        # Listening before the app starts, so its first lines are not lost.
        listener = socket.socket()
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("", LOG_PORT))
        listener.listen(1)

    # Only a real send connects: a connection that sends nothing (a port check) leaves the loader showing
    # "Error getting name length: err=11" until it is opened again.
    deadline = time.monotonic() + args.wait
    while True:
        try:
            sock = socket.create_connection((args.address, args.port), timeout=60)
            break
        except (ConnectionRefusedError, socket.timeout, OSError):
            if time.monotonic() > deadline:
                sys.exit(f"The loader at {args.address}:{args.port} is not listening.")
            time.sleep(2)
    started = time.monotonic()
    with sock:
        sock.sendall(struct.pack("<i", len(name)) + name.encode() + struct.pack("<i", len(data)))
        response = recv_int(sock)
        if response != 0:
            sys.exit(REFUSED.get(response, f"the loader refused the file ({response})"))
        packer = zlib.compressobj()
        sent = 0
        step = 1 << 20
        for offset in range(0, len(data), step):
            packed = packer.compress(data[offset:offset + step])
            if offset + step >= len(data):
                packed += packer.flush()
            for i in range(0, len(packed), CHUNK):
                piece = packed[i:i + CHUNK]
                sock.sendall(struct.pack("<i", len(piece)) + piece)
                sent += len(piece)
        recv_int(sock)
        # argv: the file's path, then the app's switches. The loader adds this PC's address itself
        # ("<8 hex digits>_NXLINK_") on every netload.
        argv = f"sdmc:/switch/{name}\0".encode()
        if args.log:
            argv += b"--live-log\0"
        if args.script:
            argv += f"--script\0{args.script}\0".encode()
        if args.report:
            argv += b"--report\0"
        if args.backup_saves:
            argv += b"--backup-saves\0"
        sock.sendall(struct.pack("<i", len(argv)) + argv)
    print(f"Sent {name}: {len(data):,} bytes as {sent:,} ({sent * 100 // len(data)} %) in {time.monotonic() - started:.1f} s",
          flush=True)

    if args.log:
        listener.settimeout(120)
        try:
            log, _ = listener.accept()
        except socket.timeout:
            sys.exit("The app did not connect back within 2 minutes (a build without the live log, or a firewall).")
        connected = time.monotonic()
        with log:
            while chunk := log.recv(4096):
                sys.stdout.write(chunk.decode("utf-8", "replace"))
                sys.stdout.flush()
        print(f"--- the app closed the log after {time.monotonic() - connected:.1f} s ---")

    if args.report:
        listener.settimeout(args.run_timeout)
        try:
            report, _ = listener.accept()
        except socket.timeout:
            sys.exit("No report: the run did not finish in time (did it crash?).")
        with report:
            length = recv_int(report)
            text = b""
            while len(text) < length and (part := report.recv(length - len(text))):
                text += part
        text = text.decode("utf-8", "replace")
        print(text, end="")
        if args.script and f"script.{args.script}=pass" not in text:
            sys.exit(1)


if __name__ == "__main__":
    main()
