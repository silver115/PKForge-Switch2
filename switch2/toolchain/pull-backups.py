"""
Downloads the newest save backup (netload.py --backup-saves, or the copy made before a first write) from the console's
FTP server into switch2/findings/inbox/save-backups/<folder>/, and checks every file against its manifest.txt.

    python switch2/toolchain/pull-backups.py [--address <console IP>] [--port 1337]

Prints "ok <n> files", or each file that is missing or differs, and exits 1 if any does. git ignores the inbox: the
folders hold the console's account ids. --address defaults to the PKF_CONSOLE_ADDRESS environment
variable.
"""
import argparse
import ftplib
import hashlib
import os
import sys

REMOTE = "/switch/PKForge/save-backups"
LOCAL = os.path.join(os.path.dirname(__file__), "..", "findings", "inbox", "save-backups")


def names(ftp, path):
    return [os.path.basename(name.rstrip("/")) for name in ftp.nlst(path)]


def is_folder(ftp, path):
    here = ftp.pwd()
    try:
        ftp.cwd(path)
        return True
    except ftplib.error_perm:
        return False
    finally:
        ftp.cwd(here)


def download(ftp, remote, local):
    os.makedirs(local, exist_ok=True)
    for name in names(ftp, remote):
        source = f"{remote}/{name}"
        # The console writes names the SD card refuses (":", non-ASCII) as %XX; the folders here have none of those.
        target = os.path.join(local, name)
        if is_folder(ftp, source):
            download(ftp, source, target)
        else:
            with open(target, "wb") as file:
                ftp.retrbinary(f"RETR {source}", file.write)


def check(folder):
    """The manifest's lines that do not match the files on the PC."""
    problems = []
    with open(os.path.join(folder, "manifest.txt"), encoding="utf-8") as manifest:
        lines = [line.rstrip("\n") for line in manifest if line.strip()]
    for line in lines:
        digest, size, relative = line.split("  ", 2)
        path = os.path.join(folder, *relative.split("/"))
        if not os.path.exists(path):
            problems.append(f"missing {relative}")
            continue
        with open(path, "rb") as file:
            data = file.read()
        if len(data) != int(size) or hashlib.sha256(data).hexdigest() != digest:
            problems.append(f"differs {relative}")
    return len(lines), problems


def main():
    parser = argparse.ArgumentParser(description="Download and check the newest console save backup.")
    parser.add_argument("--address", default=os.environ.get("PKF_CONSOLE_ADDRESS"),
                        required="PKF_CONSOLE_ADDRESS" not in os.environ, help="the console's IP address")
    parser.add_argument("--port", type=int, default=1337)
    args = parser.parse_args()

    ftp = ftplib.FTP()
    ftp.connect(args.address, args.port, timeout=30)
    ftp.login()
    ftp.set_pasv(True)
    dated = sorted(name for name in names(ftp, REMOTE) if name[:8].isdigit())
    if not dated:
        sys.exit(f"No backup in {REMOTE} on the console.")
    newest = dated[-1]
    local = os.path.abspath(os.path.join(LOCAL, newest))
    download(ftp, f"{REMOTE}/{newest}", local)
    ftp.quit()

    count, problems = check(local)
    for problem in problems:
        print(problem)
    if problems:
        sys.exit(1)
    print(f"ok {count} files in {local}")


if __name__ == "__main__":
    main()
