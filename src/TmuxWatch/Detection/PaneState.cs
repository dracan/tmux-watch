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
