namespace TmuxWatch.Detection;

public enum PaneState
{
    /// <summary>Blocked on the user (command approval or ask_user prompt).</summary>
    Waiting,

    /// <summary>Actively producing output (spinner + "Working").</summary>
    Working,

    /// <summary>Turn finished, sitting at the input box.</summary>
    Idle,

    /// <summary>Copilot process has exited / pane gone.</summary>
    Dead,

    /// <summary>No known fingerprint matched.</summary>
    Unknown,
}
