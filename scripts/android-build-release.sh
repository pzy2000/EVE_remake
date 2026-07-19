#!/usr/bin/env bash
set -Eeuo pipefail

export_root="${1:?usage: android-build-release.sh EXPORTED_GRADLE_PROJECT OUTPUT_DIRECTORY}"
output_directory="${2:?missing output directory}"
script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
repository_root="$(cd "$script_directory/.." && pwd -P)"
unity_project_root="$repository_root/UnityProject"

require_environment() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "Required environment variable is missing: $name" >&2
    exit 2
  fi
}

for variable in \
  ANDROID_KEYSTORE_BASE64 \
  ANDROID_KEYSTORE_PASSWORD \
  ANDROID_KEY_ALIAS \
  ANDROID_KEY_PASSWORD \
  ANDROID_SDK_ROOT \
  ANDROID_NDK_HOME \
  JAVA_HOME \
  GRADLE_EXECUTABLE \
  STARFALL_VERSION_CODE \
  STARFALL_VERSION_NAME; do
  require_environment "$variable"
done

export_root="$(cd "$export_root" && pwd -P)"
mkdir -p "$output_directory"
output_directory="$(cd "$output_directory" && pwd -P)"

if [[ ! -f "$export_root/settings.gradle" ]] || \
   [[ ! -f "$export_root/build.gradle" ]] || \
   [[ ! -d "$export_root/launcher" ]] || \
   [[ ! -d "$export_root/unityLibrary" ]]; then
  echo "Not a Unity-exported Android Gradle project: $export_root" >&2
  exit 2
fi
if [[ ! -x "$GRADLE_EXECUTABLE" ]]; then
  echo "Pinned Gradle executable is unavailable: $GRADLE_EXECUTABLE" >&2
  exit 2
fi
if [[ ! -d "$unity_project_root/Assets" ]] || [[ ! -d "$unity_project_root/ProjectSettings" ]]; then
  echo "Unity project root is invalid: $unity_project_root" >&2
  exit 2
fi

mapfile -t custom_activity_sources < <(find "$export_root" -type f \
  -path '*/src/main/java/com/pzy/starfall/mobile/StarfallUnityGameActivity.java' -print)
if (( ${#custom_activity_sources[@]} != 1 )); then
  echo "Expected exactly one exported StarfallUnityGameActivity.java, found ${#custom_activity_sources[@]}." >&2
  exit 1
fi
grep -Fq 'extends UnityPlayerGameActivity' "${custom_activity_sources[0]}" || {
  echo "Exported custom GameActivity source is invalid." >&2
  exit 1
}

il2cpp_output="$(find "$export_root" -type d -path '*/Il2CppOutputProject/Source/il2cppOutput' -print -quit)"
if [[ -z "$il2cpp_output" ]]; then
  echo "Unity release export does not contain IL2CPP generated sources." >&2
  exit 1
fi
if grep -R -E -q \
  'OnAndroidCiCommand|ShowDeathOverlayForAndroidCi|STARFALL_ANDROID_CI_COMMAND_(ACK|ERR)' \
  "$il2cpp_output"; then
  echo "Smoke-only Android CI command callback leaked into release IL2CPP output." >&2
  exit 1
fi

temporary_root="$(mktemp -d "${RUNNER_TEMP:-/tmp}/starfall-android-signing.XXXXXX")"
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT INT TERM

keystore_path="$temporary_root/upload.keystore"
if ! printf '%s' "$ANDROID_KEYSTORE_BASE64" | base64 --decode >"$keystore_path"; then
  echo "ANDROID_KEYSTORE_BASE64 could not be decoded." >&2
  exit 1
fi
chmod 600 "$keystore_path"

if ! keytool -list \
  -keystore "$keystore_path" \
  -storepass "$ANDROID_KEYSTORE_PASSWORD" \
  -alias "$ANDROID_KEY_ALIAS" >/dev/null; then
  echo "The configured keystore or alias is invalid." >&2
  exit 1
fi

# Unity 6 exports android.ndkPath into the Gradle project. AGP rejects a
# simultaneous legacy ndk.dir entry with CXX1100, so local.properties owns only
# the SDK location while rewrite-unity-android-paths.py normalizes ndkPath.
printf 'sdk.dir=%s\n' "$ANDROID_SDK_ROOT" >"$export_root/local.properties"

ndk_version="${ANDROID_NDK_HOME##*/}"
python3 "$script_directory/rewrite-unity-android-paths.py" \
  "$export_root" "$ANDROID_SDK_ROOT" "$ANDROID_NDK_HOME" "$JAVA_HOME" \
  "$ndk_version" "$unity_project_root"

(
  cd "$export_root"
  "$GRADLE_EXECUTABLE" \
    --no-daemon \
    --console=plain \
    --stacktrace \
    -Pandroid.injected.signing.store.file="$keystore_path" \
    -Pandroid.injected.signing.store.password="$ANDROID_KEYSTORE_PASSWORD" \
    -Pandroid.injected.signing.key.alias="$ANDROID_KEY_ALIAS" \
    -Pandroid.injected.signing.key.password="$ANDROID_KEY_PASSWORD" \
    :launcher:assembleRelease \
    :launcher:bundleRelease
)

mapfile -t apk_candidates < <(find "$export_root/launcher/build/outputs/apk/release" \
  -type f -name '*.apk' ! -name '*unaligned*' -print | sort)
mapfile -t aab_candidates < <(find "$export_root/launcher/build/outputs/bundle/release" \
  -type f -name '*.aab' -print | sort)

if (( ${#apk_candidates[@]} != 1 )); then
  echo "Expected exactly one signed release APK, found ${#apk_candidates[@]}." >&2
  exit 1
fi
if (( ${#aab_candidates[@]} != 1 )); then
  echo "Expected exactly one signed release AAB, found ${#aab_candidates[@]}." >&2
  exit 1
fi

artifact_stem="starfall-odyssey-${STARFALL_VERSION_NAME}-${STARFALL_VERSION_CODE}"
apk_output="$output_directory/${artifact_stem}.apk"
aab_output="$output_directory/${artifact_stem}.aab"
symbols_output="$output_directory/${artifact_stem}-il2cpp-symbols.zip"

cp "${apk_candidates[0]}" "$apk_output"
cp "${aab_candidates[0]}" "$aab_output"

symbols_archive="$(find "$export_root" -type f \
  \( -iname '*symbols*.zip' -o -iname '*symbol*.zip' \) -print -quit)"
if [[ -n "$symbols_archive" ]]; then
  cp "$symbols_archive" "$symbols_output"
else
  symbols_stage="$temporary_root/symbols"
  mkdir -p "$symbols_stage"
  symbol_count=0

  copy_symbol_file() {
    local symbol_file="$1"
    local relative_path="${symbol_file#"$export_root"/}"
    mkdir -p "$symbols_stage/$(dirname "$relative_path")"
    cp "$symbol_file" "$symbols_stage/$relative_path"
    symbol_count=$((symbol_count + 1))
  }

  # Unity 6 LegacyExtensions writes native symbols as ordinary .so files below
  # unityLibrary/symbols/<abi>. Those files are symbol artifacts even though
  # their names do not carry a .sym/.dbg suffix, so preserve the complete
  # symbols directory hierarchy.
  while IFS= read -r -d '' symbol_file; do
    copy_symbol_file "$symbol_file"
  done < <(find "$export_root" -type f -path '*/symbols/*' -print0)

  # Some Unity/NDK variants emit explicit debug-symbol suffixes elsewhere.
  # Include only those recognized files and never sweep ordinary .so files
  # outside a dedicated symbols directory.
  while IFS= read -r -d '' symbol_file; do
    copy_symbol_file "$symbol_file"
  done < <(find "$export_root" -type f \
    ! -path '*/symbols/*' \
    \( -name '*.sym.so' -o -name '*.dbg.so' -o -name '*.so.debug' \) -print0)

  if (( symbol_count == 0 )); then
    echo "Unity release export did not contain IL2CPP symbol files." >&2
    exit 1
  fi
  (
    cd "$symbols_stage"
    zip -qry "$symbols_output" .
  )
fi

symbols_manifest="$output_directory/${artifact_stem}-il2cpp-symbols.manifest.txt"
unzip -tq "$symbols_output" >/dev/null
unzip -Z1 "$symbols_output" >"$symbols_manifest"
if [[ ! -s "$symbols_manifest" ]] || ! grep -Eqi '(^|/)libil2cpp([^/]*)$' "$symbols_manifest"; then
  echo "IL2CPP symbols archive does not contain a libil2cpp symbol artifact." >&2
  exit 1
fi

build_report="$(find "$(dirname "$export_root")" -maxdepth 4 \
  -type f -name '*.build-report.json' -print -quit)"
if [[ -z "$build_report" ]]; then
  echo "Unity build report JSON was not found beside the Gradle export." >&2
  exit 1
fi
cp "$build_report" "$output_directory/BuildReport.json"
python3 - "$output_directory/BuildReport.json" <<'PY'
import json
import sys

report = json.load(open(sys.argv[1], encoding="utf-8"))
actual = report.get("graphicsApis")
expected = "Vulkan,OpenGLES3"
if actual != expected:
    raise SystemExit(
        f"Release BuildReport graphics API order is {actual!r}; expected {expected!r}"
    )
PY

printf 'APK=%s\nAAB=%s\nSYMBOLS=%s\n' \
  "$apk_output" \
  "$aab_output" \
  "$symbols_output" >"$output_directory/release-paths.env"

printf '%s\n' \
  'STARFALL_ANDROID_CI define absent from release export' \
  'OnAndroidCiCommand absent from release IL2CPP output' \
  'Dynamic debug receivers are disabled when ApplicationInfo is non-debuggable' \
  >"$output_directory/release-ci-surface.txt"

echo "Signed Android release artifacts were written to: $output_directory"
