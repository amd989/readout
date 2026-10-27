using Readout.Core.Logging;

namespace Readout.Core.Display;

/// <summary>A run of readings drawn as one column of a long graph.</summary>
public readonly record struct TrendBucket(DateTimeOffset First, DateTimeOffset Last, double Min, double Max, double Average)
{
    public DateTimeOffset Middle => First + (Last - First) / 2;

    /// <summary>
    /// Groups time-ordered readings into slots <paramref name="width"/> long, counted from
    /// <paramref name="origin"/>. Readings without a value are left out, and empty slots are
    /// skipped, so pauses stay gaps.
    /// </summary>
    public static IReadOnlyList<TrendBucket> Group(IReadOnlyList<Sample> samples, DateTimeOffset origin, TimeSpan width)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return Group(samples, 0, samples.Count, origin, width);
    }

    /// <summary>As the other overload, for the readings from <paramref name="start"/> up to (not including) <paramref name="end"/>.</summary>
    public static IReadOnlyList<TrendBucket> Group(IReadOnlyList<Sample> samples, int start, int end, DateTimeOffset origin, TimeSpan width)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (width <= TimeSpan.Zero) width = TimeSpan.FromTicks(1);

        var buckets = new List<TrendBucket>();
        long slot = 0;
        var count = 0;
        double min = 0, max = 0, sum = 0;
        DateTimeOffset first = default, last = default;
        for (var i = start; i < end; i++)
        {
            var sample = samples[i];
            if (sample.Value is not { } v) continue;

            var k = (long)Math.Floor((sample.Time - origin) / width);
            if (count > 0 && k != slot)
            {
                buckets.Add(new TrendBucket(first, last, min, max, sum / count));
                count = 0;
            }
            if (count == 0)
            {
                slot = k;
                first = sample.Time;
                min = max = sum = v;
            }
            else
            {
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                sum += v;
            }
            last = sample.Time;
            count++;
        }
        if (count > 0)
            buckets.Add(new TrendBucket(first, last, min, max, sum / count));
        return buckets;
    }
}
