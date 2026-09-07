#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
player="${FPS_BENCHMARK_PLAYER:-$project_root/Builds/Issue1/FPS.app/Contents/MacOS/My project}"
output_dir="${ISSUE44_OUTPUT_DIR:-$project_root/artifacts/performance/issue44}"
rounds="${ISSUE44_ROUNDS:-4}"
warmup="${ISSUE44_WARMUP_SECONDS:-5}"
duration="${ISSUE44_SAMPLE_SECONDS:-20}"
cooldown="${ISSUE44_COOLDOWN_SECONDS:-20}"

if [[ ! -x "$player" ]]; then
  printf 'Benchmark Player not found: %s\n' "$player" >&2
  exit 66
fi

mkdir -p "$output_dir/raw" "$output_dir/logs"
sync_round=0
job_round=0
reports=()
sequence=(disabled enabled enabled disabled disabled enabled enabled disabled)

for sequence_index in "${!sequence[@]}"; do
  mode="${sequence[$sequence_index]}"
  if [[ "$mode" == "disabled" ]]; then
    ((sync_round += 1))
    round="$sync_round"
    label="sync"
  else
    ((job_round += 1))
    round="$job_round"
    label="job"
  fi

  if (( round <= rounds )); then
    name="$label-round-$(printf '%02d' "$round")"
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
      -benchmark-job-sight "$mode" \
      -benchmark-variant "issue44-$name" \
      -benchmark-output "$report" \
      -logFile "$log"
    test -s "$report"
    reports+=("$report")

    if (( sequence_index + 1 < ${#sequence[@]} )); then
      sleep "$cooldown"
    fi
  fi
done

python3 "$script_dir/aggregate_issue44.py" \
  "${reports[@]}" \
  --expected-rounds "$rounds" \
  --output-json "$output_dir/summary.json" \
  --output-markdown "$output_dir/summary.md"

printf 'Issue 44 comparison completed: %s\n' "$output_dir/summary.md"
