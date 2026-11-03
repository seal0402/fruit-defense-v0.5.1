#!/usr/bin/env python3
"""Create a source/assets ZIP and an optional Windows offline ZIP with integrity reports."""
import argparse
from datetime import datetime, timezone, timedelta
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parent.parent
IGNORED_NAMES = {".godot","bin","obj",".local",".git",".vs","__pycache__","历史版本"}
GENERATED_TOOLS = {"dotnet","godot","downloads"}
GENERATED_ROOT_FILES = {"MANIFEST.sha256","SOURCE_SNAPSHOT.json"}

def excluded(path):
    rel = path.relative_to(ROOT)
    if any(part in IGNORED_NAMES for part in rel.parts): return True
    if len(rel.parts)>1 and rel.parts[0]=="tools" and rel.parts[1] in GENERATED_TOOLS: return True
    return rel.as_posix() in GENERATED_ROOT_FILES

def sha256(path):
    h=hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda:f.read(1024*1024),b""): h.update(block)
    return h.hexdigest()

def build():
    parser=argparse.ArgumentParser()
    parser.add_argument("--out",required=True)
    parser.add_argument("--offline-dir")
    parser.add_argument("--version", default="0.5.1")
    parser.add_argument("--label")
    args=parser.parse_args()
    out=Path(args.out).resolve()
    if out.is_relative_to(ROOT) and out.relative_to(ROOT).parts[0] != "历史版本":
        raise SystemExit("Output must be outside the source project.")
    out.mkdir(parents=True,exist_ok=True)
    date=datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d")
    label=args.label or f"保卫水果_{args.version}_{date}"
    if any(c in label for c in '/\\:*?"<>|') or label in (".",".."):
        raise SystemExit("Invalid archive label.")
    source=out/(label+"_源码与素材.zip")
    offline=out/(label+"_Windows完整离线开发.zip")
    if source.exists() or (args.offline_dir and offline.exists()):
        raise SystemExit("Output archives already exist; use a new output directory.")
    files=[]
    directories=[]
    for p in sorted(ROOT.rglob("*")):
        if excluded(p): continue
        if p.is_symlink():
            raise SystemExit(f"Inspect symbolic link before packaging: {p}")
        if p.is_file(): files.append(p)
        elif p.is_dir(): directories.append(p)
    snapshot={
        "gameVersion":args.version,"handoffVersion":2,
        "createdAt":datetime.now(timezone(timedelta(hours=8))).isoformat(),
        "sourceFiles":len(files),"sourceBytes":sum(p.stat().st_size for p in files),
        "omitted":[".godot","bin","obj",".local",".git",".vs","__pycache__","历史版本","tools/dotnet","tools/godot","tools/downloads"],
        "includes":["complete C# sources","existing assets and source references","prompts and requirements","tests and records","handoff documents","portable scripts","Godot NuGet packages"],
        "implemented":["3 chapters and 9 stages","3 difficulties with relaxed balance","3 hero charge skills","stackable upgrade cards","boss mechanics","local saves and chapter checkpoints","portrait mouse/touch controls","barrier-based star rating"],
        "verifiedChecks":{"domain":45,"godotInput":27},
        "notIncludedAsCompleted":["mobile installer","formal integrated artwork","production home page artwork","real lobby video and background music integration"],
        "assetNote":"Assets under production are captured as found; this snapshot does not claim all character states are complete. External sibling download libraries are not project runtime dependencies."
    }
    snapshot_data=(json.dumps(snapshot,ensure_ascii=False,indent=2)+"\n").encode("utf-8")
    manifest=[]
    with zipfile.ZipFile(source,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p in directories:
            z.writestr("FruitDefense/"+p.relative_to(ROOT).as_posix()+"/",b"")
        for p in files:
            data=p.read_bytes()
            name=p.relative_to(ROOT).as_posix()
            manifest.append(f"{hashlib.sha256(data).hexdigest()}  {name}")
            z.writestr("FruitDefense/"+name,data)
        z.writestr("FruitDefense/SOURCE_SNAPSHOT.json",snapshot_data)
        manifest.append(f"{hashlib.sha256(snapshot_data).hexdigest()}  SOURCE_SNAPSHOT.json")
        manifest_data=("\n".join(manifest)+"\n").encode("utf-8")
        z.writestr("FruitDefense/MANIFEST.sha256",manifest_data)
    with zipfile.ZipFile(source) as z:
        if bad:=z.testzip(): raise SystemExit(f"Source ZIP CRC failure: {bad}")
    reports=[{"file":source.name,"bytes":source.stat().st_size,"sha256":sha256(source)}]
    if args.offline_dir:
        offline_dir=Path(args.offline_dir).resolve()
        metadata=json.loads((ROOT/"tools/platform-manifest.json").read_text(encoding="utf-8-sig"))
        profile=metadata["platforms"]["win-x64"]
        archives=[]
        for component in ("godot","dotnet"):
            path=offline_dir/profile[component]["name"]
            h=hashlib.sha512()
            with path.open("rb") as f:
                for block in iter(lambda:f.read(1024*1024),b""):h.update(block)
            if h.hexdigest()!=profile[component]["sha512"]:
                raise SystemExit(f"Official tool SHA512 mismatch: {path.name}")
            archives.append(path)
        with zipfile.ZipFile(offline,"w",compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z, zipfile.ZipFile(source) as src:
            for entry in src.infolist():
                z.writestr(entry.filename,src.read(entry.filename))
            for archive in archives:
                z.write(archive,"offline-tools/"+archive.name,compress_type=zipfile.ZIP_STORED)
            z.writestr("offline-tools/README.txt",
                "Windows x64 only. Keep offline-tools next to FruitDefense. Run FruitDefense/初始化开发环境.cmd.\n"
                "Contains unchanged official Godot .NET and .NET SDK archives; verify SHA512 using platform-manifest.json.\n")
        with zipfile.ZipFile(offline) as z:
            if bad:=z.testzip():raise SystemExit(f"Offline ZIP CRC failure: {bad}")
        reports.append({"file":offline.name,"bytes":offline.stat().st_size,"sha256":sha256(offline)})
    (out/"打包清单.json").write_text(json.dumps({"archives":reports,"snapshot":snapshot},ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    (out/"SHA256SUMS.txt").write_text("".join(f"{r['sha256']}  {r['file']}\n" for r in reports),encoding="utf-8")
    print(json.dumps(reports,ensure_ascii=False),flush=True)

if __name__=="__main__": build()
