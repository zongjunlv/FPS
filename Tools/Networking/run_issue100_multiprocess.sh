#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd "${script_dir}/../.." && pwd)"
if [[ "${FPS_ISSUE100_SKIP_VIDEO:-0}" != "1" ]]; then
  set -- --record-video "$@"
fi

python3 "${project_dir}/scripts/networking/run_issue100_multiprocess.py" \
  --project "${project_dir}" \
  "$@"
