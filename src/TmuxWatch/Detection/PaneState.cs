namespace TmuxWatch.Detection;

public enum PaneState
{
    /// <summary>Blocked on the user (command approval or ask_user prompt).</summary>
    Waiting,

    /// <summary>Actively producing output (spinner + "Working").</summary>
    Working,

    /// <summary>Turn finished, sitting at the input box.</summary>
    Idle,

    /// <summary>
    /// Turn finished, but background work the agent started - a shell or a monitor - is
    /// still running. A **quiet** state: the agent has handed the turn back, yet the work
    /// it started has not fully finished, so the pane is not treated as needing the user
    /// and the completed-turn announcement is deferred (see AttentionMonitor).
    ///
    /// Unlike <see cref="Done"/>, this IS produced by the stateless classifier: "not
    /// blocked, not working, background task alive" is fully visible on one screen and
    /// needs no history.
    ///
    /// The classifier reports a <see cref="BackgndReason"/> alongside this state, saying
    /// which fingerprint matched - a background-task counter, a detached sub-agent's row,
    /// or both. It exists solely so the monitor's grace-period backstop can apply to work
    /// that may never end while leaving a sub-agent, which always terminates and releases
    /// the pane itself, to do so. The reason changes nothing else: badge, priority rank and
    /// classification precedence are the same whatever it says.
    /// </summary>
    Backgnd,

    /// <summary>
    /// Turn just finished - your move. Monitor-derived, never returned by the
    /// stateless <see cref="PaneClassifier"/>: the monitor promotes a classified IDLE
    /// pane to DONE when its previous state was WORKING, and holds it there until the
    /// pane is acknowledged or leaves IDLE. See AttentionMonitor.
    /// </summary>
    Done,

    /// <summary>Copilot process has exited / pane gone.</summary>
    Dead,

    /// <summary>No known fingerprint matched.</summary>
    Unknown,
}
