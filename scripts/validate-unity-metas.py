#!/usr/bin/env python3
"""Validate Unity .meta companions without entering embedded Android libraries."""

from __future__ import annotations

import argparse
from pathlib import Path


def ignored(path: Path, assets: Path) -> bool:
    relative = path.relative_to(assets)
    if any(part.startswith(".") for part in relative.parts):
        return True
    # Unity imports the .androidlib directory as one plug-in asset. Its Gradle
    # project contents are opaque and must not receive individual .meta files.
    return any(parent.name.endswith(".androidlib") for parent in path.parents if parent != assets)


def validate(assets: Path) -> tuple[list[Path], list[Path]]:
    missing: list[Path] = []
    orphaned: list[Path] = []
    for path in sorted(assets.rglob("*")):
        if ignored(path, assets) or path.name.endswith(".meta"):
            continue
        companion = Path(f"{path}.meta")
        if not companion.is_file():
            missing.append(path)
    for meta in sorted(assets.rglob("*.meta")):
        if ignored(meta, assets):
            continue
        source = Path(str(meta)[:-5])
        if not source.exists():
            orphaned.append(meta)
    return missing, orphaned


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("assets", type=Path)
    arguments = parser.parse_args()
    assets = arguments.assets.resolve()
    if not assets.is_dir():
        parser.error(f"Unity Assets directory does not exist: {assets}")
    missing, orphaned = validate(assets)
    for path in missing:
        print(f"MISSING_META {path.relative_to(assets.parent)}")
    for path in orphaned:
        print(f"ORPHAN_META {path.relative_to(assets.parent)}")
    if missing or orphaned:
        print(f"Unity meta validation failed: missing={len(missing)} orphaned={len(orphaned)}")
        return 1
    print("Unity meta validation passed: every imported asset has exactly one companion.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
