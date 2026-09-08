#!/usr/bin/env bash

# Shared readiness and installation helpers for the Android emulator gates.
# The caller owns `set -e` policy and must define a writable results directory.

# A runner can keep the adb client blocked indefinitely after the emulator
# transport drops under memory pressure. Route every ordinary adb invocation
# in the acceptance scripts through one bounded client process. Calls which
# need a shorter timeout (for example transport probing) retain their explicit
# outer `timeout` command and therefore bypass this shell function.
starfall_adb_executable="${STARFALL_ADB_EXECUTABLE:-$(type -P adb || true)}"
adb() {
  local timeout_seconds="${STARFALL_ADB_COMMAND_TIMEOUT_SECONDS:-30}"
  local status
  if [[ -z "$starfall_adb_executable" ]]; then
    echo 'adb executable was not found on PATH.' >&2
    return 127
  fi
  if timeout "${timeout_seconds}s" "$starfall_adb_executable" "$@"; then
    return 0
  else
    status=$?
  fi
  if (( status == 124 )); then
    printf 'ADB command timed out after %ss: adb %q %q\n' \
      "$timeout_seconds" "${1:-}" "${2:-}" >&2
  fi
  return "$status"
}

# Evidence reads are idempotent, so a transient hosted-runner transport stall
# can be retried without repeating a tap, Back event, broadcast, or any other
# state-changing operation. Keep the partial file out of the final artifact.
starfall_adb_capture_file() {
  local destination="${1:?capture destination is required}"
  shift
  local attempts="${STARFALL_ADB_READ_ATTEMPTS:-3}"
  local attempt
  local partial="${destination}.adb-partial"
  local status=1

  mkdir -p "$(dirname "$destination")"
  for attempt in $(seq 1 "$attempts"); do
    rm -f -- "$partial"
    if adb "$@" >"$partial"; then
      mv -f -- "$partial" "$destination"
      return 0
    else
      status=$?
    fi
    if (( attempt < attempts )); then
      printf 'Retrying idempotent ADB evidence read (%s/%s): adb %q %q\n' \
        "$attempt" "$attempts" "${1:-}" "${2:-}" >&2
      sleep 1
    fi
  done

  rm -f -- "$partial"
  return "$status"
}

starfall_adb_retry_read() {
  local attempts="${STARFALL_ADB_READ_ATTEMPTS:-3}"
  local attempt
  local status=1

  for attempt in $(seq 1 "$attempts"); do
    if adb "$@"; then
      return 0
    else
      status=$?
    fi
    if (( attempt < attempts )); then
      printf 'Retrying idempotent ADB evidence command (%s/%s): adb %q %q\n' \
        "$attempt" "$attempts" "${1:-}" "${2:-}" >&2
      sleep 1
    fi
  done
  return "$status"
}

# `adb shell` joins ordinary argv into an unquoted device-side command. JSON
# objects containing commas are then subject to mksh brace expansion, which
# silently turns the payload into positional arguments for `am`. Send one
# explicitly quoted command string so the extra reaches BroadcastReceiver
# byte-for-byte. CI payloads are single-line; apostrophes are escaped here so
# this helper remains safe for arbitrary string extras.
starfall_adb_broadcast_string_extra() {
  local action="${1:?broadcast action is required}"
  local key="${2:?string extra key is required}"
  local value="${3-}"
  local escaped_value

  if [[ ! "$action" =~ ^[A-Za-z0-9_.]+$ || ! "$key" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "Invalid Android broadcast action or extra key: $action $key" >&2
    return 2
  fi
  if [[ "$value" == *$'\n'* || "$value" == *$'\r'* ]]; then
    echo 'Android broadcast string extras must be single-line text.' >&2
    return 2
  fi

  escaped_value="${value//\'/\'\\\'\'}"
  adb shell "am broadcast -a $action --es $key '$escaped_value'"
}

starfall_android_app_files_directory() {
  local package_name="${1:?package name is required}"
  if [[ ! "$package_name" =~ ^[A-Za-z0-9_]+([.][A-Za-z0-9_]+)+$ ]]; then
    echo "Invalid Android package name: $package_name" >&2
    return 2
  fi
  printf '/storage/emulated/0/Android/data/%s/files\n' "$package_name"
}

starfall_android_app_file_path() {
  local package_name="${1:?package name is required}"
  local relative_path="${2:?relative app file path is required}"
  if [[ "$relative_path" == /* || "$relative_path" == "." || \
    "$relative_path" == ".." || "$relative_path" == ../* || \
    "$relative_path" == */../* || "$relative_path" == */.. ]]; then
    echo "Invalid relative app file path: $relative_path" >&2
    return 2
  fi
  printf '%s/%s\n' \
    "$(starfall_android_app_files_directory "$package_name")" "$relative_path"
}

starfall_wait_for_unity_render_ready() {
  local package_name="${1:?package name is required}"
  local destination="${2:?render-ready evidence destination is required}"
  local expected_scene="${3:-MainMenu}"
  local attempts="${4:-240}"
  local remote_path
  remote_path="$(starfall_android_app_file_path \
    "$package_name" starfall-ci-render-ready.json)"
  mkdir -p "$(dirname "$destination")"

  for _ in $(seq 1 "$attempts"); do
    if adb exec-out cat "$remote_path" >"$destination" 2>/dev/null && \
      python3 - "$destination" "$expected_scene" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
ready = (
    payload.get("scene") == sys.argv[2]
    and int(payload.get("frameCount") or 0) > 0
)
raise SystemExit(0 if ready else 1)
PY
    then
      return 0
    fi
    sleep 0.25
  done

  echo "Timed out waiting for Unity's rendered $expected_scene frame." >&2
  return 1
}

starfall_start_continuous_logcat() {
  local destination="${1:?logcat destination is required}"
  starfall_stop_continuous_logcat
  mkdir -p "$(dirname "$destination")"
  timeout 180s "$starfall_adb_executable" logcat -v threadtime \
    >"$destination" 2>&1 &
  starfall_logcat_capture_pid=$!
}

starfall_stop_continuous_logcat() {
  if [[ "${starfall_logcat_capture_pid:-}" =~ ^[0-9]+$ ]]; then
    kill "$starfall_logcat_capture_pid" >/dev/null 2>&1 || true
    wait "$starfall_logcat_capture_pid" >/dev/null 2>&1 || true
  fi
  starfall_logcat_capture_pid=""
}

starfall_capture_emulator_failure() {
  local package_name="${1:?package name is required}"
  local evidence_directory="${2:?evidence directory is required}"
  local label="${3:-failure}"
  local process_id

  mkdir -p "$evidence_directory"
  process_id="$(adb shell pidof "$package_name" 2>/dev/null | tr -d '\r' || true)"
  if [[ "$process_id" =~ ^[0-9]+$ ]]; then
    adb logcat -d --pid="$process_id" \
      >"$evidence_directory/$label.app.logcat.txt" 2>&1 || true
  else
    printf 'process=missing\npackage=%s\n' "$package_name" \
      >"$evidence_directory/$label.process.txt"
  fi
  adb logcat -b all -d >"$evidence_directory/$label.system.logcat.txt" 2>&1 || true
  adb shell dumpsys activity top \
    >"$evidence_directory/$label.activity-top.txt" 2>&1 || true
  adb shell dumpsys window \
    >"$evidence_directory/$label.window.txt" 2>&1 || true
  adb exec-out screencap -p \
    >"$evidence_directory/$label.png" 2>/dev/null || true
}

starfall_confirm_immersive_mode() {
  local evidence_file="${1:?evidence file is required}"
  local actual

  mkdir -p "$(dirname "$evidence_file")"
  adb shell settings put secure immersive_mode_confirmations confirmed
  actual="$(adb shell settings get secure immersive_mode_confirmations \
    | tr -d '\r')"
  printf 'requested=confirmed\nactual=%s\n' "$actual" >"$evidence_file"
  if [[ "$actual" != "confirmed" ]]; then
    echo "Could not pre-confirm the System UI immersive-mode prompt; see $evidence_file." >&2
    return 1
  fi
}

starfall_clear_immersive_mode_confirmation() {
  local evidence_directory="${1:?evidence directory is required}"
  local consecutive_clear=0
  local attempt
  local window_file

  mkdir -p "$evidence_directory"
  for attempt in $(seq 1 20); do
    window_file="$evidence_directory/window-attempt-$attempt.txt"
    adb shell dumpsys window windows >"$window_file"
    if grep -Eq 'mCurrentFocus=.*ImmersiveModeConfirmation' "$window_file"; then
      printf 'attempt=%s action=back owner=SystemUI overlay=ImmersiveModeConfirmation\n' \
        "$attempt" >>"$evidence_directory/actions.txt"
      adb shell input keyevent KEYCODE_BACK
      consecutive_clear=0
    else
      consecutive_clear=$((consecutive_clear + 1))
      if (( consecutive_clear >= 12 )); then
        printf 'status=clear\nattempts=%s\n' "$attempt" \
          >"$evidence_directory/summary.txt"
        return 0
      fi
    fi
    sleep 0.25
  done

  echo "System UI immersive-mode confirmation did not clear; see $evidence_directory." >&2
  return 1
}

starfall_wait_for_adb_transport() {
  local evidence_file="${1:?evidence file is required}"
  local attempts="${2:-3}"
  local attempt_log
  local state
  local attempt

  mkdir -p "$(dirname "$evidence_file")"
  : >"$evidence_file"
  for attempt in $(seq 1 "$attempts"); do
    attempt_log="${evidence_file%.txt}.attempt-$attempt.txt"
    timeout 30s adb wait-for-device >"$attempt_log" 2>&1 || true
    state="$(timeout 10s adb get-state 2>/dev/null | tr -d '\r' || true)"
    printf 'attempt=%s state=%q\n' "$attempt" "$state" >>"$evidence_file"
    if [[ "$state" == "device" ]]; then
      return 0
    fi

    adb reconnect >>"$attempt_log" 2>&1 || true
    if (( attempt == 2 )); then
      adb kill-server >>"$attempt_log" 2>&1 || true
      adb start-server >>"$attempt_log" 2>&1 || true
    fi
    sleep 2
  done

  echo "ADB transport did not become ready; see $evidence_file." >&2
  return 1
}

starfall_wait_for_android_services() {
  local evidence_file="${1:?evidence file is required}"
  local attempts="${2:-120}"
  local consecutive_ready=0
  local boot_completed
  local package_service
  local package_command_status
  local attempt

  mkdir -p "$(dirname "$evidence_file")"
  : >"$evidence_file"
  starfall_wait_for_adb_transport "${evidence_file%.txt}.transport.txt"

  for attempt in $(seq 1 "$attempts"); do
    boot_completed="$(timeout 10s adb shell getprop sys.boot_completed 2>/dev/null \
      | tr -d '\r' || true)"
    package_service="$(timeout 10s adb shell service check package 2>/dev/null \
      | tr -d '\r' || true)"
    if timeout 15s adb shell cmd package list packages >/dev/null 2>&1; then
      package_command_status="ready"
    else
      package_command_status="unavailable"
    fi

    printf 'attempt=%s boot=%q packageService=%q packageCommand=%s\n' \
      "$attempt" "$boot_completed" "$package_service" "$package_command_status" \
      >>"$evidence_file"

    if [[ "$boot_completed" == "1" && "$package_service" == *"found"* && \
      "$package_command_status" == "ready" ]]; then
      consecutive_ready=$((consecutive_ready + 1))
      if (( consecutive_ready >= 2 )); then
        return 0
      fi
    else
      consecutive_ready=0
    fi
    sleep 2
  done

  echo "Android package service did not become stable; see $evidence_file." >&2
  return 1
}

starfall_install_apk_with_system_retries() {
  local apk="${1:?APK is required}"
  local evidence_directory="${2:?evidence directory is required}"
  local readiness_file
  local attempt_file
  local install_status
  local attempt

  mkdir -p "$evidence_directory"
  for attempt in $(seq 1 3); do
    readiness_file="$evidence_directory/android-services-attempt-$attempt.txt"
    starfall_wait_for_android_services "$readiness_file"
    attempt_file="$evidence_directory/install-attempt-$attempt.txt"

    if adb install -r -t "$apk" >"$attempt_file" 2>&1; then
      cp "$attempt_file" "$evidence_directory/install.txt"
      printf 'successfulAttempt=%s\n' "$attempt" \
        >"$evidence_directory/install-summary.txt"
      # Google API images can briefly drop the ADB transport while Package
      # Manager completes post-install work under memory pressure. Do not let
      # the first resolve/start command race that transition: require the
      # transport and package service to become stable again after success.
      starfall_wait_for_android_services \
        "$evidence_directory/android-services-post-install-attempt-$attempt.txt"
      return 0
    else
      install_status=$?
    fi
    cat "$attempt_file" >&2

    # Only transport and Android package-service failures are retried. Any
    # INSTALL_FAILED_* result is an APK defect and remains an immediate failure.
    if grep -Eqi \
      'Broken pipe|device (offline|unauthorized)|no devices/emulators found|cannot connect to daemon|closed$|Failure calling service package' \
      "$attempt_file" && ! grep -Fq 'INSTALL_FAILED_' "$attempt_file"; then
      if (( attempt < 3 )); then
        starfall_wait_for_adb_transport \
          "$evidence_directory/adb-transport-after-install-failure-attempt-$attempt.txt"
        sleep 2
        continue
      fi
    fi

    cp "$attempt_file" "$evidence_directory/install.txt"
    return "$install_status"
  done

  echo "APK installation exhausted system-service retries." >&2
  return 1
}

# First-run consent is a real UI action, never a preference injection or CI bypass.
starfall_accept_privacy_if_required() {
  local package_name="$1"
  local evidence_root="$2/privacy-consent"
  mkdir -p "$evidence_root"
  for attempt in $(seq 1 45); do
    if [[ -n "$(adb shell pidof "$package_name" 2>/dev/null | tr -d '\r')" ]]; then
      return 0
    fi
    adb shell uiautomator dump /sdcard/starfall-privacy-ui.xml >/dev/null 2>&1 || true
    adb pull /sdcard/starfall-privacy-ui.xml "$evidence_root/ui.xml" >/dev/null 2>&1 || true
    local coordinate consent_x consent_y
    coordinate="$(python3 - "$evidence_root/ui.xml" "$package_name" <<'PY'
import sys, re, xml.etree.ElementTree as E
try: root=E.parse(sys.argv[1]).getroot()
except (OSError,E.ParseError): raise SystemExit(0)
for node in root.iter('node'):
    if node.get('package')==sys.argv[2] and node.get('text')=='同意并进入':
        b=list(map(int,re.findall(r'\d+',node.get('bounds',''))))
        if len(b)==4: print((b[0]+b[2])//2,(b[1]+b[3])//2)
        break
PY
)"
    if [[ "$coordinate" =~ ^[0-9]+\ [0-9]+$ ]]; then
      adb shell screencap -p /sdcard/starfall-privacy.png
      adb pull /sdcard/starfall-privacy.png "$evidence_root/consent.png" >/dev/null
      read -r consent_x consent_y <<<"$coordinate"
      adb shell input tap "$consent_x" "$consent_y"
    fi
    sleep 1
  done
  echo "Privacy consent did not reach the Unity process: $evidence_root" >&2
  return 1
}
