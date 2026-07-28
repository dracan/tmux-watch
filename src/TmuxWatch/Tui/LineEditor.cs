namespace TmuxWatch.Tui;

/// <summary>What a keystroke did to the editor: kept editing, finished, or backed out.</summary>
internal enum LineEditorOutcome
{
    Editing,
    Submit,
    Cancel,
}

/// <summary>
/// A single-line text field: the entered text plus a cursor position within it. Editing
/// is a pure transformation - <see cref="Apply"/> maps a keystroke to a new
/// <see cref="LineEditor"/> and an outcome, touching no console - so the whole key table
/// is unit-testable. The TUI owns the rendering and the reading of keys.
/// <para>
/// Out-of-range movement clamps rather than throwing, so no keystroke can produce an
/// invalid state.
/// </para>
/// </summary>
internal readonly record struct LineEditor(string Text, int Cursor)
{
    public static readonly LineEditor Empty = new("", 0);

    /// <summary>The text before the cursor, used to render the caret in place.</summary>
    public string Before => Text[..Cursor];

    /// <summary>The text from the cursor onward.</summary>
    public string After => Text[Cursor..];

    /// <summary>
    /// The editor state after <paramref name="key"/>, plus what the keystroke means.
    /// Keys with no binding leave the state untouched and keep editing.
    /// </summary>
    public (LineEditor Editor, LineEditorOutcome Outcome) Apply(ConsoleKeyInfo key)
    {
        var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return (this, LineEditorOutcome.Submit);
            case ConsoleKey.Escape:
                return (this, LineEditorOutcome.Cancel);
            case ConsoleKey.LeftArrow:
                return (Move(-1), LineEditorOutcome.Editing);
            case ConsoleKey.RightArrow:
                return (Move(1), LineEditorOutcome.Editing);
            case ConsoleKey.Home:
                return (this with { Cursor = 0 }, LineEditorOutcome.Editing);
            case ConsoleKey.End:
                return (this with { Cursor = Text.Length }, LineEditorOutcome.Editing);
            case ConsoleKey.Backspace:
                return (DeleteBefore(), LineEditorOutcome.Editing);
            case ConsoleKey.Delete:
                return (DeleteAfter(), LineEditorOutcome.Editing);
        }

        if (ctrl)
        {
            // Readline conventions. Matched on Key rather than KeyChar, because a control
            // chord delivers a control character in KeyChar rather than the letter itself.
            return key.Key switch
            {
                ConsoleKey.W => (DeleteWordBefore(), LineEditorOutcome.Editing),
                ConsoleKey.U => (Empty, LineEditorOutcome.Editing),
                ConsoleKey.A => (this with { Cursor = 0 }, LineEditorOutcome.Editing),
                ConsoleKey.E => (this with { Cursor = Text.Length }, LineEditorOutcome.Editing),
                _ => (this, LineEditorOutcome.Editing),
            };
        }

        // Everything else is text if it is printable, and ignored otherwise - so an
        // unmapped function or navigation key can never corrupt the buffer.
        return char.IsControl(key.KeyChar)
            ? (this, LineEditorOutcome.Editing)
            : (Insert(key.KeyChar), LineEditorOutcome.Editing);
    }

    private LineEditor Move(int delta) =>
        this with { Cursor = Math.Clamp(Cursor + delta, 0, Text.Length) };

    private LineEditor Insert(char c) =>
        new(Text.Insert(Cursor, c.ToString()), Cursor + 1);

    private LineEditor DeleteBefore() =>
        Cursor == 0 ? this : new(Text.Remove(Cursor - 1, 1), Cursor - 1);

    private LineEditor DeleteAfter() =>
        Cursor >= Text.Length ? this : this with { Text = Text.Remove(Cursor, 1) };

    /// <summary>
    /// ctrl+w: drop the word before the cursor. Any run of spaces immediately before the
    /// cursor goes first, then the non-space run behind it, so pressing it after a
    /// trailing space still removes a word rather than only the space.
    /// </summary>
    private LineEditor DeleteWordBefore()
    {
        if (Cursor == 0)
            return this;

        var start = Cursor;
        while (start > 0 && Text[start - 1] == ' ')
            start--;
        while (start > 0 && Text[start - 1] != ' ')
            start--;

        return new(Text.Remove(start, Cursor - start), start);
    }
}
