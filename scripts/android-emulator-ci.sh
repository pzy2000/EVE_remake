#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
source "$script_directory/android-emulator-common.sh"

apk="${1:?usage: android-emulator-ci.sh APK [RESULTS_DIRECTORY]}"
results_directory="${2:-artifacts/android-emulator}"
package_name="com.pzy.starfallodyssey"
expected_activity="$package_name/com.pzy.starfall.mobile.StarfallUnityGameActivity"
debug_layout_action="com.pzy.starfall.mobile.DEBUG_WINDOW_LAYOUT"
debug_command_action="com.pzy.starfall.mobile.DEBUG_COMMAND"
expected_architecture="${STARFALL_EMULATOR_ARCH:-x86_64}"
acceptance_suite="${STARFALL_ANDROID_SUITE:-all}"
scenario_filter="${STARFALL_ANDROID_SCENARIOS:-all}"
command_request_id=500000
world_swipe_duration_ms=1500
persistent_data_directory="$(starfall_android_app_files_directory "$package_name")"

if [[ "$expected_architecture" != "x86_64" && "$expected_architecture" != "arm64-v8a" ]]; then
  echo "STARFALL_EMULATOR_ARCH must be x86_64 or arm64-v8a." >&2
  exit 2
fi
case "$acceptance_suite" in
  all|scenarios|lifecycle) ;;
  *)
    echo "STARFALL_ANDROID_SUITE must be all, scenarios, or lifecycle." >&2
    exit 2
    ;;
esac
if [[ ! -f "$apk" ]]; then
  echo "Smoke APK does not exist: $apk" >&2
  exit 2
fi

mkdir -p "$results_directory"
results_directory="$(cd "$results_directory" && pwd -P)"

scenario_is_selected() {
  local wanted_label="$1"
  local selected_label
  local -a selected_labels=()
  if [[ "$scenario_filter" == "all" ]]; then
    return 0
  fi
  IFS=',' read -r -a selected_labels <<<"$scenario_filter"
  for selected_label in "${selected_labels[@]}"; do
    if [[ "$selected_label" == "$wanted_label" ]]; then
      return 0
    fi
  done
  return 1
}

record_scenario_stage() {
  local stage_file="$1"
  local stage_name="$2"
  scenario_stage_sequence=$((scenario_stage_sequence + 1))
  printf '%02d\tPASS\t%s\n' "$scenario_stage_sequence" "$stage_name" >>"$stage_file"
}

tap_coordinate() {
  local coordinate="$1"
  local tap_x
  local tap_y
  local extra
  read -r tap_x tap_y extra <<<"$coordinate"
  if [[ ! "$tap_x" =~ ^[0-9]+$ || ! "$tap_y" =~ ^[0-9]+$ || -n "$extra" ]]; then
    echo "Invalid tap coordinate: $coordinate" >&2
    return 1
  fi
  adb shell input tap "$tap_x" "$tap_y"
  # `input tap` returns before Unity necessarily consumes the pointer-up event.
  # SwiftShader runners can render below 4 fps while a scene is settling. Give
  # each system tap a full second so sequential Station tab/save/settings taps
  # cannot collapse into one Unity player frame and lose the final action.
  sleep 1
}

dismiss_known_system_startup_dialogs() {
  local label="$1"
  local evidence_directory="$results_directory/system-startup-dialogs/$label"
  local decision_file
  local status
  local coordinate
  local consecutive_clear=0
  mkdir -p "$evidence_directory"

  for attempt in $(seq 1 12); do
    local stem
    stem="$evidence_directory/$(printf '%02d' "$attempt")"
    adb shell dumpsys window windows >"$stem.window.txt"
    adb shell dumpsys activity top >"$stem.activity.txt"
    adb exec-out screencap -p >"$stem.png"
    adb shell rm -f /sdcard/starfall-startup-dialog.xml >/dev/null 2>&1 || true
    if timeout 15s adb shell uiautomator dump /sdcard/starfall-startup-dialog.xml \
      >"$stem.uiautomator.txt" 2>&1; then
      # A freshly booted API 36 system can return zero before the XML is
      # materialized. The dumpsys ownership evidence remains authoritative;
      # retain the pull result without turning optional UI text into a failure.
      adb pull /sdcard/starfall-startup-dialog.xml "$stem.xml" \
        >"$stem.uiautomator-pull.txt" 2>&1 || true
    fi

    decision_file="$stem.decision.json"
    python3 - \
      "$stem.window.txt" "$stem.xml" "$decision_file" "$package_name" "$attempt" <<'PY'
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

window_path = pathlib.Path(sys.argv[1])
xml_path = pathlib.Path(sys.argv[2])
output_path = pathlib.Path(sys.argv[3])
app_package = sys.argv[4]
attempt = int(sys.argv[5])
window_text = window_path.read_text(errors="replace")
focus_text = "\n".join(
    line for line in window_text.splitlines()
    if any(marker in line for marker in (
        "mCurrentFocus", "mFocusedApp", "Application Error", "Application Not Responding"
    ))
)
dialog_owner_text = "\n".join(
    line for line in window_text.splitlines()
    if any(marker in line for marker in (
        "mCurrentFocus", "Application Error", "Application Not Responding"
    ))
)

nodes = []
xml_file = xml_path
if xml_file.is_file():
    try:
        nodes = list(ET.parse(xml_file).getroot().iter("node"))
    except ET.ParseError:
        nodes = []

visible_text = " ".join(
    value
    for node in nodes
    for value in (node.attrib.get("text", ""), node.attrib.get("content-desc", ""))
    if value
)
combined = (focus_text + " " + visible_text).lower()
error_pattern = re.compile(
    r"(?:isn't responding|is not responding|not responding|keeps stopping|has stopped)"
)
has_error = bool(error_pattern.search(combined))
app_markers = (app_package.lower(), "starfall odyssey")
allowed_packages = (
    "com.android.systemui",
    "com.android.launcher3",
    "com.google.android.apps.nexuslauncher",
)
allowed_names = ("system ui", "pixel launcher", "android launcher")

if has_error and any(marker in combined for marker in app_markers):
    status = "app-error"
elif has_error and (
    any(package in dialog_owner_text.lower() for package in allowed_packages)
    or any(name in combined for name in allowed_names)
):
    status = "known-system-dialog"
elif has_error:
    status = "unknown-error-dialog"
else:
    status = "clear"

button = None
if status == "known-system-dialog":
    preferred = (
        ("wait", "close app", "ok", "got it")
        if attempt <= 2
        else ("close app", "ok", "wait", "got it")
    )
    for wanted in preferred:
        for node in nodes:
            label = " ".join((
                node.attrib.get("text", ""), node.attrib.get("content-desc", "")
            )).strip().lower()
            if label != wanted:
                continue
            match = re.fullmatch(
                r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.attrib.get("bounds", "")
            )
            if not match:
                continue
            x1, y1, x2, y2 = map(int, match.groups())
            if x2 > x1 and y2 > y1:
                button = {"label": wanted, "x": (x1 + x2) // 2, "y": (y1 + y2) // 2}
                break
        if button:
            break

payload = {
    "status": status,
    "button": button,
    "focusedWindowEvidence": focus_text,
    "visibleText": visible_text,
}
pathlib.Path(output_path).write_text(json.dumps(payload, indent=2) + "\n")
PY
    status="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["status"])' "$decision_file")"
    case "$status" in
      clear)
        consecutive_clear=$((consecutive_clear + 1))
        if (( consecutive_clear >= 2 )); then
          printf 'status=clear\nattempts=%s\n' "$attempt" >"$evidence_directory/summary.txt"
          return 0
        fi
        sleep 1
        ;;
      app-error)
        echo "Refusing to dismiss an error dialog owned by $package_name ($decision_file)." >&2
        return 1
        ;;
      unknown-error-dialog)
        echo "Refusing to dismiss an error dialog not owned by System UI/Launcher ($decision_file)." >&2
        return 1
        ;;
      known-system-dialog)
        consecutive_clear=0
        coordinate="$(python3 - "$decision_file" <<'PY'
import json
import sys

button = json.load(open(sys.argv[1], encoding="utf-8")).get("button")
if button:
    print(f"{button['x']} {button['y']}")
PY
)"
        if [[ -n "$coordinate" ]]; then
          tap_coordinate "$coordinate"
        else
          # Ownership and error text were both proven above. Back is only used
          # for this restricted System UI/Launcher fallback.
          adb shell input keyevent KEYCODE_BACK
        fi
        sleep 3
        ;;
      *)
        echo "Unexpected startup-dialog decision: $status" >&2
        return 1
        ;;
    esac
  done

  echo "Known System UI/Launcher startup dialog did not clear ($evidence_directory)." >&2
  return 1
}

reset_emulator() {
  adb shell wm size reset >/dev/null 2>&1 || true
  adb shell wm density reset >/dev/null 2>&1 || true
}

on_exit() {
  local status=$?
  trap - EXIT INT TERM
  starfall_stop_continuous_logcat
  if (( status != 0 )); then
    starfall_capture_emulator_failure \
      "$package_name" "$results_directory" "failure"
  fi
  reset_emulator
  exit "$status"
}
trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

starfall_wait_for_adb_transport \
  "$results_directory/adb-transport-before-dialog-check.txt"
starfall_wait_for_android_services \
  "$results_directory/android-services-before-dialog-check.txt"
dismiss_known_system_startup_dialogs "before-install"
starfall_confirm_immersive_mode \
  "$results_directory/system-startup-dialogs/immersive-mode-setting.txt"
actual_abi="$(adb shell getprop ro.product.cpu.abi | tr -d '\r')"
case "$expected_architecture:$actual_abi" in
  x86_64:x86_64|arm64-v8a:arm64-v8a) ;;
  *)
    echo "Emulator ABI mismatch: expected $expected_architecture, received $actual_abi" >&2
    exit 1
    ;;
esac

mapfile -t packaged_abis < <(unzip -Z1 "$apk" \
  | awk -F/ '$1 == "lib" && NF > 2 { print $2 }' | sort -u)
if [[ "${packaged_abis[*]}" != "$expected_architecture" ]]; then
  echo "Smoke APK ABI mismatch: expected $expected_architecture, found ${packaged_abis[*]:-(none)}" >&2
  exit 1
fi

starfall_install_apk_with_system_retries "$apk" "$results_directory"

activity="$(adb shell cmd package resolve-activity --brief "$package_name" \
  | tr -d '\r' | tail -n 1)"
if [[ "$activity" != "$expected_activity" ]]; then
  echo "Launch activity mismatch: expected $expected_activity, received $activity" >&2
  exit 1
fi

wait_for_process() {
  local process_id=""
  for _ in $(seq 1 45); do
    process_id="$(adb shell pidof "$package_name" | tr -d '\r')"
    if [[ -n "$process_id" ]]; then
      printf '%s\n' "$process_id"
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for $package_name to start." >&2
  return 1
}

wait_for_app_file() {
  local remote_name="$1"
  local remote_path
  remote_path="$(starfall_android_app_file_path "$package_name" "$remote_name")"
  for _ in $(seq 1 45); do
    if adb shell test -f "$remote_path" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "Timed out waiting for app evidence file: $remote_name" >&2
  return 1
}

pull_app_file() {
  local remote_name="$1"
  local destination="$2"
  local remote_path
  remote_path="$(starfall_android_app_file_path "$package_name" "$remote_name")"
  wait_for_app_file "$remote_name"
  adb exec-out cat "$remote_path" >"$destination"
  python3 -m json.tool "$destination" >/dev/null
}

assert_png_dimensions() {
  local png="$1"
  local expected_width="$2"
  local expected_height="$3"
  python3 - "$png" "$expected_width" "$expected_height" <<'PY'
import pathlib
import struct
import sys

path = pathlib.Path(sys.argv[1])
expected = (int(sys.argv[2]), int(sys.argv[3]))
data = path.read_bytes()[:24]
if data[:8] != b"\x89PNG\r\n\x1a\n":
    raise SystemExit(f"Not a PNG: {path}")
actual = struct.unpack(">II", data[16:24])
if actual != expected:
    raise SystemExit(f"Screenshot {path} is {actual[0]}x{actual[1]}, expected {expected[0]}x{expected[1]}")
PY
}

configure_landscape_display() {
  local width="$1"
  local height="$2"
  local density_dpi="$3"
  local evidence_prefix="$4"
  local probe_png="$results_directory/.display-probe.png"

  # Pixel Launcher can lock itself to rotation 0 even after user_rotation is
  # changed. Set the final logical landscape axes directly so the acceptance
  # framebuffer is deterministic before the Unity activity starts.
  adb shell settings put system accelerometer_rotation 0
  adb shell settings put system user_rotation 0
  adb shell wm size "${width}x${height}"
  adb shell wm density "$density_dpi"

  for _ in $(seq 1 40); do
    if adb exec-out screencap -p >"$probe_png" 2>/dev/null && \
      assert_png_dimensions "$probe_png" "$width" "$height" 2>/dev/null; then
      adb shell wm size >"$evidence_prefix.wm-size.txt"
      adb shell wm density >"$evidence_prefix.wm-density.txt"
      adb shell dumpsys display >"$evidence_prefix.dumpsys-display.txt"
      rm -f -- "$probe_png"
      return 0
    fi
    sleep 0.5
  done

  adb shell wm size >"$evidence_prefix.wm-size.txt"
  adb shell wm density >"$evidence_prefix.wm-density.txt"
  adb shell dumpsys display >"$evidence_prefix.dumpsys-display.txt"
  if [[ -s "$probe_png" ]]; then
    mv "$probe_png" "$evidence_prefix.unexpected-framebuffer.png"
  fi
  echo "Logical framebuffer did not settle at ${width}x${height} @ ${density_dpi}dpi." >&2
  return 1
}

capture_screen() {
  local label="$1"
  local width="$2"
  local height="$3"
  local layout_json="${4:-}"
  local png="$results_directory/$label.png"
  local hierarchy="$results_directory/$label.uiautomator.xml"
  starfall_adb_capture_file "$png" exec-out screencap -p
  assert_png_dimensions "$png" "$width" "$height"
  if starfall_adb_retry_read \
    shell uiautomator dump /sdcard/starfall-window.xml >/dev/null 2>&1; then
    starfall_adb_retry_read \
      pull /sdcard/starfall-window.xml "$hierarchy" >/dev/null
  fi
  starfall_adb_capture_file \
    "$results_directory/$label.window.txt" shell dumpsys window displays
  if [[ -n "$layout_json" ]]; then
    python3 scripts/assert-png-color.py "$png" "$layout_json"
  fi
}

validate_layout_json() {
  local layout_json="$1"
  local expected_mode="$2"
  local require_hinge="$3"
  local expected_source_json="$4"
  python3 - \
    "$layout_json" "$expected_mode" "$require_hinge" "$expected_source_json" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected_mode = sys.argv[2]
require_hinge = sys.argv[3] == "true"
expected_source = json.loads(pathlib.Path(sys.argv[4]).read_text())
data = json.loads(path.read_text())
if data.get("mode") != expected_mode:
    raise SystemExit(f"{path}: expected mode {expected_mode}, received {data.get('mode')}")
actual_source = data.get("source") or {}
if actual_source.get("source") != "debug-broadcast":
    raise SystemExit(f"{path}: window metrics were not normalized by the debug broadcast")
if not isinstance(actual_source.get("timestampMs"), int):
    raise SystemExit(f"{path}: normalized window metrics are missing timestampMs")
for field in (
    "schemaVersion", "origin", "safeAreaOrigin", "widthPx", "heightPx",
    "densityDpi", "safeArea", "foldingFeatures",
):
    if actual_source.get(field) != expected_source.get(field):
        raise SystemExit(
            f"{path}: injected window field {field} changed: "
            f"expected {expected_source.get(field)!r}, got {actual_source.get(field)!r}"
        )

hinge = data.get("foldingBoundsDp") or {}
hinge_rect = (
    float(hinge.get("x", 0)),
    float(hinge.get("y", 0)),
    float(hinge.get("width", 0)),
    float(hinge.get("height", 0)),
)
has_hinge = hinge_rect[2] > 0 or hinge_rect[3] > 0
if require_hinge and not has_hinge:
    raise SystemExit(f"{path}: expected a non-empty folding feature")
PY
}

pull_expected_layout_json() {
  local remote_name="$1"
  local destination="$2"
  local expected_mode="$3"
  local require_hinge="$4"
  local expected_source_json="$5"
  local remote_path
  remote_path="$(starfall_android_app_file_path "$package_name" "$remote_name")"
  for _ in $(seq 1 80); do
    if adb exec-out cat "$remote_path" \
      >"$destination" 2>/dev/null && \
      python3 -m json.tool "$destination" >/dev/null 2>&1 && \
      validate_layout_json \
        "$destination" "$expected_mode" "$require_hinge" "$expected_source_json" \
        >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.25
  done
  echo "Timed out waiting for injected window metrics evidence: $remote_name" >&2
  if [[ -s "$destination" ]]; then
    validate_layout_json \
      "$destination" "$expected_mode" "$require_hinge" "$expected_source_json"
  fi
  return 1
}

# Retained temporarily as a behavior reference while the aggregate validator
# below is exercised by CI. It is intentionally not used by the acceptance run.
validate_ui_layout_json_first_failure_reference() {
  local ui_json="$1"
  local expected_mode="$2"
  local require_hinge="$3"
  local expected_surface="${4:-}"
  python3 - "$ui_json" "$expected_mode" "$require_hinge" "$expected_surface" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected_mode = sys.argv[2]
require_hinge = sys.argv[3] == "true"
expected_surface = sys.argv[4]
data = json.loads(path.read_text())
if data.get("mode") != expected_mode:
    raise SystemExit(f"{path}: expected mode {expected_mode}, received {data.get('mode')}")

safe = data.get("safeBoundsDp") or {}
safe_rect = (
    float(safe.get("x", 0)),
    float(safe.get("y", 0)),
    float(safe.get("width", 0)),
    float(safe.get("height", 0)),
)
hinge = data.get("foldingBoundsDp") or {}
hinge_rect = (
    float(hinge.get("x", 0)),
    float(hinge.get("y", 0)),
    float(hinge.get("width", 0)),
    float(hinge.get("height", 0)),
)
has_hinge = hinge_rect[2] > 0 or hinge_rect[3] > 0
if require_hinge and not has_hinge:
    raise SystemExit(f"{path}: expected a non-empty folding feature")

def intersects(first, second):
    return (
        first[0] < second[0] + second[2]
        and first[0] + first[2] > second[0]
        and first[1] < second[1] + second[3]
        and first[1] + first[3] > second[1]
    )

def rect_from(item, key):
    bounds = item.get(key) or {}
    return (
        float(bounds.get("x", 0)),
        float(bounds.get("y", 0)),
        float(bounds.get("width", 0)),
        float(bounds.get("height", 0)),
    )

def validate_visible_rect(rect, item_kind, name):
    if rect[2] <= 0 or rect[3] <= 0:
        raise SystemExit(f"{path}: visible {item_kind} {name} has empty visibleBoundsDp")
    if safe_rect[2] > 0 and safe_rect[3] > 0:
        if (
            rect[0] < safe_rect[0] - 0.01
            or rect[1] < safe_rect[1] - 0.01
            or rect[0] + rect[2] > safe_rect[0] + safe_rect[2] + 0.01
            or rect[1] + rect[3] > safe_rect[1] + safe_rect[3] + 0.01
        ):
            raise SystemExit(f"{path}: visible {item_kind} {name} leaves the safe bounds")
    if require_hinge and intersects(rect, hinge_rect):
        raise SystemExit(f"{path}: visible {item_kind} {name} intersects the folding feature")

controls = data.get("controls") or []
# Preserve Unity's internal ScrollView/Scroller elements in the evidence JSON,
# but do not treat implementation-detail controls such as `unity-slider` as
# application touch targets. The named parent ScrollView and app-level Slider
# remain covered by the surface and required-control gates below.
visible_controls = [
    item
    for item in controls
    if item.get("visible", True)
    and not str(item.get("name") or "").startswith("unity-")
]
if not visible_controls:
    raise SystemExit(f"{path}: no visible interactive controls were captured")
for control in visible_controls:
    name = control.get("name") or "<unnamed>"
    raw = rect_from(control, "boundsDp")
    visible = rect_from(control, "visibleBoundsDp")
    if raw[2] + 0.01 < 48 or raw[3] + 0.01 < 48:
        raise SystemExit(
            f"{path}: touch target {name} is {raw[2]}x{raw[3]} dp; minimum is 48x48 dp"
        )
    if not control.get("inScrollView", False):
        if visible[2] + 0.01 < 48 or visible[3] + 0.01 < 48:
            raise SystemExit(
                f"{path}: visible touch target {name} is {visible[2]}x{visible[3]} dp; "
                "minimum visible size is 48x48 dp"
            )
        if not control.get("fullyVisible", False):
            raise SystemExit(f"{path}: non-scrolling touch target {name} is clipped")
    validate_visible_rect(visible, "control", name)

required_controls = {
    "MainMenu": {
        "pilot-name", "empire-aurelian", "empire-kaldari", "empire-meridian",
        "empire-varkhald", "launch", "continue", "import", "settings",
    },
    "Station": {
        "settings", "repair", "save", "undock", "tab-agents", "tab-market",
        "tab-fitting", "tab-ships", "tab-lp",
    },
    "Space": {"map", "journal", "pilot", "save", "settings", "module-1"},
}.get(data.get("screen"), set())
if data.get("screen") == "Space":
    if expected_mode == "CompactLandscape":
        required_controls |= {
            "mobile-overview-toggle", "mobile-target-toggle", "mobile-log-toggle",
        }
    else:
        required_controls |= {"approach", "orbit", "warp", "lock", "dock"}

required_by_surface = {
    "settings-card": {"settings-close", "quality-cycle", "music-volume", "music-muted"},
    "confirmation-card": {"confirmation-cancel", "confirmation-confirm"},
    "starmap-card": {"map-close"},
    "journal-card": {"journal-close"},
    "death-card": {"respawn"},
    "target-panel": {"approach", "orbit", "warp", "lock", "dock"},
}

required_controls |= required_by_surface.get(expected_surface, set())
visible_control_names = {item.get("name") for item in visible_controls}
missing_controls = sorted(required_controls - visible_control_names)
if missing_controls:
    raise SystemExit(
        f"{path}: required controls are not fully present in the visible UI: {missing_controls}"
    )

surfaces = data.get("surfaces") or []
visible_surfaces = [item for item in surfaces if item.get("visible", True)]
surface_names = {item.get("name") for item in visible_surfaces}
if expected_surface and expected_surface not in surface_names:
    raise SystemExit(
        f"{path}: expected visible surface {expected_surface}; found {sorted(surface_names)}"
    )
for surface in visible_surfaces:
    name = surface.get("name") or "<unnamed>"
    visible = rect_from(surface, "visibleBoundsDp")
    if visible[2] <= 0 or visible[3] <= 0:
        raise SystemExit(f"{path}: surface {name} was listed without a visible region")
    # Unlike scroll rows, a modal/panel must fit as a complete resolved box.
    # Validate raw bounds so viewport clipping cannot conceal layout overflow.
    validate_visible_rect(rect_from(surface, "boundsDp"), "surface", name)

texts = data.get("texts") or []
visible_texts = [item for item in texts if item.get("visible", True)]
if not visible_texts:
    raise SystemExit(f"{path}: no readable visible text evidence was captured")
for text in visible_texts:
    name = text.get("name") or (text.get("text") or "<unnamed>")[:80]
    font_size = float(text.get("fontSizeDp") or 0)
    if font_size + 0.01 < 14:
        raise SystemExit(
            f"{path}: visible text {name!r} uses {font_size}dp; minimum is 14dp"
        )
    if not text.get("inScrollView", False) and not text.get("fullyVisible", False):
        raise SystemExit(f"{path}: non-scrolling text {name!r} is clipped")
    validate_visible_rect(rect_from(text, "visibleBoundsDp"), "text", name)
PY
}

# Override the original inline validator with the aggregate reporter. Every
# layout snapshot now emits a machine-readable verdict and all defects found
# in that snapshot instead of stopping at the first one.
validate_ui_layout_json() {
  local ui_json="$1"
  local expected_mode="$2"
  local require_hinge="$3"
  local expected_surface="${4:-}"
  python3 "$script_directory/validate-android-ui-layout.py" \
    "$ui_json" "$expected_mode" "$require_hinge" "$expected_surface" \
    "$ui_json.validation.json"
}

wait_for_ui_surface() {
  local remote_name="$1"
  local destination="$2"
  local expected_surface="$3"
  local expected_control="${4:-}"
  local remote_path
  remote_path="$(starfall_android_app_file_path "$package_name" "$remote_name")"
  for _ in $(seq 1 60); do
    if adb exec-out cat "$remote_path" \
      >"$destination" 2>/dev/null && \
      python3 - "$destination" "$expected_surface" "$expected_control" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
names = {
    surface.get("name")
    for surface in payload.get("surfaces", [])
    if surface.get("visible", True)
}
control_ready = not sys.argv[3] or any(
    control.get("name") == sys.argv[3]
    and control.get("visible", False)
    and control.get("fullyVisible", False)
    for control in payload.get("controls", [])
)
raise SystemExit(0 if sys.argv[2] in names and control_ready else 1)
PY
    then
      return 0
    fi
    sleep 0.25
  done
  echo "Timed out waiting for visible UI surface $expected_surface in $remote_name." >&2
  return 1
}

wait_for_ui_surface_absent() {
  local remote_name="$1"
  local expected_surface="$2"
  local remote_path
  remote_path="$(starfall_android_app_file_path "$package_name" "$remote_name")"
  for _ in $(seq 1 60); do
    if adb exec-out cat "$remote_path" 2>/dev/null \
      | python3 -c 'import json,sys; expected=sys.argv[1]; payload=json.load(sys.stdin); names={item.get("name") for item in payload.get("surfaces", []) if item.get("visible", True)}; raise SystemExit(1 if expected in names else 0)' \
        "$expected_surface"; then
      return 0
    fi
    sleep 0.25
  done
  echo "Timed out waiting for UI surface $expected_surface to close." >&2
  return 1
}

wait_for_gesture_evidence() {
  local remote_path="$1"
  local destination="$2"
  local expected_generation="$3"
  local expected_gesture="$4"
  local report_timeout="${5:-true}"
  for _ in $(seq 1 80); do
    if adb exec-out cat "$remote_path" >"$destination" 2>/dev/null && \
      python3 - "$destination" "$expected_generation" "$expected_gesture" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
generation = int(payload.get("generation", -1))
gesture = str(payload.get("gesture", ""))
raise SystemExit(0 if generation >= int(sys.argv[2]) and gesture == sys.argv[3] else 1)
PY
    then
      return 0
    fi
    sleep 0.025
  done
  if [[ "$report_timeout" == "true" ]]; then
    echo "Timed out waiting for gesture $expected_gesture generation $expected_generation." >&2
  fi
  return 1
}

dispatch_ci_command() {
  local command="$1"
  local evidence="$2"
  local attempt
  local broadcast_attempts=0
  local process_id
  command_request_id=$((command_request_id + 1))

  # The Activity process becomes visible before Unity's AppRoot has registered
  # the debug receiver. Re-send the same idempotent request once per second
  # until the managed ACK exists, while treating process loss as a hard failure.
  for attempt in $(seq 1 180); do
    if (( attempt == 1 || (attempt - 1) % 4 == 0 )); then
      process_id="$(adb shell pidof "$package_name" | tr -d '\r')"
      if [[ -z "$process_id" ]]; then
        adb logcat -b all -d >"$evidence.process-missing.logcat.txt"
        echo "$package_name exited while waiting for Android CI command: $command" >&2
        return 1
      fi
      broadcast_attempts=$((broadcast_attempts + 1))
      adb shell am broadcast \
        -a "$debug_command_action" \
        --ei requestId "$command_request_id" \
        --es command "$command" >"$evidence.broadcast.txt"
      printf 'attempt=%s requestId=%s command=%s pid=%s\n' \
        "$broadcast_attempts" "$command_request_id" "$command" "$process_id" \
        >>"$evidence.broadcast-attempts.txt"
    fi
    if adb exec-out cat \
      "$persistent_data_directory/starfall-ci-command.json" \
      >"$evidence" 2>/dev/null && \
      python3 - "$evidence" "$command_request_id" "$command" <<'PY'
import json
import sys

try:
    payload = json.load(open(sys.argv[1], encoding="utf-8"))
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
ok = (
    payload.get("requestId") == int(sys.argv[2])
    and payload.get("command") == sys.argv[3]
    and payload.get("status") == "ACK"
)
raise SystemExit(0 if ok else 1)
PY
    then
      return 0
    fi
    sleep 0.25
  done
  adb logcat -b all -d >"$evidence.timeout.logcat.txt"
  echo "Timed out waiting for Android CI command after ${broadcast_attempts} broadcasts: $command" >&2
  return 1
}

tap_control() {
  local ui_json="$1"
  local control_name="$2"
  local coordinate
  coordinate="$(python3 - "$ui_json" "$control_name" <<'PY'
import json
import sys

data = json.load(open(sys.argv[1], encoding="utf-8"))
name = sys.argv[2]
density = float(data["density"])
for control in data.get("controls", []):
    if (control.get("name") == name and control.get("enabled", True)
            and control.get("visible", True)):
        bounds = control["visibleBoundsDp"]
        x = round((float(bounds["x"]) + float(bounds["width"]) / 2) * density)
        y = round((float(bounds["y"]) + float(bounds["height"]) / 2) * density)
        print(f"{x} {y}")
        break
else:
    raise SystemExit(f"Enabled control not found: {name}")
PY
)"
  tap_coordinate "$coordinate"
}

tap_control_until_surface() {
  local ui_json="$1"
  local control_name="$2"
  local remote_name="$3"
  local destination="$4"
  local expected_surface="$5"
  local expected_control="${6:-}"
  local attempt
  for attempt in $(seq 1 3); do
    printf 'attempt=%s control=%s expectedSurface=%s\n' \
      "$attempt" "$control_name" "$expected_surface" \
      >>"$destination.tap-attempts.txt"
    tap_control "$ui_json" "$control_name"
    if wait_for_ui_surface \
      "$remote_name" "$destination" "$expected_surface" "$expected_control"; then
      return 0
    fi
    sleep 1
  done
  echo "Control $control_name did not open $expected_surface after three physical taps." >&2
  return 1
}

assert_no_app_failures() {
  local label="$1"
  local app_log="$results_directory/$label.app.logcat.txt"
  local system_log="$results_directory/$label.system.logcat.txt"
  local process_id
  process_id="$(adb shell pidof "$package_name" | tr -d '\r')"
  if [[ -n "$process_id" ]]; then
    adb logcat -d --pid="$process_id" >"$app_log"
  else
    printf '%s\n' 'App process missing; PID-filtered log unavailable.' >"$app_log"
  fi
  adb logcat -b all -d >"$system_log"

  if grep -Eqi \
    'FATAL EXCEPTION|Unhandled Exception|(^|[[:space:]])([[:alpha:]_][[:alnum:]_.]*Exception|UnityException):|SIGABRT|SIGSEGV|VK_ERROR_DEVICE_LOST|VK_ERROR_OUT_OF_(DEVICE|HOST)_MEMORY|Vulkan.*(device lost|out of memory)|OutOfMemoryError' \
    "$app_log"; then
    echo "Application-owned fatal error detected in $app_log" >&2
    return 1
  fi
  if grep -Eqi \
    "ANR in ${package_name}|am_anr.*${package_name}|Process: ${package_name},|Fatal signal.*${package_name}" \
    "$system_log"; then
    echo "ANR/native crash detected for $package_name in $system_log" >&2
    return 1
  fi
  if [[ -z "$process_id" ]]; then
    echo "$package_name is no longer running." >&2
    return 1
  fi
}

picker_dump_index=0
tap_picker_node() {
  local pattern="$1"
  local dump_file
  local coordinate
  picker_dump_index=$((picker_dump_index + 1))
  dump_file="$results_directory/legacy-saf/picker-$(printf '%02d' "$picker_dump_index").xml"
  adb shell uiautomator dump /sdcard/starfall-picker.xml >/dev/null
  adb pull /sdcard/starfall-picker.xml "$dump_file" >/dev/null
  coordinate="$(python3 - "$dump_file" "$pattern" <<'PY'
import re
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
pattern = re.compile(sys.argv[2], re.IGNORECASE)
for node in root.iter("node"):
    haystack = " ".join((node.attrib.get("text", ""), node.attrib.get("content-desc", "")))
    if not pattern.search(haystack):
        continue
    match = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", node.attrib.get("bounds", ""))
    if not match:
        continue
    x1, y1, x2, y2 = map(int, match.groups())
    if x2 > x1 and y2 > y1:
        print(f"{(x1 + x2) // 2} {(y1 + y2) // 2}")
        raise SystemExit(0)
raise SystemExit(1)
PY
)" || return 1
  tap_coordinate "$coordinate"
  sleep 1
}

run_legacy_saf_import() {
  local legacy_directory="$results_directory/legacy-saf"
  local source_file="$legacy_directory/starfall-legacy-v1.json"
  local remote_source="/sdcard/Download/starfall-legacy-v1.json"
  local source_sha
  local remote_sha_before
  local remote_sha_after
  local source_json
  mkdir -p "$legacy_directory"

  python3 - "$source_file" <<'PY'
import json
import pathlib
import sys

payload = {
    "version": 1,
    "seed": 12345,
    "time": 12.5,
    "currentSystemId": "sys_0",
    "player": {
        "name": "Legacy Pilot",
        "empire": "aurelian",
        "credits": 50000,
        "lp": {},
        "standings": {"aurelian": 1.0},
        "ships": [{
            "instId": "ship_start",
            "shipId": "acolyte",
            "name": "Acolyte",
            "hp": {"shield": 320, "armor": 280, "hull": 220},
            "fitting": {
                "high": ["pulse_laser", "pulse_laser"],
                "mid": ["shield_booster", None],
                "low": [None, None],
            },
        }],
        "activeShip": "ship_start",
        "cargo": {"ferrite": 12},
        "hangar": {"mining_laser": 1},
        "missions": [],
        "missionCounts": {},
        "location": {
            "systemId": "sys_0",
            "dockedAt": "sys_0_st0",
            "x": 123.5,
            "y": -77.25,
        },
        "homeSystemId": "sys_0",
        "homeStationId": "sys_0_st0",
        "criminalTimer": 0,
        "destination": None,
        "stats": {"kills": 0, "missionsDone": 0, "oreMined": 0, "jumps": 0},
    },
}
pathlib.Path(sys.argv[1]).write_text(
    json.dumps(payload, separators=(",", ":")), encoding="utf-8"
)
PY
  if (( $(wc -c <"$source_file") > 5 * 1024 * 1024 )); then
    echo "Legacy SAF fixture exceeds 5 MiB." >&2
    return 1
  fi
  source_sha="$(shasum -a 256 "$source_file" | awk '{print $1}')"
  adb push "$source_file" "$remote_source" >"$legacy_directory/adb-push.txt"
  remote_sha_before="$(adb shell sha256sum "$remote_source" | awk '{print $1}' | tr -d '\r')"
  [[ "$remote_sha_before" == "$source_sha" ]] || {
    echo "Pushed SAF source hash differs from the local fixture." >&2
    return 1
  }

  adb shell pm clear "$package_name" >/dev/null
  dismiss_known_system_startup_dialogs "legacy-saf-before-start"
  configure_landscape_display 2748 1172 420 "$legacy_directory/display"
  adb logcat -c
  starfall_start_continuous_logcat "$legacy_directory/session.logcat.txt"
  adb shell am start -W -n "$activity" >"$legacy_directory/start.txt"
  wait_for_process >/dev/null
  starfall_clear_immersive_mode_confirmation \
    "$legacy_directory/immersive-mode-confirmation"
  starfall_wait_for_unity_render_ready \
    "$package_name" "$legacy_directory/render-ready.json" MainMenu
  source_json='{"schemaVersion":1,"source":"github-actions","origin":"top-left","safeAreaOrigin":"top-left","widthPx":2748,"heightPx":1172,"densityDpi":420,"safeArea":{"x":0,"y":0,"width":2748,"height":1172},"foldingFeatures":[]}'
  printf '%s\n' "$source_json" >"$legacy_directory/injected-window.json"
  starfall_adb_broadcast_string_extra "$debug_layout_action" json "$source_json" \
    >"$legacy_directory/window-broadcast.txt"
  pull_expected_layout_json \
    "starfall-ci-layout.json" "$legacy_directory/mobile-layout.json" \
    CompactLandscape false "$legacy_directory/injected-window.json"
  pull_app_file "starfall-ci-layout-MainMenu.json" "$legacy_directory/MainMenu.layout.json"
  validate_ui_layout_json "$legacy_directory/MainMenu.layout.json" CompactLandscape false
  tap_control "$legacy_directory/MainMenu.layout.json" import

  for _ in $(seq 1 40); do
    if adb shell dumpsys activity activities | grep -Fq LegacyDocumentPickerActivity; then
      break
    fi
    sleep 0.25
  done
  adb shell dumpsys activity activities >"$legacy_directory/picker-activity.txt"
  grep -Fq LegacyDocumentPickerActivity "$legacy_directory/picker-activity.txt" || {
    echo "LegacyDocumentPickerActivity did not open through the app Import button." >&2
    return 1
  }
  capture_screen "legacy-saf-DocumentsUI-before-selection" 2748 1172

  if ! tap_picker_node 'starfall-legacy-v1(\.json)?'; then
    tap_picker_node 'show roots|navigation drawer|open navigation' || true
    tap_picker_node '^Downloads?$' || tap_picker_node 'Downloads?'
    capture_screen "legacy-saf-DocumentsUI-downloads" 2748 1172
    tap_picker_node 'starfall-legacy-v1(\.json)?'
  fi

  for _ in $(seq 1 180); do
    if adb shell test -s \
      "$persistent_data_directory/Saves/slot1.json" >/dev/null 2>&1; then
      break
    fi
    sleep 0.5
  done
  if ! adb shell test -s \
    "$persistent_data_directory/Saves/slot1.json" >/dev/null 2>&1; then
    echo "SAF import did not create Slot 1." >&2
    return 1
  fi
  adb exec-out cat \
    "$persistent_data_directory/Saves/slot1.json" \
    >"$legacy_directory/slot1.json"
  python3 - "$legacy_directory/slot1.json" "$source_sha" <<'PY'
import json
import sys

slot = json.load(open(sys.argv[1], encoding="utf-8"))
if slot.get("legacySourceSha256") != sys.argv[2]:
    raise SystemExit("Slot 1 legacySourceSha256 does not match the selected source")
if (slot.get("player") or {}).get("name") != "Legacy Pilot":
    raise SystemExit("Slot 1 does not contain the imported Legacy Pilot")
PY

  remote_sha_after="$(adb shell sha256sum "$remote_source" | awk '{print $1}' | tr -d '\r')"
  [[ "$remote_sha_after" == "$remote_sha_before" ]] || {
    echo "SAF import modified the provider source file." >&2
    return 1
  }
  printf 'localSha256=%s\nremoteSha256Before=%s\nremoteSha256After=%s\nsourceBytes=%s\n' \
    "$source_sha" "$remote_sha_before" "$remote_sha_after" "$(wc -c <"$source_file")" \
    >"$legacy_directory/source-integrity.txt"

  adb shell run-as "$package_name" find cache/legacy-import -type f -print \
    >"$legacy_directory/remaining-import-cache.txt" 2>/dev/null || true
  if [[ -s "$legacy_directory/remaining-import-cache.txt" ]]; then
    echo "Temporary legacy import files remain after a successful import." >&2
    return 1
  fi
  adb logcat -d --pid="$(adb shell pidof "$package_name" | tr -d '\r')" \
    >"$legacy_directory/app.logcat.txt"
  assert_no_app_failures "legacy-saf-import"
}

run_scenario() {
  local label="$1"
  local width="$2"
  local height="$3"
  local density_dpi="$4"
  local expected_mode="$5"
  local graphics_argument="$6"
  local folding_json="$7"
  local require_hinge="$8"
  local density_scale
  local actual_graphics_device
  local actual_graphics_name
  local graphics_verdict
  local qemu_marker
  local scenario_stage_sequence=0
  density_scale="$(python3 -c "print(${density_dpi} / 160.0)")"
  qemu_marker="$(adb shell getprop ro.kernel.qemu | tr -d '\r')"

  local scenario_directory="$results_directory/$label"
  mkdir -p "$scenario_directory"
  local stage_file="$scenario_directory/stages.tsv"
  : >"$stage_file"
  record_scenario_stage "$stage_file" "APK installed"
  adb shell pm clear "$package_name" >/dev/null
  dismiss_known_system_startup_dialogs "$label-before-start"
  configure_landscape_display \
    "$width" "$height" "$density_dpi" "$scenario_directory/display"

  adb logcat -c
  starfall_start_continuous_logcat "$scenario_directory/session.logcat.txt"
  adb shell am start -W -n "$activity" --es unity "$graphics_argument" \
    >"$scenario_directory/start.txt"
  wait_for_process >/dev/null
  starfall_clear_immersive_mode_confirmation \
    "$scenario_directory/immersive-mode-confirmation"
  starfall_wait_for_unity_render_ready \
    "$package_name" "$scenario_directory/render-ready.json" MainMenu
  record_scenario_stage "$stage_file" "Unity initialized"

  local expected_graphics_device
  case "$graphics_argument" in
    -force-vulkan) expected_graphics_device="Vulkan" ;;
    -force-gles30) expected_graphics_device="OpenGLES3" ;;
    *)
      echo "Unsupported graphics argument: $graphics_argument" >&2
      return 1
      ;;
  esac
  dispatch_ci_command "status" "$scenario_directory/graphics-device.command.json"
  python3 "$script_directory/verify-android-emulator-graphics.py" \
    "$scenario_directory/graphics-device.command.json" \
    "$expected_graphics_device" \
    "$graphics_argument" \
    "$qemu_marker" \
    "$scenario_directory/graphics-verification.json"

  local source_json
  source_json="{\"schemaVersion\":1,\"source\":\"github-actions\",\"origin\":\"top-left\",\"safeAreaOrigin\":\"top-left\",\"widthPx\":${width},\"heightPx\":${height},\"densityDpi\":${density_dpi},\"safeArea\":{\"x\":0,\"y\":0,\"width\":${width},\"height\":${height}},\"foldingFeatures\":${folding_json}}"
  printf '%s\n' "$source_json" >"$scenario_directory/injected-window.json"
  starfall_adb_broadcast_string_extra "$debug_layout_action" json "$source_json" \
    >"$scenario_directory/broadcast.txt"

  sleep 2
  pull_expected_layout_json \
    "starfall-ci-layout.json" "$scenario_directory/mobile-layout.json" \
    "$expected_mode" "$require_hinge" "$scenario_directory/injected-window.json"

  pull_app_file "starfall-ci-layout-MainMenu.json" "$scenario_directory/MainMenu.layout.json"
  validate_ui_layout_json "$scenario_directory/MainMenu.layout.json" "$expected_mode" "$require_hinge"
  capture_screen "$label-MainMenu" "$width" "$height" "$scenario_directory/MainMenu.layout.json"
  record_scenario_stage "$stage_file" "MainMenu layout and PNG"

  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface \
    "starfall-ci-layout-MainMenu.json" "$scenario_directory/MainMenu.BackConfirmation.layout.json" "confirmation-card"
  validate_ui_layout_json \
    "$scenario_directory/MainMenu.BackConfirmation.layout.json" "$expected_mode" "$require_hinge" "confirmation-card"
  capture_screen "$label-MainMenu-BackConfirmation" "$width" "$height" \
    "$scenario_directory/MainMenu.BackConfirmation.layout.json"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-MainMenu.json" "confirmation-card"

  tap_control "$scenario_directory/MainMenu.layout.json" "launch"
  pull_app_file "starfall-ci-layout-Station.json" "$scenario_directory/Station.layout.json"
  validate_ui_layout_json "$scenario_directory/Station.layout.json" "$expected_mode" "$require_hinge"
  capture_screen "$label-Station" "$width" "$height" "$scenario_directory/Station.layout.json"
  record_scenario_stage "$stage_file" "Station layout and PNG"

  tap_control "$scenario_directory/Station.layout.json" "tab-market"
  tap_control "$scenario_directory/Station.layout.json" "tab-fitting"
  tap_control "$scenario_directory/Station.layout.json" "save"
  tap_control "$scenario_directory/Station.layout.json" "settings"
  wait_for_ui_surface \
    "starfall-ci-layout-Station.json" "$scenario_directory/Station.Settings.layout.json" "settings-card"
  validate_ui_layout_json \
    "$scenario_directory/Station.Settings.layout.json" "$expected_mode" "$require_hinge" "settings-card"
  capture_screen "$label-Station-Settings" "$width" "$height"
  record_scenario_stage "$stage_file" "Station Settings layout and PNG"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Station.json" "settings-card"

  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface \
    "starfall-ci-layout-Station.json" "$scenario_directory/Station.BackConfirmation.layout.json" "confirmation-card"
  validate_ui_layout_json \
    "$scenario_directory/Station.BackConfirmation.layout.json" "$expected_mode" "$require_hinge" "confirmation-card"
  capture_screen "$label-Station-BackConfirmation" "$width" "$height"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Station.json" "confirmation-card"

  tap_control "$scenario_directory/Station.layout.json" "undock"

  pull_app_file "starfall-ci-layout-Space.json" "$scenario_directory/Space.layout.json"
  validate_ui_layout_json "$scenario_directory/Space.layout.json" "$expected_mode" "$require_hinge"
  capture_screen "$label-Space" "$width" "$height" "$scenario_directory/Space.layout.json"
  record_scenario_stage "$stage_file" "Space layout and PNG"

  if [[ "$expected_mode" == "CompactLandscape" ]]; then
    tap_control "$scenario_directory/Space.layout.json" "mobile-target-toggle"
    wait_for_ui_surface \
      "starfall-ci-layout-Space.json" "$scenario_directory/Target.layout.json" "target-panel"
    validate_ui_layout_json \
      "$scenario_directory/Target.layout.json" "$expected_mode" "$require_hinge" "target-panel"
    capture_screen "$label-Target" "$width" "$height"
    tap_control "$scenario_directory/Target.layout.json" "mobile-overview-toggle"
    wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "target-panel"
  fi

  # Exercise the real Android -> Input System -> world/UI paths. The smoke-only
  # status response supplies a raycast-proven world target and a UI-free drag path;
  # it does not invoke the gestures themselves.
  dispatch_ci_command \
    "prepare-touch-target" "$scenario_directory/Touch.Prepare.command.json"
  local touch_initial="$scenario_directory/Touch.Initial.command.json"
  local touch_target_ready=false
  for _ in $(seq 1 40); do
    dispatch_ci_command "status" "$touch_initial"
    if python3 - "$touch_initial" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
required = ("touchTargetId", "touchTargetX", "touchTargetY")
raise SystemExit(0 if all(name in payload for name in required) else 1)
PY
    then
      touch_target_ready=true
      break
    fi
    sleep 0.25
  done
  if [[ "$touch_target_ready" != "true" ]]; then
    echo "The prepared world target never became visible and raycast-selectable." >&2
    exit 1
  fi
  local touch_coordinates
  touch_coordinates="$(python3 - "$touch_initial" "$height" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
height = int(sys.argv[2])
required = (
    "touchTargetId", "touchTargetX", "touchTargetY",
    "dragStartX", "dragStartY", "dragEndX", "dragEndY",
    "cameraYawDegrees", "module0Active",
)
missing = [name for name in required if name not in payload]
if missing:
    raise SystemExit(f"Touch evidence is missing fields: {missing}")

def adb_point(x_name, y_name):
    return round(float(payload[x_name])), round(height - float(payload[y_name]))

target = adb_point("touchTargetX", "touchTargetY")
drag_start = adb_point("dragStartX", "dragStartY")
drag_end = adb_point("dragEndX", "dragEndY")
print(*target, *drag_start, *drag_end)
PY
)"
  read -r target_x target_y drag_start_x drag_start_y drag_end_x drag_end_y \
    <<<"$touch_coordinates"

  adb shell input tap "$target_x" "$target_y"
  local selected_by_touch=false
  for _ in $(seq 1 40); do
    dispatch_ci_command "status" "$scenario_directory/Touch.Selected.command.json"
    if python3 - "$touch_initial" "$scenario_directory/Touch.Selected.command.json" <<'PY'
import json
import sys

before = json.load(open(sys.argv[1], encoding="utf-8"))
after = json.load(open(sys.argv[2], encoding="utf-8"))
raise SystemExit(0 if after.get("selectedId") == before.get("touchTargetId") else 1)
PY
    then
      selected_by_touch=true
      break
    fi
    sleep 0.1
  done
  if [[ "$selected_by_touch" != "true" ]]; then
    echo "A real ADB tap did not select the projected world target." >&2
    exit 1
  fi

  # Send the physical pair within the 300 ms product contract. EnhancedTouch
  # records every state change, so both taps remain observable even when the
  # software renderer consumes down/up/down/up in one slow Unity update.
  sleep 0.35
  local first_gesture="$scenario_directory/Touch.DoubleTap.First.gesture.json"
  local second_gesture="$scenario_directory/Touch.DoubleTap.Second.gesture.json"
  local double_tap_max_attempts=3
  local double_tap_shell_gap_seconds=0.02
  local double_tap_succeeded=false
  local double_tap_attempt
  local gesture_generation
  local first_gesture_remote_path
  local second_gesture_remote_path
  for double_tap_attempt in $(seq 1 "$double_tap_max_attempts"); do
    local attempt_prefix="$scenario_directory/Touch.DoubleTap.Attempt-$double_tap_attempt"
    local baseline_gesture="$attempt_prefix.Baseline.gesture.json"
    local attempt_first_gesture="$attempt_prefix.First.gesture.json"
    local attempt_second_gesture="$attempt_prefix.Second.gesture.json"

    # A failed pair must age beyond the 300 ms recognition window before the
    # next baseline is sampled, otherwise a retry could join an earlier Tap.
    if (( double_tap_attempt > 1 )); then
      sleep 0.35
    fi
    pull_app_file "starfall-ci-gesture.json" "$baseline_gesture"
    gesture_generation="$(python3 - "$baseline_gesture" <<'PY'
import json
import sys

print(int(json.load(open(sys.argv[1], encoding="utf-8"))["generation"]))
PY
)"
    first_gesture_remote_path="$(starfall_android_app_file_path \
      "$package_name" "starfall-ci-gesture-$((gesture_generation + 1)).json")"
    second_gesture_remote_path="$(starfall_android_app_file_path \
      "$package_name" "starfall-ci-gesture-$((gesture_generation + 2)).json")"
    adb shell "rm -f '$first_gesture_remote_path' '$second_gesture_remote_path'; \
      input tap '$target_x' '$target_y'; sleep '$double_tap_shell_gap_seconds'; \
      input tap '$target_x' '$target_y'" \
      >"$attempt_prefix.Input.txt" 2>&1

    local first_gesture_ready=false
    local second_gesture_ready=false
    if wait_for_gesture_evidence \
      "$first_gesture_remote_path" "$attempt_first_gesture" \
      "$((gesture_generation + 1))" "Tap" false; then
      first_gesture_ready=true
    fi
    if wait_for_gesture_evidence \
      "$second_gesture_remote_path" "$attempt_second_gesture" \
      "$((gesture_generation + 2))" "DoubleTap" false; then
      second_gesture_ready=true
    fi
    if [[ "$first_gesture_ready" == "true" && "$second_gesture_ready" == "true" ]]; then
      cp "$attempt_first_gesture" "$first_gesture"
      cp "$attempt_second_gesture" "$second_gesture"
      cp "$attempt_prefix.Input.txt" "$scenario_directory/Touch.DoubleTap.Input.txt"
      double_tap_succeeded=true
      break
    fi
    echo "Physical double-tap attempt $double_tap_attempt/$double_tap_max_attempts did not complete inside 300 ms." >&2
  done
  if [[ "$double_tap_succeeded" != "true" ]]; then
    echo "Timed out waiting for a physical ADB double-tap inside the 300 ms product window." >&2
    exit 1
  fi
  python3 - "$first_gesture" "$second_gesture" <<'PY'
import json
import sys

first = json.load(open(sys.argv[1], encoding="utf-8"))
second = json.load(open(sys.argv[2], encoding="utf-8"))
cadence = float(second["inputStartTime"]) - float(first["inputStartTime"])
if cadence < 0.0 or cadence > 0.3:
    raise SystemExit(f"physical double-tap cadence outside 300 ms: {cadence:.6f}s")
PY
  local approached_by_double_tap=false
  for _ in $(seq 1 40); do
    dispatch_ci_command "status" "$scenario_directory/Touch.DoubleTap.command.json"
    if python3 - "$scenario_directory/Touch.DoubleTap.command.json" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
raise SystemExit(0 if payload.get("movement") == "Approach" else 1)
PY
    then
      approached_by_double_tap=true
      break
    fi
    sleep 0.1
  done
  if [[ "$approached_by_double_tap" != "true" ]]; then
    echo "Two real ADB taps did not trigger the world double-tap Approach gesture." >&2
    exit 1
  fi

  adb shell input swipe \
    "$drag_start_x" "$drag_start_y" "$drag_end_x" "$drag_end_y" \
    "$world_swipe_duration_ms"
  local rotated_by_drag=false
  for _ in $(seq 1 40); do
    dispatch_ci_command "status" "$scenario_directory/Touch.Drag.command.json"
    if python3 - "$touch_initial" "$scenario_directory/Touch.Drag.command.json" <<'PY'
import json
import sys

before = float(json.load(open(sys.argv[1], encoding="utf-8"))["cameraYawDegrees"])
after = float(json.load(open(sys.argv[2], encoding="utf-8"))["cameraYawDegrees"])
angular_delta = abs((after - before + 180.0) % 360.0 - 180.0)
raise SystemExit(0 if angular_delta >= 2.0 else 1)
PY
    then
      rotated_by_drag=true
      break
    fi
    sleep 0.1
  done
  if [[ "$rotated_by_drag" != "true" ]]; then
    echo "A real ADB world swipe did not rotate the camera." >&2
    exit 1
  fi

  tap_control "$scenario_directory/Space.layout.json" "module-1"
  local module_toggled_by_touch=false
  for _ in $(seq 1 40); do
    dispatch_ci_command "status" "$scenario_directory/Touch.Module.command.json"
    if python3 - "$touch_initial" "$scenario_directory/Touch.Module.command.json" <<'PY'
import json
import sys

before = json.load(open(sys.argv[1], encoding="utf-8"))["module0Active"]
after = json.load(open(sys.argv[2], encoding="utf-8")).get("module0Active")
raise SystemExit(0 if isinstance(after, bool) and after != before else 1)
PY
    then
      module_toggled_by_touch=true
      break
    fi
    sleep 0.1
  done
  if [[ "$module_toggled_by_touch" != "true" ]]; then
    echo "A real ADB tap on module-1 did not toggle the live module state." >&2
    exit 1
  fi
  record_scenario_stage "$stage_file" "ADB touch selection, double-tap, drag, and module"

  tap_control "$scenario_directory/Space.layout.json" "settings"
  wait_for_ui_surface \
    "starfall-ci-layout-Space.json" "$scenario_directory/Settings.layout.json" "settings-card"
  validate_ui_layout_json \
    "$scenario_directory/Settings.layout.json" "$expected_mode" "$require_hinge" "settings-card"
  capture_screen "$label-Settings" "$width" "$height"
  record_scenario_stage "$stage_file" "Space Settings layout and PNG"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "settings-card"

  tap_control "$scenario_directory/Space.layout.json" "map"
  wait_for_ui_surface \
    "starfall-ci-layout-Space.json" "$scenario_directory/Starmap.layout.json" \
    "starmap-card" "map-close"
  validate_ui_layout_json \
    "$scenario_directory/Starmap.layout.json" "$expected_mode" "$require_hinge" "starmap-card"
  capture_screen "$label-Starmap" "$width" "$height"
  record_scenario_stage "$stage_file" "Starmap layout and PNG"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "starmap-card"

  tap_control_until_surface \
    "$scenario_directory/Space.layout.json" "journal" \
    "starfall-ci-layout-Space.json" "$scenario_directory/Journal.layout.json" "journal-card"
  validate_ui_layout_json \
    "$scenario_directory/Journal.layout.json" "$expected_mode" "$require_hinge" "journal-card"
  capture_screen "$label-Journal" "$width" "$height"
  record_scenario_stage "$stage_file" "Journal layout and PNG"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "journal-card"

  if ! python3 - "$scenario_directory/Space.layout.json" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
names = {
    surface.get("name")
    for surface in payload.get("surfaces", [])
    if surface.get("visible", True)
}
raise SystemExit(0 if "combat-log-scroll" in names else 1)
PY
  then
    tap_control "$scenario_directory/Space.layout.json" "mobile-log-toggle"
  fi
  wait_for_ui_surface \
    "starfall-ci-layout-Space.json" "$scenario_directory/CombatLog.layout.json" "combat-log-scroll"
  validate_ui_layout_json \
    "$scenario_directory/CombatLog.layout.json" "$expected_mode" "$require_hinge" "combat-log-scroll"
  capture_screen "$label-CombatLog" "$width" "$height"
  record_scenario_stage "$stage_file" "Combat Log layout and PNG"
  if [[ "$expected_mode" == "CompactLandscape" ]]; then
    tap_control "$scenario_directory/CombatLog.layout.json" "mobile-overview-toggle"
    wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "combat-log-scroll"
  fi

  # The first Back from gameplay must produce the return confirmation; the next
  # Back must close only that topmost overlay.
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface \
    "starfall-ci-layout-Space.json" "$scenario_directory/BackConfirmation.layout.json" "confirmation-card"
  validate_ui_layout_json \
    "$scenario_directory/BackConfirmation.layout.json" "$expected_mode" "$require_hinge" "confirmation-card"
  capture_screen "$label-BackConfirmation" "$width" "$height"
  record_scenario_stage "$stage_file" "Back behavior and confirmation layout"
  adb shell input keyevent KEYCODE_BACK
  wait_for_ui_surface_absent "starfall-ci-layout-Space.json" "confirmation-card"

  dispatch_ci_command \
    "show-death-overlay" "$scenario_directory/show-death-overlay.command.json"
  wait_for_ui_surface \
    "starfall-ci-layout-Space.json" "$scenario_directory/Death.layout.json" "death-card"
  validate_ui_layout_json \
    "$scenario_directory/Death.layout.json" "$expected_mode" "$require_hinge" "death-card"
  capture_screen "$label-Death-Fixture" "$width" "$height"
  record_scenario_stage "$stage_file" "Death overlay layout and PNG"
  printf 'source=STARFALL_ANDROID_CI show-death-overlay fixture\nreleaseIncluded=false\n' \
    >"$scenario_directory/death-fixture.txt"

  adb shell dumpsys meminfo "$package_name" >"$scenario_directory/meminfo.txt"

  assert_no_app_failures "$label"
  actual_graphics_device="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["graphicsDeviceType"])' \
    "$scenario_directory/graphics-device.command.json")"
  actual_graphics_name="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["graphicsDeviceName"])' \
    "$scenario_directory/graphics-device.command.json")"
  graphics_verdict="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["verdict"])' \
    "$scenario_directory/graphics-verification.json")"
  printf 'label=%s\nwidth=%s\nheight=%s\ndensityDpi=%s\ndensity=%s\nmode=%s\ngraphics=%s\n' \
    "$label" "$width" "$height" "$density_dpi" "$density_scale" \
    "$expected_mode" "$graphics_argument" >"$scenario_directory/scenario.txt"
  printf 'actualGraphicsDeviceType=%s\nactualGraphicsDeviceName=%s\ngraphicsVerification=%s\n' \
    "$actual_graphics_device" "$actual_graphics_name" "$graphics_verdict" \
    >>"$scenario_directory/scenario.txt"
  record_scenario_stage "$stage_file" "shader and logcat gates"
}

run_selected_scenario() {
  local label="$1"
  if ! scenario_is_selected "$label"; then
    return 0
  fi
  selected_scenario_count=$((selected_scenario_count + 1))
  run_scenario "$@"
}

no_fold='[]'
vertical_hinge='[{"bounds":{"x":1198,"y":0,"width":84,"height":2200},"orientation":"VERTICAL","state":"FLAT","occlusion":"FULL","separating":true}]'
horizontal_hinge='[{"bounds":{"x":0,"y":1060,"width":2480,"height":80},"orientation":"HORIZONTAL","state":"HALF_OPENED","occlusion":"NONE","separating":true}]'

selected_scenario_count=0
if [[ "$acceptance_suite" == "all" || "$acceptance_suite" == "scenarios" ]]; then
  run_selected_scenario "2748x1172-420-vulkan-preferred" 2748 1172 420 CompactLandscape -force-vulkan "$no_fold" false
  run_selected_scenario "2480x2200-420-vulkan-preferred" 2480 2200 420 SquareExpanded -force-vulkan "$no_fold" false
  run_selected_scenario "2480x2200-420-vertical-hinge" 2480 2200 420 SquareExpanded -force-vulkan "$vertical_hinge" true
  run_selected_scenario "2480x2200-420-horizontal-half-opened" 2480 2200 420 SquareExpanded -force-vulkan "$horizontal_hinge" true
  run_selected_scenario "2748x1172-420-gles3" 2748 1172 420 CompactLandscape -force-gles30 "$no_fold" false
  run_selected_scenario "2748x1172-320-geometry" 2748 1172 320 CompactLandscape -force-vulkan "$no_fold" false
  run_selected_scenario "2480x2200-560-geometry" 2480 2200 560 SquareExpanded -force-vulkan "$no_fold" false
  if (( selected_scenario_count == 0 )); then
    echo "STARFALL_ANDROID_SCENARIOS did not select a known scenario: $scenario_filter" >&2
    exit 2
  fi
fi

if [[ "$acceptance_suite" == "all" || "$acceptance_suite" == "lifecycle" ]]; then
  run_legacy_saf_import

# Exercise actual simulation transitions, repeated procedural presentation,
# twelve jumps, hostile engagement, and the post-warmup PSS growth rule.
dismiss_known_system_startup_dialogs "stability-before-start"
bash scripts/android-stability-ci.sh "$results_directory/stability"
assert_no_app_failures "stability"

# Exercise lifecycle save coalescing, low-memory cleanup, force-stop recovery,
# and a real Continue action after the geometry/import/stability gates.
adb exec-out cat \
  "$persistent_data_directory/Saves/auto.json" \
  >"$results_directory/lifecycle-auto-before.json"
auto_mtime_before="$(adb shell stat -c %Y \
  "$persistent_data_directory/Saves/auto.json" | tr -d '\r')"
for _ in $(seq 1 10); do
  adb shell input keyevent KEYCODE_HOME
  adb shell am start -W -n "$activity" >/dev/null
done
adb exec-out cat \
  "$persistent_data_directory/Saves/auto.json" \
  >"$results_directory/lifecycle-auto-after.json"
auto_mtime_after="$(adb shell stat -c %Y \
  "$persistent_data_directory/Saves/auto.json" | tr -d '\r')"
python3 - \
  "$results_directory/lifecycle-auto-before.json" \
  "$results_directory/lifecycle-auto-after.json" \
  "$auto_mtime_before" "$auto_mtime_after" <<'PY'
import json
import sys

before = json.load(open(sys.argv[1], encoding="utf-8"))
after = json.load(open(sys.argv[2], encoding="utf-8"))
for label, payload in (("before", before), ("after", after)):
    if payload.get("schemaVersion") != 2:
        raise SystemExit(f"Lifecycle auto save {label} has an invalid schema")
    player = payload.get("player") or {}
    runtime = player.get("runtime") if isinstance(player, dict) else None
    stats_owner = runtime if isinstance(runtime, dict) else player
    jumps = int((stats_owner.get("stats") or {}).get("jumps") or 0)
    if jumps < 12:
        raise SystemExit(f"Lifecycle auto save {label} lost jump progress: {jumps}")
if float(after.get("simulationTime") or 0) < float(before.get("simulationTime") or 0):
    raise SystemExit("Lifecycle auto save simulation time moved backwards")
if int(sys.argv[4]) <= int(sys.argv[3]):
    raise SystemExit("Ten pause/focus cycles did not rewrite the atomic auto save")
PY
printf 'mtimeBefore=%s\nmtimeAfter=%s\ncycles=10\n' \
  "$auto_mtime_before" "$auto_mtime_after" >"$results_directory/lifecycle-save-evidence.txt"
  dispatch_ci_command \
    "seed-low-memory-fixture" "$results_directory/trim-memory.seed.command.json"
  adb shell rm -f \
    "$persistent_data_directory/starfall-ci-low-memory.json"
  adb shell am send-trim-memory "$package_name" RUNNING_CRITICAL \
    >"$results_directory/trim-memory.txt"
  low_memory_observed=false
  for _ in $(seq 1 80); do
    if adb exec-out cat \
      "$persistent_data_directory/starfall-ci-low-memory.json" \
      >"$results_directory/trim-memory.callback.json" 2>/dev/null && \
      python3 - "$results_directory/trim-memory.callback.json" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
if int(payload.get("generation") or 0) < 1:
    raise SystemExit("low-memory callback generation was not incremented")
for field in (
    "releasedTransientVfx", "releasedSpaceCacheEntries", "releasedShipCacheEntries"
):
    value = payload.get(field)
    if not isinstance(value, int) or value < 0:
        raise SystemExit(f"invalid low-memory release count {field}={value!r}")
released = sum(int(payload[field]) for field in (
    "releasedTransientVfx", "releasedSpaceCacheEntries", "releasedShipCacheEntries"
))
if released < 2:
    raise SystemExit(f"low-memory fixture was not released: total={released}")
PY
    then
      low_memory_observed=true
      break
    fi
    sleep 0.25
  done
  if [[ "$low_memory_observed" != "true" ]]; then
    echo "Unity did not publish evidence that AppRoot.OnLowMemory handled RUNNING_CRITICAL." >&2
    exit 1
  fi
  trim_pid="$(adb shell pidof "$package_name" | tr -d '\r')"
if [[ ! "$trim_pid" =~ ^[0-9]+$ ]]; then
  echo "App process did not survive the RUNNING_CRITICAL trim-memory callback." >&2
  exit 1
fi
printf '%s\n' "$trim_pid" >"$results_directory/trim-memory.pid.txt"
adb shell dumpsys meminfo "$package_name" >"$results_directory/trim-memory.meminfo.txt"
adb logcat -d --pid="$trim_pid" >"$results_directory/trim-memory.app.logcat.txt"
dispatch_ci_command status "$results_directory/trim-memory.post-status.command.json"
assert_no_app_failures "low-memory-trim"
adb shell am force-stop "$package_name"
adb shell rm -f \
  "$persistent_data_directory/starfall-ci-render-ready.json"
adb shell am start -W -n "$activity" >"$results_directory/force-stop-restart.txt"
wait_for_process >/dev/null
starfall_wait_for_unity_render_ready \
  "$package_name" "$results_directory/force-stop-render-ready.json" MainMenu
pull_app_file "starfall-ci-layout-MainMenu.json" "$results_directory/Continue.MainMenu.layout.json"
tap_control "$results_directory/Continue.MainMenu.layout.json" continue

continued=false
for _ in $(seq 1 120); do
  if dispatch_ci_command status "$results_directory/continue-status.json" && \
    python3 - "$results_directory/continue-status.json" <<'PY'
import json
import sys

payload = json.load(open(sys.argv[1], encoding="utf-8"))
ok = (
    payload.get("hasSession") is True
    and payload.get("scene") == "Space"
    and int(payload.get("jumps") or 0) >= 12
)
raise SystemExit(0 if ok else 1)
PY
  then
    continued=true
    break
  fi
  sleep 0.5
done
if [[ "$continued" != true ]]; then
  echo "Force-stop recovery did not continue the valid saved Space session." >&2
  exit 1
fi
adb shell ls -l "$persistent_data_directory/Saves" \
  >"$results_directory/force-stop-save-files.txt"
assert_no_app_failures "lifecycle-force-stop-continue"
fi

echo "Android emulator acceptance evidence: $results_directory"
