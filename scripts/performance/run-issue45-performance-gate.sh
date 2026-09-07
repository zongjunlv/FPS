#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
output_dir="${ISSUE45_OUTPUT_DIR:-$project_root/artifacts/performance/issue45}"
rounds="${ISSUE45_ROUNDS:-3}"
warmup="${ISSUE45_WARMUP_SECONDS:-5}"
duration="${ISSUE45_SAMPLE_SECONDS:-20}"
cooldown="${ISSUE45_COOLDOWN_SECONDS:-10}"
unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_root/ProjectSettings/ProjectVersion.txt")"
unity="${UNITY_EXECUTABLE:-/Users/${USER}/UnityEditors/$unity_version/Unity.app/Contents/MacOS/Unity}"
player="${FPS_BENCHMARK_PLAYER:-$project_root/Builds/Issue1/FPS.app/Contents/MacOS/My project}"

mkdir -p "$output_dir/raw" "$output_dir/logs"

if [[ -z "${FPS_BENCHMARK_PLAYER:-}" ]]; then
  "$unity" -batchmode -nographics -quit \
    -projectPath "$project_root" \
    -executeMethod Issue1Build.BuildMacDevelopment \
    -logFile "$output_dir/logs/build.log"
fi

if [[ ! -x "$player" ]]; then
  printf 'Benchmark Player not found: %s\n' "$player" >&2
  exit 66
fi

reports=()
for ((round = 1; round <= rounds; round++)); do
  name="round-$(printf '%02d' "$round")"
  report="$output_dir/raw/$name.json"
  log="$output_dir/logs/$name.log"
  "$player" \
    -fps-benchmark \
    -benchmark-enemies 100 \
    -benchmark-warmup "$warmup" \
    -benchmark-duration "$duration" \
    -benchmark-seed 30030 \
    -benchmark-width 1920 \
    -benchmark-height 1080 \
    -benchmark-quality PC \
    -benchmark-perception-budget 32 \
    -benchmark-ai-lod enabled \
    -benchmark-spatial-index enabled \
    -benchmark-job-sight enabled \
    -benchmark-variant "issue45-$name" \
    -benchmark-output "$report" \
    -logFile "$log"
  test -s "$report"
  reports+=("$report")

  if (( round < rounds )); then
    sleep "$cooldown"
  fi
done

python3 "$script_dir/performance_gate.py" \
  "${reports[@]}" \
  --output-json "$output_dir/summary.json" \
  --output-markdown "$output_dir/summary.md"
