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

    /// <summary>
    /// A name that's safe in a file name on any system the CSV may end up on:
    /// "AC voltage, Nov 4 15:56" → "AC-voltage-Nov-4-15-56". Letters, digits, "_" and "." stay;
    /// anything else (spaces, ":" which Windows refuses, ",") becomes one "-".
    /// </summary>
    public static string FileName(string name)
    {
        var cleaned = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '.')
                cleaned.Append(c);
            else if (cleaned.Length > 0 && cleaned[^1] != '-')
                cleaned.Append('-');
        }
        var result = cleaned.ToString().Trim('-', '.');
        return result.Length == 0 ? "session" : result;
    }
}
