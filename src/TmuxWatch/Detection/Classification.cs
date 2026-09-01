namespace TmuxWatch.Detection;

/// <summary>
/// A classifier verdict: the pane's state, plus - when that state is
/// <see cref="PaneState.Backgnd"/> - which fingerprint produced it.
///
/// <paramref name="Reason"/> is <see cref="BackgndReason.None"/> for every state other than
/// BACKGND. It carries no history: like the state itself it is derived from a single
/// capture, so the classifier stays pure and fixture-testable.
/// </summary>
public readonly record struct Classification(PaneState State, BackgndReason Reason)
{
    /// <summary>A verdict for a state that carries no BACKGND reason.</summary>
    public static Classification Of(PaneState state) => new(state, BackgndReason.None);
}
