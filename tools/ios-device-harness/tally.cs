using System.Text.Json;

var documents = File.ReadAllLines(args[0])
    .Where(x => x.Trim().Length > 0)
    .Select(x => JsonDocument.Parse(x).RootElement)
    .Where(x => x.TryGetProperty("run", out var run) && run.GetString()!.StartsWith("device-"))
    .ToList();

string Str(JsonElement element, string name)
{
    return element.TryGetProperty(name, out var value) ? value.ToString() : "";
}

var finals = documents
    .Where(x => Str(x, "stage") == "results")
    .GroupBy(x => Str(x, "run"))
    .Select(x => x.Last())
    .ToList();
Console.WriteLine($"complete pipeline runs: {string.Join(", ", finals.Select(x => Str(x, "run")))}");

var rows = new List<(string Pipeline, string Phase, string Run, int Blank, int Total, int Renders)>();
foreach (var final in finals)
{
    var run = Str(final, "run");
    var stats = final.GetProperty("decodeStats");
    foreach (var group in final.GetProperty("results").EnumerateArray().GroupBy(x => (Pipeline: Str(x, "pipeline"), Phase: Str(x, "phase"))))
    {
        if (group.Key.Phase == "meta")
        {
            continue;
        }

        var capturePhase = group.Key.Phase.StartsWith("cold") ? "cold" : group.Key.Phase;
        var renders = stats.TryGetProperty($"{group.Key.Pipeline}/{capturePhase} svg-data-url", out var stat)
            ? stat.GetProperty("calls").GetInt32()
            : -1;
        rows.Add((group.Key.Pipeline, group.Key.Phase, run, group.Count(x => Str(x, "verdict") == "BLANK"), group.Count(), renders));
    }
}

Console.WriteLine();
Console.WriteLine("pipeline         phase        complete/runs  blank fixtures per run     svg renders per run");
foreach (var group in rows.GroupBy(x => (x.Pipeline, x.Phase)).OrderBy(x => x.Key.Pipeline).ThenBy(x => x.Key.Phase))
{
    var complete = group.Count(x => x.Blank == 0);
    var blanks = string.Join(",", group.Select(x => x.Blank));
    var renders = string.Join(",", group.Select(x => x.Renders));
    Console.WriteLine($"{group.Key.Pipeline,-16} {group.Key.Phase,-12} {complete}/{group.Count(),-12} {blanks,-26} {renders}");
}

Console.WriteLine();
Console.WriteLine("decode() totals over every device run, by image kind (calls/resolved/rejected):");
var totals = new Dictionary<string, (int Calls, int Resolved, int Rejected)>();
foreach (var run in documents.GroupBy(x => Str(x, "run")))
{
    var last = run.LastOrDefault(x => x.TryGetProperty("decodeStats", out _));
    if (last.ValueKind == JsonValueKind.Undefined)
    {
        continue;
    }

    foreach (var stat in last.GetProperty("decodeStats").EnumerateObject())
    {
        var kind = stat.Name.Split(' ').Last();
        var current = totals.GetValueOrDefault(kind);
        totals[kind] = (current.Calls + stat.Value.GetProperty("calls").GetInt32(),
            current.Resolved + stat.Value.GetProperty("resolved").GetInt32(),
            current.Rejected + stat.Value.GetProperty("rejected").GetInt32());
    }
}

foreach (var total in totals.OrderBy(x => x.Key))
{
    Console.WriteLine($"  {total.Key,-16} {total.Value.Calls}/{total.Value.Resolved}/{total.Value.Rejected}");
}
