#!/usr/bin/env python3
"""Install exact official tools locally; no administrator rights or global PATH edits."""
import argparse
import hashlib
import inspect
import json
import os
import platform
from pathlib import Path
import stat
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"

def digest(path):
    h = hashlib.sha512()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def platform_id():
    system = platform.system()
    arch = platform.machine().lower()
    arch = {"amd64": "x64", "x86_64": "x64", "aarch64": "arm64", "arm64": "arm64"}.get(arch, arch)
    family = {"Windows": "win", "Linux": "linux", "Darwin": "osx"}.get(system)
    if not family:
        raise RuntimeError(f"Unsupported OS: {system}")
    return f"{family}-{arch}"

def safe_target(base, name):
    candidate = (base / name).resolve()
    if not candidate.is_relative_to(base.resolve()):
        raise RuntimeError(f"Unsafe archive path: {name}")
    return candidate

def extract(archive, destination):
    destination.mkdir(parents=True, exist_ok=True)
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as z:
            for info in z.infolist():
                safe_target(destination, info.filename)
                if stat.S_ISLNK(info.external_attr >> 16):
                    raise RuntimeError(f"Archive symlink requires manual inspection: {info.filename}")
            z.extractall(destination)
            if os.name != "nt":
                for info in z.infolist():
                    mode = (info.external_attr >> 16) & 0o777
                    if mode:
                        safe_target(destination, info.filename).chmod(mode)
    else:
        with tarfile.open(archive) as tar:
            for item in tar.getmembers():
                target = safe_target(destination, item.name)
                if item.issym():
                    safe_target(destination, str(target.parent.relative_to(destination.resolve()) / item.linkname))
                elif item.islnk():
                    safe_target(destination, item.linkname)
                elif item.isdev() or item.isfifo():
                    raise RuntimeError(f"Unexpected special file: {item.name}")
            if "filter" in inspect.signature(tar.extractall).parameters:
                tar.extractall(destination, filter="data")
            else:
                tar.extractall(destination)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--download", action="store_true", help="Allow downloads from pinned official URLs")
    parser.add_argument("--check-only", action="store_true", help="Print detected platform and expected tools only")
    args = parser.parse_args()
    manifest = json.loads((TOOLS / "platform-manifest.json").read_text(encoding="utf-8-sig"))
    rid = platform_id()
    if rid not in manifest["platforms"]:
        raise RuntimeError(f"No pinned tools for {rid}; inspect official platform support.")
    profile = manifest["platforms"][rid]
    print(f"Detected {rid}; Godot {manifest['godotVersion']} .NET; SDK {manifest['dotnetVersion']}")
    if args.check_only:
        print(json.dumps(profile, indent=2))
        return
    for component in ("dotnet", "godot"):
        spec = profile[component]
        target = TOOLS / component
        marker = target / ".fruitdefense-ready"
        if marker.exists():
            print(f"Already initialized: {component}")
            continue
        if target.exists() and any(target.iterdir()):
            raise RuntimeError(f"Existing non-empty tool directory has no marker: {target}. Inspect it; nothing was deleted.")
        options = [ROOT.parent / "offline-tools" / spec["name"], TOOLS / "downloads" / spec["name"]]
        archive = next((p for p in options if p.is_file()), None)
        if archive is None:
            if not args.download:
                raise RuntimeError(f"Archive missing: {spec['name']}. Retry with --download or provide offline-tools.")
            archive = options[1]
            archive.parent.mkdir(parents=True, exist_ok=True)
            temporary = archive.with_name(archive.name + ".part")
            print(f"Downloading official {component}: {spec['url']}", flush=True)
            request = urllib.request.Request(spec["url"], headers={"User-Agent": "FruitDefense-Setup"})
            with urllib.request.urlopen(request, timeout=90) as response, temporary.open("wb") as out:
                while chunk := response.read(1024 * 1024):
                    out.write(chunk)
            if digest(temporary) != spec["sha512"]:
                raise RuntimeError(f"Downloaded SHA512 mismatch: {temporary}")
            temporary.replace(archive)
        print(f"Verifying SHA512: {archive.name}", flush=True)
        if digest(archive) != spec["sha512"]:
            raise RuntimeError(f"SHA512 mismatch: {archive}")
        extract(archive, target)
        if os.name != "nt":
            for binary in target.rglob("*"):
                if binary.is_file() and (binary.name == "dotnet" or binary.name.startswith("Godot_v") or binary.as_posix().endswith("/Contents/MacOS/Godot")):
                    binary.chmod(binary.stat().st_mode | 0o755)
        marker.write_text(manifest["metadataDate"], encoding="ascii")
    print("Tools initialized locally. Next: project.sh doctor (Linux/macOS) or Project.ps1 -Action Doctor (Windows).")

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        raise SystemExit(f"Setup failed: {error}")
