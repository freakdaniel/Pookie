# SPDX-FileCopyrightText: 2026 Daniel Freak
# SPDX-License-Identifier: BSD-3-Clause
"""Exercise Pookie's X11 image offer with a real Qt Wayland/X11 consumer.

Requires Mutter's headless backend, Xwayland, dbus-run-session and a compiled
qt-image-consumer. Uses a separate compositor and never the desktop clipboard.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time

root = Path(__file__).resolve().parents[3]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--producer", type=Path, default=root / "artifacts/flatpak/login-probe/Pookie.App.SmokeTests")
parser.add_argument("--consumer", type=Path, default=root / "artifacts/qt-image-consumer")
parser.add_argument("--backend", choices=["wayland", "xcb"], default="wayland")
parser.add_argument("--private-bus", action="store_true", help=argparse.SUPPRESS)
args = parser.parse_args()
if not args.private_bus:
    sys.exit(subprocess.call(["dbus-run-session", "--", sys.executable, __file__, *sys.argv[1:], "--private-bus"]))

task = Path(tempfile.mkdtemp(prefix="pookie-isolated-clipboard-"))
env = os.environ.copy()
for key in ("SESSION_MANAGER", "AT_SPI_BUS_ADDRESS", "XAUTHORITY", "DISPLAY"):
    env.pop(key, None)
for key, suffix in [("XDG_RUNTIME_DIR", "run"), ("XDG_DATA_HOME", "data"),
                    ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache")]:
    path = task / suffix
    path.mkdir(mode=0o700)
    env[key] = str(path)
env.update(WAYLAND_DISPLAY="pookie-clipboard-test", XDG_SESSION_TYPE="wayland",
           XDG_CURRENT_DESKTOP="GNOME", GDK_BACKEND="x11", NO_AT_BRIDGE="1",
           GTK_IM_MODULE="simple", QT_IM_MODULE="compose", GIO_USE_VFS="local",
           POOKIE_ISOLATED_CLIPBOARD="1")
processes = []
success = False
try:
    with open(task / "compositor.log", "w") as log:
        compositor = subprocess.Popen([
            "mutter", "--headless", "--wayland", "--virtual-monitor=1280x900",
            "--wayland-display=pookie-clipboard-test"], env=env, stdout=log,
            stderr=subprocess.STDOUT, start_new_session=True)
    processes.append(compositor)
    deadline = time.monotonic() + 25
    while time.monotonic() < deadline:
        # Read this compositor's display instead of guessing a free display ID.
        match = re.search(r"Using public X11 display (:\d+)", (task / "compositor.log").read_text())
        auth = list((task / "run").glob("*Xwaylandauth*"))
        if match and auth and (task / "run/pookie-clipboard-test").exists():
            env.update(DISPLAY=match[1], XAUTHORITY=str(auth[0]))
            break
        if compositor.poll() is not None:
            raise RuntimeError("Private compositor exited")
        time.sleep(.1)
    else:
        raise TimeoutError("Private Xwayland did not initialize")

    files = task / "files"
    files.mkdir()
    with open(task / "producer.log", "w") as log:
        producer = subprocess.Popen([str(args.producer.resolve()),
            "--window-image-clipboard-smoke-test", str(files)], env=env,
            stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    processes.append(producer)
    for phase in range(3):
        deadline = time.monotonic() + 25
        while time.monotonic() < deadline:
            if producer.poll() is not None:
                raise RuntimeError("Producer exited early")
            ready = files / "ready"
            if ready.exists() and ready.read_text() == str(phase):
                break
            time.sleep(.05)
        else:
            raise TimeoutError("Producer phase did not start")
        qt_env = dict(env, QT_QPA_PLATFORM=args.backend)
        result = subprocess.run([str(args.consumer.resolve()), str(files / f"image-{phase}.png"),
            str(files / f"requesting-{phase}")], env=qt_env, capture_output=True,
            text=True, timeout=15)
        print(result.stdout, end="", flush=True)
        if result.returncode:
            raise RuntimeError(f"Qt paste failed ({result.returncode}): {result.stderr}")
        (files / f"done-{phase}").touch()
    if producer.wait(timeout=15):
        raise RuntimeError("Producer verification failed")
    print((task / "producer.log").read_text(), end="")
    print(f"ISOLATED_QT_PASTE_OK backend={args.backend}")
    success = True
except Exception:
    print(f"Test diagnostics: {task}", file=sys.stderr)
    if (task / "producer.log").exists():
        print((task / "producer.log").read_text(), file=sys.stderr)
    raise
finally:
    for process in reversed(processes):
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=5)
    if success:
        shutil.rmtree(task)
