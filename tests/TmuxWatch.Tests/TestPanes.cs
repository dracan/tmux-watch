namespace TmuxWatch.Tests;

/// <summary>
/// Writes an enumeration line in the order a human reads it - id, session, window, pane,
/// command, dead, name, path, window_active, pane_active, pid, activity - and emits it in
/// the order <see cref="TmuxWatch.Discovery.PaneDiscovery.Format"/> actually uses, which
/// is sorted by whether a field can contain the delimiter rather than by meaning (see the
/// doc comment on Format). Keeping the readable order here is what lets a test line say
/// what it means; tests that care about the wire order build their line directly.
/// </summary>
internal static class TestPanes
{
    // Position of each readable field within the real format. Index i of this array is
    // the readable field that lands at wire position i.
    private static readonly int[] WireOrder = { 0, 2, 3, 5, 8, 9, 10, 11, 4, 1, 7, 6 };

    public static string Line(string readable)
    {
        var lines = readable.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = OneLine(lines[i]);
        return string.Join('\n', lines);
    }

    private static string OneLine(string readable)
    {
        if (readable.Trim().Length == 0)
            return readable;

        var read = readable.Split('|');
        var wire = new string[WireOrder.Length];
        for (var i = 0; i < wire.Length; i++)
        {
            var source = WireOrder[i];
            wire[i] = source < read.Length ? read[source] : "";
        }
        return string.Join('|', wire);
    }
}
