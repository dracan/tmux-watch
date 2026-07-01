using System.Diagnostics;
using System.Text;

namespace TmuxWatch.Pointer;

/// <summary>
/// WSL/Linux backend. tmux-watch is a Linux process here and cannot P/Invoke
/// <c>user32</c>, so it reaches the same interactive Windows session by invoking
/// <c>powershell.exe</c>, which performs the equivalent <c>SetSystemCursor</c> /
/// <c>SystemParametersInfo(SPI_SETCURSORS)</c> calls. The pointer change is global
/// Windows-session state that outlives the spawned shell, so set and restore are
/// separate invocations. Only fires on aggregate transitions, so spawn cost is rare.
/// </summary>
public sealed class WslPointerSignal : PointerSignalBase
{
    private readonly string _powershell;
    private readonly string _waitingWindowsPath;
    private readonly string _doneWindowsPath;
    private readonly uint[] _shapeIds;

    public WslPointerSignal(string waitingCursorFile, string doneCursorFile, IEnumerable<string> shapes, string powershell = "powershell.exe")
    {
        _powershell = powershell;
        _waitingWindowsPath = ToWindowsPath(waitingCursorFile);
        _doneWindowsPath = ToWindowsPath(doneCursorFile);
        _shapeIds = shapes.Select(MapShape).Where(id => id != 0).Distinct().ToArray();
    }

    protected override void ApplyWaiting() => ApplyCursor(_waitingWindowsPath);

    protected override void ApplyDone() => ApplyCursor(_doneWindowsPath);

    private void ApplyCursor(string windowsCursorPath)
    {
        if (_shapeIds.Length == 0 || string.IsNullOrEmpty(windowsCursorPath))
            return;

        var ids = string.Join(",", _shapeIds);
        var path = windowsCursorPath.Replace("'", "''");
        var script = $@"
$src = @'
using System;
using System.Runtime.InteropServices;
public static class TmuxWatchCursor {{
  [DllImport(""user32.dll"", CharSet = CharSet.Unicode)] public static extern IntPtr LoadCursorFromFile(string f);
  [DllImport(""user32.dll"")] public static extern bool SetSystemCursor(IntPtr h, uint id);
}}
'@
Add-Type $src
foreach ($id in @({ids})) {{
  $h = [TmuxWatchCursor]::LoadCursorFromFile('{path}')
  if ($h -ne [IntPtr]::Zero) {{ [void][TmuxWatchCursor]::SetSystemCursor($h, [uint32]$id) }}
}}";
        RunPowershell(script);
    }

    protected override void ApplyNormal()
    {
        const string script = @"
$src = @'
using System;
using System.Runtime.InteropServices;
public static class TmuxWatchCursorRestore {
  [DllImport(""user32.dll"")] public static extern bool SystemParametersInfo(uint a, uint b, IntPtr c, uint d);
}
'@
Add-Type $src
[void][TmuxWatchCursorRestore]::SystemParametersInfo(0x0057, 0, [IntPtr]::Zero, 0x02)";
        RunPowershell(script);
    }

    private void RunPowershell(string script)
    {
        try
        {
            // -EncodedCommand takes base64 UTF-16LE, sidestepping all shell quoting.
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = _powershell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch
        {
            // powershell.exe missing / execution policy / not in a Windows session:
            // degrade to a no-op rather than crash the watcher.
        }
    }

    /// <summary>Translate a Linux path to its Windows form via <c>wslpath -w</c>.</summary>
    private static string ToWindowsPath(string linuxPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wslpath",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add(linuxPath);

            using var proc = Process.Start(psi);
            if (proc is null)
                return linuxPath;
            var win = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return proc.ExitCode == 0 && win.Length > 0 ? win : linuxPath;
        }
        catch
        {
            return linuxPath;
        }
    }

    private static uint MapShape(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "arrow" or "normal" => 32512,
        "ibeam" or "text" => 32513,
        "wait" or "busy" => 32514,
        _ => 0,
    };
}
