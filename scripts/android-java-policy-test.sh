#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
java_root="$repository_root/UnityProject/Assets/Plugins/Android/StarfallMobile.androidlib/src"
activity="$repository_root/UnityProject/Assets/Starfall/Android/StarfallUnityPlayerActivity.java"
activity_meta="$activity.meta"
manifest="$repository_root/UnityProject/Assets/Plugins/Android/AndroidManifest.xml"
project_settings="$repository_root/UnityProject/ProjectSettings/ProjectSettings.asset"
bridge="$java_root/main/java/com/pzy/starfall/mobile/StarfallMobileBridge.java"
picker="$java_root/main/java/com/pzy/starfall/mobile/LegacyDocumentPickerActivity.java"
library_manifest="$java_root/main/AndroidManifest.xml"
library_gradle="$repository_root/UnityProject/Assets/Plugins/Android/StarfallMobile.androidlib/build.gradle"
classes_directory="$(mktemp -d "${RUNNER_TEMP:-/tmp}/starfall-java-policy.XXXXXX")"
stub_source="$repository_root/scripts/java-policy-stubs"

cleanup() {
  rm -rf -- "$classes_directory"
}
trap cleanup EXIT INT TERM

if ! grep -Fq "implementation 'androidx.core:core:1.16.0'" "$library_gradle"; then
  echo "StarfallMobile.androidlib must provide the AGP 8.7-compatible AndroidX Core for Window's Consumer API." >&2
  exit 1
fi

javac --release 8 -Xlint:all -Werror \
  -d "$classes_directory" \
  "$java_root/main/java/com/pzy/starfall/mobile/BridgeLifecycleGeneration.java" \
  "$java_root/main/java/com/pzy/starfall/mobile/CompatBackPolicy.java" \
  "$java_root/test/java/com/pzy/starfall/mobile/StarfallMobileBridgeLifecycleTest.java"

javac --release 8 -Xlint:all -Werror \
  -cp "$classes_directory" \
  -d "$classes_directory" \
  "$stub_source/android/app/Activity.java" \
  "$stub_source/android/os/Build.java" \
  "$stub_source/android/view/KeyEvent.java" \
  "$stub_source/com/unity3d/player/UnityPlayerGameActivity.java" \
  "$stub_source/com/pzy/starfall/mobile/StarfallMobileBridge.java" \
  "$activity"

java -cp "$classes_directory" com.pzy.starfall.mobile.StarfallMobileBridgeLifecycleTest \
  "$bridge" \
  "$activity" \
  "$manifest" \
  "$project_settings" \
  "$picker" \
  "$library_manifest" \
  "$activity_meta"

python3 "$repository_root/scripts/android-privacy-policy-test.py"

echo "Android Java lifecycle, custom GameActivity, and Back policy tests passed."
