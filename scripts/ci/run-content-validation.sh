#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_root/ProjectSettings/ProjectVersion.txt")"
unity="${UNITY_EXECUTABLE:-/Users/${USER}/UnityEditors/$unity_version/Unity.app/Contents/MacOS/Unity}"
if [[ ! -x "$unity" ]]; then
  printf 'Unity 不可用，请设置 UNITY_EXECUTABLE。\n' >&2
  exit 2
fi
output="${CONTENT_VALIDATION_OUTPUT_DIR:-$project_root/artifacts/content-validation}"
mkdir -p "$output"
run_output="$(mktemp -d "$output/run.XXXXXX")"
printf '内容校验报告：%s\n' "$run_output"
"$unity" -batchmode -nographics -disable-audio -quit -projectPath "$project_root" \
  -executeMethod ContentValidationCli.Run \
  -contentValidationReport "$run_output/report.json" \
  -logFile "$run_output/unity.log"
if [[ ! -s "$run_output/report.json" ]]; then
  printf 'Unity 未生成内容报告，校验不能视为通过。\n' >&2
  exit 2
fi
