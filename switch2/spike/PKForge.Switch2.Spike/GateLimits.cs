namespace PKForge.Switch2.Spike;

public sealed record GateMeasurements(TimeSpan? ColdStart, TimeSpan? OpenSave, TimeSpan? LegalityOneWorst,
    TimeSpan? LegalityBox, double? AverageFps, double? FreeHeapFraction, long? NroBytes);

/// <param name="Passed">Null when the value was not measured.</param>
public sealed record GateRow(string Measure, string Limit, string? Value, bool? Passed);

/// <param name="Passed">True only when every row passed.</param>
public sealed record GateVerdict(bool Passed, IReadOnlyList<GateRow> Rows);

/// <summary>The phase 0 go/no-go limits, in the design's order and wording (section 9). A value equal to its limit passes.</summary>
public static class GateLimits
{
    public static readonly TimeSpan ColdStart = TimeSpan.FromSeconds(20), OpenSave = TimeSpan.FromSeconds(5),
        LegalityOne = TimeSpan.FromMilliseconds(100), LegalityBox = TimeSpan.FromSeconds(3);
    public const double MinFps = 30, MinFreeHeapFraction = 0.25;
    public const long MaxNroBytes = 500L * 1024 * 1024;

    public static GateVerdict Evaluate(GateMeasurements m)
    {
        GateRow[] rows =
        [
            AtMost("Cold start to a usable screen", "20 s or less", m.ColdStart, ColdStart),
            AtMost("Opening the test console's Legends: Z-A save", "5 s or less", m.OpenSave, OpenSave),
            AtMost("Legality check, one Pokémon", "100 ms or less", m.LegalityOneWorst, LegalityOne),
            AtMost("Legality check, a full box of 30", "3 s or less", m.LegalityBox, LegalityBox),
            Row("Frame rate", "60 fps target; never below 30 fps", m.AverageFps, v => $"{v:0.#} fps", v => v >= MinFps),
            Row("Free memory with that save open", "at least a quarter of the heap", m.FreeHeapFraction, v => $"{v:P0}", v => v >= MinFreeHeapFraction),
            Row(".nro size", "500 MB or less", m.NroBytes, v => $"{v / (1024.0 * 1024):0} MB", v => v <= MaxNroBytes),
        ];
        return new GateVerdict(rows.All(row => row.Passed == true), rows);
    }

    private static GateRow AtMost(string measure, string limit, TimeSpan? value, TimeSpan max) =>
        Row(measure, limit, value, v => v.TotalSeconds >= 1 ? $"{v.TotalSeconds:0.##} s" : $"{v.TotalMilliseconds:0} ms", v => v <= max);

    private static GateRow Row<T>(string measure, string limit, T? value, Func<T, string> show, Func<T, bool> passes) where T : struct =>
        value is { } v ? new GateRow(measure, limit, show(v), passes(v)) : new GateRow(measure, limit, null, null);
}
