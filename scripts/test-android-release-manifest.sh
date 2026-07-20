#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/android-release-manifest.sh
source "$script_directory/android-release-manifest.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/starfall-manifest-test.XXXXXX")"
cleanup() {
  rm -rf -- "$temporary_root"
}
trap cleanup EXIT INT TERM

symbolic_manifest="$temporary_root/symbolic.xml"
numeric_manifest="$temporary_root/numeric.xml"
hex_manifest="$temporary_root/hex.xml"
locked_manifest="$temporary_root/locked.xml"

printf '%s\n' '<activity android:screenOrientation="sensorLandscape" />' >"$symbolic_manifest"
printf '%s\n' '<activity android:screenOrientation="6" />' >"$numeric_manifest"
printf '%s\n' '<activity android:screenOrientation="0x00000006" />' >"$hex_manifest"
printf '%s\n' '<activity android:screenOrientation="landscape" />' >"$locked_manifest"

starfall_manifest_allows_both_landscape_orientations "$symbolic_manifest"
starfall_manifest_allows_both_landscape_orientations "$numeric_manifest"
starfall_manifest_allows_both_landscape_orientations "$hex_manifest"
if starfall_manifest_allows_both_landscape_orientations "$locked_manifest"; then
  echo "A one-direction landscape activity must fail release validation." >&2
  exit 1
fi

echo "Android release manifest orientation tests passed."
