#!/usr/bin/env python3
"""Fast source-level guard for the Android package/export split."""

from pathlib import Path
import sys


repository_root = Path(__file__).resolve().parent.parent
build_source = repository_root / "UnityProject/Assets/Editor/Android/StarfallAndroidBuild.cs"
source = build_source.read_text(encoding="utf-8")

required_fragments = (
    'var expectedExportType = flavor == SmokeFlavor ? "androidPackage" : "androidStudioProject";',
    "EditorUserBuildSettings.exportAsGoogleAndroidProject = !isSmoke;",
    "EditorUserBuildSettings.buildAppBundle = false;",
)

missing = [fragment for fragment in required_fragments if fragment not in source]
if missing:
    for fragment in missing:
        print(f"Missing Android build policy: {fragment}", file=sys.stderr)
    raise SystemExit(1)

if "BuildOptions.AcceptExternalModificationsToPlayer" in source:
    print(
        "A fresh Android Gradle export must not append to an existing external project.",
        file=sys.stderr,
    )
    raise SystemExit(1)

print("Android smoke/package and release/Gradle export policy passed.")
