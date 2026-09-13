#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "$script_dir/../.." && pwd)"
unity_version="$(sed -n 's/^m_EditorVersion: //p' "$project_root/ProjectSettings/ProjectVersion.txt")"

output_dir="${ISSUE63_OUTPUT_DIR:-$project_root/artifacts/balance/issue63}"
seed_start="${ISSUE63_SEED_START:-0}"
seed_count="${ISSUE63_SEED_COUNT:-1000}"
parallelism="${ISSUE63_PARALLELISM:-4}"
thresholds="${ISSUE63_THRESHOLDS:-$script_dir/thresholds.json}"
baseline="${ISSUE63_BASELINE:-}"
skip_simulation=0

usage() {
  cat <<'EOF'
用法: run-issue63-balance-gate.sh [选项]

选项:
  --seed-start N       首个 Seed，默认 0
  --seed-count N       模拟 Seed 数，默认 1000
  --parallelism N      最大并行数，默认 4
  --output-dir PATH    报告目录
  --thresholds PATH    门禁阈值 JSON
  --baseline PATH      可选的已审核基线报告
  --skip-simulation    不启动 Unity，仅重新评估已有 report.json
  -h, --help           显示帮助

同名 ISSUE63_* 环境变量也可覆盖默认值。
EOF
}

while (($# > 0)); do
  case "$1" in
    --seed-start)
      seed_start="${2:?--seed-start 缺少参数}"
      shift 2
      ;;
    --seed-count)
      seed_count="${2:?--seed-count 缺少参数}"
      shift 2
      ;;
    --parallelism)
      parallelism="${2:?--parallelism 缺少参数}"
      shift 2
      ;;
    --output-dir)
      output_dir="${2:?--output-dir 缺少参数}"
      shift 2
      ;;
    --thresholds)
      thresholds="${2:?--thresholds 缺少参数}"
      shift 2
      ;;
    --baseline)
      baseline="${2:?--baseline 缺少参数}"
      shift 2
      ;;
    --skip-simulation)
      skip_simulation=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf '未知参数：%s\n' "$1" >&2
      usage >&2
      exit 64
      ;;
  esac
done

if ! [[ "$seed_start" =~ ^-?[0-9]+$ ]]; then
  printf 'seed-start 必须是整数。\n' >&2
  exit 64
fi
if ! [[ "$seed_count" =~ ^[1-9][0-9]*$ ]]; then
  printf 'seed-count 必须是正整数。\n' >&2
  exit 64
fi
if ! [[ "$parallelism" =~ ^[1-9][0-9]*$ ]]; then
  printf 'parallelism 必须是正整数。\n' >&2
  exit 64
fi
if [[ ! -s "$thresholds" ]]; then
  printf '找不到门禁阈值：%s\n' "$thresholds" >&2
  exit 66
fi
if [[ -n "$baseline" && ! -s "$baseline" ]]; then
  printf '找不到基线报告：%s\n' "$baseline" >&2
  exit 66
fi

mkdir -p "$output_dir/reproductions"
report="$output_dir/report.json"
gate_json="$output_dir/gate.json"
summary="$output_dir/summary.md"
unity_log="$output_dir/unity.log"

if ((skip_simulation == 0)); then
  unity="${UNITY_EXECUTABLE:-}"
  if [[ -z "$unity" ]]; then
    candidates=(
      "/Applications/Unity/Hub/Editor/$unity_version/Unity.app/Contents/MacOS/Unity"
      "/Users/${USER}/UnityEditors/$unity_version/Unity.app/Contents/MacOS/Unity"
    )
    for candidate in "${candidates[@]}"; do
      if [[ -x "$candidate" ]]; then
        unity="$candidate"
        break
      fi
    done
  fi
  if [[ -z "$unity" || ! -x "$unity" ]]; then
    printf 'Unity %s 不可用，请设置 UNITY_EXECUTABLE。\n' "$unity_version" >&2
    exit 66
  fi
  if [[ -e "$project_root/Temp/UnityLockfile" ]]; then
    printf '项目正被 Unity Editor 占用；请先关闭 Editor 再运行离线门禁。\n' >&2
    exit 73
  fi

  rm -f "$report" "$unity_log"
  "$unity" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "$project_root" \
    -executeMethod FPS.Editor.Balance.Issue63BalanceSimulationCli.Run \
    -issue63SeedStart "$seed_start" \
    -issue63SeedCount "$seed_count" \
    -issue63Parallelism "$parallelism" \
    -issue63Output "$report" \
    -issue63ReproductionDirectory "$output_dir/reproductions" \
    -logFile "$unity_log"
fi

if [[ ! -s "$report" ]]; then
  printf '离线模拟没有生成报告：%s\n' "$report" >&2
  exit 2
fi

gate_arguments=(
  "$report"
  --thresholds "$thresholds"
  --output-json "$gate_json"
  --output-markdown "$summary"
)
if [[ -n "$baseline" ]]; then
  gate_arguments+=(--baseline "$baseline")
fi

python3 "$script_dir/balance_gate.py" "${gate_arguments[@]}"
printf 'Issue 63 离线平衡门禁完成：%s\n' "$summary"
