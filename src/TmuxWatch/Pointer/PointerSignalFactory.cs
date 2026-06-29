using System.Runtime.InteropServices;
using TmuxWatch.Config;

namespace TmuxWatch.Pointer;

/// <summary>
/// Selects a pointer backend from config and host, mirroring
/// <see cref="Notifications.NotifierFactory"/>. Disabled config or an unsupported host
/// yields a <see cref="NullPointerSignal"/> so callers never branch on platform.
/// </summary>
public static class PointerSignalFactory
{
    public static IPointerSignal Create(WatchConfig cfg) =>
        Create(cfg.PointerSignal, AppContext.BaseDirectory);

    internal static IPointerSignal Create(PointerSignalConfig cfg, string baseDir)
    {
        if (!cfg.Enabled)
            return new NullPointerSignal();

        var cursorFile = Path.IsPathRooted(cfg.WaitingCursorFile)
            ? cfg.WaitingCursorFile
            : Path.Combine(baseDir, cfg.WaitingCursorFile);

        if (OperatingSystem.IsWindows())
            return new WindowsPointerSignal(cursorFile, cfg.Shapes);

        if (IsWsl())
            return new WslPointerSignal(cursorFile, cfg.Shapes);

        // Linux desktop, macOS, etc.: no path to a Windows pointer - degrade quietly.
        return new NullPointerSignal();
    }

    /// <summary>True when running under WSL, where powershell.exe can reach the
    /// Windows session.</summary>
    private static bool IsWsl()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
            return true;
        try
        {
            return File.Exists("/proc/version") &&
                   File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
