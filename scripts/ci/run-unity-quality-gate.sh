#!/usr/bin/env bash
set -uo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
phase="${1:-all}"
output_dir="${QUALITY_GATE_OUTPUT_DIR:-$project_root/artifacts/quality-gate}"
reporter="$script_dir/quality_gate_report.py"
unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_root/ProjectSettings/ProjectVersion.txt")"

resolve_unity() {
  if [[ -n "${UNITY_EXECUTABLE:-}" && -x "${UNITY_EXECUTABLE}" ]]; then
    printf '%s\n' "$UNITY_EXECUTABLE"
    return 0
  fi

  local candidates=(
    "/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/MacOS/Unity"
    "/Users/${USER}/UnityEditors/$unity_version/Unity.app/Contents/MacOS/Unity"
  )
  local candidate

  for candidate in "${candidates[@]}"; do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  command -v Unity 2>/dev/null || return 1
}

evaluate_phase() {
  local current_phase="$1"
  local exit_code="$2"
  local log_path="$3"
  local results_path="${4:-}"
  local marker_path="${5:-}"
  local arguments=(
    evaluate
    --phase "$current_phase"
    --exit-code "$exit_code"
    --log "$log_path"
    --output-dir "$output_dir"
  )

  if [[ -n "$results_path" ]]; then
    arguments+=(--results "$results_path")
  fi

  if [[ -n "$marker_path" ]]; then
    arguments+=(--marker "$marker_path")
  fi

  python3 "$reporter" "${arguments[@]}"
}

run_compile() {
  local log_path="$output_dir/compile.log"
  local marker_path="$output_dir/compile.marker"
  rm -f "$log_path" "$marker_path"
  FPS_QUALITY_GATE_MARKER="$marker_path" "$unity" \
    -batchmode \
    -nographics \
    -disable-audio \
    -quit \
    -projectPath "$project_root" \
    -executeMethod FPS.Editor.CI.UnityQualityGate.CompileCheck \
    -logFile "$log_path"
  local exit_code=$?
  evaluate_phase compile "$exit_code" "$log_path" "" "$marker_path"
}

run_tests() {
  local current_phase="$1"
  local platform="$2"
  local graphics_flag="$3"
  local log_path="$output_dir/${current_phase}.log"
  local results_path="$output_dir/${current_phase}.xml"
  rm -f "$log_path" "$results_path"
  local command=(
    "$unity"
    -batchmode
    -disable-audio
    -projectPath "$project_root"
    -runTests
    -testPlatform "$platform"
    -testResults "$results_path"
    -logFile "$log_path"
  )

  if [[ -n "$graphics_flag" ]]; then
    command+=("$graphics_flag")
  fi

  "${command[@]}"
  local exit_code=$?
  evaluate_phase "$current_phase" "$exit_code" "$log_path" "$results_path"
}

case "$phase" in
  all|compile|editmode|playmode) ;;
  *)
    printf '用法: %s [all|compile|editmode|playmode]\n' "$0" >&2
    exit 64
    ;;
esac

mkdir -p "$output_dir"
rm -f "$output_dir"/{compile,editmode,playmode,summary}.{json,md}

if ! unity="$(resolve_unity)"; then
  printf 'Unity %s executable was not found.\n' "$unity_version" \
    > "$output_dir/compile.log"
  evaluate_phase compile 127 "$output_dir/compile.log" || true
  python3 "$reporter" aggregate --output-dir "$output_dir"
  exit $?
fi

if [[ -e "$project_root/Temp/UnityLockfile" ]]; then
  printf 'Unity project is already open; close the Editor before running CI.\n' \
    > "$output_dir/compile.log"
  evaluate_phase compile 73 "$output_dir/compile.log" || true
  python3 "$reporter" aggregate --output-dir "$output_dir"
  exit $?
fi

overall_failure=0

if [[ "$phase" == "all" || "$phase" == "compile" ]]; then
  run_compile || overall_failure=1
fi

if [[ "$phase" == "all" && "$overall_failure" -ne 0 ]]; then
  python3 "$reporter" aggregate --output-dir "$output_dir" || true
  exit 1
fi

if [[ "$phase" == "all" || "$phase" == "editmode" ]]; then
  run_tests editmode EditMode -nographics || overall_failure=1
fi

if [[ "$phase" == "all" || "$phase" == "playmode" ]]; then
  run_tests playmode PlayMode "" || overall_failure=1
fi

python3 "$reporter" aggregate --output-dir "$output_dir" || overall_failure=1
exit "$overall_failure"
