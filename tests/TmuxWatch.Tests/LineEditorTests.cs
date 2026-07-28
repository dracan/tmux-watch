using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The name prompt's editing is hand-rolled rather than delegated to Spectre's
/// TextPrompt (which cannot run inside a live display), so every key in its table is
/// pinned here. The editor is pure, so none of this needs a console.
/// </summary>
public class LineEditorTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, false, false, false);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: true);

    /// <summary>Type a string into a fresh editor, one keystroke at a time.</summary>
    private static LineEditor Typed(string text)
    {
        var editor = LineEditor.Empty;
        foreach (var c in text)
            editor = editor.Apply(Char(c)).Editor;
        return editor;
    }

    private static LineEditor Press(LineEditor editor, ConsoleKey key, int times = 1)
    {
        for (var i = 0; i < times; i++)
            editor = editor.Apply(Key(key)).Editor;
        return editor;
    }

    [Fact]
    public void Typing_appends_and_advances_the_cursor()
    {
        var editor = Typed("scratch");

        Assert.Equal("scratch", editor.Text);
        Assert.Equal(7, editor.Cursor);
    }

    [Fact]
    public void Enter_submits_and_escape_cancels()
    {
        var editor = Typed("notes");

        Assert.Equal(LineEditorOutcome.Submit, editor.Apply(Key(ConsoleKey.Enter)).Outcome);
        Assert.Equal(LineEditorOutcome.Cancel, editor.Apply(Key(ConsoleKey.Escape)).Outcome);

        // Neither disturbs the text - the caller decides what to do with it.
        Assert.Equal("notes", editor.Apply(Key(ConsoleKey.Enter)).Editor.Text);
        Assert.Equal("notes", editor.Apply(Key(ConsoleKey.Escape)).Editor.Text);
    }

    [Fact]
    public void Backspace_deletes_before_the_cursor()
    {
        var editor = Typed("scratchh").Apply(Key(ConsoleKey.Backspace)).Editor;

        Assert.Equal("scratch", editor.Text);
        Assert.Equal(7, editor.Cursor);
    }

    [Fact]
    public void Delete_removes_forward_and_leaves_the_cursor_put()
    {
        // "scr|aatch" -> delete -> "scr|atch"
        var editor = Press(Typed("scraatch"), ConsoleKey.LeftArrow, 5);
        Assert.Equal(3, editor.Cursor);

        editor = editor.Apply(Key(ConsoleKey.Delete)).Editor;

        Assert.Equal("scratch", editor.Text);
        Assert.Equal(3, editor.Cursor);
    }

    [Fact]
    public void Typo_is_fixed_mid_string_without_losing_the_tail()
    {
        // "my-proj|ct" -> insert the missing 'e' -> "my-project"
        var editor = Press(Typed("my-projct"), ConsoleKey.LeftArrow, 2);
        editor = editor.Apply(Char('e')).Editor;

        Assert.Equal("my-project", editor.Text);
        Assert.Equal(8, editor.Cursor);
    }

    [Fact]
    public void Home_and_end_jump_to_either_end()
    {
        var editor = Typed("scratch");

        editor = editor.Apply(Key(ConsoleKey.Home)).Editor;
        Assert.Equal(0, editor.Cursor);

        editor = editor.Apply(Key(ConsoleKey.End)).Editor;
        Assert.Equal(7, editor.Cursor);
    }

    [Fact]
    public void Ctrl_a_and_ctrl_e_match_home_and_end()
    {
        var editor = Typed("scratch");

        Assert.Equal(0, editor.Apply(Ctrl(ConsoleKey.A)).Editor.Cursor);
        Assert.Equal(7, editor.Apply(Ctrl(ConsoleKey.A)).Editor.Apply(Ctrl(ConsoleKey.E)).Editor.Cursor);
    }

    [Fact]
    public void Ctrl_w_deletes_the_word_before_the_cursor()
    {
        var editor = Typed("my project scratch").Apply(Ctrl(ConsoleKey.W)).Editor;

        Assert.Equal("my project ", editor.Text);
        Assert.Equal(11, editor.Cursor);
    }

    [Fact]
    public void Ctrl_w_after_a_trailing_space_still_removes_a_word()
    {
        var editor = Typed("my project ").Apply(Ctrl(ConsoleKey.W)).Editor;

        Assert.Equal("my ", editor.Text);
        Assert.Equal(3, editor.Cursor);
    }

    [Fact]
    public void Ctrl_w_mid_string_keeps_the_tail()
    {
        // "my project |scratch" -> ctrl+w -> "my |scratch"
        var editor = Press(Typed("my project scratch"), ConsoleKey.LeftArrow, 7);
        editor = editor.Apply(Ctrl(ConsoleKey.W)).Editor;

        Assert.Equal("my scratch", editor.Text);
        Assert.Equal(3, editor.Cursor);
    }

    [Fact]
    public void Ctrl_u_clears_the_line()
    {
        var editor = Typed("scratch").Apply(Ctrl(ConsoleKey.U)).Editor;

        Assert.Equal("", editor.Text);
        Assert.Equal(0, editor.Cursor);
    }

    [Fact]
    public void Cursor_clamps_at_the_start()
    {
        var editor = Press(Typed("ab"), ConsoleKey.LeftArrow, 5);
        Assert.Equal(0, editor.Cursor);

        // Backspace at the start is a no-op rather than a throw.
        var after = editor.Apply(Key(ConsoleKey.Backspace)).Editor;
        Assert.Equal("ab", after.Text);
        Assert.Equal(0, after.Cursor);
    }

    [Fact]
    public void Cursor_clamps_at_the_end()
    {
        var editor = Press(Typed("ab"), ConsoleKey.RightArrow, 5);
        Assert.Equal(2, editor.Cursor);

        var after = editor.Apply(Key(ConsoleKey.Delete)).Editor;
        Assert.Equal("ab", after.Text);
        Assert.Equal(2, after.Cursor);
    }

    [Fact]
    public void Ctrl_w_and_ctrl_u_on_an_empty_line_are_no_ops()
    {
        Assert.Equal("", LineEditor.Empty.Apply(Ctrl(ConsoleKey.W)).Editor.Text);
        Assert.Equal("", LineEditor.Empty.Apply(Ctrl(ConsoleKey.U)).Editor.Text);
    }

    [Fact]
    public void Unmapped_and_control_keys_never_corrupt_the_buffer()
    {
        var editor = Typed("scratch");

        foreach (var key in new[] { ConsoleKey.F5, ConsoleKey.UpArrow, ConsoleKey.PageDown, ConsoleKey.Tab })
        {
            var (after, outcome) = editor.Apply(Key(key));
            Assert.Equal("scratch", after.Text);
            Assert.Equal(7, after.Cursor);
            Assert.Equal(LineEditorOutcome.Editing, outcome);
        }
    }

    [Fact]
    public void Command_and_address_keys_are_just_text_here()
    {
        // The prompt is modal, so keys that are commands in the table are plain input.
        var editor = Typed("a1pow");

        Assert.Equal("a1pow", editor.Text);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("[red]not markup[/]")]
    [InlineData("brackets [ ] and more [[")]
    [InlineData("")]
    public void Prompt_renders_any_name_without_being_read_as_markup(string name)
    {
        // Spectre would throw on unescaped markup, so a name containing brackets must not
        // be able to crash the live view mid-typing.
        var editor = Typed(name);

        var rendered = WatcherApp.BuildPromptLine(editor, "wo[rk]");

        Assert.NotNull(rendered);
    }

    [Fact]
    public void A_surrogate_pair_is_typed_deleted_and_stepped_over_as_one_character()
    {
        // Console.ReadKey delivers a non-BMP character as its two UTF-16 halves, so the
        // editor must not let a single backspace or arrow split the pair.
        const string emoji = "\U0001F680"; // rocket, 2 chars
        var editor = Typed($"a{emoji}b");
        Assert.Equal(4, editor.Text.Length);

        // Left from the end steps over 'b', then over the whole pair.
        var moved = Press(editor, ConsoleKey.LeftArrow, 2);
        Assert.Equal(1, moved.Cursor);

        // Backspace from just after the pair removes both halves at once.
        var back = Press(editor, ConsoleKey.LeftArrow).Apply(Key(ConsoleKey.Backspace)).Editor;
        Assert.Equal("ab", back.Text);
        Assert.Equal(1, back.Cursor);

        // Delete from just before it does the same.
        var fwd = moved.Apply(Key(ConsoleKey.Delete)).Editor;
        Assert.Equal("ab", fwd.Text);
        Assert.Equal(1, fwd.Cursor);
    }

    [Fact]
    public void A_directly_constructed_editor_has_its_cursor_clamped()
    {
        Assert.Equal(3, new LineEditor("abc", 99).Cursor);
        Assert.Equal(0, new LineEditor("abc", -5).Cursor);
    }

    [Fact]
    public void Before_and_after_split_at_the_cursor_for_rendering()
    {
        var editor = Press(Typed("scratch"), ConsoleKey.LeftArrow, 3);

        Assert.Equal("scra", editor.Before);
        Assert.Equal("tch", editor.After);
    }
}
