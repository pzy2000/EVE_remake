#!/usr/bin/env bash
set -Eeuo pipefail

check_path="${1:-.}"
minimum_gib="${2:-25}"

if [[ ! "$minimum_gib" =~ ^[0-9]+$ ]] || (( minimum_gib < 1 )); then
  echo "minimum_gib must be a positive integer, received: $minimum_gib" >&2
  exit 2
fi

available_kib="$(df -Pk "$check_path" | awk 'NR == 2 { print $4 }')"
required_kib=$((minimum_gib * 1024 * 1024))

if [[ -z "$available_kib" ]] || [[ ! "$available_kib" =~ ^[0-9]+$ ]]; then
  echo "Could not determine free space for: $check_path" >&2
  exit 1
fi

echo "Free space: $((available_kib / 1024 / 1024)) GiB; required: ${minimum_gib} GiB."
if (( available_kib < required_kib )); then
  echo "Insufficient free disk space for Unity Android CI." >&2
  exit 1
fi
