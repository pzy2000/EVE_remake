#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/android-release-badging.sh
source "$script_directory/android-release-badging.sh"
# shellcheck source=scripts/android-release-manifest.sh
source "$script_directory/android-release-manifest.sh"

apk="${1:?usage: validate-android-release.sh APK AAB PACKAGE VERSION_CODE VERSION_NAME OUTPUT_DIRECTORY}"
aab="${2:?missing AAB}"
expected_package="${3:?missing package name}"
expected_version_code="${4:?missing version code}"
expected_version_name="${5:?missing version name}"
output_directory="${6:?missing validation output directory}"

for required in ANDROID_SDK_ROOT ANDROID_NDK_HOME BUNDLETOOL_JAR; do
  if [[ -z "${!required:-}" ]]; then
    echo "Required environment variable is missing: $required" >&2
    exit 2
  fi
done

for artifact in "$apk" "$aab" "$BUNDLETOOL_JAR"; do
  if [[ ! -f "$artifact" ]]; then
    echo "Required file does not exist: $artifact" >&2
    exit 2
  fi
done

mkdir -p "$output_directory"
apk="$(cd "$(dirname "$apk")" && pwd -P)/$(basename "$apk")"
aab="$(cd "$(dirname "$aab")" && pwd -P)/$(basename "$aab")"
output_directory="$(cd "$output_directory" && pwd -P)"
temporary_root="$(mktemp -d "${RUNNER_TEMP:-/tmp}/starfall-android-validation.XXXXXX")"
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT INT TERM

build_tools="$ANDROID_SDK_ROOT/build-tools/36.0.0"
aapt2="$build_tools/aapt2"
apksigner="$build_tools/apksigner"
zipalign="$build_tools/zipalign"
readelf="$(find "$ANDROID_NDK_HOME/toolchains/llvm/prebuilt" \
  \( -type f -o -type l \) -name llvm-readelf -print -quit)"

for tool in "$aapt2" "$apksigner" "$zipalign" "$readelf"; do
  if [[ ! -x "$tool" ]]; then
    echo "Required Android validation tool is unavailable: $tool" >&2
    exit 2
  fi
done

badging="$output_directory/aapt2-badging.txt"
apk_signature="$output_directory/apksigner.txt"
aab_signature="$output_directory/aab-keytool.txt"
bundle_manifest="$output_directory/bundle-manifest.xml"
universal_apks="$output_directory/bundletool-universal.apks"
universal_apk="$output_directory/bundletool-universal.apk"
universal_badging="$output_directory/bundletool-universal-aapt2-badging.txt"
universal_signature="$output_directory/bundletool-universal-apksigner.txt"
apk_icon_xmltree="$output_directory/aapt2-application-icon-xmltree.txt"
validation_log="$output_directory/release-validation.txt"

"$aapt2" dump badging "$apk" | tee "$badging"

actual_package="$(sed -n "s/^package: name='\([^']*\)'.*/\1/p" "$badging")"
actual_version_code="$(sed -n "s/^package:.* versionCode='\([^']*\)'.*/\1/p" "$badging")"
actual_version_name="$(sed -n "s/^package:.* versionName='\([^']*\)'.*/\1/p" "$badging")"
actual_min_sdk="$(starfall_badging_sdk_value "$badging" min)"
actual_target_sdk="$(starfall_badging_sdk_value "$badging" target)"
actual_launch_activity="$(sed -n "s/^launchable-activity: name='\([^']*\)'.*/\1/p" "$badging")"
actual_icon_path="$(starfall_badging_application_icon "$badging")"

[[ "$actual_package" == "$expected_package" ]] || {
  echo "APK package mismatch: $actual_package" >&2
  exit 1
}
[[ "$actual_version_code" == "$expected_version_code" ]] || {
  echo "APK versionCode mismatch: $actual_version_code" >&2
  exit 1
}
[[ "$actual_version_name" == "$expected_version_name" ]] || {
  echo "APK versionName mismatch: $actual_version_name" >&2
  exit 1
}
[[ "$actual_min_sdk" == "26" ]] || {
  echo "APK minSdk must be 26, received: $actual_min_sdk" >&2
  exit 1
}
[[ "$actual_target_sdk" == "36" ]] || {
  echo "APK targetSdk must be 36, received: $actual_target_sdk" >&2
  exit 1
}
[[ "$actual_launch_activity" == "com.pzy.starfall.mobile.StarfallUnityGameActivity" ]] || {
  echo "APK launch activity mismatch: $actual_launch_activity" >&2
  exit 1
}
if grep -q '^application-debuggable' "$badging"; then
  echo "Release APK is debuggable." >&2
  exit 1
fi

mapfile -t apk_abis < <(unzip -Z1 "$apk" | awk -F/ '$1 == "lib" && NF > 2 { print $2 }' | sort -u)
mapfile -t aab_abis < <(unzip -Z1 "$aab" | awk -F/ '$1 == "base" && $2 == "lib" && NF > 3 { print $3 }' | sort -u)
if [[ "${apk_abis[*]}" != "arm64-v8a" ]]; then
  echo "APK must contain only arm64-v8a; found: ${apk_abis[*]:-(none)}" >&2
  exit 1
fi
if [[ "${aab_abis[*]}" != "arm64-v8a" ]]; then
  echo "AAB must contain only arm64-v8a; found: ${aab_abis[*]:-(none)}" >&2
  exit 1
fi

for density in 120 160 240 320 480 640; do
  density_icon="$(starfall_badging_density_icon "$badging" "$density")"
  if [[ -z "$density_icon" ]]; then
    echo "APK has no launcher icon for density $density." >&2
    exit 1
  fi
done
if [[ ! "$actual_icon_path" =~ ^res/.+\.xml$ ]]; then
  echo "APK application icon is not an adaptive XML resource: $actual_icon_path" >&2
  exit 1
fi
"$aapt2" dump xmltree "$apk" --file "$actual_icon_path" >"$apk_icon_xmltree"
grep -Eq '^[[:space:]]*E: adaptive-icon([[:space:]]|$)' "$apk_icon_xmltree" || {
  echo "APK application icon XML is not rooted at adaptive-icon." >&2
  exit 1
}

"$apksigner" verify --verbose --print-certs "$apk" | tee "$apk_signature"
"$zipalign" -c -P 16 -v 4 "$apk" >>"$validation_log"

java -jar "$BUNDLETOOL_JAR" validate --bundle="$aab" | tee -a "$validation_log"
java -jar "$BUNDLETOOL_JAR" dump manifest \
  --bundle="$aab" \
  --module=base >"$bundle_manifest"
jarsigner -verify -certs "$aab" >>"$validation_log"
keytool -printcert -jarfile "$aab" | tee "$aab_signature"

grep -Fq "package=\"$expected_package\"" "$bundle_manifest" || {
  echo "AAB manifest package mismatch." >&2
  exit 1
}
grep -Eq 'android:minSdkVersion="26"' "$bundle_manifest" || {
  echo "AAB manifest minSdk is not 26." >&2
  exit 1
}
grep -Eq 'android:targetSdkVersion="36"' "$bundle_manifest" || {
  echo "AAB manifest targetSdk is not 36." >&2
  exit 1
}
if grep -Eq 'android:debuggable="true"' "$bundle_manifest"; then
  echo "Release AAB is debuggable." >&2
  exit 1
fi
if grep -Eq 'com\.pzy\.starfall\.mobile\.DEBUG_(WINDOW_LAYOUT|COMMAND)' "$bundle_manifest"; then
  echo "Release manifest exposes a smoke-only debug broadcast action." >&2
  exit 1
fi
grep -Fq 'android:name="com.pzy.starfall.mobile.StarfallUnityGameActivity"' "$bundle_manifest" || {
  echo "AAB does not use the custom Unity GameActivity entry point." >&2
  exit 1
}
[[ "$(grep -Fc 'android.intent.category.LAUNCHER' "$bundle_manifest")" == "1" ]] || {
  echo "AAB must contain exactly one LAUNCHER category." >&2
  exit 1
}
grep -Fq 'android:resizeableActivity="true"' "$bundle_manifest" || {
  echo "AAB custom GameActivity is not resizable." >&2
  exit 1
}
starfall_manifest_allows_both_landscape_orientations "$bundle_manifest" || {
  echo "AAB custom GameActivity does not permit both landscape orientations." >&2
  exit 1
}
grep -Fq 'android:enableOnBackInvokedCallback="true"' "$bundle_manifest" || {
  echo "AAB application did not opt into predictive Back." >&2
  exit 1
}

apk_cert="$(sed -n 's/^Signer #1 certificate SHA-256 digest: //p' "$apk_signature" \
  | head -n 1 | tr -d ':[:space:]' | tr '[:upper:]' '[:lower:]')"
aab_cert="$(sed -n 's/^[[:space:]]*SHA256: //p' "$aab_signature" \
  | head -n 1 | tr -d ':[:space:]' | tr '[:upper:]' '[:lower:]')"
if [[ -z "$apk_cert" ]] || [[ -z "$aab_cert" ]] || [[ "$apk_cert" != "$aab_cert" ]]; then
  echo "APK and AAB signing certificates differ." >&2
  exit 1
fi

validation_keystore="$temporary_root/bundletool-validation.keystore"
keytool -genkeypair \
  -keystore "$validation_keystore" \
  -storepass starfall-validation \
  -keypass starfall-validation \
  -alias validation \
  -keyalg RSA \
  -keysize 2048 \
  -validity 2 \
  -dname 'CN=STARFALL CI Validation,O=Local CI,C=US' >/dev/null 2>&1
java -jar "$BUNDLETOOL_JAR" build-apks \
  --bundle="$aab" \
  --output="$universal_apks" \
  --mode=universal \
  --ks="$validation_keystore" \
  --ks-pass=pass:starfall-validation \
  --ks-key-alias=validation \
  --key-pass=pass:starfall-validation
unzip -p "$universal_apks" universal.apk >"$universal_apk"
test -s "$universal_apk"

"$aapt2" dump badging "$universal_apk" | tee "$universal_badging"
universal_package="$(sed -n "s/^package: name='\([^']*\)'.*/\1/p" "$universal_badging")"
universal_version_code="$(sed -n "s/^package:.* versionCode='\([^']*\)'.*/\1/p" "$universal_badging")"
universal_version_name="$(sed -n "s/^package:.* versionName='\([^']*\)'.*/\1/p" "$universal_badging")"
universal_min_sdk="$(starfall_badging_sdk_value "$universal_badging" min)"
universal_target_sdk="$(starfall_badging_sdk_value "$universal_badging" target)"
[[ "$universal_package" == "$expected_package" ]] || {
  echo "Universal APK package mismatch: $universal_package" >&2
  exit 1
}
[[ "$universal_version_code" == "$expected_version_code" ]] || {
  echo "Universal APK versionCode mismatch: $universal_version_code" >&2
  exit 1
}
[[ "$universal_version_name" == "$expected_version_name" ]] || {
  echo "Universal APK versionName mismatch: $universal_version_name" >&2
  exit 1
}
[[ "$universal_min_sdk" == "26" && "$universal_target_sdk" == "36" ]] || {
  echo "Universal APK SDK metadata must be min 26 / target 36." >&2
  exit 1
}
if grep -q '^application-debuggable' "$universal_badging"; then
  echo "Bundletool universal APK is debuggable." >&2
  exit 1
fi
mapfile -t universal_abis < <(unzip -Z1 "$universal_apk" \
  | awk -F/ '$1 == "lib" && NF > 2 { print $2 }' | sort -u)
if [[ "${universal_abis[*]}" != "arm64-v8a" ]]; then
  echo "Bundletool universal APK must contain only arm64-v8a; found: ${universal_abis[*]:-(none)}" >&2
  exit 1
fi
"$apksigner" verify --verbose --print-certs "$universal_apk" | tee "$universal_signature"
"$zipalign" -c -P 16 -v 4 "$universal_apk" >>"$validation_log"

mkdir -p "$temporary_root/apk" "$temporary_root/aab" "$temporary_root/universal"
(
  cd "$temporary_root/apk"
  unzip -qq "$apk" 'lib/arm64-v8a/*.so'
)
(
  cd "$temporary_root/aab"
  unzip -qq "$aab" 'base/lib/arm64-v8a/*.so'
)
(
  cd "$temporary_root/universal"
  unzip -qq "$universal_apk" 'lib/arm64-v8a/*.so'
)

python3 - "$readelf" "$temporary_root/apk" "$temporary_root/aab" "$temporary_root/universal" <<'PY'
import pathlib
import re
import subprocess
import sys

readelf = sys.argv[1]
roots = [pathlib.Path(path) for path in sys.argv[2:]]
libraries = sorted(file for root in roots for file in root.rglob("*.so"))
if not libraries:
    raise SystemExit("No ARM64 shared libraries were extracted")

for library in libraries:
    output = subprocess.check_output([readelf, "-lW", str(library)], text=True)
    alignments = [
        int(value, 16)
        for value in re.findall(r"^\s*LOAD\s+.*\s+(0x[0-9a-fA-F]+)\s*$", output, re.MULTILINE)
    ]
    if not alignments:
        raise SystemExit(f"No ELF LOAD segments found in {library}")
    if min(alignments) < 0x4000:
        raise SystemExit(
            f"{library} has LOAD alignment below 16 KiB: "
            + ", ".join(hex(value) for value in alignments)
        )
    print(f"16 KiB ELF alignment OK: {library}")
PY

{
  echo "package=$actual_package"
  echo "versionCode=$actual_version_code"
  echo "versionName=$actual_version_name"
  echo "minSdk=$actual_min_sdk"
  echo "targetSdk=$actual_target_sdk"
  echo "launchActivity=$actual_launch_activity"
  echo "abis=${apk_abis[*]}"
  echo "certificateSha256=$apk_cert"
  echo "debuggable=false"
  echo "zipAlignment=16KiB"
  echo "elfLoadAlignment>=16KiB"
  echo "bundletool=validated"
  echo "bundletoolUniversalApk=validated-arm64-only"
  echo "releaseDebugBroadcastReceiver=absent"
} >>"$validation_log"

echo "Android release validation passed."
