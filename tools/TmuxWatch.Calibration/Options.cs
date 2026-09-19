namespace TmuxWatch.Calibration;

public sealed class Options
{
    public string Command { get; private set; } = "help";
    public string[] Agents { get; private set; } = ["copilot", "claude", "codex"];
    public string[] Scenarios { get; private set; } = [];
    public int[] Widths { get; private set; } = [120, 70];
    public int Height { get; private set; } = 40;
    public int ScenarioSeconds { get; private set; } = 90;
    public int DeadlineSeconds { get; private set; } = 1800;
    public int SampleMs { get; private set; } = 500;
    public int StableSamples { get; private set; } = 6;
    public int Retries { get; private set; } = 1;
    public bool Unattended { get; private set; }
    public bool RetainFailures { get; private set; }
    public bool PhysicalNotifications { get; private set; }
    public string Output { get; private set; } = Path.GetFullPath(".calibration-runs");
    public Dictionary<string, string> Models { get; } = new();
    public string[] Positionals { get; private set; } = [];

    public static Options Parse(string[] args)
    {
        var o = new Options();
        if (args.Length == 0) return o;
        o.Command = args[0];
        var positional = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value");
            switch (args[i])
            {
                case "--agents": o.Agents = Value().Split(','); break;
                case "--scenarios": o.Scenarios = Value().Split(','); break;
                case "--widths": o.Widths = Value().Split(',').Select(int.Parse).ToArray(); break;
                case "--height": o.Height = int.Parse(Value()); break;
                case "--scenario-seconds": o.ScenarioSeconds = int.Parse(Value()); break;
                case "--deadline-seconds": o.DeadlineSeconds = int.Parse(Value()); break;
                case "--sample-ms": o.SampleMs = int.Parse(Value()); break;
                case "--stable-samples": o.StableSamples = int.Parse(Value()); break;
                case "--retries": o.Retries = int.Parse(Value()); break;
                case "--output": o.Output = Path.GetFullPath(Value()); break;
                case "--model":
                    var pair = Value().Split('=', 2);
                    if (pair.Length != 2 || !AgentAdapter.Ids.Contains(pair[0])) throw new ArgumentException("Use --model agent=model");
                    o.Models[pair[0]] = pair[1]; break;
                case "--unattended": o.Unattended = true; break;
                case "--retain-failures": o.RetainFailures = true; break;
                case "--physical-notifications": o.PhysicalNotifications = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException("Unknown option: " + args[i]);
                    positional.Add(args[i]); break;
            }
        }
        o.Positionals = positional.ToArray();
        if (o.Agents.Length == 0 || o.Agents.Any(a => !AgentAdapter.Ids.Contains(a))) throw new ArgumentException("Unknown agent");
        if (o.Widths.Length == 0 || o.Widths.Any(w => w < 40 || w > 300) || o.Height < 20 || o.Height > 100)
            throw new ArgumentException("Widths must be 40..300 and height 20..100");
        if (o.SampleMs < 100 || o.StableSamples < 2 || o.ScenarioSeconds < 5 || o.DeadlineSeconds < 5 || o.Retries is < 0 or > 3)
            throw new ArgumentException("Invalid sampling, deadline, or retry limit");
        if (o.Unattended && o.RetainFailures) throw new ArgumentException("Unattended runs always clean up");
        return o;
    }
}
