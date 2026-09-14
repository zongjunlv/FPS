#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "${script_dir}/../.." && pwd)"
output_dir="${ISSUE64_OUTPUT_DIR:-${project_root}/artifacts/performance/issue64}"
rounds="${ISSUE64_ROUNDS:-3}"
warmup_frames="${ISSUE64_WARMUP_FRAMES:-300}"
sample_frames="${ISSUE64_SAMPLE_FRAMES:-3600}"
seed="${ISSUE64_SEED:-64064}"
quality="${ISSUE64_QUALITY:-PC}"
width="${ISSUE64_WIDTH:-1920}"
height="${ISSUE64_HEIGHT:-1080}"
fixed_delta="${ISSUE64_FIXED_DELTA:-0.016666667}"
content_version="${ISSUE64_CONTENT_VERSION:-workspace-current}"
unity_version="$(sed -n 's/^m_EditorVersion: //p' \
  "${project_root}/ProjectSettings/ProjectVersion.txt")"
unity="${UNITY_EXECUTABLE:-/Users/${USER}/UnityEditors/${unity_version}/Unity.app/Contents/MacOS/Unity}"

if [[ ! -x "${unity}" ]]; then
  printf 'Unity executable not found: %s\n' "${unity}" >&2
  exit 66
fi

if [[ -e "${project_root}/Temp/UnityLockfile" ]]; then
  printf '关闭当前 Unity Editor 后再运行独立 A/B 压测。\n' >&2
  exit 73
fi

mkdir -p "${output_dir}/raw" "${output_dir}/logs"
for ((round = 1; round <= rounds; round++)); do
  stem="round-$(printf '%02d' "${round}")"
  rm -f "${output_dir}/raw/${stem}.json" \
    "${output_dir}/raw/${stem}.md"
done

"${unity}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${project_root}" \
  -executeMethod Issue64PerformanceBenchmarkCli.RunFromCommandLine \
  -issue64-output "${output_dir}/raw" \
  -issue64-runs "${rounds}" \
  -issue64-warmup-frames "${warmup_frames}" \
  -issue64-sample-frames "${sample_frames}" \
  -issue64-seed "${seed}" \
  -issue64-quality "${quality}" \
  -issue64-width "${width}" \
  -issue64-height "${height}" \
  -issue64-fixed-delta "${fixed_delta}" \
  -issue64-content-version "${content_version}" \
  -logFile "${output_dir}/logs/benchmark.log"

reports=()
for ((round = 1; round <= rounds; round++)); do
  report="${output_dir}/raw/round-$(printf '%02d' "${round}").json"
  if [[ ! -s "${report}" ]]; then
    printf 'Issue 64 benchmark did not create: %s\n' "${report}" >&2
    exit 65
  fi
  reports+=("${report}")
done

python3 "${script_dir}/issue64_gate.py" \
  "${reports[@]}" \
  --output-json "${output_dir}/gate.json" \
  --output-markdown "${output_dir}/summary.md"

printf 'Issue 64 A/B report: %s\n' "${output_dir}/summary.md"
