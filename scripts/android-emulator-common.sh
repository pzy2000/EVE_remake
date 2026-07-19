#!/usr/bin/env bash

# Shared readiness and installation helpers for the Android emulator gates.
# The caller owns `set -e` policy and must define a writable results directory.

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
  adb wait-for-device

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
        adb reconnect >/dev/null 2>&1 || true
        adb wait-for-device
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
