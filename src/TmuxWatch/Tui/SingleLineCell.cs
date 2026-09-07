using Spectre.Console.Rendering;

namespace TmuxWatch.Tui;

/// <summary>
/// Keeps normal table measurement and styling but displays only the first rendered
/// line. Column NoWrap changes width allocation and can crowd out adjacent columns.
/// </summary>
internal sealed class SingleLineCell(IRenderable content) : IRenderable
{
    public Measurement Measure(RenderOptions options, int maxWidth) => content.Measure(options, maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) =>
        content.Render(options, maxWidth).TakeWhile(segment => !segment.IsLineBreak);
}
