#!/usr/bin/env bash
# Builds BinPm and runs the test suite.
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

dotnet build "$ROOT/BinPm.sln" -c Release "$@"
dotnet test "$ROOT/tests/BinPm.Core.Tests/BinPm.Core.Tests.csproj" -c Release --no-build "$@"
