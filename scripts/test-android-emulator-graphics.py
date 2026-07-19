#!/usr/bin/env python3
"""Regression tests for the tightly scoped emulator Vulkan fallback policy."""

from __future__ import annotations

import importlib.util
import pathlib


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
MODULE_PATH = SCRIPT_DIRECTORY / "verify-android-emulator-graphics.py"
SPEC = importlib.util.spec_from_file_location("starfall_graphics_verifier", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Could not load {MODULE_PATH}")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def payload(device_type: str, name: str) -> dict[str, object]:
    return {"graphicsDeviceType": device_type, "graphicsDeviceName": name}


def assert_rejected(
    value: dict[str, object], expected: str, requested_flag: str, qemu_marker: str
) -> None:
    try:
        MODULE.verify(value, expected, requested_flag, qemu_marker)
    except ValueError:
        return
    raise AssertionError(f"Unexpectedly accepted graphics payload: {value}")


vulkan = MODULE.verify(
    payload("Vulkan", "Adreno test device"), "Vulkan", "-force-vulkan", "0"
)
assert vulkan["verdict"] == "runtime-match"
assert vulkan["isAndroidEmulator"] is False

fallback = MODULE.verify(
    payload(
        "OpenGLES3", "Android Emulator OpenGL ES Translator (Google SwiftShader)"
    ),
    "Vulkan",
    "-force-vulkan",
    "1",
)
assert fallback["verdict"] == "verified-emulator-vulkan-fallback"
assert fallback["isAndroidEmulator"] is True

assert_rejected(
    payload(
        "OpenGLES3", "Android Emulator OpenGL ES Translator (Google SwiftShader)"
    ),
    "Vulkan",
    "-force-vulkan",
    "0",
)
assert_rejected(
    payload("OpenGLES3", "Mali-G78"), "Vulkan", "-force-vulkan", "1"
)

gles = MODULE.verify(
    payload("OpenGLES3", "Android Emulator OpenGL ES Translator (Google SwiftShader)"),
    "OpenGLES3",
    "-force-gles30",
    "1",
)
assert gles["verdict"] == "runtime-match"
assert_rejected(
    payload("Vulkan", "SwiftShader Device (Subzero)"),
    "OpenGLES3",
    "-force-gles30",
    "1",
)

print("Android emulator graphics verification tests passed.")
