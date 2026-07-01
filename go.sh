#!/usr/bin/env bash
# Run tmux-watch from a WSL2/Linux host. Forwards any args (e.g. ./go.sh --once).
set -euo pipefail
cd "$(dirname "$0")"
exec dotnet run --project ./src/TmuxWatch -- "$@"
