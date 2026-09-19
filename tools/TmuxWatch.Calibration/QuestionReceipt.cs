using TmuxWatch.Detection;

namespace TmuxWatch.Calibration;

/// <summary>Backfills only the captured interval whose native answer was received.</summary>
public static class QuestionReceipt
{
    public static bool IsPending(EvidenceEvent question, IReadOnlyList<EvidenceEvent> events) =>
        !events.Any(e => e.At > question.At && e.Event is "PreToolUse" or "PostToolUse" or "Stop" or "UserPromptSubmit");

    public static bool Establish(List<Sample> samples, EvidenceEvent question, DateTimeOffset settled,
        DateTimeOffset sent, IReadOnlyList<EvidenceEvent> events)
    {
        if (question.Call.Length == 0 || !events.Any(e => e.Event == "PostToolUse" &&
            e.Tool == question.Tool && e.Call == question.Call && e.At >= sent && e.AnswerReceived)) return false;
        if (events.Any(e => e.At > question.At && e.At < sent &&
            (e.Event is "PreToolUse" or "Stop" or "UserPromptSubmit" || (e.Event == "PostToolUse" && e.Call == question.Call)))) return false;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (sample.At >= settled && sample.At <= sent)
                samples[i] = sample with { Expected = new(PaneState.Waiting, BackgndReason.None, settled,
                    "native question result confirmed synthetic answer for this captured request") };
        }
        return true;
    }
}
