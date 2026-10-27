using Readout.Core.Logging;

namespace Readout.Core.Display;

/// <summary>A run of readings drawn as one column of a long graph.</summary>
public readonly record struct TrendBucket(DateTimeOffset First, DateTimeOffset Last, double Min, double Max, double Average)
{
    public DateTimeOffset Middle => First + (Last - First) / 2;

    /// <summary>
    /// Groups time-ordered readings (all with values) into slots <paramref name="width"/> long,
    /// counted from <paramref name="origin"/>. Empty slots are skipped, so pauses stay gaps.
    /// </summary>
    public static IReadOnlyList<TrendBucket> Group(IReadOnlyList<Sample> samples, DateTimeOffset origin, TimeSpan width)
    {
        var buckets = new List<TrendBucket>();
        if (width <= TimeSpan.Zero) width = TimeSpan.FromTicks(1);
        var i = 0;
        while (i < samples.Count)
        {
            var slot = (long)Math.Floor((samples[i].Time - origin) / width);
            double min = double.MaxValue, max = double.MinValue, sum = 0;
            var j = i;
            for (; j < samples.Count && (long)Math.Floor((samples[j].Time - origin) / width) == slot; j++)
            {
                var v = samples[j].Value!.Value;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                sum += v;
            }
            buckets.Add(new TrendBucket(samples[i].Time, samples[j - 1].Time, min, max, sum / (j - i)));
            i = j;
        }
        return buckets;
    }
}
