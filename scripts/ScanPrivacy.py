"""Scan distributable source, staged files, reachable Git history and optional ZIP.
Print rule names and counts, never matched values. This is a release guard, not a
replacement for reviewing the exact files being published.
"""
import argparse
import pathlib
import re
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
RULES = {
    "steam-identifier": re.compile(r"(?<!\d)\d{17}(?!\d)"),
    "api-key-shape": re.compile(r"\b[0-9a-fA-F]{32}\b"),
    "discord-destination": re.compile(r"https?://(?:canary\.|ptb\.)?discord(?:app)?\.com/api/webhooks/[^\s\x22\x27>]+", re.I),
    "personal-home-path": re.compile(r"[A-Za-z]:[\\/]+Users[\\/]+(?![<%$])[^\\/\s\x22\x27]+", re.I),
    "private-key": re.compile(r"-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----"),
    "github-token": re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b"),
}
SKIP = {".git", "bin", "obj", "dist", "artifacts", ".vs", "__pycache__"}
failures = []


def run_git(*args):
    return subprocess.run(["git", "-C", str(ROOT), *args], stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, check=False)


def check(label, data):
    texts = [data.decode("utf-8", errors="ignore"), data.decode("utf-16-le", errors="ignore")]
    for rule, pattern in RULES.items():
        if any(pattern.search(text) for text in texts):
            failures.append((label, rule))


def private_name(name):
    parts = pathlib.PurePosixPath(name.replace("\\", "/")).parts
    return any(p == ".env" or p.startswith(".env.") for p in parts) or name.endswith((".conf", ".log", ".csv")) or pathlib.PurePosixPath(name).name == "settings.json"


def scan_source():
    git = run_git("rev-parse", "--show-toplevel")
    if git.returncode == 0:
        names = run_git("ls-files", "--cached", "--others", "--exclude-standard", "-z").stdout.split(b"\0")
        files = [ROOT / n.decode("utf-8") for n in names if n]
    else:
        files = [p for p in ROOT.rglob("*") if p.is_file() and not any(x in SKIP for x in p.relative_to(ROOT).parts)]
    for number, path in enumerate(files):
        relative = path.relative_to(ROOT).as_posix()
        if private_name(relative):
            failures.append(("source-file-" + str(number), "private-file-name"))
        if path.is_file():
            check("source-file-" + str(number), relative.encode("utf-8") + b"\n" + path.read_bytes())
    # Scan index bytes as well: the staged revision can differ from the working tree.
    for number, record in enumerate(run_git("ls-files", "--stage", "-z").stdout.split(b"\0")):
        if not record:
            continue
        metadata, name = record.split(b"\t", 1)
        blob = metadata.split()[1].decode("ascii")
        check("index-blob-" + str(number), name + b"\n" + run_git("cat-file", "blob", blob).stdout)
    seen = set()
    for commit in run_git("rev-list", "--all").stdout.splitlines():
        for entry in run_git("ls-tree", "-rz", commit.decode("ascii")).stdout.split(b"\0"):
            if not entry:
                continue
            metadata, name = entry.split(b"\t", 1)
            fields = metadata.split()
            if fields[1] != b"blob":
                continue
            if private_name(name.decode("utf-8")):
                failures.append(("history", "private-file-name"))
            blob = fields[2].decode("ascii")
            if blob not in seen:
                seen.add(blob)
                check("history-blob-" + str(len(seen)), name + b"\n" + run_git("cat-file", "blob", blob).stdout)
    print("Source/index/history scan:", len(files), "files;", len(seen), "historical blobs")


def scan_archive(path):
    allowed = {"SteamMonitorTray.exe", "SteamMonitorTray.Core.dll", "SteamMonitorTray.Session.exe", "Session.ps1", "InspectConfig.py", "README.md", "README.en.md", "LICENSE", "install.ps1", "uninstall.ps1"}
    with zipfile.ZipFile(path) as archive:
        actual = set()
        for number, item in enumerate(archive.infolist()):
            if item.is_dir():
                continue
            name = pathlib.PurePosixPath(item.filename).name
            actual.add(name)
            if name not in allowed:
                failures.append(("archive-entry-" + str(number), "unapproved-file"))
            check("archive-entry-" + str(number), archive.read(item))
        if actual != allowed:
            failures.append(("archive", "runtime-file-list-mismatch"))
        print("Archive scan:", len(actual), "runtime files")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=pathlib.Path)
    arguments = parser.parse_args()
    scan_source()
    if arguments.archive:
        scan_archive(arguments.archive)
    for label, rule in failures:
        print("BLOCKED", label, rule)
    print("PASS privacy scan" if not failures else "FAIL privacy scan")
    sys.exit(bool(failures))
