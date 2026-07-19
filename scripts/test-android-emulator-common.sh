#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

sleep() {
  :
}

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT INT TERM
apk="$temporary_directory/smoke.apk"
: >"$apk"

expected_files_directory="/storage/emulated/0/Android/data/com.pzy.starfallodyssey/files"
[[ "$(starfall_android_app_files_directory com.pzy.starfallodyssey)" == \
  "$expected_files_directory" ]]
[[ "$(starfall_android_app_file_path \
  com.pzy.starfallodyssey Saves/auto.json)" == \
  "$expected_files_directory/Saves/auto.json" ]]
if starfall_android_app_file_path com.pzy.starfallodyssey ../files >/dev/null 2>&1; then
  echo 'App file path traversal was incorrectly accepted.' >&2
  exit 1
fi
if starfall_android_app_files_directory invalid-package >/dev/null 2>&1; then
  echo 'Invalid Android package name was incorrectly accepted.' >&2
  exit 1
fi

starfall_wait_for_android_services() {
  : >"${1:?evidence file is required}"
}

install_mode="recoverable"
install_calls=0
immersive_value="confirmed"
immersive_window_attempts=0
immersive_back_calls=0
adb() {
  case "${1:-}" in
    install)
      install_calls=$((install_calls + 1))
      if [[ "$install_mode" == "recoverable" && "$install_calls" == "1" ]]; then
        echo 'cmd: Failure calling service package: Broken pipe (32)' >&2
        return 32
      fi
      if [[ "$install_mode" == "apk-error" ]]; then
        echo 'Failure [INSTALL_FAILED_NO_MATCHING_ABIS]' >&2
        return 1
      fi
      echo 'Success'
      ;;
    reconnect|wait-for-device)
      return 0
      ;;
    shell)
      if [[ "$*" == "shell settings put secure immersive_mode_confirmations confirmed" ]]; then
        return 0
      fi
      if [[ "$*" == "shell settings get secure immersive_mode_confirmations" ]]; then
        printf '%s\n' "$immersive_value"
        return 0
      fi
      if [[ "$*" == "shell dumpsys window windows" ]]; then
        immersive_window_attempts=$((immersive_window_attempts + 1))
        if (( immersive_window_attempts == 1 )); then
          echo 'mCurrentFocus=Window{123 u0 ImmersiveModeConfirmation}'
        else
          echo 'mCurrentFocus=Window{456 u0 com.pzy.starfallodyssey/Main}'
        fi
        return 0
      fi
      if [[ "$*" == "shell input keyevent KEYCODE_BACK" ]]; then
        immersive_back_calls=$((immersive_back_calls + 1))
        return 0
      fi
      echo "Unexpected adb shell command in test: $*" >&2
      return 2
      ;;
    *)
      echo "Unexpected adb command in test: $*" >&2
      return 2
      ;;
  esac
}

first_results="$temporary_directory/recoverable"
starfall_install_apk_with_system_retries "$apk" "$first_results"
grep -Fqx 'successfulAttempt=2' "$first_results/install-summary.txt"
grep -Fqx 'Success' "$first_results/install.txt"
[[ "$install_calls" == "2" ]]

install_mode="apk-error"
install_calls=0
second_results="$temporary_directory/apk-error"
if starfall_install_apk_with_system_retries "$apk" "$second_results"; then
  echo 'APK validation failure was incorrectly retried or accepted.' >&2
  exit 1
fi
[[ "$install_calls" == "1" ]]
grep -Fq 'INSTALL_FAILED_NO_MATCHING_ABIS' "$second_results/install.txt"

immersive_evidence="$temporary_directory/immersive-mode-setting.txt"
starfall_confirm_immersive_mode "$immersive_evidence"
grep -Fqx 'requested=confirmed' "$immersive_evidence"
grep -Fqx 'actual=confirmed' "$immersive_evidence"

immersive_value="null"
if starfall_confirm_immersive_mode "$temporary_directory/immersive-mode-rejected.txt"; then
  echo 'A rejected immersive-mode setting was incorrectly accepted.' >&2
  exit 1
fi

immersive_clear_evidence="$temporary_directory/immersive-mode-confirmation"
starfall_clear_immersive_mode_confirmation "$immersive_clear_evidence"
[[ "$immersive_back_calls" == "1" ]]
grep -Fq 'owner=SystemUI overlay=ImmersiveModeConfirmation' \
  "$immersive_clear_evidence/actions.txt"
grep -Fqx 'status=clear' "$immersive_clear_evidence/summary.txt"

echo 'Android emulator system-retry policy tests passed.'
