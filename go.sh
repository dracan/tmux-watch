#!/usr/bin/env bash
# Run tmux-watch from a WSL2/Linux host. Forwards any args (e.g. ./go.sh --once).
set -euo pipefail
cd "$(dirname "$0")"
# --config precedes "$@" so an explicit --config in the args still wins (last-wins).
exec dotnet run --project ./src/TmuxWatch -- --config ./config.json "$@"
