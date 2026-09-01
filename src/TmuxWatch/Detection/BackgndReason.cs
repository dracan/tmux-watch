namespace TmuxWatch.Detection;

/// <summary>
/// Which of the BACKGND fingerprints a capture matched. Flags, because the two are not
/// mutually exclusive on screen: a pane can be running a detached sub-agent *and* a shell
/// of its own, and a consumer that treats them differently has to know when both apply.
///
/// This is deliberately NOT a pair of <see cref="PaneState"/> members. BACKGND stays one
/// state with one badge, one priority rank and one position in the classification
/// precedence, whatever its reason - every consumer that does not care about the
/// distinction (badge, rank, precedence, the "needs you" aggregate, the TUI sort) would
/// otherwise have to match both members, and each of those is a place to forget one.
/// Exactly one caller cares: <c>AttentionMonitor</c>, which gates the grace-period release.
///
/// The distinction it draws is a **termination guarantee**, not a kind of work. A shell or
/// monitor may run indefinitely - a dev server never exits on its own - so a pane holding
/// one needs a backstop or it would never chime. A detached sub-agent always terminates and
/// removes its own row when it reports, so the pane releases itself and needs no backstop;
/// applying one announces a finished turn that has not finished.
/// </summary>
[Flags]
public enum BackgndReason
{
    /// <summary>No BACKGND fingerprint matched. The reason of every non-BACKGND state.</summary>
    None = 0,

    /// <summary>
    /// The background-task counter in the agent's status chrome (`· 2 shells · 1 monitor ·`).
    /// Shells and monitors share this one fingerprint and mean the same thing here, so both
    /// report this reason and neither is distinguished from the other.
    /// </summary>
    BackgroundTask = 1,

    /// <summary>
    /// A background-agent row in the agent's status chrome - one per live detached
    /// sub-agent. Never covered by <see cref="BackgroundTask"/>: agents are not counted in
    /// that slot.
    /// </summary>
    BackgroundAgent = 2,
}
