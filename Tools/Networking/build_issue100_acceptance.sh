#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd "${script_dir}/../.." && pwd)"
unity_cli="${UNITY_CLI:-/Users/jungle/.unity/bin/unity}"
platform="${FPS_ACCEPTANCE_PLATFORM:-macos}"
build_root="${FPS_ACCEPTANCE_BUILD_ROOT:-${project_dir}/Builds/Issue100}"
log_dir="${project_dir}/Logs/Issue100"
mkdir -p "${log_dir}"

"${unity_cli}" run "${project_dir}" \
  --timeout 3600 \
  --non-interactive \
  -- \
  -nographics \
  -disable-audio \
  -executeMethod Issue100AcceptanceBuild.Build \
  -issue100BuildTarget "${platform}" \
  -issue100BuildRoot "${build_root}" \
  -logFile "${log_dir}/build.log"

echo "Issue100 服务器与客户端构建完成：${build_root}"
