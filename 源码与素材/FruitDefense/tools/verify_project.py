#!/usr/bin/env python3
"""Verify project payload hashes; generated build caches are intentionally ignored."""
import hashlib
from pathlib import Path
import sys

root = Path(__file__).resolve().parent.parent
manifest = root / "MANIFEST.sha256"
if not manifest.exists():
    raise SystemExit("MANIFEST.sha256 missing; this may be an unpackaged working copy.")
failed = []
count = 0
for line in manifest.read_text(encoding="utf-8-sig").splitlines():
    if not line.strip(): continue
    expected, name = line.split("  ", 1)
    path = (root / name).resolve()
    if not path.is_relative_to(root.resolve()):
        failed.append(f"Unsafe path: {name}")
        continue
    if not path.is_file():
        failed.append(f"Missing: {name}")
        continue
    h = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024*1024), b""):
            h.update(block)
    if h.hexdigest() != expected:
        failed.append(f"Changed: {name}")
    count += 1
print(f"Checked {count} files; failures={len(failed)}")
for failure in failed: print(failure)
sys.exit(1 if failed else 0)
