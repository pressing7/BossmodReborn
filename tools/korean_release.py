"""Prepare and publish pressing7's separate BossMod build. Python stdlib only.

prepare edits only the temporary Actions checkout. The upstream project name,
namespace and source files do not need permanent identity changes.
"""

import argparse
import base64
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


REPOSITORY = "pressing7/BossmodReborn"
OLD_NAME = "BossModReborn"
NAME = "BossModRebornKR"
OUT = Path("release-output")


def version_tuple(value):
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", value):
        raise ValueError(f"Invalid four-part version: {value}")
    parts = tuple(map(int, value.split(".")))
    if any(n >= 65535 for n in parts):
        raise ValueError("Assembly version components must be below 65535.")
    return parts


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def prepare():
    run = int(os.environ["GITHUB_RUN_NUMBER"])
    attempt = int(os.environ["GITHUB_RUN_ATTEMPT"])
    # Monotonic, including manual retries; four numeric assembly components.
    version = f"1.{run // 65000}.{run % 65000}.{attempt}"
    version_tuple(version)
    project = Path("BossMod/BossModReborn.csproj")
    source = project.read_text(encoding="utf-8-sig")
    root = ET.fromstring(source)
    if root.findtext(".//AssemblyName") != OLD_NAME:
        raise ValueError("Upstream AssemblyName changed; review the KR build adapter.")
    # Preserve RootNamespace: obstacle resources are loaded by their old prefix.
    namespace = root.findtext(".//RootNamespace") or OLD_NAME
    source, count = re.subn(
        r"<AssemblyName>BossModReborn</AssemblyName>",
        f"<AssemblyName>{NAME}</AssemblyName>", source,
    )
    if count != 1:
        raise ValueError("Unexpected AssemblyName declaration.")
    if root.find(".//RootNamespace") is None:
        source = source.replace(
            f"<AssemblyName>{NAME}</AssemblyName>",
            f"<AssemblyName>{NAME}</AssemblyName>\n  <RootNamespace>{namespace}</RootNamespace>",
        )
    # Upstream CleanupOutput otherwise deletes the renamed DLL after compiling.
    old_exclusion = "$(OutputPath)BossModReborn.*;"
    if source.count(old_exclusion) != 1:
        raise ValueError("Upstream cleanup rules changed; review before releasing.")
    source = source.replace(old_exclusion, "$(OutputPath)BossModRebornKR.*;")
    project.write_text(source, encoding="utf-8")
    manifest_path = Path("BossMod/BossModReborn.json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    manifest.update({
        "InternalName": NAME,
        "Name": "BossMod Reborn KR",
        "Author": "The Combat Reborn Team; pressing7 (KR modifications)",
        "RepoUrl": f"https://github.com/{REPOSITORY}",
        "Punchline": "pressing7's custom DMU strategy build",
        "AcceptsFeedback": False,
    })
    # ApiLevel is retained from the real source manifest, never fabricated.
    write_json(Path(f"BossMod/{NAME}.json"), manifest)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
        stream.write(f"version={version}\n")
    print(f"Prepared {NAME} {version}; Dalamud API {manifest['DalamudApiLevel']}")


def package(version):
    version_tuple(version)
    build = Path("build")
    manifest_file = build / f"{NAME}.json"
    manifest = json.loads(manifest_file.read_text(encoding="utf-8-sig"))
    if manifest.get("InternalName") != NAME or manifest.get("AssemblyVersion") != version:
        raise ValueError("Built manifest identity/version mismatch.")
    if not isinstance(manifest.get("DalamudApiLevel"), int) or manifest["DalamudApiLevel"] <= 0:
        raise ValueError("Built manifest has no valid DalamudApiLevel.")
    required = [f"{NAME}.dll", f"{NAME}.json", "DefaultRotationPresets.json", "RebornPresets.json"]
    for filename in required:
        if not (build / filename).is_file():
            raise FileNotFoundError(build / filename)
    # Follow upstream packaging; do not bundle Dalamud/FFXIVClientStructs DLLs.
    files = [build / filename for filename in required]
    for filename in (f"{NAME}.pdb", "PInvoke.User32.dll"):
        if (build / filename).is_file():
            files.append(build / filename)
    OUT.mkdir(exist_ok=True)
    with zipfile.ZipFile(OUT / "latest.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, path.name)
    link = f"https://github.com/{REPOSITORY}/releases/download/kr-{version}/latest.zip"
    entry = dict(manifest)
    entry.update({"DownloadLinkInstall": link, "DownloadLinkUpdate": link,
                  "LastUpdate": str(int(time.time())),
                  "Changelog": f"KR build {version}; source {os.environ.get('GITHUB_SHA', 'local')}"})
    for key in ("DownloadLinkTesting", "TestingAssemblyVersion", "TestingDalamudApiLevel", "TestingChangelog"):
        entry.pop(key, None)
    write_json(OUT / "pluginmaster.json", [entry])
    print(f"Packaged {len(files)} files into {OUT / 'latest.zip'}")


def api(method, path, data=None, missing_ok=False):
    payload = None if data is None else json.dumps(data).encode()
    request = urllib.request.Request(
        f"https://api.github.com/repos/{REPOSITORY}/{path}", data=payload, method=method,
        headers={"Authorization": f"Bearer {os.environ['GH_TOKEN']}",
                 "Accept": "application/vnd.github+json", "Content-Type": "application/json",
                 "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "pressing7-kr-release"},
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code == 404 and missing_ok:
            return None
        raise RuntimeError(f"GitHub {method} {path}: HTTP {error.code}") from None


def publish(version):
    version_tuple(version)
    if os.environ.get("GITHUB_REPOSITORY") != REPOSITORY or os.environ.get("GITHUB_REF") != "refs/heads/Korean":
        raise ValueError("Publishing is only configured for pressing7/BossmodReborn:Korean.")
    entries = json.loads((OUT / "pluginmaster.json").read_text(encoding="utf-8"))
    if len(entries) != 1 or entries[0]["InternalName"] != NAME or entries[0]["AssemblyVersion"] != version:
        raise ValueError("Unexpected release entry.")
    archive = OUT / "latest.zip"
    with zipfile.ZipFile(archive) as package_zip:
        embedded = json.loads(package_zip.read(f"{NAME}.json"))
        if embedded.get("AssemblyVersion") != version or embedded.get("InternalName") != NAME:
            raise ValueError("ZIP and repository entry differ.")
    # Read immutable commits so a concurrent dist update cannot be overwritten.
    old_ref = api("GET", "git/ref/heads/dist", missing_ok=True)
    old_sha = old_ref["object"]["sha"] if old_ref else None
    tree_base = None
    existing = []
    if old_sha:
        tree_base = api("GET", f"git/commits/{old_sha}")["tree"]["sha"]
        old_file = api("GET", f"contents/pluginmaster.json?ref={old_sha}", missing_ok=True)
        if old_file:
            existing = json.loads(base64.b64decode(old_file["content"]))
            if not isinstance(existing, list):
                raise ValueError("dist/pluginmaster.json must be an array.")
    for entry in existing:
        if entry.get("InternalName") == NAME and version_tuple(entry["AssemblyVersion"]) >= version_tuple(version):
            raise ValueError("Refusing to publish a version older than or equal to the installed feed.")
    tag = f"kr-{version}"
    if api("GET", f"releases/tags/{tag}", missing_ok=True):
        raise ValueError("This release already exists. Re-run the workflow for a new version.")
    # gh uploads the ZIP before publishing the release. No replacement of old assets.
    subprocess.run([
        "gh", "release", "create", tag, str(archive), "--repo", REPOSITORY,
        "--target", os.environ["GITHUB_SHA"], "--title", f"BossMod Reborn KR {version}",
        "--notes", f"Source: {os.environ['GITHUB_SHA']}\nDMU KR strategy build. Original: FFXIV-CombatReborn/BossmodReborn.",
    ], check=True)
    # Only expose the new version after the install archive exists.
    merged = [entry for entry in existing if entry.get("InternalName") != NAME] + entries
    tree_request = {"tree": [{"path": "pluginmaster.json", "mode": "100644", "type": "blob",
                              "content": json.dumps(merged, ensure_ascii=False, indent=2) + "\n"}]}
    if tree_base:
        tree_request["base_tree"] = tree_base
    tree = api("POST", "git/trees", tree_request)
    commit = api("POST", "git/commits", {"message": f"Publish KR {version}", "tree": tree["sha"],
                                         "parents": [old_sha] if old_sha else []})
    if old_sha:
        api("PATCH", "git/refs/heads/dist", {"sha": commit["sha"], "force": False})
    else:
        api("POST", "git/refs", {"ref": "refs/heads/dist", "sha": commit["sha"]})
    url = f"https://raw.githubusercontent.com/{REPOSITORY}/dist/pluginmaster.json"
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as stream:
        stream.write(f"Published **{NAME} {version}**.\n\nDalamud repository: `{url}`\n")
    print(f"Published {NAME} {version} and updated dist/pluginmaster.json.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["prepare", "package", "publish"])
    parser.add_argument("--version")
    args = parser.parse_args()
    if args.command == "prepare":
        prepare()
    elif not args.version:
        parser.error("--version is required for package/publish")
    elif args.command == "package":
        package(args.version)
    else:
        publish(args.version)
