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
    // Replaced (not emptied) on Clear, so a CurrentRun handed out earlier keeps its readings.
    private List<Sample> _samples = [];

    // The newest run of one mode, with its statistics kept as running totals so a long
    // recording costs the same per reading as a short one.
    private int _runStart;
    private int _runCount;
    private double _runMin, _runMax, _runSum;

    public IReadOnlyList<Sample> Samples => _samples;
    public int Count => _samples.Count;
    public DateTimeOffset? StartedAt => _samples.Count > 0 ? _samples[0].Time : null;

    /// <summary>Goes up by one every time the log is cleared.</summary>
    public int Generation { get; private set; }

    /// <summary>Raised after a sample is added or the log is cleared.</summary>
    public event EventHandler? Changed;

    public void Add(MeterReading reading)
    {
        Append(new Sample(
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
        foreach (var sample in samples)
            Append(sample);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _samples = [];
        _runStart = 0;
        _runCount = 0;
        Generation++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Append(Sample sample)
    {
        if (_samples.Count > 0 && _samples[^1].Mode != sample.Mode)
        {
            _runStart = _samples.Count;
            _runCount = 0;
        }
        _samples.Add(sample);

        if (sample.Value is not { } v) return;
        if (_runCount == 0)
        {
            _runMin = _runMax = _runSum = v;
        }
        else
        {
            _runMin = Math.Min(_runMin, v);
            _runMax = Math.Max(_runMax, v);
            _runSum += v;
        }
        _runCount++;
    }

    /// <summary>
    /// The newest run of samples in <paramref name="mode"/>: everything since the meter last
    /// switched to it. This is what the trend graph plots. A view, not a copy: it stays as it
    /// was when taken, even after more readings or a clear.
    /// </summary>
    public IReadOnlyList<Sample> CurrentRun(MeterMode mode) =>
        _samples.Count > 0 && _samples[^1].Mode == mode
            ? new ListSlice<Sample>(_samples, _runStart, _samples.Count - _runStart)
            : [];

    /// <summary>
    /// <see cref="Statistics"/> of <see cref="CurrentRun"/>, without going through it.
    /// Null when the newest run isn't in <paramref name="mode"/> or has no values.
    /// </summary>
    public LogStatistics? CurrentRunStatistics(MeterMode mode) =>
        _runCount > 0 && _samples[^1].Mode == mode
            ? new LogStatistics(_runCount, _runMin, _runMax, _runSum / _runCount)
            : null;

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
