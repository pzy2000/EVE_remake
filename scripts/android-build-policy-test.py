#!/usr/bin/env python3
"""Fast source-level guard for the Android package/export split."""

from pathlib import Path
import sys


repository_root = Path(__file__).resolve().parent.parent
build_source = repository_root / "UnityProject/Assets/Editor/Android/StarfallAndroidBuild.cs"
source = build_source.read_text(encoding="utf-8")
workflow = (repository_root / ".github/workflows/android.yml").read_text(encoding="utf-8")
release_script = (repository_root / "scripts/android-build-release.sh").read_text(
    encoding="utf-8"
)

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

workflow_requirements = (
    "https://services.gradle.org/distributions/gradle-8.9-bin.zip",
    "d725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab",
    'echo "GRADLE_EXECUTABLE=$gradle_home/bin/gradle" >>"$GITHUB_ENV"',
    "-type f -name settings.gradle",
    'sudo chown -R "$(id -u):$(id -g)" "$gradle_root"',
)
for fragment in workflow_requirements:
    if fragment not in workflow:
        print(f"Missing pinned release-build policy: {fragment}", file=sys.stderr)
        raise SystemExit(1)

if "-name gradlew" in workflow or "./gradlew" in release_script:
    print(
        "Release builds must not assume Unity exported a Gradle wrapper.",
        file=sys.stderr,
    )
    raise SystemExit(1)

for fragment in ("GRADLE_EXECUTABLE", '"$export_root/settings.gradle"'):
    if fragment not in release_script:
        print(f"Missing external Gradle validation: {fragment}", file=sys.stderr)
        raise SystemExit(1)

print("Android smoke/package and release/Gradle export policy passed.")
