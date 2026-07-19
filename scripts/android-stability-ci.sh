#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

results_directory="${1:-artifacts/android-emulator/stability}"
package_name="com.pzy.starfallodyssey"
command_action="com.pzy.starfall.mobile.DEBUG_COMMAND"
command_evidence="starfall-ci-command.json"
request_id=0
last_command_json=""
persistent_data_directory="$(starfall_android_app_files_directory "$package_name")"

mkdir -p "$results_directory/commands"
results_directory="$(cd "$results_directory" && pwd -P)"

activity="$(adb shell cmd package resolve-activity --brief "$package_name" \
  | tr -d '\r' | tail -n 1)"
if [[ "$activity" != "$package_name/"* ]]; then
  echo "Could not resolve launch activity for $package_name: $activity" >&2
  exit 1
fi

wait_for_process() {
  for _ in $(seq 1 60); do
    if [[ -n "$(adb shell pidof "$package_name" | tr -d '\r')" ]]; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $package_name." >&2
  return 1
}

run_ci_command() {
  local command="$1"
  local remote_json="$results_directory/command-current.json"
  local command_prefix
  local attempt
  local broadcast_attempts=0
  local process_id
  request_id=$((request_id + 1))
  command_prefix="$results_directory/commands/$(printf '%04d' "$request_id")-$command"

  for attempt in $(seq 1 180); do
    if (( attempt == 1 || (attempt - 1) % 4 == 0 )); then
      process_id="$(adb shell pidof "$package_name" | tr -d '\r')"
      if [[ -z "$process_id" ]]; then
        adb logcat -b all -d >"$command_prefix.process-missing.logcat.txt"
        echo "$package_name exited while waiting for Android CI command: $command" >&2
        return 1
      fi
      broadcast_attempts=$((broadcast_attempts + 1))
      adb shell am broadcast \
        -a "$command_action" \
        --ei requestId "$request_id" \
        --es command "$command" >"$command_prefix.broadcast.txt"
      printf 'attempt=%s requestId=%s command=%s pid=%s\n' \
        "$broadcast_attempts" "$request_id" "$command" "$process_id" \
        >>"$command_prefix.broadcast-attempts.txt"
    fi
    if adb exec-out cat \
      "$persistent_data_directory/$command_evidence" \
      >"$remote_json" 2>/dev/null && \
      python3 - "$remote_json" "$request_id" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
raise SystemExit(0 if payload.get("requestId") == int(sys.argv[2]) else 1)
PY
    then
      last_command_json="$command_prefix.json"
      cp "$remote_json" "$last_command_json"
      if python3 - "$last_command_json" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
raise SystemExit(0 if payload.get("status") == "ACK" else 1)
PY
      then
        return 0
      fi
      echo "Android CI command failed: $command ($last_command_json)" >&2
      return 1
    fi
    sleep 0.25
  done
  adb logcat -b all -d >"$command_prefix.timeout.logcat.txt"
  echo "Timed out waiting for Android CI command acknowledgement after " \
    "${broadcast_attempts} broadcasts: $command" >&2
  return 1
}

status_matches() {
  local predicate="$1"
  local expected="$2"
  python3 - "$last_command_json" "$predicate" "$expected" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
predicate, expected = sys.argv[2:]
if predicate == "docked":
    actual = payload.get("docked")
    wanted = expected.lower() == "true"
    ok = actual is wanted
elif predicate == "scene":
    ok = payload.get("scene") == expected
elif predicate == "movement":
    ok = payload.get("movement") == expected
elif predicate == "movement-not":
    ok = payload.get("movement") not in (None, expected)
elif predicate == "selected":
    ok = bool(payload.get("selectedId"))
elif predicate == "locked":
    ok = bool(payload.get("lockedTargetId"))
elif predicate == "jumps-at-least":
    ok = int(payload.get("jumps") or 0) >= int(expected)
else:
    raise SystemExit(f"Unknown predicate: {predicate}")
raise SystemExit(0 if ok else 1)
PY
}

wait_for_status() {
  local predicate="$1"
  local expected="$2"
  local attempts="${3:-180}"
  for _ in $(seq 1 "$attempts"); do
    run_ci_command status
    if status_matches "$predicate" "$expected"; then
      return 0
    fi
    sleep 0.5
  done
  echo "Timed out waiting for status predicate $predicate=$expected." >&2
  return 1
}

wait_for_warp() {
  wait_for_status movement-not Idle 40
  wait_for_status movement Idle 240
}

record_pss() {
  local cycle="$1"
  local meminfo
  local pss
  meminfo="$results_directory/meminfo-cycle-$(printf '%02d' "$cycle").txt"
  adb shell dumpsys meminfo "$package_name" >"$meminfo"
  pss="$(awk '/TOTAL PSS:/ { print $3; exit } /^[[:space:]]*TOTAL[[:space:]]+[0-9]+/ { print $2; exit }' "$meminfo")"
  if [[ ! "$pss" =~ ^[0-9]+$ ]]; then
    echo "Could not parse TOTAL PSS from $meminfo." >&2
    return 1
  fi
  printf '%s\t%s\n' "$cycle" "$pss" >>"$results_directory/pss-kib.tsv"
}

adb shell pm clear "$package_name" >/dev/null
adb logcat -c
adb shell am start -W -n "$activity" >"$results_directory/start.txt"
wait_for_process
run_ci_command start-new-game
wait_for_status scene Station
wait_for_status docked true

# Three cycles warm the Unity player and procedural presentation caches. The
# remaining seventeen samples are used for the first-three/last-three median gate.
for cycle in $(seq 1 20); do
  run_ci_command undock
  wait_for_status docked false
  run_ci_command activate-modules
  sleep 1
  if (( cycle >= 4 )); then
    record_pss "$cycle"
  fi

  run_ci_command select-first-station
  wait_for_status selected true
  run_ci_command warp-selected
  wait_for_warp
  run_ci_command dock-or-jump
  wait_for_status docked true
done

run_ci_command undock
wait_for_status docked false
combat_exercised=false
initial_jumps=0
run_ci_command status
initial_jumps="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["jumps"])' "$last_command_json")"

for jump_index in $(seq 1 12); do
  run_ci_command select-first-gate
  wait_for_status selected true
  run_ci_command warp-selected
  wait_for_warp
  run_ci_command dock-or-jump
  wait_for_status jumps-at-least "$((initial_jumps + jump_index))" 80

  if [[ "$combat_exercised" == false ]] && run_ci_command select-first-hostile; then
    wait_for_status selected true
    run_ci_command warp-selected
    wait_for_warp
    run_ci_command lock-selected
    if wait_for_status locked true 40; then
      run_ci_command activate-modules
      sleep 3
      combat_exercised=true
      adb exec-out screencap -p >"$results_directory/combat-exercised.png"
    fi
  fi
done

if [[ "$combat_exercised" != true ]]; then
  echo "No deterministic hostile could be warped to, locked, and engaged during 12 jumps." >&2
  exit 1
fi

run_ci_command save
expected_saved_jumps=$((initial_jumps + 12))
saved_auto_json="$results_directory/final-auto-save.json"
auto_save_ready=false
for _ in $(seq 1 120); do
  if adb exec-out cat \
    "$persistent_data_directory/Saves/auto.json" \
    >"$saved_auto_json" 2>/dev/null && \
    python3 - "$saved_auto_json" "$expected_saved_jumps" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
player = payload.get("player") or {}
runtime = player.get("runtime") if isinstance(player, dict) else None
stats_owner = runtime if isinstance(runtime, dict) else player
stats = stats_owner.get("stats") or {}
ok = (
    payload.get("schemaVersion") == 2
    and int(stats.get("jumps") or 0) >= int(sys.argv[2])
)
raise SystemExit(0 if ok else 1)
PY
  then
    auto_save_ready=true
    break
  fi
  sleep 0.25
done
if [[ "$auto_save_ready" != true ]]; then
  echo "Auto save did not persist at least $expected_saved_jumps jumps." >&2
  exit 1
fi

run_ci_command status
cp "$last_command_json" "$results_directory/final-status.json"

python3 - "$results_directory/pss-kib.tsv" "$results_directory/stability-summary.json" <<'PY'
import json
import pathlib
import statistics
import sys

source = pathlib.Path(sys.argv[1])
rows = []
for line in source.read_text().splitlines():
    cycle, pss_kib = line.split("\t")
    rows.append({"cycle": int(cycle), "pssKiB": int(pss_kib)})
if len(rows) != 17:
    raise SystemExit(f"Expected 17 post-warmup PSS samples, found {len(rows)}")
first = statistics.median(row["pssKiB"] for row in rows[:3])
last = statistics.median(row["pssKiB"] for row in rows[-3:])
growth = last - first
growth_percent = 0.0 if first <= 0 else growth / first * 100.0
failed = growth > 128 * 1024 and growth_percent > 25.0
summary = {
    "warmupCycles": 3,
    "stationSpaceCycles": 20,
    "jumpCount": 12,
    "moduleActivationCycles": 20,
    "hostileWarpLockAndModuleActivation": True,
    "pssSamples": rows,
    "firstThreeMedianKiB": first,
    "lastThreeMedianKiB": last,
    "growthKiB": growth,
    "growthPercent": round(growth_percent, 3),
    "failureRule": "growth > 25% AND growth > 128 MiB",
    "passed": not failed,
}
pathlib.Path(sys.argv[2]).write_text(json.dumps(summary, indent=2) + "\n")
if failed:
    raise SystemExit(
        f"PSS grew {growth_percent:.1f}% and {growth / 1024:.1f} MiB; both limits exceeded"
    )
PY

adb logcat -d --pid="$(adb shell pidof "$package_name" | tr -d '\r')" \
  >"$results_directory/app.logcat.txt"
echo "Android stability evidence: $results_directory"
