using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Logging;

namespace Readout.Drawing;

/// <summary>
/// A line graph of readings: value over time, scaled to fit what's visible, with the range
/// and time span as labels. <see cref="Window"/> picks the stretch of time shown.
/// </summary>
public sealed class TrendDrawable : IDrawable
{
    public IReadOnlyList<Sample> Samples { get; set; } = [];

    /// <summary>The visible stretch of time; null shows everything.</summary>
    public TrendWindow? Window { get; set; }

    /// <summary>Seconds from the first to the last reading with a value.</summary>
    public static double DataSpan(IReadOnlyList<Sample> samples)
    {
        var first = NextWithValue(samples, 0, 1);
        var last = NextWithValue(samples, samples.Count - 1, -1);
        return first < 0 || last < first ? 0 : (samples[last].Time - samples[first].Time).TotalSeconds;
    }

    /// <summary>The index of the first reading with a value from <paramref name="from"/> in <paramref name="step"/>'s direction, or -1.</summary>
    private static int NextWithValue(IReadOnlyList<Sample> samples, int from, int step)
    {
        for (var i = from; i >= 0 && i < samples.Count; i += step)
            if (samples[i].Value is not null) return i;
        return -1;
    }

    /// <summary>The first index in [lo, hi) whose time is after <paramref name="time"/> (or at it, unless <paramref name="after"/>).</summary>
    private static int Search(IReadOnlyList<Sample> samples, DateTimeOffset time, int lo, int hi, bool after)
    {
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            var t = samples[mid].Time;
            if (t < time || (after && t == time)) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// When above zero and not zoomed, shows only the newest this-many seconds, so a small
    /// live graph stays readable however long the recording gets.
    /// </summary>
    public double RecentSeconds { get; set; }

    public Color LineColor { get; set; } = Color.FromArgb("#E07A1F");
    public Color GridColor { get; set; } = Colors.Gray.WithAlpha(0.3f);
    public Color TextColor { get; set; } = Colors.Gray;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var rect = dirtyRect;
        const float padLeft = 8, padRight = 8, padTop = 22, padBottom = 20;
        var plot = new RectF(rect.X + padLeft, rect.Y + padTop, rect.Width - padLeft - padRight, rect.Height - padTop - padBottom);
        if (plot.Width <= 10 || plot.Height <= 10) return;

        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        for (var k = 0; k <= 4; k++)
        {
            var y = plot.Y + plot.Height * k / 4;
            canvas.DrawLine(plot.X, y, plot.Right, y);
        }

        canvas.FontColor = TextColor;
        canvas.FontSize = 11;

        // Readings are in time order, so the visible ones are found by binary search and read
        // in place: a long recording costs only what's on screen.
        var samples = Samples;
        var first = NextWithValue(samples, 0, 1);
        var last = NextWithValue(samples, samples.Count - 1, -1);
        if (first < 0 || last <= first)
        {
            canvas.DrawString(samples.Count == 0 ? "No readings yet" : "Waiting for readings…",
                plot, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        var start = samples[first].Time;
        var dataSpan = Math.Max((samples[last].Time - start).TotalSeconds, 1e-3);
        var (fromSec, toSec) = Window?.Resolve(dataSpan) ?? (0, dataSpan);
        var showsRecent = RecentSeconds > 0 && Window?.IsZoomed != true && dataSpan > RecentSeconds;
        if (showsRecent) (fromSec, toSec) = (dataSpan - RecentSeconds, dataSpan);
        var t0 = start.AddSeconds(fromSec);
        var span = Math.Max(toSec - fromSec, 1e-3);

        // The visible readings, plus one with a value each side so the line reaches the edges.
        var lo = Search(samples, t0, first, last + 1, after: false);
        var hi = Search(samples, start.AddSeconds(toSec), first, last + 1, after: true) - 1;
        if (NextWithValue(samples, lo - 1, -1) is var before and >= 0 && before >= first) lo = before;
        if (NextWithValue(samples, hi + 1, 1) is var after and >= 0 && after <= last) hi = after;

        var count = 0;
        double min = double.MaxValue, max = double.MinValue;
        var unit = "";
        for (var i = lo; i <= hi; i++)
        {
            if (samples[i].Value is not { } v) continue;
            count++;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            unit = samples[i].Unit;
        }
        if (count == 0) return;

        if (max - min < Math.Abs(max) * 1e-6 + 1e-12)
        {
            var pad = Math.Max(Math.Abs(max) * 0.01, 1e-9);
            min -= pad;
            max += pad;
        }
        else
        {
            var pad = (max - min) * 0.08;
            min -= pad;
            max += pad;
        }

        float X(Sample s) => plot.X + (float)((s.Time - t0).TotalSeconds / span) * plot.Width;
        float Y(double v) => plot.Bottom - (float)((v - min) / (max - min)) * plot.Height;

        // A pause in the readings (disconnected, logging paused, overload) leaves a gap rather
        // than a straight line across it. Long logs draw buckets, so allow a few buckets apart.
        var columns = Math.Max(1, (int)plot.Width);
        var gap = TimeSpan.FromSeconds(Math.Max(2, span / columns * 3));

        canvas.SaveState();
        canvas.ClipRectangle(plot);
        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 2;
        canvas.StrokeLineJoin = LineJoin.Round;

        if (count <= columns * 2)
        {
            // Few enough readings to draw each one. Each stretch between gaps is its own path:
            // on Windows, Win2D rejects some paths with several figures ("BeginFigure ... when
            // the figure was already begun") and the app goes down with it.
            PathF? path = null;
            DateTimeOffset? previous = null;
            for (var i = lo; i <= hi; i++)
            {
                var s = samples[i];
                if (s.Value is not { } v) continue;
                var p = new PointF(X(s), Y(v));
                if (path is null || s.Time - previous!.Value > gap)
                {
                    if (path is not null) canvas.DrawPath(path);
                    path = new PathF();
                    path.MoveTo(p);
                }
                else path.LineTo(p);
                previous = s.Time;
            }
            if (path is not null) canvas.DrawPath(path);
        }
        else
        {
            // Long logs: about one bucket per pixel column. Each bucket's min to max is a faint
            // band (so spikes and noise still show) and its average is the line, so a noisy
            // reading doesn't turn into a solid block.
            var buckets = TrendBucket.Group(samples, lo, hi + 1, t0, TimeSpan.FromSeconds(span / columns));
            var run = new List<TrendBucket>();
            void Flush()
            {
                if (run.Count == 0) return;
                var band = new PathF();
                band.MoveTo(BucketX(run[0]), Y(run[0].Max));
                foreach (var b in run.Skip(1)) band.LineTo(BucketX(b), Y(b.Max));
                for (var k = run.Count - 1; k >= 0; k--) band.LineTo(BucketX(run[k]), Y(run[k].Min));
                band.Close();
                canvas.FillColor = LineColor.WithAlpha(0.28f);
                canvas.FillPath(band);
                if (run.Count == 1)
                    canvas.DrawLine(BucketX(run[0]), Y(run[0].Max), BucketX(run[0]), Y(run[0].Min));

                var line = new PathF();
                line.MoveTo(BucketX(run[0]), Y(run[0].Average));
                foreach (var b in run.Skip(1)) line.LineTo(BucketX(b), Y(b.Average));
                canvas.DrawPath(line);
                run.Clear();
            }
            foreach (var b in buckets)
            {
                if (run.Count > 0 && b.First - run[^1].Last > gap) Flush();
                run.Add(b);
            }
            Flush();
        }
        canvas.RestoreState();

        float BucketX(TrendBucket b) => plot.X + (float)((b.Middle - t0).TotalSeconds / span) * plot.Width;

        canvas.DrawString(SiFormat.Format(max, unit, provider: CultureInfo.CurrentCulture), plot.X, rect.Y + 2, plot.Width, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString(SiFormat.Format(min, unit, provider: CultureInfo.CurrentCulture), plot.X, plot.Bottom + 2, plot.Width / 2, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        var spanText = Window?.IsZoomed == true || showsRecent ? $"{FormatSpan(span)} of {FormatSpan(dataSpan)}" : FormatSpan(span);
        canvas.DrawString(spanText, plot.X + plot.Width / 2, plot.Bottom + 2, plot.Width / 2, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        canvas.DrawString(t0.ToLocalTime().ToString("T", CultureInfo.CurrentCulture) + " – " + t0.AddSeconds(span).ToLocalTime().ToString("T", CultureInfo.CurrentCulture),
            plot.X + plot.Width / 2, rect.Y + 2, plot.Width / 2, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
    }

    private static string FormatSpan(double seconds) => seconds switch
    {
        < 120 => $"{seconds:0} s",
        < 7200 => $"{seconds / 60:0.#} min",
        _ => $"{seconds / 3600:0.#} h",
    };
}
