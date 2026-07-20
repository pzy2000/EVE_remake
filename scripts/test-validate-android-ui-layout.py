#!/usr/bin/env python3
from __future__ import annotations

import json
import pathlib
import subprocess
import sys
import tempfile


SCRIPT = pathlib.Path(__file__).with_name("validate-android-ui-layout.py")


def run_validator(root: pathlib.Path, payload: dict, *expected: str):
    layout = root / "layout.json"
    report = root / "report.json"
    layout.write_text(json.dumps(payload), encoding="utf-8")
    result = subprocess.run(
        [sys.executable, str(SCRIPT), str(layout), *expected, str(report)],
        check=False,
        capture_output=True,
        text=True,
    )
    return result, json.loads(report.read_text(encoding="utf-8"))


with tempfile.TemporaryDirectory() as directory:
    root = pathlib.Path(directory)
    valid = {
        "screen": "Fixture",
        "mode": "CompactLandscape",
        "safeBoundsDp": {"x": 0, "y": 0, "width": 200, "height": 120},
        "foldingBoundsDp": {"x": 0, "y": 0, "width": 0, "height": 0},
        "controls": [
            {
                "name": "fixture-button",
                "boundsDp": {"x": 10, "y": 10, "width": 48, "height": 48},
                "visibleBoundsDp": {
                    "x": 10,
                    "y": 10,
                    "width": 48,
                    "height": 48,
                },
                "fullyVisible": True,
            }
        ],
        "surfaces": [],
        "texts": [
            {
                "name": "fixture-text",
                "fontSizeDp": 14,
                "visibleBoundsDp": {
                    "x": 70,
                    "y": 10,
                    "width": 80,
                    "height": 20,
                },
                "fullyVisible": True,
            }
        ],
    }
    result, report = run_validator(root, valid, "CompactLandscape", "false", "")
    assert result.returncode == 0, result.stderr
    assert report["status"] == "passed"
    assert report["failureCount"] == 0

    invalid = {
        "screen": "Fixture",
        "mode": "SquareExpanded",
        "safeBoundsDp": {"x": 0, "y": 0, "width": 100, "height": 100},
        "foldingBoundsDp": {"x": 0, "y": 0, "width": 0, "height": 0},
        "controls": [
            {
                "name": "broken-button",
                "boundsDp": {"x": 95, "y": 95, "width": 20, "height": 20},
                "visibleBoundsDp": {"x": 95, "y": 95, "width": 0, "height": 0},
                "fullyVisible": False,
            }
        ],
        "surfaces": [],
        "texts": [],
    }
    result, report = run_validator(
        root, invalid, "CompactLandscape", "true", "settings-card"
    )
    assert result.returncode == 1
    assert report["status"] == "failed"
    assert report["failureCount"] >= 7
    combined = "\n".join(report["failures"])
    for marker in (
        "expected mode",
        "non-empty folding feature",
        "minimum is 48x48",
        "is clipped",
        "empty visibleBoundsDp",
        "expected visible surface settings-card",
        "no readable visible text evidence",
    ):
        assert marker in combined, marker

print("Android UI layout aggregate validator tests passed.")
