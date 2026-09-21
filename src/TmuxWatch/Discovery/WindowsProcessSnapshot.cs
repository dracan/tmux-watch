using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TmuxWatch.Discovery;

/// <summary>
/// Read-only Toolhelp snapshot plus process identity/lifetime checks. No shell, WMI,
/// command lines, environment or process memory reads. Non-Windows hosts do no work.
/// </summary>
internal static class WindowsProcessSnapshot
{
    internal static ProcessSnapshot? Capture(IReadOnlySet<int> paneRoots)
    {
        if (!OperatingSystem.IsWindows() || paneRoots.Count == 0)
            return null;

        var capturedAt = DateTime.UtcNow.Ticks;
        using var handle = CreateToolhelp32Snapshot(0x00000002, 0); // TH32CS_SNAPPROCESS
        if (handle.IsInvalid)
            return null;

        var entry = new NativeProcessEntry { Size = (uint)Marshal.SizeOf<NativeProcessEntry>() };
        if (!Process32FirstW(handle, ref entry))
            return null;

        var entries = new List<ProcessEntry>();
        do
        {
            if (entry.ProcessId <= int.MaxValue && entry.ParentProcessId <= int.MaxValue)
                entries.Add(new ProcessEntry((int)entry.ProcessId, (int)entry.ParentProcessId,
                    entry.ExeFile, 0));
        } while (Process32NextW(handle, ref entry));
        if (Marshal.GetLastWin32Error() != 18) // ERROR_NO_MORE_FILES; never use a partial snapshot
            return null;

        // Query lifetimes only within the listed pane trees, not every desktop process.
        var byId = entries.ToDictionary(p => p.Pid);
        var children = entries.ToLookup(p => p.ParentPid);
        var pending = new Stack<int>(paneRoots);
        var visited = new HashSet<int>();
        var verified = new List<ProcessEntry>();
        while (pending.TryPop(out var pid))
        {
            if (!visited.Add(pid) || !byId.TryGetValue(pid, out var candidate))
                continue;
            try
            {
                using var process = Process.GetProcessById(pid);
                var startedAt = process.StartTime.ToUniversalTime().Ticks;
                // Reject replacement processes created after the snapshot began and
                // identities that changed between Toolhelp and the live process query.
                if (startedAt > capturedAt || process.HasExited ||
                    !string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(candidate.Command),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                verified.Add(candidate with { StartedAt = startedAt });
                foreach (var child in children[pid])
                    pending.Push(child.Pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Exited, inaccessible or otherwise unverifiable: no ownership evidence.
            }
        }
        return new ProcessSnapshot(verified);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref NativeProcessEntry entry);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref NativeProcessEntry entry);
}
