#!/usr/bin/env python3
# SPDX-FileCopyrightText: 2026 Daniel Freak
# SPDX-License-Identifier: BSD-3-Clause
"""Render the native SVG app icon with the Linux system SVG loader."""
import ctypes as c
import ctypes.util
from pathlib import Path
import struct

root = Path(__file__).resolve().parents[1]
assets = root / "src/Pookie.App/Assets/Branding"
pixbuf = c.CDLL(ctypes.util.find_library("gdk_pixbuf-2.0"))
gobject = c.CDLL(ctypes.util.find_library("gobject-2.0"))
pixbuf.gdk_pixbuf_new_from_file_at_scale.argtypes = [c.c_char_p, c.c_int, c.c_int, c.c_int, c.c_void_p]
pixbuf.gdk_pixbuf_new_from_file_at_scale.restype = c.c_void_p
pixbuf.gdk_pixbuf_savev.argtypes = [c.c_void_p, c.c_char_p, c.c_char_p, c.c_void_p, c.c_void_p, c.c_void_p]
pixbuf.gdk_pixbuf_savev.restype = c.c_int
gobject.g_object_unref.argtypes = [c.c_void_p]

images = []
for size in (16, 24, 32, 48, 64, 128, 256, 512):
    bitmap = pixbuf.gdk_pixbuf_new_from_file_at_scale(bytes(assets / "app-icon.svg"), size, size, 1, None)
    if not bitmap:
        raise RuntimeError("Unable to load app-icon.svg; install the librsvg GdkPixbuf loader")
    output = assets / f"app-icon-{size}.png"
    try:
        if not pixbuf.gdk_pixbuf_savev(bitmap, bytes(output), b"png", None, None, None):
            raise RuntimeError(f"Unable to save {output}")
    finally:
        gobject.g_object_unref(bitmap)
    if size <= 256:
        images.append((size, output.read_bytes()))

offset = 6 + 16 * len(images)
directory = bytearray(struct.pack("<HHH", 0, 1, len(images)))
for size, data in images:
    directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
(assets / "app-icon.ico").write_bytes(directory + b"".join(data for _, data in images))
print(f"Generated PNG sizes and ICO in {assets}")
