#!/usr/bin/env bash
set -Eeuo pipefail

repository="${1:-.}"

git -C "$repository" diff --exit-code
git -C "$repository" diff --cached --exit-code

untracked="$(git -C "$repository" ls-files --others --exclude-standard)"
if [[ -n "$untracked" ]]; then
  echo "Unexpected untracked files were produced:" >&2
  printf '%s\n' "$untracked" >&2
  exit 1
fi

echo "Tracked and untracked workspace state is clean."
