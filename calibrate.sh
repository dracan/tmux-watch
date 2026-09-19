#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
dotnet build tools/TmuxWatch.Calibration --nologo --verbosity quiet
exec dotnet tools/TmuxWatch.Calibration/bin/Debug/net10.0/TmuxWatch.Calibration.dll "$@"
