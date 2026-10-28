using System.Text.Json;

var path = args[0];
var runFilter = args.Length > 1 ? args[1] : null;
var lines = File.ReadAllLines(path).Where(x => x.Trim().Length > 0).ToList();
var documents = lines.Select(x => JsonDocument.Parse(x).RootElement).ToList();

string Str(JsonElement element, string name)
{
    return element.TryGetProperty(name, out var value) ? value.ToString() : "";
}

var runs = documents
    .Where(x => runFilter is null || Str(x, "run") == runFilter)
    .GroupBy(x => $"{Str(x, "run")} [{Str(x, "mode")}]");

foreach (var run in runs)
{
    Console.WriteLine($"===== run {run.Key}");
    var userAgent = run.Select(x => Str(x, "userAgent")).FirstOrDefault(x => x.Length > 0);
    Console.WriteLine($"UA: {userAgent}");

    foreach (var error in run.Where(x => Str(x, "stage") is "js-error" or "unhandled-rejection" or "boot-failed" or "stress-skipped-reload-after-start"))
    {
        Console.WriteLine($"  {Str(error, "stage")}: {Str(error, "message")}");
    }

    var final = run.LastOrDefault(x => Str(x, "stage") == "results") ;
    var finalOrProgress = final.ValueKind != JsonValueKind.Undefined
        ? final
        : run.LastOrDefault(x => Str(x, "stage") == "progress");
    if (finalOrProgress.ValueKind != JsonValueKind.Undefined)
    {
        Console.WriteLine($"  source stage: {Str(finalOrProgress, "stage")} {Str(finalOrProgress, "summary")}");
        var results = finalOrProgress.GetProperty("results").EnumerateArray().ToList();
        foreach (var group in results.GroupBy(x => (Pipeline: Str(x, "pipeline"), Phase: Str(x, "phase"))))
        {
            if (group.Key.Phase == "meta")
            {
                var metas = string.Join(", ", group.Select(x => $"{Str(x, "fixture")}={Str(x, "got")}"));
                Console.WriteLine($"  {group.Key.Pipeline,-16} meta        {metas}");
                continue;
            }

            var verdicts = string.Join(" ", group.GroupBy(x => Str(x, "verdict")).Select(x => $"{x.Key}={x.Count()}"));
            var blanks = string.Join(",", group.Where(x => Str(x, "verdict") != "PASS").Select(x => Str(x, "fixture")));
            Console.WriteLine($"  {group.Key.Pipeline,-16} {group.Key.Phase,-11} {verdicts} {(blanks.Length > 0 ? $"[not PASS: {blanks}]" : "")}");
        }
    }

    var statsSource = run.LastOrDefault(x => x.TryGetProperty("decodeStats", out _));
    if (statsSource.ValueKind != JsonValueKind.Undefined)
    {
        Console.WriteLine("  decode() calls (phase kind: calls/resolved/rejected):");
        foreach (var stat in statsSource.GetProperty("decodeStats").EnumerateObject())
        {
            var rejections = string.Join(" | ", stat.Value.GetProperty("rejections").EnumerateArray().Select(x => x.GetString()));
            Console.WriteLine($"    {stat.Name,-40} {Str(stat.Value, "calls")}/{Str(stat.Value, "resolved")}/{Str(stat.Value, "rejected")} {rejections}");
        }
    }

    foreach (var ready in run.Where(x => Str(x, "stage") == "stress-ready"))
    {
        Console.WriteLine($"  stress-ready svgChars={Str(ready, "svgChars")} bitmapPngBytes={Str(ready, "bitmapPngBytes")} {Str(ready, "bitmapPixels")}");
    }

    foreach (var step in run.Where(x => Str(x, "stage") == "stress-step"))
    {
        Console.WriteLine($"  stress held={Str(step, "heldBitmaps"),-4} bitmapDecode={Str(step, "freshBitmapDecodeOutcomes")} svgDecode={Str(step, "svgDecodeOutcomes")} capture={Str(step, "captureOutcome")} {Str(step, "captureMs")}ms blanks={Str(step, "captureBlankFixtures")}");
    }

    var background = run.LastOrDefault(x => Str(x, "stage") == "background-done");
    if (background.ValueKind != JsonValueKind.Undefined)
    {
        var captures = background.GetProperty("captures").EnumerateArray().ToList();
        var origin = captures.Count > 0 ? captures.First().GetProperty("start").GetInt64() : 0;
        Console.WriteLine($"  background: {captures.Count} captures, heartbeats={Str(background, "heartbeatCount")} (t relative to first capture start, ms)");
        foreach (var visibilityEvent in background.GetProperty("visibilityEvents").EnumerateArray())
        {
            Console.WriteLine($"    visibility {Str(visibilityEvent, "state"),-8} at {visibilityEvent.GetProperty("t").GetInt64() - origin}");
        }

        foreach (var gap in background.GetProperty("heartbeatGaps").EnumerateArray())
        {
            Console.WriteLine($"    heartbeat gap {Str(gap, "ms")} ms from {gap.GetProperty("from").GetInt64() - origin} to {gap.GetProperty("to").GetInt64() - origin}");
        }

        foreach (var capture in captures)
        {
            var start = capture.GetProperty("start").GetInt64() - origin;
            var end = capture.GetProperty("end").GetInt64() - origin;
            if (end - start > 3000 || Str(capture, "visibilityAtStart") != "visible" || Str(capture, "visibilityAtEnd") != "visible" || !Str(capture, "outcome").StartsWith("ok"))
            {
                Console.WriteLine($"    capture {start}..{end} ({end - start} ms) {Str(capture, "visibilityAtStart")}->{Str(capture, "visibilityAtEnd")} {Str(capture, "outcome")}");
            }
        }

        var durations = captures.Select(x => x.GetProperty("end").GetInt64() - x.GetProperty("start").GetInt64()).Order().ToList();
        if (durations.Count > 0)
        {
            Console.WriteLine($"    capture ms: min={durations.First()} median={durations[durations.Count / 2]} max={durations.Last()}; ok={captures.Count(x => Str(x, "outcome").StartsWith("ok"))}");
        }
    }
}
