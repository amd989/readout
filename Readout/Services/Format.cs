using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Logging;

namespace Readout.Services;

/// <summary>How the app words numbers and durations.</summary>
public static class Format
{
    public static string Value(double value, string unit) =>
        SiFormat.Format(value, unit, provider: CultureInfo.CurrentCulture);

    public static string Stats(LogStatistics s, string unit) =>
        $"Min {Value(s.Min, unit)}   Max {Value(s.Max, unit)}   Avg {Value(s.Average, unit)}   P-P {Value(s.PeakToPeak, unit)}";

    public static string Duration(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => $"{span.TotalSeconds:0} s",
        < 3600 => $"{(int)span.TotalMinutes} min {span.Seconds} s",
        _ => $"{(int)span.TotalHours} h {span.Minutes} min",
    };

    /// <summary>A name that's safe in a file name: "DC voltage Oct 27" → "DC-voltage-Oct-27".</summary>
    public static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)]);
        return string.IsNullOrWhiteSpace(cleaned.Trim('-')) ? "session" : cleaned.Trim('-');
    }
}
