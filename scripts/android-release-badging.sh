#!/usr/bin/env bash

# aapt2 36 reports the minimum SDK as minSdkVersion, while older build tools
# used sdkVersion. Keep release validation compatible with both spellings.
starfall_badging_sdk_value() {
  local badging_path="${1:?missing aapt2 badging path}"
  local sdk_kind="${2:?missing SDK kind}"

  case "$sdk_kind" in
    min)
      awk -F"'" '$1 == "minSdkVersion:" || $1 == "sdkVersion:" { print $2; exit }' \
        "$badging_path"
      ;;
    target)
      awk -F"'" '$1 == "targetSdkVersion:" { print $2; exit }' "$badging_path"
      ;;
    *)
      echo "Unsupported SDK metadata kind: $sdk_kind" >&2
      return 2
      ;;
  esac
}
