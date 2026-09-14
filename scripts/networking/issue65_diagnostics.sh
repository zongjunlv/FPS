#!/usr/bin/env bash
set -euo pipefail

PROJECT_PATH="${ISSUE65_PROJECT_PATH:-$(cd "$(dirname "$0")/../.." && pwd)}"
UNITY_EDITOR_PATH="${UNITY_EDITOR_PATH:-/Users/jungle/UnityEditors/6000.5.3f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_PATH="${ISSUE65_OUTPUT_PATH:-$PROJECT_PATH/artifacts/networking/issue65}"
mkdir -p "$OUTPUT_PATH"

"$UNITY_EDITOR_PATH" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_PATH" \
  -executeMethod Issue65NetworkDiagnosticsCli.RunFixtureFromCommandLine \
  -issue65-output "$OUTPUT_PATH" \
  -logFile "$OUTPUT_PATH/fixture-unity.log"

GATE_ARGUMENTS=("$PROJECT_PATH/scripts/networking/issue65_report_gate.py" "$OUTPUT_PATH/fixture-report.json")
if [[ "${ISSUE65_REQUIRE_MULTIPROCESS:-0}" == "1" ]]; then
  GATE_ARGUMENTS+=(--require-multiprocess)
fi

python3 "${GATE_ARGUMENTS[@]}"
