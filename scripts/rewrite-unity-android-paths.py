#!/usr/bin/env python3
"""Rewrite container-only paths in a Unity-exported Android Gradle project."""

from __future__ import annotations

import json
import pathlib
import re
import sys


PROPERTY_PATTERN = re.compile(r"^\s*([^#!\s=]+)\s*=")
NDK_PATH_PATTERN = re.compile(r"(\bndkPath\s*(?:=\s*)?)[\"'][^\"']*[\"']")
NDK_VERSION_PATTERN = re.compile(r"(\bndkVersion\s*(?:=\s*)?)[\"'][^\"']*[\"']")
STALE_PATH_PATTERN = re.compile(r"/(?:opt/unity|github/workspace)/")


def rewrite_export_paths(
    root: pathlib.Path,
    sdk: pathlib.Path,
    ndk: pathlib.Path,
    jdk: pathlib.Path,
    ndk_version: str,
    unity_project: pathlib.Path,
) -> None:
    root = root.resolve()
    sdk = sdk.resolve()
    ndk = ndk.resolve()
    jdk = jdk.resolve()
    unity_project = unity_project.resolve()
    if not (unity_project / "Assets").is_dir() or not (
        unity_project / "ProjectSettings"
    ).is_dir():
        raise SystemExit(
            "Unity project path must contain Assets and ProjectSettings: "
            f"{unity_project}"
        )

    properties_path = root / "gradle.properties"
    if not properties_path.is_file():
        raise SystemExit(f"Unity export is missing {properties_path}")

    values = {
        "unity.androidSdkPath": str(sdk),
        "unity.androidNdkPath": str(ndk),
        "unity.androidNdkVersion": ndk_version,
        "unity.jdkPath": str(jdk),
        # Unity 6.1 reads this exact dotted key. The older camel-case spelling
        # is ignored and can leave the container's export path in the build.
        "unity.projectPath": str(unity_project),
    }
    lines = properties_path.read_text(encoding="utf-8").splitlines()
    seen: set[str] = set()
    rewritten: list[str] = []
    for line in lines:
        match = PROPERTY_PATTERN.match(line)
        key = match.group(1) if match else None
        if key in values:
            rewritten.append(f"{key}={values[key]}")
            seen.add(key)
        elif key == "unityProjectPath":
            # Drop the unsupported spelling instead of preserving a stale and
            # misleading duplicate beside the authoritative Unity 6.1 key.
            continue
        else:
            rewritten.append(line)
    for key, value in values.items():
        if key not in seen:
            rewritten.append(f"{key}={value}")
    properties_path.write_text("\n".join(rewritten) + "\n", encoding="utf-8")

    ndk_literal = json.dumps(str(ndk))
    version_literal = json.dumps(ndk_version)
    gradle_files = sorted(root.rglob("*.gradle")) + sorted(root.rglob("*.gradle.kts"))
    for gradle_file in gradle_files:
        source = gradle_file.read_text(encoding="utf-8")
        source = NDK_PATH_PATTERN.sub(lambda match: match.group(1) + ndk_literal, source)
        source = NDK_VERSION_PATTERN.sub(
            lambda match: match.group(1) + version_literal, source
        )
        gradle_file.write_text(source, encoding="utf-8")

    stale: list[str] = []
    for candidate in [properties_path, *gradle_files]:
        for line_number, line in enumerate(
            candidate.read_text(encoding="utf-8").splitlines(), 1
        ):
            if STALE_PATH_PATTERN.search(line):
                stale.append(
                    f"{candidate.relative_to(root)}:{line_number}:{line.strip()}"
                )
    if stale:
        raise SystemExit(
            "Unity export retained container-only tool paths:\n" + "\n".join(stale)
        )

    for gradle_file in gradle_files:
        source = gradle_file.read_text(encoding="utf-8")
        for match in NDK_PATH_PATTERN.finditer(source):
            if match.group(0).split(maxsplit=1)[-1].strip("= ") != ndk_literal:
                raise SystemExit(f"Unrewritten ndkPath in {gradle_file}")


def main(argv: list[str]) -> int:
    if len(argv) != 7:
        raise SystemExit(
            "usage: rewrite-unity-android-paths.py "
            "EXPORT_ROOT SDK_ROOT NDK_ROOT JDK_ROOT NDK_VERSION UNITY_PROJECT"
        )
    rewrite_export_paths(
        pathlib.Path(argv[1]),
        pathlib.Path(argv[2]),
        pathlib.Path(argv[3]),
        pathlib.Path(argv[4]),
        argv[5],
        pathlib.Path(argv[6]),
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
