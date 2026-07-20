#!/usr/bin/env python3
"""Validate one Android UI Toolkit layout snapshot and report every defect."""

from __future__ import annotations

import json
import pathlib
import sys
from typing import Any


def main() -> int:
    if len(sys.argv) != 6:
        print(
            "usage: validate-android-ui-layout.py LAYOUT EXPECTED_MODE "
            "REQUIRE_HINGE EXPECTED_SURFACE REPORT",
            file=sys.stderr,
        )
        return 2

    path = pathlib.Path(sys.argv[1])
    expected_mode = sys.argv[2]
    require_hinge = sys.argv[3] == "true"
    expected_surface = sys.argv[4]
    report_path = pathlib.Path(sys.argv[5])
    data = json.loads(path.read_text(encoding="utf-8"))
    failures: list[str] = []

    def fail(message: str) -> None:
        failures.append(f"{path}: {message}")

    if data.get("mode") != expected_mode:
        fail(f"expected mode {expected_mode}, received {data.get('mode')}")

    safe = data.get("safeBoundsDp") or {}
    safe_rect = rect_from_mapping(safe)
    hinge = data.get("foldingBoundsDp") or {}
    hinge_rect = rect_from_mapping(hinge)
    has_hinge = hinge_rect[2] > 0 or hinge_rect[3] > 0
    if require_hinge and not has_hinge:
        fail("expected a non-empty folding feature")

    def validate_visible_rect(
        rect: tuple[float, float, float, float], item_kind: str, name: str
    ) -> None:
        if rect[2] <= 0 or rect[3] <= 0:
            fail(f"visible {item_kind} {name} has empty visibleBoundsDp")
            return
        if safe_rect[2] > 0 and safe_rect[3] > 0 and (
            rect[0] < safe_rect[0] - 0.01
            or rect[1] < safe_rect[1] - 0.01
            or rect[0] + rect[2] > safe_rect[0] + safe_rect[2] + 0.01
            or rect[1] + rect[3] > safe_rect[1] + safe_rect[3] + 0.01
        ):
            fail(f"visible {item_kind} {name} leaves the safe bounds")
        if require_hinge and intersects(rect, hinge_rect):
            fail(f"visible {item_kind} {name} intersects the folding feature")

    controls = data.get("controls") or []
    visible_controls = [
        item
        for item in controls
        if item.get("visible", True)
        and not str(item.get("name") or "").startswith("unity-")
    ]
    if not visible_controls:
        fail("no visible interactive controls were captured")
    for control in visible_controls:
        name = control.get("name") or "<unnamed>"
        raw = rect_from_item(control, "boundsDp")
        visible = rect_from_item(control, "visibleBoundsDp")
        if raw[2] + 0.01 < 48 or raw[3] + 0.01 < 48:
            fail(
                f"touch target {name} is {raw[2]}x{raw[3]} dp; "
                "minimum is 48x48 dp"
            )
        if not control.get("inScrollView", False):
            if visible[2] + 0.01 < 48 or visible[3] + 0.01 < 48:
                fail(
                    f"visible touch target {name} is {visible[2]}x{visible[3]} dp; "
                    "minimum visible size is 48x48 dp"
                )
            if not control.get("fullyVisible", False):
                fail(f"non-scrolling touch target {name} is clipped")
        validate_visible_rect(visible, "control", name)

    required_controls = required_controls_for(data, expected_mode)
    required_controls |= {
        "settings-card": {
            "settings-close",
            "quality-cycle",
            "music-volume",
            "music-muted",
        },
        "confirmation-card": {"confirmation-cancel", "confirmation-confirm"},
        "starmap-card": {"map-close"},
        "journal-card": {"journal-close"},
        "death-card": {"respawn"},
        "target-panel": {"approach", "orbit", "warp", "lock", "dock"},
    }.get(expected_surface, set())
    visible_control_names = {item.get("name") for item in visible_controls}
    missing_controls = sorted(required_controls - visible_control_names)
    if missing_controls:
        fail(
            "required controls are not fully present in the visible UI: "
            f"{missing_controls}"
        )

    surfaces = data.get("surfaces") or []
    visible_surfaces = [item for item in surfaces if item.get("visible", True)]
    surface_names = {item.get("name") for item in visible_surfaces}
    if expected_surface and expected_surface not in surface_names:
        fail(
            f"expected visible surface {expected_surface}; "
            f"found {sorted(surface_names)}"
        )
    for surface in visible_surfaces:
        name = surface.get("name") or "<unnamed>"
        visible = rect_from_item(surface, "visibleBoundsDp")
        if visible[2] <= 0 or visible[3] <= 0:
            fail(f"surface {name} was listed without a visible region")
        validate_visible_rect(rect_from_item(surface, "boundsDp"), "surface", name)

    texts = data.get("texts") or []
    visible_texts = [item for item in texts if item.get("visible", True)]
    if not visible_texts:
        fail("no readable visible text evidence was captured")
    for text in visible_texts:
        name = text.get("name") or (text.get("text") or "<unnamed>")[:80]
        font_size = float(text.get("fontSizeDp") or 0)
        if font_size + 0.01 < 14:
            fail(f"visible text {name!r} uses {font_size}dp; minimum is 14dp")
        if not text.get("inScrollView", False) and not text.get(
            "fullyVisible", False
        ):
            fail(f"non-scrolling text {name!r} is clipped")
        validate_visible_rect(rect_from_item(text, "visibleBoundsDp"), "text", name)

    report = {
        "input": str(path),
        "status": "failed" if failures else "passed",
        "failureCount": len(failures),
        "failures": failures,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if failures:
        print("\n".join(failures), file=sys.stderr)
        return 1
    return 0


def rect_from_mapping(mapping: dict[str, Any]) -> tuple[float, float, float, float]:
    return (
        float(mapping.get("x", 0)),
        float(mapping.get("y", 0)),
        float(mapping.get("width", 0)),
        float(mapping.get("height", 0)),
    )


def rect_from_item(
    item: dict[str, Any], key: str
) -> tuple[float, float, float, float]:
    return rect_from_mapping(item.get(key) or {})


def intersects(
    first: tuple[float, float, float, float],
    second: tuple[float, float, float, float],
) -> bool:
    return (
        first[0] < second[0] + second[2]
        and first[0] + first[2] > second[0]
        and first[1] < second[1] + second[3]
        and first[1] + first[3] > second[1]
    )


def required_controls_for(data: dict[str, Any], expected_mode: str) -> set[str]:
    required = set(
        {
            "MainMenu": {
                "pilot-name",
                "empire-aurelian",
                "empire-kaldari",
                "empire-meridian",
                "empire-varkhald",
                "launch",
                "continue",
                "import",
                "settings",
            },
            "Station": {
                "settings",
                "repair",
                "save",
                "undock",
                "tab-agents",
                "tab-market",
                "tab-fitting",
                "tab-ships",
                "tab-lp",
            },
            "Space": {"map", "journal", "pilot", "save", "settings", "module-1"},
        }.get(data.get("screen"), set())
    )
    if data.get("screen") == "Space":
        if expected_mode == "CompactLandscape":
            required |= {
                "mobile-overview-toggle",
                "mobile-target-toggle",
                "mobile-log-toggle",
            }
        else:
            required |= {"approach", "orbit", "warp", "lock", "dock"}
    return required


if __name__ == "__main__":
    raise SystemExit(main())
