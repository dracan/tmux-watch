using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TmuxWatch.Pointer;

/// <summary>
/// Native Windows backend. Replaces the system cursor for the configured shapes with a
/// coloured cursor loaded from a file (<c>SetSystemCursor</c>) - red while WAITING,
/// green while DONE - and restores the user's normal cursors by reloading them from the
/// registry (<c>SystemParametersInfo(SPI_SETCURSORS)</c>). The change is global to the
/// desktop and persists in the Windows session independent of this process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPointerSignal : PointerSignalBase
{
    private const uint OCR_NORMAL = 32512;
    private const uint OCR_IBEAM = 32513;
    private const uint OCR_WAIT = 32514;
    private const uint SPI_SETCURSORS = 0x0057;
    private const uint SPIF_SENDCHANGE = 0x02;

    private readonly string _waitingCursorFile;
    private readonly string _doneCursorFile;
    private readonly uint[] _shapeIds;

    public WindowsPointerSignal(string waitingCursorFile, string doneCursorFile, IEnumerable<string> shapes)
    {
        _waitingCursorFile = waitingCursorFile;
        _doneCursorFile = doneCursorFile;
        _shapeIds = shapes.Select(MapShape).Where(id => id != 0).Distinct().ToArray();
    }

    protected override void ApplyWaiting() => ApplyCursor(_waitingCursorFile);

    protected override void ApplyDone() => ApplyCursor(_doneCursorFile);

    private void ApplyCursor(string cursorFile)
    {
        try
        {
            if (!File.Exists(cursorFile) || _shapeIds.Length == 0)
                return;

            // SetSystemCursor takes ownership of (destroys) the handle it is given, so
            // load a fresh copy per shape.
            foreach (var id in _shapeIds)
            {
                var hcur = LoadCursorFromFile(cursorFile);
                if (hcur != IntPtr.Zero)
                    SetSystemCursor(hcur, id);
            }
        }
        catch
        {
            // Degrade to a no-op: a pointer failure must never crash the watcher.
        }
    }

    protected override void ApplyNormal()
    {
        try
        {
            // Reload every system cursor from the registry - scheme-agnostic restore.
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
        }
        catch
        {
        }
    }

    private static uint MapShape(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "arrow" or "normal" => OCR_NORMAL,
        "ibeam" or "text" => OCR_IBEAM,
        "wait" or "busy" => OCR_WAIT,
        _ => 0,
    };

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursorFromFile(string lpFileName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
}
