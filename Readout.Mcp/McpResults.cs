using System.Text.Json;
using System.Text.Json.Serialization;

namespace Readout.Mcp;

// What the tools return, as JSON. Source-generated, so it works in trimmed release builds.

public sealed record StatusResult(
    string Connection, string? Meter, bool Demo, int? SignalDbm, string? Signal,
    double? BatteryVolts, bool LowBattery, bool Logging, int LoggedReadings, ReadingResult? Reading);

/// <param name="Display">The main reading as the LCD shows it, with its unit ("12.106 mV").</param>
/// <param name="Value">The main reading in <paramref name="Unit"/>, an SI base unit (V, A, Ω, Hz, F, °C); null on overload.</param>
/// <param name="SubDisplay">The small display ("50.00 Hz"), if it shows anything.</param>
/// <param name="SetupPage">The SETUP page the small display is on ("Battery"), if any.</param>
/// <param name="Symbols">The LCD symbols that are lit ("AUTO", "HOLD", "REL", "MAX"…).</param>
public sealed record ReadingResult(
    DateTimeOffset Time, double AgeSeconds, string Mode, string Display, double? Value, string Unit,
    bool Overload, string? SubDisplay, string? SetupPage, IReadOnlyList<string> Symbols, string Packet);

public sealed record StatsResult(int Count, double Min, double Max, double Average, double PeakToPeak, string Unit);

/// <param name="Display">As the LCD showed it ("12.106 mV").</param>
public sealed record SampleResult(DateTimeOffset Time, string Mode, double? Value, string Unit, string Display);

public sealed record RecentReadingsResult(
    double Seconds, bool Logging, int Count, bool Thinned, StatsResult? Statistics, IReadOnlyList<SampleResult> Readings);

public sealed record PressResult(string Button, bool LongPress, ReadingResult? ReadingAfter);

/// <param name="SetTo">This computer's local time when the clock was set.</param>
public sealed record ClockResult(DateTime SetTo);

public sealed record LoggingResult(bool Logging, int LoggedReadings);

public sealed record SessionResult(
    string Id, string Name, string Notes, DateTimeOffset Started, DateTimeOffset Ended,
    int Readings, string Mode, string Unit, StatsResult? Statistics);

public sealed record SessionListResult(IReadOnlyList<SessionResult> Sessions);

public sealed record SessionDetailResult(SessionResult Session, bool Thinned, IReadOnlyList<SampleResult> Readings);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StatusResult))]
[JsonSerializable(typeof(ClockResult))]
[JsonSerializable(typeof(ReadingResult))]
[JsonSerializable(typeof(RecentReadingsResult))]
[JsonSerializable(typeof(PressResult))]
[JsonSerializable(typeof(LoggingResult))]
[JsonSerializable(typeof(SessionResult))]
[JsonSerializable(typeof(SessionListResult))]
[JsonSerializable(typeof(SessionDetailResult))]
internal sealed partial class McpResultJson : JsonSerializerContext
{
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, typeof(T), Default);
}
