#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

sleep() {
  :
}

timeout() {
  shift
  "$@"
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

if grep -En \
  'run-as.*(persistent_data_directory|starfall_android_app_file_path|/storage/emulated/0/Android/data)' \
  "$script_directory"/android-{back-compat-ci,emulator-ci,stability-ci}.sh; then
  echo 'External Unity persistent data must be accessed by the ADB shell, not run-as.' >&2
  exit 1
fi

graphics_settings="$script_directory/../UnityProject/ProjectSettings/GraphicsSettings.asset"
grep -Fq \
  '{fileID: 4800000, guid: 650dd9526735d5b46b79224bc6e94025, type: 3}' \
  "$graphics_settings" || {
  echo 'URP Unlit must remain in Always Included Shaders for runtime materials.' >&2
  exit 1
}
grep -Fq 'timeout 30s adb wait-for-device' \
  "$script_directory/android-emulator-common.sh" || {
  echo 'ADB wait-for-device must remain bounded by a timeout.' >&2
  exit 1
}
if grep -En '^[[:space:]]*adb wait-for-device([[:space:]]|$)' \
  "$script_directory"/android-{back-compat-ci,emulator-ci,emulator-common}.sh; then
  echo 'Every ADB wait-for-device call must use the bounded transport helper.' >&2
  exit 1
fi

starfall_wait_for_android_services() {
  local evidence_file="${1:?evidence file is required}"
  service_wait_files+=("$(basename "$evidence_file")")
  : >"$evidence_file"
}

install_mode="recoverable"
install_calls=0
service_wait_files=()
transport_mode="ready"
transport_wait_calls=0
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
    wait-for-device)
      transport_wait_calls=$((transport_wait_calls + 1))
      if [[ "$transport_mode" == "recoverable" && "$transport_wait_calls" == "1" ]]; then
        echo 'device offline' >&2
        return 1
      fi
      return 0
      ;;
    get-state)
      if [[ "$transport_mode" == "recoverable" && "$transport_wait_calls" == "1" ]]; then
        echo 'offline'
      else
        echo 'device'
      fi
      ;;
    reconnect|kill-server|start-server)
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

transport_mode="recoverable"
transport_evidence="$temporary_directory/transport.txt"
starfall_wait_for_adb_transport "$transport_evidence"
[[ "$transport_wait_calls" == "2" ]]
grep -Fqx 'attempt=1 state=offline' "$transport_evidence"
grep -Fqx 'attempt=2 state=device' "$transport_evidence"
transport_mode="ready"
transport_wait_calls=0

first_results="$temporary_directory/recoverable"
starfall_install_apk_with_system_retries "$apk" "$first_results"
grep -Fqx 'successfulAttempt=2' "$first_results/install-summary.txt"
grep -Fqx 'Success' "$first_results/install.txt"
[[ "$install_calls" == "2" ]]
[[ "${service_wait_files[*]}" == \
  "android-services-attempt-1.txt android-services-attempt-2.txt android-services-post-install-attempt-2.txt" ]]
test -f "$first_results/android-services-post-install-attempt-2.txt"

install_mode="apk-error"
install_calls=0
service_wait_files=()
second_results="$temporary_directory/apk-error"
if starfall_install_apk_with_system_retries "$apk" "$second_results"; then
  echo 'APK validation failure was incorrectly retried or accepted.' >&2
  exit 1
fi
[[ "$install_calls" == "1" ]]
[[ "${service_wait_files[*]}" == "android-services-attempt-1.txt" ]]
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
python3 "$script_directory/test-android-emulator-graphics.py"
