using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// The highlighted row is anchored to a pane id so it follows its pane through a re-sort,
/// and every visible row is addressable: digits 1-9, then shift+letter.
/// </summary>
public class HighlightAndAddressingTests
{
    private static readonly HashSet<string> None = new();

    private static TrackedPaneView Agent(string id, PaneState state = PaneState.Idle, int window = 0) =>
        new(
            new Pane(id, "s", window, 0, "claude", false, $"w{window}", ""),
            state,
            DateTimeOffset.UnixEpoch,
            false);

    private static Pane Other(string id, int window) =>
        new(id, "s", window, 0, "bash", false, $"w{window}", "");

    private static IReadOnlyList<WatchRow> Rows(params string[] ids) =>
        WatcherApp.BuildLayout(
            ids.Select(i => Agent(i)).ToList(),
            Array.Empty<Pane>(),
            None,
            showOtherPanes: true,
            showCompanionPanes: true).All;

    [Fact]
    public void Highlight_follows_its_pane_through_a_resort()
    {
        var before = WatcherApp.BuildLayout(
            new[] { Agent("%1", PaneState.Waiting), Agent("%2", PaneState.Idle, window: 1) },
            Array.Empty<Pane>(), None, true, true).All;
        Assert.Equal(new[] { "%1", "%2" }, before.Select(r => r.Id));

        // Highlight %2 (row index 1), then %2 becomes WAITING and sorts to the top.
        var after = WatcherApp.BuildLayout(
            new[] { Agent("%1", PaneState.Idle), Agent("%2", PaneState.Waiting, window: 1) },
            Array.Empty<Pane>(), None, true, true).All;
        Assert.Equal(new[] { "%2", "%1" }, after.Select(r => r.Id));

        var resolved = WatcherApp.ResolveHighlightIndex(after, "%2", lastIndex: 1);

        Assert.Equal(0, resolved);
        Assert.Equal("%2", after[resolved].Id);
    }

    [Fact]
    public void Highlight_falls_back_to_the_nearest_row_when_its_pane_disappears()
    {
        var rows = Rows("%1", "%2", "%3");

        // The pane that was highlighted at index 2 is gone.
        var resolved = WatcherApp.ResolveHighlightIndex(rows, "%gone", lastIndex: 2);

        Assert.Equal(2, resolved);
    }

    [Fact]
    public void Fallback_clamps_when_the_list_shrank()
    {
        var rows = Rows("%1", "%2");

        var resolved = WatcherApp.ResolveHighlightIndex(rows, "%gone", lastIndex: 7);

        Assert.Equal(1, resolved);
    }

    [Fact]
    public void Hiding_a_table_moves_the_highlight_to_a_visible_row()
    {
        var agents = new[] { Agent("%1") };
        var others = new[] { Other("%7", 5) };

        var shown = WatcherApp.BuildLayout(agents, others, None, true, true).All;
        var hidden = WatcherApp.BuildLayout(agents, others, None, false, true).All;

        // Highlight sits on the non-agent row, then that table is hidden.
        Assert.Equal(1, WatcherApp.ResolveHighlightIndex(shown, "%7", lastIndex: 0));
        var resolved = WatcherApp.ResolveHighlightIndex(hidden, "%7", lastIndex: 1);

        Assert.Equal(0, resolved);
        Assert.Equal("%1", hidden[resolved].Id);
    }

    [Fact]
    public void No_rows_means_nothing_is_highlighted()
    {
        Assert.Equal(-1, WatcherApp.ResolveHighlightIndex(Array.Empty<WatchRow>(), "%1", 0));
    }

    [Fact]
    public void First_nine_rows_are_addressed_by_digits()
    {
        Assert.Equal("1", WatcherApp.AddressKey(0));
        Assert.Equal("9", WatcherApp.AddressKey(8));
    }

    [Fact]
    public void Rows_past_the_ninth_are_addressed_by_shift_letter()
    {
        Assert.Equal("A", WatcherApp.AddressKey(9));
        Assert.Equal("B", WatcherApp.AddressKey(10));
        Assert.Equal("Z", WatcherApp.AddressKey(34));
    }

    [Fact]
    public void Rows_past_the_addressable_range_get_no_key()
    {
        Assert.Equal("", WatcherApp.AddressKey(35));
        Assert.Equal("", WatcherApp.AddressKey(-1));
    }

    [Theory]
    [InlineData('1', 0)]
    [InlineData('9', 8)]
    [InlineData('A', 9)]
    [InlineData('Z', 34)]
    public void Address_keys_map_back_to_their_row(char key, int expected)
    {
        Assert.Equal(expected, WatcherApp.IndexForAddressKey(key));
    }

    [Theory]
    [InlineData('a')]   // ack
    [InlineData('c')]   // companions
    [InlineData('o')]   // others
    [InlineData('p')]   // pause
    [InlineData('w')]   // wide
    [InlineData('q')]   // quit
    [InlineData('0')]
    public void Lowercase_keys_and_zero_address_no_row(char key)
    {
        Assert.Equal(-1, WatcherApp.IndexForAddressKey(key));
    }

    [Fact]
    public void Switching_selects_session_window_then_pane()
    {
        var fake = new FakeTmuxClient();
        var pane = new Pane("%12", "work", 1, 1, "lazygit", false, "logs", "");

        WatcherApp.SwitchTo(fake, pane);

        Assert.Equal(new[] { "work" }, fake.SwitchedSessions);
        Assert.Equal(new[] { "work:1" }, fake.SelectedWindows);
        // Without this the jump would land on whichever pane the window last had active.
        Assert.Equal(new[] { "%12" }, fake.SelectedPanes);
        // Focus verbs only - a jump never captures or reads the pane.
        Assert.Empty(fake.CapturedPanes);
    }

    [Fact]
    public void Switching_to_a_non_agent_pane_uses_the_same_focus_only_path()
    {
        var fake = new FakeTmuxClient();

        WatcherApp.SwitchTo(fake, Other("%7", 5));

        Assert.Equal(new[] { "s" }, fake.SwitchedSessions);
        Assert.Equal(new[] { "s:5" }, fake.SelectedWindows);
        Assert.Equal(new[] { "%7" }, fake.SelectedPanes);
        Assert.Empty(fake.CapturedPanes);
    }

    [Fact]
    public void Address_keys_round_trip_across_the_whole_range()
    {
        for (var i = 0; i < 35; i++)
        {
            var key = WatcherApp.AddressKey(i);
            Assert.Single(key);
            Assert.Equal(i, WatcherApp.IndexForAddressKey(key[0]));
        }
    }
}
