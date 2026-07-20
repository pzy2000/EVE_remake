#!/usr/bin/env bash
set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
# shellcheck source=scripts/android-emulator-common.sh
export STARFALL_ADB_EXECUTABLE=/usr/bin/false
source "$script_directory/android-emulator-common.sh"

sleep() {
  :
}

timeout_wrapper_calls=0
timeout() {
  if [[ "${1:-}" == "30s" && "${2:-}" == "/usr/bin/false" ]]; then
    timeout_wrapper_calls=$((timeout_wrapper_calls + 1))
    return 124
  fi
  shift
  "$@"
}

if adb get-state; then
  echo 'The shared ADB timeout wrapper incorrectly accepted a timed-out command.' >&2
  exit 1
fi
[[ "$timeout_wrapper_calls" == "1" ]]

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT INT TERM
apk="$temporary_directory/smoke.apk"
: >"$apk"

capture_calls=0
adb() {
  capture_calls=$((capture_calls + 1))
  if (( capture_calls < 3 )); then
    return 124
  fi
  printf '%s\n' 'complete evidence'
}
capture_evidence="$temporary_directory/retried-evidence.txt"
starfall_adb_capture_file "$capture_evidence" exec-out screencap -p
[[ "$capture_calls" == "3" ]]
grep -Fqx 'complete evidence' "$capture_evidence"
test ! -e "$capture_evidence.adb-partial"

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
android_build_entry="$script_directory/../UnityProject/Assets/Editor/Android/StarfallAndroidBuild.cs"
back_compat_entry="$script_directory/android-back-compat-ci.sh"
emulator_entry="$script_directory/android-emulator-ci.sh"
android_ci_automation="$script_directory/../UnityProject/Assets/Starfall/App/AndroidCiAutomation.cs"
procedural_ship_factory="$script_directory/../UnityProject/Assets/Starfall/Presentation/ProceduralShipFactory.cs"
procedural_space_materials="$script_directory/../UnityProject/Assets/Starfall/Presentation/ProceduralSpaceMaterials.cs"
space_world_presenter="$script_directory/../UnityProject/Assets/Starfall/Presentation/SpaceWorldPresenter.cs"
ci_minimal_shader="$script_directory/../UnityProject/Assets/Starfall/Shaders/StarfallCiMinimalUnlit.shader"
android_panel_settings="$script_directory/../UnityProject/Assets/Resources/StarfallAndroidPanelSettings.asset"
android_scene_processor="$script_directory/../UnityProject/Assets/Editor/Android/StarfallAndroidSceneProcessor.cs"
workflow_entry="$script_directory/../.github/workflows/android.yml"
grep -Fq \
  '{fileID: 4800000, guid: 650dd9526735d5b46b79224bc6e94025, type: 3}' \
  "$graphics_settings" || {
  echo 'URP Unlit must remain in Always Included Shaders for runtime materials.' >&2
  exit 1
}
if grep -Fq 'BuildOptions.AllowDebugging' "$android_build_entry"; then
  echo 'The emulator smoke player must not enable Unity script debugging.' >&2
  exit 1
fi
grep -Fq 'buildOptions |= BuildOptions.Development;' "$android_build_entry" || {
  echo 'The smoke player must remain debuggable for its CI-only native receiver.' >&2
  exit 1
}
grep -Fq 'timeout 30s adb wait-for-device' \
  "$script_directory/android-emulator-common.sh" || {
  echo 'ADB wait-for-device must remain bounded by a timeout.' >&2
  exit 1
}
grep -Fq 'timeout "${timeout_seconds}s" "$starfall_adb_executable" "$@"' \
  "$script_directory/android-emulator-common.sh" || {
  echo 'Every ordinary ADB command must remain behind the shared timeout wrapper.' >&2
  exit 1
}
if grep -En '^[[:space:]]*adb wait-for-device([[:space:]]|$)' \
  "$script_directory"/android-{back-compat-ci,emulator-ci,emulator-common}.sh; then
  echo 'Every ADB wait-for-device call must use the bounded transport helper.' >&2
  exit 1
fi
[[ "$(grep -Fc 'wait_for_main_menu_layout' "$back_compat_entry")" == "2" ]] || {
  echo 'Back compatibility evidence polling must not nest two retry loops.' >&2
  exit 1
}
grep -Fq 'if read_main_menu_layout "$confirmation_layout"' "$back_compat_entry" || {
  echo 'Back confirmation polling must use a single-read inner operation.' >&2
  exit 1
}
[[ "$(grep -Fc 'ram-size: 3072M' "$workflow_entry")" == "2" ]] || {
  echo 'Both emulator jobs must keep the bounded 3072M guest-memory budget.' >&2
  exit 1
}
if grep -Eq 'ram-size: (4096|6144)M' "$workflow_entry"; then
  echo 'The emulator must not restore a memory reservation that destabilizes the hosted runner.' >&2
  exit 1
fi
[[ "$(grep -Fc 'emulator-build: 15004761' "$workflow_entry")" == "2" ]] || {
  echo 'Both emulator jobs must pin Android Emulator 36.4.10 build 15004761.' >&2
  exit 1
}
[[ "$(grep -Fc -- '-gpu software -feature -Vulkan ' "$workflow_entry")" == "2" ]] || {
  echo 'Both emulator jobs must use the supported adaptive software backend with Vulkan disabled.' >&2
  exit 1
}
if grep -Eq -- '-gpu (swiftshader|swiftshader_indirect)' "$workflow_entry"; then
  echo 'The ColorBuffer-crashing or deprecated SwiftShader modes must not return.' >&2
  exit 1
fi
grep -Fq 'PlayerSettings.SplashScreen.show = !isSmoke;' "$android_build_entry" || {
  echo 'The smoke player must disable the Unity splash without changing release builds.' >&2
  exit 1
}
grep -Fq 'smokePipeline.Apply(flavor == SmokeFlavor);' "$android_build_entry" || {
  echo 'The smoke build must apply its GLES3-minimum URP settings only to ci-smoke.' >&2
  exit 1
}
grep -Fq 'WriteSerializedSettings(LightRenderingMode.Disabled, false);' "$android_build_entry" || {
  echo 'The smoke URP variant must stay within the emulator GLES3 uniform budget.' >&2
  exit 1
}
grep -Fq '"m_AdditionalLightsRenderingMode"' "$android_build_entry" || {
  echo 'The smoke URP configuration must use the URP serialized lighting field.' >&2
  exit 1
}
grep -Fq 'smokePipeline.Restore();' "$android_build_entry" || {
  echo 'The release Mobile URP settings must be restored after every build attempt.' >&2
  exit 1
}
grep -Fq 'SmokeMaterialShaderSnapshot.Capture(' "$android_build_entry" || {
  echo 'The smoke build must replace project Lit materials for minimum-spec GLES3.' >&2
  exit 1
}
grep -Fq 'private const string SmokeShaderName = "Starfall/CI/MinimalUnlit";' "$android_build_entry" || {
  echo 'The smoke material replacement must use the minimum-uniform CI shader.' >&2
  exit 1
}
grep -Fq 'smokeMaterials.Restore();' "$android_build_entry" || {
  echo 'The release material shader references must be restored after every build attempt.' >&2
  exit 1
}
grep -Fq 'Shader "Starfall/CI/MinimalUnlit"' "$ci_minimal_shader" || {
  echo 'The minimum-uniform smoke shader asset is missing.' >&2
  exit 1
}
if grep -Eq 'Packages/com\.unity\.render-pipelines|UnityInstancing|multi_compile' "$ci_minimal_shader"; then
  echo 'The minimum-uniform smoke shader must not import URP global buffers or variants.' >&2
  exit 1
fi
grep -Fq 'm_SpriteShader: {fileID: 19012' "$android_panel_settings" || {
  echo "Android PanelSettings must retain Unity's built-in sprite shader." >&2
  exit 1
}
grep -Fq 'IProcessSceneWithReport' "$android_scene_processor" || {
  echo 'Android builds must bind PanelSettings before UIDocument player serialization.' >&2
  exit 1
}
for runtime_material_source in "$procedural_ship_factory" "$procedural_space_materials"; do
  grep -Fq '#if STARFALL_ANDROID_CI && UNITY_ANDROID' "$runtime_material_source" || {
    echo "Runtime material source must isolate its emulator shader: $runtime_material_source" >&2
    exit 1
  }
  grep -Fq 'Shader.Find("Starfall/CI/MinimalUnlit")' "$runtime_material_source" || {
    echo "Runtime smoke materials must use the minimum-uniform CI shader: $runtime_material_source" >&2
    exit 1
  }
done
android_ci_automation="$script_directory/../UnityProject/Assets/Starfall/App/AndroidCiAutomation.cs"
if grep -Eq 'yield return new WaitForEndOfFrame|while .*Time\.frameCount' "$android_ci_automation"; then
  echo 'Android CI scene readiness must not depend on a visible display surface or frame clock.' >&2
  exit 1
fi
grep -Fq 'SceneManager.GetActiveScene().name == "Bootstrap"' "$android_ci_automation" || {
  echo 'Android CI readiness must still wait until MainMenu replaces Bootstrap.' >&2
  exit 1
}
grep -Fq 'starfall_wait_for_unity_render_ready' "$back_compat_entry" || {
  echo 'The API 32 Back gate must wait for a rendered MainMenu frame.' >&2
  exit 1
}
[[ "$(grep -Fc 'starfall_wait_for_unity_render_ready' "$emulator_entry")" == "3" ]] || {
  echo 'Every API 36 cold-start path must wait for a rendered post-splash frame.' >&2
  exit 1
}
grep -A17 '^tap_coordinate()' "$emulator_entry" | grep -Fq 'sleep 1' || {
  echo 'Sequential ADB UI taps must settle on distinct Unity player frames.' >&2
  exit 1
}
grep -Fq 'wait_for_gesture_evidence' "$emulator_entry" || {
  echo 'The real Android double-tap gate must synchronize against Unity gesture frames.' >&2
  exit 1
}
grep -Fq 'double_tap_max_attempts=3' "$emulator_entry" || {
  echo 'The physical ADB double-tap gate must retry incomplete pairs a bounded number of times.' >&2
  exit 1
}
grep -Fq 'if (( double_tap_attempt > 1 )); then' "$emulator_entry" || {
  echo 'A failed physical tap pair must age out before retrying.' >&2
  exit 1
}
grep -Fq '"$first_gesture_remote_path" "$attempt_first_gesture"' \
  "$emulator_entry" || {
  echo 'The double-tap gate must retain first-tap recognizer evidence.' >&2
  exit 1
}
grep -Fq 'double_tap_shell_gap_seconds=0.02' "$emulator_entry" || {
  echo 'The physical ADB pair needs margin for Android input command overhead.' >&2
  exit 1
}
grep -Fq "input tap '\$target_x' '\$target_y'; sleep '\$double_tap_shell_gap_seconds';" "$emulator_entry" || {
  echo 'The physical ADB tap pair must stay inside the 300 ms product window.' >&2
  exit 1
}
grep -Fq '"$second_gesture_remote_path" "$attempt_second_gesture"' \
  "$emulator_entry" || {
  echo 'The double-tap gate must retain recognizer evidence for the completed pair.' >&2
  exit 1
}
grep -Fq 'if cadence < 0.0 or cadence > 0.3:' "$emulator_entry" || {
  echo 'The accepted physical tap pair must be checked against the product cadence.' >&2
  exit 1
}
grep -Fq 'AndroidCiGestureEvidenceFile = "starfall-ci-gesture.json"' \
  "$space_world_presenter" || {
  echo 'The smoke presenter must publish gesture-frame evidence for real ADB input.' >&2
  exit 1
}
grep -Fq 'EnhancedTouchSupport.Enable();' \
  "$space_world_presenter" || {
  echo 'Runtime touch input must preserve state changes shorter than one render frame.' >&2
  exit 1
}
grep -Fq 'var tapTime = touch.startTime;' \
  "$space_world_presenter" || {
  echo 'Double-tap cadence must use the physical EnhancedTouch timestamp.' >&2
  exit 1
}
grep -Fq '"starfall-ci-gesture-" + androidCiGestureGeneration + ".json"' \
  "$space_world_presenter" || {
  echo 'Per-generation gesture evidence must survive same-frame tap processing.' >&2
  exit 1
}
grep -Fq 'world_swipe_duration_ms=1500' "$emulator_entry" || {
  echo 'The real ADB drag must span multiple frames on the slow square software renderer.' >&2
  exit 1
}
grep -Fq 'local expected_control="${4:-}"' "$emulator_entry" || {
  echo 'Overlay evidence waits must support a required settled control.' >&2
  exit 1
}
grep -A3 'Starmap.layout.json' "$emulator_entry" | grep -Fq '"starmap-card" "map-close"' || {
  echo 'Starmap validation must wait until its close control has settled.' >&2
  exit 1
}
grep -A2 'adb shell input swipe' "$emulator_entry" | \
  grep -Fq '"$world_swipe_duration_ms"' || {
  echo 'The real ADB drag must use the multi-frame swipe duration.' >&2
  exit 1
}
grep -Fq 'and not str(item.get("name") or "").startswith("unity-")' \
  "$emulator_entry" || {
  echo 'Unity internal controls must not masquerade as application touch targets.' >&2
  exit 1
}

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
render_ready_calls=0
broadcast_calls=0
broadcast_command=""
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
    exec-out)
      if [[ "$*" == "exec-out cat $expected_files_directory/starfall-ci-render-ready.json" ]]; then
        render_ready_calls=$((render_ready_calls + 1))
        if (( render_ready_calls == 1 )); then
          printf '%s\n' '{"scene":"MainMenu","frameCount":0}'
        else
          printf '%s\n' '{"scene":"MainMenu","frameCount":42}'
        fi
        return 0
      fi
      echo "Unexpected adb exec-out command in test: $*" >&2
      return 2
      ;;
    reconnect|kill-server|start-server)
      return 0
      ;;
    shell)
      if [[ "$#" == "2" && "${2:-}" == am\ broadcast\ -a\ * ]]; then
        broadcast_calls=$((broadcast_calls + 1))
        broadcast_command="$2"
        printf '%s\n' 'Broadcast completed: result=0'
        return 0
      fi
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

render_ready_evidence="$temporary_directory/render-ready.json"
starfall_wait_for_unity_render_ready \
  com.pzy.starfallodyssey "$render_ready_evidence" MainMenu 3
[[ "$render_ready_calls" == "2" ]]
grep -Fq '"frameCount":42' "$render_ready_evidence"

broadcast_json='{"schemaVersion":1,"source":"github-actions","safeArea":{"x":0,"y":0},"label":"pilot'\''s screen"}'
starfall_adb_broadcast_string_extra \
  com.pzy.starfall.mobile.DEBUG_WINDOW_LAYOUT json "$broadcast_json" \
  >"$temporary_directory/broadcast.txt"
[[ "$broadcast_calls" == "1" ]]
[[ "$broadcast_command" == \
  "am broadcast -a com.pzy.starfall.mobile.DEBUG_WINDOW_LAYOUT --es json '{\"schemaVersion\":1,\"source\":\"github-actions\",\"safeArea\":{\"x\":0,\"y\":0},\"label\":\"pilot'\\''s screen\"}'" ]]
grep -Fqx 'Broadcast completed: result=0' "$temporary_directory/broadcast.txt"
if starfall_adb_broadcast_string_extra 'invalid action' json '{}'; then
  echo 'An invalid Android broadcast action was incorrectly accepted.' >&2
  exit 1
fi

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
