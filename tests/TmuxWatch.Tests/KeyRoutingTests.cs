using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The modal rule: while the name prompt is open every keystroke belongs to it, so no
/// command runs and no row is addressed mid-name. Classification is pure, so the rule is
/// pinned here rather than inferred from the key loop.
/// </summary>
public class KeyRoutingTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, ConsoleKey.None, false, false, false);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    /// <summary>
    /// A letter as the real console reports it: KeyChar *and* the physical
    /// <see cref="ConsoleKey"/>, with shift where the character is uppercase.
    /// <see cref="Char"/> passes ConsoleKey.None, which Console.ReadKey never produces
    /// for a letter - and that gap is what hid shift+Q quitting instead of addressing.
    /// </summary>
    private static ConsoleKeyInfo Letter(char c) =>
        new(c, (ConsoleKey)char.ToUpperInvariant(c), char.IsUpper(c), false, false);

    private static WatcherApp.KeyAction Closed(ConsoleKeyInfo key) =>
        WatcherApp.ClassifyKey(key, promptOpen: false);

    private static WatcherApp.KeyAction Open(ConsoleKeyInfo key) =>
        WatcherApp.ClassifyKey(key, promptOpen: true);

    [Fact]
    public void Commands_route_normally_while_the_prompt_is_closed()
    {
        Assert.Equal(WatcherApp.KeyAction.TogglePauseRow, Closed(Char('p')));
        Assert.Equal(WatcherApp.KeyAction.ToggleWide, Closed(Char('w')));
        Assert.Equal(WatcherApp.KeyAction.ToggleOthers, Closed(Char('o')));
        Assert.Equal(WatcherApp.KeyAction.ToggleCompanions, Closed(Char('c')));
        Assert.Equal(WatcherApp.KeyAction.AcknowledgeRow, Closed(Char('a')));
        Assert.Equal(WatcherApp.KeyAction.OpenNewWindowPrompt, Closed(Char('n')));
        Assert.Equal(WatcherApp.KeyAction.Quit, Closed(Letter('q')));
        Assert.Equal(WatcherApp.KeyAction.Quit, Closed(Key(ConsoleKey.Escape)));
        Assert.Equal(WatcherApp.KeyAction.MoveUp, Closed(Key(ConsoleKey.UpArrow)));
        Assert.Equal(WatcherApp.KeyAction.MoveDown, Closed(Key(ConsoleKey.DownArrow)));
        Assert.Equal(WatcherApp.KeyAction.ActivateHighlighted, Closed(Key(ConsoleKey.Enter)));
    }

    [Theory]
    [InlineData('1')]
    [InlineData('9')]
    [InlineData('A')]
    [InlineData('Z')]
    public void Address_keys_route_to_a_row_while_the_prompt_is_closed(char c) =>
        Assert.Equal(WatcherApp.KeyAction.AddressRow, Closed(Char(c)));

    /// <summary>
    /// Every letter command has a shifted address-key twin, and the twin must win: the
    /// physical key is the same for both cases, so classifying on ConsoleKey rather than
    /// KeyChar sends the address key to the command. 'Q' (row 26) is the one that used to
    /// - and row 26 is routine once Other panes lists a whole tmux server.
    /// </summary>
    [Theory]
    [InlineData('Q')]
    [InlineData('P')]
    [InlineData('W')]
    [InlineData('O')]
    [InlineData('C')]
    [InlineData('A')]
    [InlineData('N')]
    public void Shifted_letters_address_rows_rather_than_running_their_command(char c)
    {
        Assert.Equal(WatcherApp.KeyAction.AddressRow, Closed(Letter(c)));
        Assert.True(WatcherApp.IndexForAddressKey(c) >= 0);
    }

    /// <summary>The lowercase commands still route as commands when reported realistically.</summary>
    [Theory]
    [InlineData('p', WatcherApp.KeyAction.TogglePauseRow)]
    [InlineData('w', WatcherApp.KeyAction.ToggleWide)]
    [InlineData('o', WatcherApp.KeyAction.ToggleOthers)]
    [InlineData('c', WatcherApp.KeyAction.ToggleCompanions)]
    [InlineData('a', WatcherApp.KeyAction.AcknowledgeRow)]
    [InlineData('n', WatcherApp.KeyAction.OpenNewWindowPrompt)]
    [InlineData('q', WatcherApp.KeyAction.Quit)]
    internal void Lowercase_letters_run_their_command(char c, WatcherApp.KeyAction expected) =>
        Assert.Equal(expected, Closed(Letter(c)));

    /// <summary>'Q' addresses row 26, which is the row AddressKey names it for.</summary>
    [Fact]
    public void Address_key_for_row_26_is_Q() =>
        Assert.Equal("Q", WatcherApp.AddressKey(25));

    [Theory]
    [InlineData('q')]   // would otherwise quit
    [InlineData('p')]
    [InlineData('a')]
    [InlineData('n')]
    [InlineData('o')]
    [InlineData('c')]
    [InlineData('w')]
    [InlineData('1')]   // would otherwise jump to a row
    [InlineData('A')]
    [InlineData('z')]
    public void Every_character_is_prompt_input_while_the_prompt_is_open(char c) =>
        Assert.Equal(WatcherApp.KeyAction.PromptInput, Open(Char(c)));

    [Theory]
    [InlineData(ConsoleKey.Q)]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.UpArrow)]
    [InlineData(ConsoleKey.DownArrow)]
    [InlineData(ConsoleKey.Backspace)]
    [InlineData(ConsoleKey.F5)]
    public void Named_keys_are_prompt_input_too_while_it_is_open(ConsoleKey key) =>
        Assert.Equal(WatcherApp.KeyAction.PromptInput, Open(Key(key)));

    [Fact]
    public void Escape_cancels_the_prompt_rather_than_the_app()
    {
        // The distinction that matters: esc reaches the editor (which turns it into a
        // cancel) instead of being read as quit.
        Assert.Equal(WatcherApp.KeyAction.Quit, Closed(Key(ConsoleKey.Escape)));
        Assert.Equal(WatcherApp.KeyAction.PromptInput, Open(Key(ConsoleKey.Escape)));
    }

    [Fact]
    public void Unbound_keys_are_ignored_while_the_prompt_is_closed()
    {
        Assert.Equal(WatcherApp.KeyAction.Ignore, Closed(Key(ConsoleKey.F5)));
        Assert.Equal(WatcherApp.KeyAction.Ignore, Closed(Char('z')));
        Assert.Equal(WatcherApp.KeyAction.Ignore, Closed(Char('0')));
    }
}
