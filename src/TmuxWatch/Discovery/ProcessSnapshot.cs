using TmuxWatch.Config;

namespace TmuxWatch.Discovery;

/// <summary>A verified live process. StartedAt is UTC ticks, not a pid-order heuristic.</summary>
internal sealed record ProcessEntry(int Pid, int ParentPid, string Command, long StartedAt);

/// <summary>
/// A single enumeration's ownership evidence. No matches survive into another poll.
/// Process existence establishes identity only; the screen still determines state.
/// </summary>
internal sealed class ProcessSnapshot(IReadOnlyList<ProcessEntry> processes)
{
    private readonly Dictionary<int, ProcessEntry> _processes = processes.ToDictionary(p => p.Pid);
    private readonly ILookup<int, ProcessEntry> _children = processes.ToLookup(p => p.ParentPid);

    internal AgentProfile? FindOwner(int rootPid, IReadOnlySet<int> paneRoots,
        IReadOnlyList<AgentProfile> agents)
    {
        if (rootPid <= 0 || !_processes.TryGetValue(rootPid, out var root) || root.StartedAt <= 0)
            return null;

        AgentProfile? owner = null;
        var pending = new Stack<ProcessEntry>();
        var visited = new HashSet<int>();
        pending.Push(root);
        while (pending.TryPop(out var process))
        {
            if (!visited.Add(process.Pid))
                return null;

            if (agents.FirstOrDefault(a => a.MatchesCommand(process.Command)) is { } match)
            {
                // Two independent owners are ambiguous, even when both are Copilot.
                if (owner is not null)
                    return null;
                owner = match;
                // An agent's own tools or nested agents do not replace its ownership.
                continue;
            }

            foreach (var child in _children[process.Pid])
            {
                if (paneRoots.Contains(child.Pid) && child.Pid != rootPid)
                    continue;
                // Parent ids can outlive their original process and be reused.
                if (child.StartedAt < process.StartedAt)
                    continue;
                pending.Push(child);
            }
        }
        return owner;
    }
}
