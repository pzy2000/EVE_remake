#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

apk="${1:?usage: android-back-compat-ci.sh APK [RESULTS_DIRECTORY]}"
results_directory="${2:-artifacts/android-back-compat}"
package_name="com.pzy.starfallodyssey"
expected_activity="$package_name/com.pzy.starfall.mobile.StarfallUnityGameActivity"
expected_architecture="x86_64"
# API 32 clamps a logical width larger than twice the Pixel 2 physical width.
# This job only gates the legacy Back callback, so use the largest stable
# CompactLandscape framebuffer; API 36 separately gates the exact 2748x1172.
width=2160
height=1172
density_dpi=420

if [[ ! -f "$apk" ]]; then
  echo "Smoke APK does not exist: $apk" >&2
  exit 2
fi

mkdir -p "$results_directory"
results_directory="$(cd "$results_directory" && pwd -P)"

reset_emulator() {
  adb shell wm size reset >/dev/null 2>&1 || true
  adb shell wm density reset >/dev/null 2>&1 || true
}
trap reset_emulator EXIT INT TERM

assert_png_dimensions() {
  local png="$1"
  python3 - "$png" "$width" "$height" <<'PY'
import pathlib
import struct
import sys

path = pathlib.Path(sys.argv[1])
expected = (int(sys.argv[2]), int(sys.argv[3]))
data = path.read_bytes()[:24]
if data[:8] != b"\x89PNG\r\n\x1a\n":
    raise SystemExit(f"Not a PNG: {path}")
actual = struct.unpack(">II", data[16:24])
if actual != expected:
    raise SystemExit(f"{path} is {actual[0]}x{actual[1]}, expected {expected[0]}x{expected[1]}")
PY
}

pull_main_menu_layout() {
  local destination="$1"
  for _ in $(seq 1 120); do
    if adb exec-out run-as "$package_name" cat files/starfall-ci-layout-MainMenu.json \
      >"$destination" 2>/dev/null && python3 -m json.tool "$destination" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.25
  done
  echo "Timed out waiting for MainMenu layout evidence." >&2
  return 1
}

layout_has_confirmation() {
  local layout="$1"
  local expected="$2"
  python3 - "$layout" "$expected" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
names = {
    item.get("name")
    for item in payload.get("surfaces", [])
    if item.get("visible", True)
}
present = "confirmation-card" in names
expected = sys.argv[2].lower() == "true"
raise SystemExit(0 if present is expected else 1)
PY
}

adb wait-for-device
starfall_wait_for_android_services "$results_directory/android-services-before-api-check.txt"
sdk="$(adb shell getprop ro.build.version.sdk | tr -d '\r')"
abi="$(adb shell getprop ro.product.cpu.abi | tr -d '\r')"
[[ "$sdk" == "32" ]] || {
  echo "Back compatibility job requires API 32; emulator reported API $sdk." >&2
  exit 1
}
[[ "$abi" == "$expected_architecture" ]] || {
  echo "Back compatibility emulator ABI mismatch: $abi" >&2
  exit 1
}

mapfile -t packaged_abis < <(unzip -Z1 "$apk" \
  | awk -F/ '$1 == "lib" && NF > 2 { print $2 }' | sort -u)
if [[ "${packaged_abis[*]}" != "$expected_architecture" ]]; then
  echo "Smoke APK must contain only $expected_architecture; found ${packaged_abis[*]:-(none)}" >&2
  exit 1
fi

starfall_install_apk_with_system_retries "$apk" "$results_directory"
activity="$(adb shell cmd package resolve-activity --brief "$package_name" \
  | tr -d '\r' | tail -n 1)"
if [[ "$activity" != "$expected_activity" ]]; then
  echo "Launch activity mismatch: expected $expected_activity, received $activity" >&2
  exit 1
fi

# Pixel Launcher may ignore user_rotation while it owns the foreground. Apply
# the final logical landscape axes directly before starting the Unity activity.
adb shell settings put system accelerometer_rotation 0
adb shell settings put system user_rotation 0
adb shell wm size "${width}x${height}"
adb shell wm density "$density_dpi"
for _ in $(seq 1 40); do
  if adb exec-out screencap -p >"$results_directory/display-probe.png" 2>/dev/null && \
    assert_png_dimensions "$results_directory/display-probe.png" 2>/dev/null; then
    break
  fi
  sleep 0.5
done
assert_png_dimensions "$results_directory/display-probe.png"
adb shell wm size >"$results_directory/wm-size.txt"
adb shell wm density >"$results_directory/wm-density.txt"
adb shell dumpsys display >"$results_directory/dumpsys-display.txt"

adb shell pm clear "$package_name" >/dev/null
adb logcat -c
adb shell am start -W -n "$activity" --es unity -force-gles30 \
  >"$results_directory/start.txt"

pid_before=""
for _ in $(seq 1 60); do
  pid_before="$(adb shell pidof "$package_name" | tr -d '\r')"
  [[ "$pid_before" =~ ^[0-9]+$ ]] && break
  sleep 0.5
done
if [[ ! "$pid_before" =~ ^[0-9]+$ ]]; then
  echo "$package_name did not start on API 32." >&2
  exit 1
fi

base_layout="$results_directory/MainMenu.layout.json"
pull_main_menu_layout "$base_layout"
python3 - "$base_layout" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
if payload.get("screen") != "MainMenu":
    raise SystemExit("API 32 evidence is not for MainMenu")
if payload.get("mode") != "CompactLandscape":
    raise SystemExit(f"API 32 MainMenu mode is {payload.get('mode')!r}, expected CompactLandscape")
PY
layout_has_confirmation "$base_layout" false
adb exec-out screencap -p >"$results_directory/MainMenu.png"
assert_png_dimensions "$results_directory/MainMenu.png"

# API 26-32 must take StarfallUnityGameActivity.onBackPressed(), which forwards
# the real system key through StarfallMobileBridge's managed callback. API 33+
# never enters this branch and remains on predictive back.
adb shell input keyevent KEYCODE_BACK
confirmation_layout="$results_directory/MainMenu.BackConfirmation.layout.json"
confirmation_visible=false
for _ in $(seq 1 120); do
  if pull_main_menu_layout "$confirmation_layout" && \
    layout_has_confirmation "$confirmation_layout" true; then
    confirmation_visible=true
    break
  fi
  sleep 0.25
done
if [[ "$confirmation_visible" != true ]]; then
  echo "API 32 KEYCODE_BACK did not open the MainMenu confirmation." >&2
  exit 1
fi
adb exec-out screencap -p >"$results_directory/MainMenu.BackConfirmation.png"
assert_png_dimensions "$results_directory/MainMenu.BackConfirmation.png"

pid_after="$(adb shell pidof "$package_name" | tr -d '\r')"
if [[ "$pid_after" != "$pid_before" ]]; then
  echo "App process changed after API 32 Back: before=$pid_before after=${pid_after:-missing}" >&2
  exit 1
fi

adb shell input keyevent KEYCODE_BACK
confirmation_closed=false
for _ in $(seq 1 80); do
  if pull_main_menu_layout "$results_directory/MainMenu.AfterClose.layout.json" && \
    layout_has_confirmation "$results_directory/MainMenu.AfterClose.layout.json" false; then
    confirmation_closed=true
    break
  fi
  sleep 0.25
done
if [[ "$confirmation_closed" != true ]]; then
  echo "Second API 32 Back did not close the MainMenu confirmation." >&2
  exit 1
fi

pid_final="$(adb shell pidof "$package_name" | tr -d '\r')"
if [[ "$pid_final" != "$pid_before" ]]; then
  echo "App process changed after closing API 32 Back confirmation: " \
    "before=$pid_before final=${pid_final:-missing}" >&2
  exit 1
fi

adb logcat -d --pid="$pid_final" >"$results_directory/app.logcat.txt"
adb logcat -b all -d >"$results_directory/system.logcat.txt"
if grep -Eqi \
  'FATAL EXCEPTION|Unhandled Exception|(^|[[:space:]])([[:alpha:]_][[:alnum:]_.]*Exception|UnityException):|SIGABRT|SIGSEGV|OutOfMemoryError' \
  "$results_directory/app.logcat.txt"; then
  echo "Application fatal error detected during API 32 Back compatibility test." >&2
  exit 1
fi
if grep -Eqi \
  "ANR in ${package_name}|am_anr.*${package_name}|Process: ${package_name},|Fatal signal.*${package_name}" \
  "$results_directory/system.logcat.txt"; then
  echo "Application ANR/native crash detected during API 32 Back compatibility test." >&2
  exit 1
fi

printf 'api=%s\nabi=%s\nactivity=%s\npidBefore=%s\npidAfter=%s\npidFinal=%s\nbackPath=custom GameActivity native compatibility callback\n' \
  "$sdk" "$abi" "$activity" "$pid_before" "$pid_after" "$pid_final" \
  >"$results_directory/summary.txt"
echo "API 32 Back compatibility evidence: $results_directory"
