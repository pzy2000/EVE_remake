#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT INT TERM
apk="$temporary_directory/smoke.apk"
: >"$apk"

starfall_wait_for_android_services() {
  : >"${1:?evidence file is required}"
}

install_mode="recoverable"
install_calls=0
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

echo 'Android emulator system-retry policy tests passed.'
