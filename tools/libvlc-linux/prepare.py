#!/usr/bin/env python3
"""Build a relocatable VLC 3 runtime from signed Ubuntu 22.04 packages, without root."""
import argparse
import fcntl
import getpass
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


# The host supplies glibc and its loader; ship the remaining ELF dependencies.
HOST_LIBRARIES = {
    "libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1",
    "libresolv.so.2", "libutil.so.1", "ld-linux-x86-64.so.2",
}


def run(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT)


def prepare(destination):
    fingerprint = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    marker = destination / "manifest.json"
    if marker.exists() and json.loads(marker.read_text()).get("builder") == fingerprint:
        print(f"Using cached Linux libVLC: {destination}", flush=True)
        return
    for command in ("apt-get", "dpkg-deb", "readelf"):
        if not shutil.which(command):
            raise RuntimeError(f"Linux libVLC packaging requires {command} (Ubuntu: apt, dpkg, binutils).")
    keyring = Path("/usr/share/keyrings/ubuntu-archive-keyring.gpg")
    if not keyring.is_file():
        raise RuntimeError("Install ubuntu-keyring to verify the Ubuntu libVLC packages, or publish on Ubuntu 22.04+.")

    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="libvlc-", dir=destination.parent) as temporary:
        work = Path(temporary).resolve()
        for directory in ("lists/partial", "archives/partial", "logs", "root", "bundle"):
            (work / directory).mkdir(parents=True, exist_ok=True)
        (work / "status").touch()
        (work / "sources.list").write_text("\n".join(
            f"deb [arch=amd64 signed-by={keyring}] https://{server}/ubuntu {suite} main universe"
            for server, suite in (("archive.ubuntu.com", "jammy"),
                                  ("archive.ubuntu.com", "jammy-updates"),
                                  ("security.ubuntu.com", "jammy-security"))
        ) + "\n")
        apt = ["apt-get", "-o", f"Dir::Etc::sourcelist={work / 'sources.list'}",
               "-o", "Dir::Etc::sourceparts=-", "-o", f"Dir::State::lists={work / 'lists'}",
               "-o", f"Dir::State::status={work / 'status'}",
               "-o", f"Dir::Cache::archives={work / 'archives'}", "-o", f"Dir::Log={work / 'logs'}",
               "-o", f"Dir::Cache::pkgcache={work / 'pkgcache.bin'}",
               "-o", f"Dir::Cache::srcpkgcache={work / 'srcpkgcache.bin'}",
               "-o", "APT::Architecture=amd64", "-o", "APT::Architectures::=amd64",
               "-o", f"APT::Sandbox::User={getpass.getuser()}", "-o", "Debug::NoLocking=true",
               "-o", "Acquire::Languages=none", "-o", "Acquire::Retries=3"]
        print("Downloading verified Ubuntu 22.04 libVLC and codec dependencies (no system installation)...", flush=True)
        print(run(*apt, "update"), flush=True)
        print(run(*apt, "--yes", "--download-only", "--no-install-recommends", "--no-install-suggests",
                  "install", "libvlc5", "vlc-plugin-base", "patchelf"), flush=True)
        root, bundle = work / "root", work / "bundle"
        packages = []
        for archive in sorted((work / "archives").glob("*.deb")):
            fields = run("dpkg-deb", "--field", str(archive))
            field = lambda name: re.search(rf"^{name}: (.+)$", fields, re.MULTILINE).group(1)
            packages.append({"package": field("Package"), "version": field("Version"),
                             "source": field("Source") if re.search(r"^Source:", fields, re.MULTILINE) else field("Package"),
                             "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()})
            run("dpkg-deb", "--extract", str(archive), str(root))

        libraries = {}
        for library in sorted(root.rglob("*.so*")):
            if library.is_file() and "/vlc/plugins/" not in str(library):
                libraries.setdefault(library.name, library)
        patchelf = root / "usr/bin/patchelf"
        pending = [(libraries["libvlc.so.5"], bundle / "libvlc.so.5"),
                   (libraries["libvlccore.so.9"], bundle / "libvlccore.so.9")]
        plugins = root / "usr/lib/x86_64-linux-gnu/vlc/plugins"
        # libvlccore discovers <its directory>/vlc/plugins through dladdr().
        # Keep this layout so native code needs no environment changes at runtime.
        pending.extend((plugin, bundle / "vlc/plugins" / plugin.relative_to(plugins))
                       for plugin in sorted(plugins.rglob("*.so")))
        if not plugins.is_dir() or not any(plugins.rglob("*avcodec*")) or not any(plugins.rglob("*avformat*")):
            raise RuntimeError("Downloaded VLC packages do not contain the required video decoder/demuxer.")
        copied = set()
        while pending:
            source, target = pending.pop()
            if target in copied:
                continue
            copied.add(target)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target, follow_symlinks=True)
            target.chmod(0o755)
            dependencies = re.findall(r"\(NEEDED\).*\[(.+?)\]", run("readelf", "--dynamic", str(source)))
            for name in dependencies:
                if name in HOST_LIBRARIES:
                    continue
                if name not in libraries:
                    raise RuntimeError(f"Missing bundled dependency {name} required by {source.name}")
                pending.append((libraries[name], bundle / name))
            relative = os.path.relpath(bundle, target.parent)
            run(str(patchelf), "--set-rpath", "$ORIGIN" if relative == "." else f"$ORIGIN/{relative}", str(target))
        # The managed binding imports 'libvlc', not its versioned SONAME.
        (bundle / "libvlc.so").symlink_to("libvlc.so.5")
        (bundle / "libvlccore.so").symlink_to("libvlccore.so.9")

        # Preserve upstream redistribution notices and exact package/source versions.
        notices = bundle / "licenses"
        shutil.copytree(root / "usr/share/doc", notices / "packages", symlinks=False, ignore_dangling_symlinks=True)
        common = root / "usr/share/common-licenses"
        if common.exists():
            shutil.copytree(common, notices / "common-licenses")
        (notices / "README.txt").write_text(
            "Linux libVLC and dependencies from signed Ubuntu 22.04 (jammy, updates and security) packages.\n"
            "Exact binary/source package versions and SHA-256 hashes are in ../manifest.json.\n"
            "Corresponding source packages: https://launchpad.net/ubuntu/jammy/+source/<source-package>\n"
            "Replace <source-package> with the source name in the manifest (without its version suffix).\n"
            "Local modification: ELF RUNPATH rewritten relative to each library ($ORIGIN).\n"
            "glibc and the ELF loader are supplied by the host, not this bundle.\n")
        (bundle / "manifest.json").write_text(json.dumps({"builder": fingerprint, "baseline": "Ubuntu 22.04 amd64",
                                                        "packages": packages}, indent=2) + "\n")
        if destination.exists():
            shutil.rmtree(destination)
        shutil.move(bundle, destination)
        print(f"Bundled Linux libVLC: {len(copied)} libraries/plugins in {destination}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        destination = args.output.resolve()
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Application and smoke-test builds can prepare the shared cache in
        # parallel. Only one process may replace it; waiters reuse its manifest.
        with destination.with_suffix(".lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            prepare(destination)
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.output) from error
