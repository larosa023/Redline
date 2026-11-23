#!/usr/bin/env bash
# Offline test run (workspace without NuGet). Usage: ./run-tests-offline.sh <TestProjectName> [filter]
set -euo pipefail
cd "$(dirname "$0")"
proj="$1"; filter="${2:-}"
export REDLINE_OFFLINE=1
dotnet build -v q -nologo tests/Offline/Runner/Runner.csproj -o .offline/runner >/dev/null
rm -f ".offline/$proj/$proj.dll"; dotnet build -v q -nologo "tests/$proj/$proj.csproj" -o ".offline/$proj" 2>&1 | grep -E "error|Build succeeded" | sort -u | head -40 || true
[ -f ".offline/$proj/$proj.dll" ] || { echo "BUILD FAILED"; exit 2; }
timeout ${RL_TIMEOUT:-240} dotnet .offline/runner/offline-runner.dll ".offline/$proj/$proj.dll" $filter
