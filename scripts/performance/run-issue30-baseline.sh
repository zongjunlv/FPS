#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
player="${FPS_BENCHMARK_PLAYER:-$project_root/Builds/Issue1/FPS.app/Contents/MacOS/My project}"
output_dir="${ISSUE30_OUTPUT_DIR:-$project_root/artifacts/performance/issue30}"
rounds="${ISSUE30_ROUNDS:-3}"
warmup="${ISSUE30_WARMUP_SECONDS:-5}"
duration="${ISSUE30_SAMPLE_SECONDS:-20}"
seed="${ISSUE30_SEED:-30030}"
width="${ISSUE30_WIDTH:-1920}"
height="${ISSUE30_HEIGHT:-1080}"
quality="${ISSUE30_QUALITY:-PC}"
perception_budget="${ISSUE30_PERCEPTION_BUDGET:-4}"

if [[ ! -x "$player" ]]; then
  printf 'Benchmark Player not found or not executable: %s\n' "$player" >&2
  exit 66
fi

if ! [[ "$rounds" =~ ^[1-9][0-9]*$ ]]; then
  printf 'ISSUE30_ROUNDS must be a positive integer.\n' >&2
  exit 64
fi

raw_dir="$output_dir/raw"
log_dir="$output_dir/logs"
mkdir -p "$raw_dir" "$log_dir"
reports=()

for ((round = 1; round <= rounds; round++)); do
  round_name="$(printf 'round-%02d' "$round")"
  report="$raw_dir/$round_name.json"
  log="$log_dir/$round_name.log"
  rm -f "$report" "$log"

  "$player" \
    -fps-benchmark \
    -benchmark-enemies 100 \
    -benchmark-warmup "$warmup" \
    -benchmark-duration "$duration" \
    -benchmark-seed "$seed" \
    -benchmark-width "$width" \
    -benchmark-height "$height" \
    -benchmark-quality "$quality" \
    -benchmark-perception-budget "$perception_budget" \
    -benchmark-variant "issue30-baseline-$round_name" \
    -benchmark-output "$report" \
    -logFile "$log"

  if [[ ! -s "$report" ]]; then
    printf 'Round %s did not produce a report. See %s\n' "$round" "$log" >&2
    exit 1
  fi

  reports+=("$report")
done

python3 "$script_dir/aggregate_issue30.py" \
  "${reports[@]}" \
  --expected-enemies 100 \
  --output-json "$output_dir/summary.json" \
  --output-markdown "$output_dir/summary.md"

printf 'Issue 30 baseline completed: %s\n' "$output_dir/summary.md"
