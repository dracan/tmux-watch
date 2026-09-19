using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TmuxWatch.Calibration;

public sealed class Attempt
{
    public required string Agent { get; init; }
    public required string Scenario { get; init; }
    public required string Directory { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Number { get; init; }
    public string? Attach { get; set; }
    public List<Sample> Samples { get; } = [];
    public List<CheckResult> Checks { get; } = [];
    public List<DriverAction> Actions { get; } = [];
}

public sealed record DriverAction(DateTimeOffset At, string Action);

public sealed class Report
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() },
    };
    public string Mode { get; init; } = "full diagnostic catalog";
    public List<CheckResult> Exclusions { get; } = [];
    public string Schema { get; } = "tmux-watch-calibration-v1";
    public int StatusLineCount { get; } = 16;
    public double LiveBackgroundGraceSeconds { get; } = 120;
    public int LiveUnknownCapturesBeforeStale { get; } = 15;
    public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Finished { get; set; }
    public required string Root { get; init; }
    public string? Socket { get; set; }
    public List<Preflight> Agents { get; } = [];
    public List<Attempt> Attempts { get; } = [];
    public List<CheckResult> Replay { get; } = [];
    public List<CheckResult> Coverage { get; } = [];
    public IEnumerable<CheckResult> AllChecks() => Replay.Concat(Coverage).Concat(Attempts.SelectMany(a =>
        a.Checks.Select(c => c with { Name = $"{a.Agent}/{a.Scenario}/{a.Width}/attempt-{a.Number}/{c.Name}" })));
    public int ExitCode
    {
        get
        {
            var code = Evaluation.ExitCode(AllChecks());
            return (Finished is null || (Exclusions.Count > 0 && Attempts.Count == 0)) && code == 0 ? 2 : code;
        }
    }

    public static string PrivateDirectory(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    public void Save()
    {
        File.WriteAllText(Path.Combine(Root, "report.json.tmp"), JsonSerializer.Serialize(this, Json));
        File.Move(Path.Combine(Root, "report.json.tmp"), Path.Combine(Root, "report.json"), true);
        var text = new StringBuilder("# Agent calibration report\n\n");
        text.AppendLine($"Started: {Started:O}\n\nExit status: {ExitCode} (0 complete, 1 mismatch, 2 incomplete)\n");
        text.AppendLine("Scope: " + Mode + "\n");
        foreach (var excluded in Exclusions)
            text.AppendLine($"Excluded: {Cell(excluded.Name)} - {Cell(excluded.Detail)}\n");
        text.AppendLine("| Check | Outcome | Detail |\n| --- | --- | --- |");
        foreach (var check in AllChecks()) text.AppendLine($"| {Cell(check.Name)} | {check.Outcome} | {Cell(check.Detail)} |");
        text.AppendLine("\nLive attempts and exact capture paths are in report.json. Replay uses scrubbed fixtures and does not establish live coverage.");
        File.WriteAllText(Path.Combine(Root, "report.md"), text.ToString());
    }

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    public static string Export(string runDirectory, string reviewedCapture, string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9-]*$")) throw new ArgumentException("Candidate name must be kebab-case");
        var report = Path.Combine(runDirectory, "report.json");
        if (!File.Exists(report)) throw new ArgumentException("Run report is missing");
        if (!File.Exists(reviewedCapture)) throw new ArgumentException("Reviewed capture is missing");
        var candidates = PrivateDirectory(Path.Combine(runDirectory, "candidates"));
        var destination = Path.Combine(candidates, name + ".txt");
        // Preserve every glyph and line break. Scrubbing is explicitly the reviewer's job.
        File.Copy(reviewedCapture, destination, overwrite: false);
        File.WriteAllText(Path.Combine(candidates, name + ".json"), JsonSerializer.Serialize(new
        {
            sourceReport = Path.GetFullPath(report), reviewedCapture = Path.GetFullPath(reviewedCapture),
            created = DateTimeOffset.UtcNow, review = "Operator supplied scrubbed text; register and review a regression test before committing",
        }, Json));
        return destination;
    }
}
