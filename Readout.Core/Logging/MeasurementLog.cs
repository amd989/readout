using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Logging;

/// <summary>One logged reading.</summary>
/// <param name="Time">When it arrived.</param>
/// <param name="Value">In <paramref name="Unit"/>, the SI base unit; null on overload.</param>
/// <param name="Display">The main reading as the LCD showed it ("12.106 mV").</param>
/// <param name="Sub">The sub display ("50.00 Hz"), or empty.</param>
public sealed record Sample(DateTimeOffset Time, MeterMode Mode, double? Value, string Unit, string Display, string Sub);

/// <summary>Summary of the logged values in one unit.</summary>
public sealed record LogStatistics(int Count, double Min, double Max, double Average)
{
    [System.Text.Json.Serialization.JsonIgnore] public double PeakToPeak => Max - Min;
}

/// <summary>
/// Every reading since the log was last cleared. Not thread-safe: add and read from one
/// thread (the app's main thread).
/// </summary>
public sealed class MeasurementLog
{
    private readonly List<Sample> _samples = [];

    public IReadOnlyList<Sample> Samples => _samples;
    public int Count => _samples.Count;
    public DateTimeOffset? StartedAt => _samples.Count > 0 ? _samples[0].Time : null;

    /// <summary>Raised after a sample is added or the log is cleared.</summary>
    public event EventHandler? Changed;

    public void Add(MeterReading reading)
    {
        _samples.Add(new Sample(
            reading.ReceivedAt,
            reading.Mode,
            reading.BaseValue,
            reading.BaseUnit,
            reading.MainLine,
            reading.Sub.Label is null ? reading.Sub.Line : ""));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds samples read back from storage (an autosaved recording).</summary>
    public void AddRange(IEnumerable<Sample> samples)
    {
        _samples.AddRange(samples);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _samples.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The newest run of samples in <paramref name="mode"/>: everything since the meter last
    /// switched to it. This is what the trend graph plots.
    /// </summary>
    public IReadOnlyList<Sample> CurrentRun(MeterMode mode)
    {
        var start = _samples.Count;
        while (start > 0 && _samples[start - 1].Mode == mode)
            start--;
        return _samples.GetRange(start, _samples.Count - start);
    }

    /// <summary>Min, max and average of <paramref name="samples"/> that have a value; null when none do.</summary>
    public static LogStatistics? Statistics(IEnumerable<Sample> samples)
    {
        var count = 0;
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        foreach (var sample in samples)
        {
            if (sample.Value is not { } v) continue;
            count++;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            sum += v;
        }
        return count == 0 ? null : new LogStatistics(count, min, max, sum / count);
    }

    /// <summary>Writes the log as CSV; see <see cref="SampleCsv"/>.</summary>
    public void WriteCsv(TextWriter writer) => SampleCsv.Write(_samples, writer);
}
