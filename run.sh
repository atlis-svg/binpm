#!/usr/bin/env bash
# Runs the BinPm CLI from source: ./run.sh install owner/repo
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exec dotnet run --project "$ROOT/src/BinPm.Cli/BinPm.Cli.csproj" -- "$@"
