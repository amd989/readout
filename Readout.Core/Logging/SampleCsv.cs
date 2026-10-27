using System.Globalization;
using System.Text;
using Readout.Core.Protocol;

namespace Readout.Core.Logging;

/// <summary>
/// The CSV format for logged samples, used both for export and for storing sessions: one row
/// per sample, UTC timestamps, seconds since the first sample, values in SI base units with
/// invariant formatting (a dot for decimals).
/// </summary>
public static class SampleCsv
{
    public const string Header = "time_utc,elapsed_s,mode,value,unit,display,sub_display";

    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static void Write(IReadOnlyList<Sample> samples, TextWriter writer)
    {
        writer.WriteLine(Header);
        if (samples.Count > 0)
            WriteRows(samples, 0, samples[0].Time, writer);
    }

    /// <summary>Writes rows from <paramref name="first"/> on, timed from <paramref name="start"/> (no header).</summary>
    public static void WriteRows(IReadOnlyList<Sample> samples, int first, DateTimeOffset start, TextWriter writer)
    {
        for (var i = first; i < samples.Count; i++)
        {
            var s = samples[i];
            writer.Write(s.Time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write((s.Time - start).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(Quote(s.Mode.ToString()));
            writer.Write(',');
            writer.Write(s.Value?.ToString("G10", CultureInfo.InvariantCulture) ?? "");
            writer.Write(',');
            writer.Write(Quote(s.Unit));
            writer.Write(',');
            writer.Write(Quote(s.Display));
            writer.Write(',');
            writer.Write(Quote(s.Sub));
            writer.WriteLine();
        }
    }

    /// <summary>Reads samples written by <see cref="Write"/>. Rows that don't parse are skipped.</summary>
    public static List<Sample> Read(TextReader reader)
    {
        var samples = new List<Sample>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line.StartsWith("time_utc,", StringComparison.Ordinal))
                continue;

            var f = Split(line);
            if (f.Count < 7
                || !DateTimeOffset.TryParseExact(f[0], TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
                || !Enum.TryParse<MeterMode>(f[2], out var mode))
                continue;

            double? value = double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            samples.Add(new Sample(time, mode, value, f[4], f[5], f[6]));
        }
        return samples;
    }

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : field;

    private static List<string> Split(string line)
    {
        var fields = new List<string>(7);
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }
}
