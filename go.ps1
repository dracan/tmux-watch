# Run tmux-watch from a Windows/PowerShell host. Forwards any args (e.g. .\go.ps1 --once).
# --config precedes @args so an explicit --config in the args still wins (last-wins).
Clear-Host
dotnet run --project .\src\TmuxWatch\ -- --config (Join-Path $PSScriptRoot 'config.json') @args
