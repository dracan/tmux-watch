using System.Globalization;

namespace TmuxWatch.Workspace;

/// <summary>A validated tmux layout tree; braces split horizontally, brackets vertically.</summary>
internal sealed record PaneLayout(int Width, int Height, int X, int Y,
    int? PaneId, char Split, IReadOnlyList<PaneLayout> Children)
{
    public IEnumerable<PaneLayout> Leaves => PaneId.HasValue
        ? new[] { this } : Children.SelectMany(c => c.Leaves);

    public string Shape => PaneId.HasValue ? "p" : Split + string.Join(",", Children.Select(c => c.Shape)) + Close;
    private char Close => Split == '{' ? '}' : ']';

    public string Encode(IReadOnlyDictionary<int, int>? ids = null)
    {
        string Body(PaneLayout node) => $"{node.Width}x{node.Height},{node.X},{node.Y}" +
            (node.PaneId is { } id ? $",{(ids is null ? id : ids[id])}"
                : node.Split + string.Join(",", node.Children.Select(Body)) + node.Close);
        var body = Body(this);
        return $"{Checksum(body):x4},{body}";
    }

    internal static ushort Checksum(string body)
    {
        ushort sum = 0;
        foreach (var c in body)
            sum = (ushort)(((sum >> 1) | ((sum & 1) << 15)) + c);
        return sum;
    }

    public static PaneLayout Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 100_000 || text.Length < 6 || text[4] != ',' ||
            !ushort.TryParse(text.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var sum) ||
            Checksum(text[5..]) != sum)
            throw new InvalidDataException("Invalid layout checksum or unsupported layout.");
        var offset = 5;
        int Number()
        {
            var start = offset;
            while (offset < text.Length && char.IsAsciiDigit(text[offset])) offset++;
            if (start == offset || !int.TryParse(text.AsSpan(start, offset - start), out var n) || n > 1_000_000)
                throw new InvalidDataException("Invalid layout number.");
            return n;
        }
        void Expect(char c)
        {
            if (offset >= text.Length || text[offset++] != c)
                throw new InvalidDataException("Malformed layout.");
        }
        PaneLayout Node(int depth)
        {
            if (depth > 32) throw new InvalidDataException("Layout is too deeply nested.");
            var w = Number(); Expect('x'); var h = Number(); Expect(',');
            var x = Number(); Expect(','); var y = Number();
            if (w < 1 || h < 1) throw new InvalidDataException("Empty layout cell.");
            if (offset < text.Length && text[offset] == ',')
            {
                offset++;
                return new(w, h, x, y, Number(), '\0', []);
            }
            if (offset >= text.Length || text[offset] is not ('{' or '['))
                throw new InvalidDataException("Missing layout children.");
            var split = text[offset++];
            var children = new List<PaneLayout> { Node(depth + 1) };
            while (offset < text.Length && text[offset] == ',')
            {
                offset++; children.Add(Node(depth + 1));
            }
            Expect(split == '{' ? '}' : ']');
            if (children.Count < 2) throw new InvalidDataException("A split requires two children.");
            var cursor = split == '{' ? x : y;
            foreach (var child in children)
            {
                if (split == '{' ? child.X != cursor || child.Y != y || child.Height != h
                    : child.Y != cursor || child.X != x || child.Width != w)
                    throw new InvalidDataException("Layout children do not tile their parent.");
                cursor += (split == '{' ? child.Width : child.Height) + 1;
            }
            if (cursor - 1 != (split == '{' ? x + w : y + h))
                throw new InvalidDataException("Layout dimensions are inconsistent.");
            return new(w, h, x, y, null, split, children);
        }
        var result = Node(0);
        if (offset != text.Length || result.Leaves.Select(l => l.PaneId).Distinct().Count() != result.Leaves.Count())
            throw new InvalidDataException("Layout contains trailing data or duplicate panes.");
        return result;
    }
}
