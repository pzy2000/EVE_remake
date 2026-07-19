#!/usr/bin/env python3
"""Verify Unity's runtime graphics API without overstating emulator coverage."""

from __future__ import annotations

import json
import pathlib
import sys


def verify(
    payload: dict[str, object],
    expected: str,
    requested_flag: str,
    qemu_marker: str,
) -> dict[str, object]:
    actual = payload.get("graphicsDeviceType")
    name = payload.get("graphicsDeviceName") or ""
    if not isinstance(name, str) or not name:
        raise ValueError("Unity did not report a graphicsDeviceName")

    is_emulator = qemu_marker == "1"
    verdict = "runtime-match"
    reason = "Unity runtime matched the requested graphics API"
    if actual != expected:
        emulator_fallback = (
            requested_flag == "-force-vulkan"
            and expected == "Vulkan"
            and actual == "OpenGLES3"
            and is_emulator
            and "Android Emulator" in name
            and "SwiftShader" in name
        )
        if not emulator_fallback:
            raise ValueError(
                f"Actual Unity graphics device is {actual!r}; expected {expected!r}"
            )
        verdict = "verified-emulator-vulkan-fallback"
        reason = (
            "Unity rejected the Android Emulator CPU Vulkan device and used the "
            "configured OpenGLES3 fallback"
        )

    return {
        "requestedFlag": requested_flag,
        "expectedDeviceType": expected,
        "actualDeviceType": actual,
        "actualDeviceName": name,
        "isAndroidEmulator": is_emulator,
        "verdict": verdict,
        "reason": reason,
    }


def main(argv: list[str]) -> int:
    if len(argv) != 6:
        print(
            "usage: verify-android-emulator-graphics.py "
            "ACK_JSON EXPECTED_TYPE REQUESTED_FLAG QEMU_MARKER OUTPUT_JSON",
            file=sys.stderr,
        )
        return 2

    source = pathlib.Path(argv[1])
    output = pathlib.Path(argv[5])
    try:
        payload = json.loads(source.read_text(encoding="utf-8"))
        result = verify(payload, argv[2], argv[3], argv[4])
    except (OSError, json.JSONDecodeError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1

    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
