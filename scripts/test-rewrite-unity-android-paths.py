#!/usr/bin/env python3
"""Regression tests for Unity Android export path normalization."""

from __future__ import annotations

import importlib.util
import pathlib
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).with_name("rewrite-unity-android-paths.py")
SPEC = importlib.util.spec_from_file_location("rewrite_unity_android_paths", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class RewriteUnityAndroidPathsTests(unittest.TestCase):
    def test_rewrites_exact_unity_61_project_key_and_gradle_ndk_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = pathlib.Path(temporary_directory) / "export"
            root.mkdir()
            (root / "gradle.properties").write_text(
                "unity.androidSdkPath=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/SDK\n"
                "unity.androidNdkPath=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/NDK\n"
                "unity.androidNdkVersion=old\n"
                "unity.jdkPath=/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK\n"
                "unityProjectPath=/github/workspace/UnityProject/Builds/export\n",
                encoding="utf-8",
            )
            (root / "build.gradle").write_text(
                'android { ndkPath "/opt/unity/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"\n'
                'ndkVersion "old" }\n',
                encoding="utf-8",
            )
            sdk = pathlib.Path(temporary_directory) / "sdk"
            ndk = pathlib.Path(temporary_directory) / "sdk" / "ndk" / "27.2.12479018"
            jdk = pathlib.Path(temporary_directory) / "jdk"
            unity_project = pathlib.Path(temporary_directory) / "UnityProject"
            (unity_project / "Assets").mkdir(parents=True)
            (unity_project / "ProjectSettings").mkdir()
            for directory in (sdk, ndk, jdk):
                directory.mkdir(parents=True, exist_ok=True)

            MODULE.rewrite_export_paths(
                root, sdk, ndk, jdk, "27.2.12479018", unity_project
            )

            properties = (root / "gradle.properties").read_text(encoding="utf-8")
            self.assertIn(
                f"unity.projectPath={unity_project.resolve()}\n", properties
            )
            self.assertNotIn(f"unity.projectPath={root.resolve()}\n", properties)
            self.assertNotIn("unityProjectPath", properties)
            self.assertNotIn("/opt/unity", properties)
            self.assertNotIn("/github/workspace", properties)
            self.assertIn(f"unity.androidSdkPath={sdk.resolve()}\n", properties)
            self.assertIn(f"unity.androidNdkPath={ndk.resolve()}\n", properties)
            self.assertIn(f"unity.jdkPath={jdk.resolve()}\n", properties)

            gradle = (root / "build.gradle").read_text(encoding="utf-8")
            self.assertIn(f'ndkPath "{ndk.resolve()}"', gradle)
            self.assertIn('ndkVersion "27.2.12479018"', gradle)


if __name__ == "__main__":
    unittest.main()
