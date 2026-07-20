#!/usr/bin/env python3
"""Fast source-level guard for the Android package/export split."""

from pathlib import Path
import subprocess
import sys


repository_root = Path(__file__).resolve().parent.parent
build_source = repository_root / "UnityProject/Assets/Editor/Android/StarfallAndroidBuild.cs"
source = build_source.read_text(encoding="utf-8")
app_root = (repository_root / "UnityProject/Assets/Starfall/App/AppRoot.cs").read_text(
    encoding="utf-8"
)
android_ci_automation = (
    repository_root / "UnityProject/Assets/Starfall/App/AndroidCiAutomation.cs"
).read_text(encoding="utf-8")
space_presenter = (
    repository_root / "UnityProject/Assets/Starfall/Presentation/SpaceWorldPresenter.cs"
).read_text(encoding="utf-8")
workflow = (repository_root / ".github/workflows/android.yml").read_text(encoding="utf-8")
release_script = (repository_root / "scripts/android-build-release.sh").read_text(
    encoding="utf-8"
)
validation_script = (
    repository_root / "scripts/validate-android-release.sh"
).read_text(encoding="utf-8")

required_fragments = (
    'var expectedExportType = flavor == SmokeFlavor ? "androidPackage" : "androidStudioProject";',
    "EditorUserBuildSettings.exportAsGoogleAndroidProject = !isSmoke;",
    "EditorUserBuildSettings.buildAppBundle = false;",
    "PlayerSettings.allowedAutorotateToPortrait = false;",
    "PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;",
    "PlayerSettings.allowedAutorotateToLandscapeLeft = true;",
    "PlayerSettings.allowedAutorotateToLandscapeRight = true;",
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
    "find downloaded-release -maxdepth 1 -type f -name '*.apk' -print",
    "find downloaded-release -maxdepth 1 -type f -name '*.aab' -print",
)
for fragment in workflow_requirements:
    if fragment not in workflow:
        print(f"Missing pinned release-build policy: {fragment}", file=sys.stderr)
        raise SystemExit(1)

if workflow.count("GH_REPO: ${{ github.repository }}") != 2:
    print(
        "Both GitHub release jobs must declare repository context without checkout.",
        file=sys.stderr,
    )
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

if "ndk.dir=" in release_script:
    print(
        "Release local.properties must not duplicate Unity's android.ndkPath.",
        file=sys.stderr,
    )
    raise SystemExit(1)
if "printf 'sdk.dir=%s\\n' \"$ANDROID_SDK_ROOT\"" not in release_script:
    print("Release local.properties must retain sdk.dir.", file=sys.stderr)
    raise SystemExit(1)

if r"\( -type f -o -type l \) -name llvm-readelf" not in validation_script:
    print(
        "Release validation must resolve the NDK llvm-readelf symlink.",
        file=sys.stderr,
    )
    raise SystemExit(1)

if ".*icon.*" in validation_script:
    print(
        "Release icon validation must not depend on resource filenames after AAPT optimization.",
        file=sys.stderr,
    )
    raise SystemExit(1)
for fragment in (
    'starfall_badging_density_icon "$badging" "$density"',
    'dump xmltree "$apk" --file "$actual_icon_path"',
    "E: adaptive-icon",
    'apk="$(cd "$(dirname "$apk")" && pwd -P)/$(basename "$apk")"',
    'aab="$(cd "$(dirname "$aab")" && pwd -P)/$(basename "$aab")"',
):
    if fragment not in validation_script:
        print(f"Missing optimized launcher icon validation: {fragment}", file=sys.stderr)
        raise SystemExit(1)

subprocess.run(
    ["bash", str(repository_root / "scripts/test-android-release-badging.sh")],
    cwd=repository_root,
    check=True,
)
subprocess.run(
    ["bash", str(repository_root / "scripts/test-android-release-manifest.sh")],
    cwd=repository_root,
    check=True,
)

android_back_guard = """#if !UNITY_ANDROID || UNITY_EDITOR
            // Android system Back is owned by StarfallMobileBridge on every supported
            // API. GameActivity can also expose the committed key through Input System;
            // handling escape here would close and immediately reopen the top overlay.
            if (keyboard.escapeKey.wasPressedThisFrame) HandleMobileBack(\"keyboard\");
#endif"""
if android_back_guard not in app_root:
    print(
        "Android player keyboard Back must remain exclusively owned by the native bridge.",
        file=sys.stderr,
    )
    raise SystemExit(1)

for fragment in (
    'case "prepare-touch-target":',
    "spacePresenter.PrepareAndroidCiTouchTarget()",
):
    if fragment not in android_ci_automation:
        print(f"Missing deterministic Android touch preparation: {fragment}", file=sys.stderr)
        raise SystemExit(1)
for fragment in (
    "TryGetAndroidCiDragPath(out var clearPoint, out _)",
    'androidCiTouchProxy.name = "AndroidCiTouchProxy"',
    "TryResolveAndroidCiTouchView(",
):
    if fragment not in space_presenter:
        print(f"Missing raycastable Android touch fixture: {fragment}", file=sys.stderr)
        raise SystemExit(1)

print("Android smoke/package and release/Gradle export policy passed.")
