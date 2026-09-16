#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd "${script_dir}/../.." && pwd)"
unity_cli="${UNITY_CLI:-/Users/jungle/.unity/bin/unity}"
platform="${FPS_SERVER_PLATFORM:-linux}"
case "${platform}" in
  linux)
    default_output="${project_dir}/Builds/DedicatedServer/FPSDedicatedServer.x86_64"
    ;;
  macos)
    default_output="${project_dir}/Builds/DedicatedServer/FPSDedicatedServer.app"
    ;;
  windows)
    default_output="${project_dir}/Builds/DedicatedServer/FPSDedicatedServer.exe"
    ;;
  *)
    echo "不支持的服务器平台：${platform}（可选 linux / macos / windows）" >&2
    exit 2
    ;;
esac
output="${1:-${default_output}}"
log_dir="${project_dir}/Logs/DedicatedServer"
mkdir -p "${log_dir}"

"${unity_cli}" run "${project_dir}" \
  --timeout 1800 \
  --non-interactive \
  -- \
  -nographics \
  -disable-audio \
  -executeMethod Issue85DedicatedServerBuild.Build \
  -buildOutput "${output}" \
  -serverBuildTarget "${platform}" \
  -logFile "${log_dir}/build.log"

echo "专用服务器构建完成：${output}"
