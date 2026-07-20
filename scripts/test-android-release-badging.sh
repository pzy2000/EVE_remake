#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/android-release-badging.sh
source "$script_directory/android-release-badging.sh"

temporary_root="$(mktemp -d "${RUNNER_TEMP:-/tmp}/starfall-badging-test.XXXXXX")"
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT INT TERM

cat >"$temporary_root/api36.txt" <<'EOF'
package: name='com.pzy.starfallodyssey' versionCode='5601' versionName='1.0.0-internal.56.1'
minSdkVersion:'26'
targetSdkVersion:'36'
EOF

cat >"$temporary_root/legacy.txt" <<'EOF'
sdkVersion:'26'
targetSdkVersion:'36'
EOF

[[ "$(starfall_badging_sdk_value "$temporary_root/api36.txt" min)" == "26" ]]
[[ "$(starfall_badging_sdk_value "$temporary_root/api36.txt" target)" == "36" ]]
[[ "$(starfall_badging_sdk_value "$temporary_root/legacy.txt" min)" == "26" ]]
[[ "$(starfall_badging_sdk_value "$temporary_root/legacy.txt" target)" == "36" ]]

if starfall_badging_sdk_value "$temporary_root/api36.txt" unsupported >/dev/null 2>&1; then
  echo "Unsupported SDK metadata kinds must fail." >&2
  exit 1
fi

echo "Android aapt2 badging parser tests passed."
