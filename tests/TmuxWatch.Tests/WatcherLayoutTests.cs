using TmuxWatch.Detection;
using TmuxWatch.Monitor;
using TmuxWatch.Tmux;
using TmuxWatch.Tui;

namespace TmuxWatch.Tests;

/// <summary>
/// Row assembly for the three tables: which panes land in which table, in what order,
/// and how the two visibility toggles filter them.
/// </summary>
public class WatcherLayoutTests
{
    private static readonly HashSet<string> None = new();

    private static TrackedPaneView Agent(
        string id, string session = "s", int window = 0, int pane = 0, PaneState state = PaneState.Idle) =>
        new(
            new Pane(id, session, window, pane, "claude", false, $"w{window}", ""),
            state,
            DateTimeOffset.UnixEpoch,
            false);

    private static Pane Other(
        string id, string session = "s", int window = 0, int pane = 0, string command = "bash") =>
        new(id, session, window, pane, command, false, $"w{window}", "");

    private static WatchLayout Layout(
        IReadOnlyList<TrackedPaneView> agents,
        IReadOnlyList<Pane> others,
        bool showOther = true,
        bool showCompanions = true,
        HashSet<string>? paused = null) =>
        WatcherApp.BuildLayout(agents, others, paused ?? None, showOther, showCompanions);

    [Fact]
    public void Other_panes_land_in_their_own_table()
    {
        var layout = Layout(
            new[] { Agent("%1") },
            new[] { Other("%2", window: 1, command: "k9s") });

        Assert.Equal(new[] { "%1" }, layout.Agent.Select(r => r.Id));
        Assert.Equal(new[] { "%2" }, layout.Other.Select(r => r.Id));
        Assert.True(layout.Agent[0].IsAgent);
        Assert.False(layout.Other[0].IsAgent);
        // The non-agent row carries its process for the state column to render.
        Assert.Equal("k9s", layout.Other[0].Pane.Command);
    }

    [Fact]
    public void Other_panes_are_ordered_by_session_window_then_pane()
    {
        var layout = Layout(
            Array.Empty<TrackedPaneView>(),
            new[]
            {
                Other("%d", session: "work", window: 1, pane: 1),
                Other("%b", session: "dots", window: 2, pane: 0),
                Other("%c", session: "work", window: 1, pane: 0),
                Other("%a", session: "dots", window: 0, pane: 0),
            });

        Assert.Equal(new[] { "%a", "%b", "%c", "%d" }, layout.Other.Select(r => r.Id));
    }

    [Fact]
    public void Ordering_is_stable_while_the_agent_table_resorts()
    {
        var others = new[] { Other("%7", window: 5), Other("%8", window: 6) };

        var before = Layout(
            new[] { Agent("%1", state: PaneState.Idle), Agent("%2", window: 1, state: PaneState.Waiting) },
            others);
        var after = Layout(
            new[] { Agent("%1", state: PaneState.Waiting), Agent("%2", window: 1, state: PaneState.Idle) },
            others);

        // The agent table re-sorted...
        Assert.Equal(new[] { "%2", "%1" }, before.Agent.Select(r => r.Id));
        Assert.Equal(new[] { "%1", "%2" }, after.Agent.Select(r => r.Id));
        // ...but the non-agent rows kept their positions.
        Assert.Equal(new[] { "%7", "%8" }, before.Other.Select(r => r.Id));
        Assert.Equal(new[] { "%7", "%8" }, after.Other.Select(r => r.Id));
    }

    [Fact]
    public void Backgnd_sorts_below_done_and_above_working()
    {
        // BACKGND is not a "needs you" state, so it stays below WAITING and DONE - but a
        // pane whose agent has finished its turn is nearer to needing the user than one
        // still mid-turn, so it outranks WORKING.
        var layout = Layout(
            new[]
            {
                Agent("%idle", window: 0, state: PaneState.Idle),
                Agent("%working", window: 1, state: PaneState.Working),
                Agent("%backgnd", window: 2, state: PaneState.Backgnd),
                Agent("%done", window: 3, state: PaneState.Done),
                Agent("%waiting", window: 4, state: PaneState.Waiting),
            },
            Array.Empty<Pane>());

        Assert.Equal(
            new[] { "%waiting", "%done", "%backgnd", "%working", "%idle" },
            layout.Agent.Select(r => r.Id));
    }

    [Fact]
    public void Priority_is_attention_order_not_enum_order()
    {
        // The one-shot output shares this function with the live tables. It used to sort
        // by the enum's own value instead, which is grouped for readability and carries
        // no urgency meaning - so DONE printed below IDLE, and inserting a state moved
        // unrelated rows.
        var byPriority = new[]
            {
                PaneState.Idle, PaneState.Dead, PaneState.Backgnd, PaneState.Unknown,
                PaneState.Done, PaneState.Working, PaneState.Waiting,
            }
            .OrderBy(WatcherApp.Priority)
            .ToArray();

        Assert.Equal(
            new[]
            {
                PaneState.Waiting, PaneState.Done, PaneState.Backgnd,
                PaneState.Working, PaneState.Idle, PaneState.Unknown, PaneState.Dead,
            },
            byPriority);
    }

    [Theory]
    // Casing carries urgency in the state column: states that need the user shout, quiet
    // ones do not. BACKGND defers its announcement rather than making one, so it belongs
    // with the quiet group - the label must not imply the pane is asking for anything.
    [InlineData(PaneState.Waiting, true)]
    [InlineData(PaneState.Done, true)]
    [InlineData(PaneState.Backgnd, false)]
    [InlineData(PaneState.Working, false)]
    [InlineData(PaneState.Idle, false)]
    [InlineData(PaneState.Dead, false)]
    public void State_label_casing_matches_urgency(PaneState state, bool shouts)
    {
        var word = Word(WatcherApp.StateMarkup(state, attention: false));
        Assert.Equal(shouts, word == word.ToUpperInvariant());
    }

    [Fact]
    public void Backgnd_label_is_distinct_and_does_not_widen_the_column()
    {
        var backgnd = WatcherApp.StateMarkup(PaneState.Backgnd, attention: false);

        Assert.NotEqual(WatcherApp.StateMarkup(PaneState.Working, false), backgnd);
        Assert.NotEqual(WatcherApp.StateMarkup(PaneState.Idle, false), backgnd);
        Assert.Equal("backgnd", Word(backgnd));

        // Same width as the widest existing label, so adding it reflows nothing.
        var widest = new[] { PaneState.Waiting, PaneState.Done, PaneState.Working, PaneState.Idle, PaneState.Dead }
            .Max(s => Visible(WatcherApp.StateMarkup(s, false)).Length);
        Assert.True(Visible(backgnd).Length <= widest);
    }

    /// <summary>The markup with its Spectre colour tags stripped, as the user sees it.</summary>
    private static string Visible(string markup) =>
        System.Text.RegularExpressions.Regex.Replace(markup, @"\[[^\]]*\]", "");

    /// <summary>The state word alone, without its colour tags or leading glyph.</summary>
    private static string Word(string markup) => Visible(markup).Split(' ').Last();

    [Fact]
    public void Both_toggles_default_to_showing_everything()
    {
        // A companion (same window as the agent) and an unrelated pane.
        var layout = Layout(
            new[] { Agent("%1", window: 0) },
            new[] { Other("%2", window: 0, pane: 1), Other("%3", window: 4) });

        Assert.Equal(new[] { "%2", "%3" }, layout.Other.Select(r => r.Id));
    }

    [Fact]
    public void Hiding_other_panes_empties_that_table()
    {
        var layout = Layout(
            new[] { Agent("%1") },
            new[] { Other("%2", window: 4) },
            showOther: false);

        Assert.Empty(layout.Other);
        Assert.Equal(new[] { "%1" }, layout.All.Select(r => r.Id));
    }

    [Fact]
    public void Excluding_companions_drops_only_panes_sharing_an_agents_window()
    {
        var layout = Layout(
            new[] { Agent("%1", window: 0) },
            new[]
            {
                Other("%2", window: 0, pane: 1),   // companion: same window as %1
                Other("%3", window: 4),            // window with no agent
            },
            showCompanions: false);

        Assert.Equal(new[] { "%3" }, layout.Other.Select(r => r.Id));
    }

    [Fact]
    public void A_companion_is_scoped_to_its_own_session()
    {
        // Same window index, different session: not a companion.
        var layout = Layout(
            new[] { Agent("%1", session: "work", window: 1) },
            new[] { Other("%2", session: "dots", window: 1, pane: 1) },
            showCompanions: false);

        Assert.Equal(new[] { "%2" }, layout.Other.Select(r => r.Id));
    }

    [Fact]
    public void Companion_toggle_is_inert_while_other_panes_are_hidden()
    {
        var agents = new[] { Agent("%1", window: 0) };
        var others = new[] { Other("%2", window: 0, pane: 1), Other("%3", window: 4) };

        var withCompanions = Layout(agents, others, showOther: false, showCompanions: true);
        var without = Layout(agents, others, showOther: false, showCompanions: false);

        Assert.Empty(withCompanions.Other);
        Assert.Empty(without.Other);
        Assert.Equal(withCompanions.All.Select(r => r.Id), without.All.Select(r => r.Id));
    }

    [Fact]
    public void All_walks_agent_then_other_then_paused()
    {
        var layout = Layout(
            new[] { Agent("%1"), Agent("%2", window: 1) },
            new[] { Other("%7", window: 5), Other("%8", window: 6) },
            paused: new HashSet<string> { "%2", "%8" });

        Assert.Equal(new[] { "%1", "%7", "%2", "%8" }, layout.All.Select(r => r.Id));
    }
}
