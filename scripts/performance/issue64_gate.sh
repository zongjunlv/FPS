#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 1 ]]; then
  echo "用法：$0 <raw-report.json> [raw-report-2.json ...]" >&2
  exit 2
fi

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
output_dir="${ISSUE64_OUTPUT_DIR:-${repo_root}/artifacts/performance/issue64}"
mkdir -p "${output_dir}"

python3 "${repo_root}/scripts/performance/issue64_gate.py" \
  "$@" \
  --output-json "${output_dir}/gate.json" \
  --output-markdown "${output_dir}/summary.md"
