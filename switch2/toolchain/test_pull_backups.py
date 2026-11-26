"""pull-backups.py's manifest check: python switch2/toolchain/test_pull_backups.py"""
import hashlib
import importlib.util
import os
import tempfile

spec = importlib.util.spec_from_file_location("pull_backups", os.path.join(os.path.dirname(__file__), "pull-backups.py"))
pull = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pull)

with tempfile.TemporaryDirectory() as folder:
    save = os.path.join(folder, "0100F43008C44000 Legends Z-A", "0001")
    os.makedirs(save)
    for name, data in (("main", b"za main"), ("backup", b"za backup")):
        with open(os.path.join(save, name), "wb") as file:
            file.write(data)
    with open(os.path.join(folder, "manifest.txt"), "w", encoding="utf-8") as manifest:
        for name, data in (("main", b"za main"), ("backup", b"za backup"), ("gone", b"x")):
            manifest.write(f"{hashlib.sha256(data).hexdigest()}  {len(data)}  0100F43008C44000 Legends Z-A/0001/{name}\n")
    with open(os.path.join(save, "backup"), "wb") as file:
        file.write(b"za backuP")

    count, problems = pull.check(folder)

    assert count == 3, count
    assert problems == ["differs 0100F43008C44000 Legends Z-A/0001/backup", "missing 0100F43008C44000 Legends Z-A/0001/gone"], problems
print("pull-backups check ok")
