#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
player="${FPS_BENCHMARK_PLAYER:-$project_root/Builds/Issue1/FPS.app/Contents/MacOS/My project}"
output_dir="${ISSUE42_OUTPUT_DIR:-$project_root/artifacts/performance/issue42}"
rounds="${ISSUE42_ROUNDS:-3}"
warmup="${ISSUE42_WARMUP_SECONDS:-5}"
duration="${ISSUE42_SAMPLE_SECONDS:-20}"

if [[ ! -x "$player" ]]; then
  printf 'Benchmark Player not found: %s\n' "$player" >&2
  exit 66
fi

mkdir -p "$output_dir/raw" "$output_dir/logs"

disabled_round=0
enabled_round=0

for sequence in disabled enabled enabled disabled disabled enabled; do
  mode="$sequence"
  if [[ "$mode" == "disabled" ]]; then
    ((disabled_round += 1))
    round="$disabled_round"
  else
    ((enabled_round += 1))
    round="$enabled_round"
  fi

  if (( round <= rounds )); then
    name="${mode}-round-$(printf '%02d' "$round")"
    report="$output_dir/raw/$name.json"
    log="$output_dir/logs/$name.log"
    rm -f "$report" "$log"
    "$player" \
      -fps-benchmark \
      -benchmark-enemies 100 \
      -benchmark-warmup "$warmup" \
      -benchmark-duration "$duration" \
      -benchmark-seed 30030 \
      -benchmark-width 1920 \
      -benchmark-height 1080 \
      -benchmark-quality PC \
      -benchmark-perception-budget 4 \
      -benchmark-ai-lod "$mode" \
      -benchmark-variant "issue42-$name" \
      -benchmark-output "$report" \
      -logFile "$log"
    test -s "$report"
  fi
done

python3 "$script_dir/aggregate_issue42.py" \
  "$output_dir/raw"/*.json \
  --output-json "$output_dir/summary.json" \
  --output-markdown "$output_dir/summary.md"

printf 'Issue 42 comparison completed: %s\n' "$output_dir/summary.md"
