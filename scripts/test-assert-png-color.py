#!/usr/bin/env python3
"""Unit tests for the two independent Android screenshot probes."""

import binascii
import json
import pathlib
import struct
import subprocess
import tempfile
import zlib


ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "assert-png-color.py"


def png_bytes(width, height, pixels):
    raw = b"".join(b"\x00" + bytes(pixels[y]) for y in range(height))

    def chunk(kind, body):
        return (
            struct.pack(">I", len(body))
            + kind
            + body
            + struct.pack(">I", binascii.crc32(kind + body) & 0xFFFFFFFF)
        )

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(raw))
        + chunk(b"IEND", b"")
    )


def fill(pixels, bounds, color):
    for y in range(bounds["y"], bounds["y"] + bounds["height"]):
        for x in range(bounds["x"], bounds["x"] + bounds["width"]):
            offset = x * 3
            pixels[y][offset:offset + 3] = color


def run_case(directory, ui_visible, frame_visible):
    width = 80
    height = 40
    pixels = [bytearray(width * 3) for _ in range(height)]
    ui_bounds = {"x": 4, "y": 4, "width": 20, "height": 20}
    frame_bounds = {"x": 40, "y": 4, "width": 24, "height": 24}
    if ui_visible:
        fill(pixels, ui_bounds, bytes.fromhex("ff00ff"))
    if frame_visible:
        fill(pixels, frame_bounds, bytes.fromhex("00ff00"))
    png = directory / f"{ui_visible}-{frame_visible}.png"
    layout = directory / f"{ui_visible}-{frame_visible}.json"
    png.write_bytes(png_bytes(width, height, pixels))
    layout.write_text(json.dumps({
        "density": 1,
        "renderProbe": ui_bounds,
        "renderProbeRgb": "ff00ff",
        "frameProbePx": frame_bounds,
        "frameProbeRgb": "00ff00",
    }), encoding="utf-8")
    return subprocess.run(
        ["python3", str(SCRIPT), str(png), str(layout)],
        check=False,
        capture_output=True,
        text=True,
    )


def main():
    with tempfile.TemporaryDirectory() as raw_directory:
        directory = pathlib.Path(raw_directory)
        passed = run_case(directory, True, True)
        assert passed.returncode == 0, passed.stderr
        ui_failure = run_case(directory, False, True)
        assert ui_failure.returncode != 0
        assert "final-frame probe visible" in ui_failure.stderr
        frame_failure = run_case(directory, True, False)
        assert frame_failure.returncode != 0
        assert "final-frame probe is absent" in frame_failure.stderr
    print("Android screenshot probe tests passed.")


if __name__ == "__main__":
    main()
