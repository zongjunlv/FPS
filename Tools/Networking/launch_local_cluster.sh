#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_dir="$(cd "${script_dir}/../.." && pwd)"
server_artifact="${FPS_SERVER_ARTIFACT:-${project_dir}/Builds/DedicatedServer/FPSDedicatedServer.x86_64}"
client_artifact="${FPS_CLIENT_ARTIFACT:-${project_dir}/Builds/Issue1/FPS.x86_64}"
port="${FPS_SERVER_PORT:-17777}"
match_id="${FPS_MATCH_ID:-local-two-client}"
seed="${FPS_SERVER_SEED:-18018}"
version="${FPS_SERVER_VERSION:-local-dev}"
log_dir="${project_dir}/Logs/LocalCluster"
mkdir -p "${log_dir}"

server_bin="${FPS_SERVER_BIN:-}"
if [[ -z "${server_bin}" && -x "${server_artifact}" ]]; then
  server_bin="${server_artifact}"
elif [[ -z "${server_bin}" && -d "${server_artifact}/Contents/MacOS" ]]; then
  while IFS= read -r candidate; do
    if [[ -x "${candidate}" ]]; then
      server_bin="${candidate}"
      break
    fi
  done < <(find "${server_artifact}/Contents/MacOS" -maxdepth 1 -type f -print)
fi
client_bin="${FPS_CLIENT_BIN:-}"
if [[ -z "${client_bin}" && -x "${client_artifact}" ]]; then
  client_bin="${client_artifact}"
elif [[ -z "${client_bin}" && -x "${client_artifact}/Contents/MacOS/FPS" ]]; then
  client_bin="${client_artifact}/Contents/MacOS/FPS"
fi
if [[ -z "${server_bin}" ]]; then
  echo "找不到专用服务器可执行文件：${server_artifact}" >&2
  echo "请先运行 Tools/Networking/build_dedicated_server.sh" >&2
  exit 2
fi
if [[ -z "${client_bin}" || ! -x "${client_bin}" ]]; then
  echo "找不到客户端：${client_artifact}" >&2
  exit 3
fi

pids=()
cleanup() {
  for pid in "${pids[@]:-}"; do
    kill -TERM "${pid}" 2>/dev/null || true
  done
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

"${server_bin}" \
  -batchmode -nographics -disable-audio \
  -fps-server \
  -server-map CityNew \
  -server-port "${port}" \
  -server-match "${match_id}" \
  -server-max-players 2 \
  -server-seed "${seed}" \
  -server-version "${version}" \
  -server-tick-rate 60 \
  -server-diagnostics "${log_dir}/server-diagnostics.json" \
  -logFile "${log_dir}/server.log" &
pids+=("$!")

server_ready=0
for attempt in {1..300}; do
  if grep -q '\[DEDICATED_SERVER\]\[READY\]' "${log_dir}/server.log" 2>/dev/null; then
    server_ready=1
    break
  fi
  if ! kill -0 "${pids[0]}" 2>/dev/null; then
    echo "服务器启动失败，请查看 ${log_dir}/server.log" >&2
    exit 4
  fi
  sleep 0.1
done
if [[ "${server_ready}" -ne 1 ]]; then
  echo "服务器在 30 秒内未进入 READY，请查看 ${log_dir}/server.log" >&2
  exit 5
fi

for client_index in 1 2; do
  "${client_bin}" \
    -screen-fullscreen 0 -disable-audio \
    -issue65-role client -issue65-port "${port}" \
    -logFile "${log_dir}/client-${client_index}.log" &
  pids+=("$!")
done

echo "本地集群已启动：服务器 PID ${pids[0]}，客户端 PID ${pids[1]} / ${pids[2]}"
echo "日志目录：${log_dir}"
wait
