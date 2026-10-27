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
        DateTimeOffset? first = null, last = null;
        foreach (var s in samples)
        {
            if (s.Value is null) continue;
            first ??= s.Time;
            last = s.Time;
        }
        return first is null ? 0 : (last!.Value - first.Value).TotalSeconds;
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

        var samples = Samples;
        var all = samples.Where(s => s.Value is not null).ToList();
        if (all.Count < 2)
        {
            canvas.DrawString(samples.Count == 0 ? "No readings yet" : "Waiting for readings…",
                plot, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        var start = all[0].Time;
        var dataSpan = Math.Max((all[^1].Time - start).TotalSeconds, 1e-3);
        var (fromSec, toSec) = Window?.Resolve(dataSpan) ?? (0, dataSpan);
        var showsRecent = RecentSeconds > 0 && Window?.IsZoomed != true && dataSpan > RecentSeconds;
        if (showsRecent) (fromSec, toSec) = (dataSpan - RecentSeconds, dataSpan);
        var t0 = start.AddSeconds(fromSec);
        var span = Math.Max(toSec - fromSec, 1e-3);

        // The visible readings, plus one each side so the line reaches the edges.
        var lo0 = all.FindIndex(s => (s.Time - start).TotalSeconds >= fromSec);
        var hi0 = all.FindLastIndex(s => (s.Time - start).TotalSeconds <= toSec);
        if (lo0 < 0 || hi0 < 0) return;
        lo0 = Math.Max(0, lo0 - 1);
        hi0 = Math.Min(all.Count - 1, hi0 + 1);
        var withValues = all.GetRange(lo0, hi0 - lo0 + 1);

        var unit = withValues[^1].Unit;
        var min = withValues.Min(s => s.Value!.Value);
        var max = withValues.Max(s => s.Value!.Value);
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

        if (withValues.Count <= columns * 2)
        {
            // Few enough readings to draw each one.
            var path = new PathF();
            DateTimeOffset? last = null;
            foreach (var s in withValues)
            {
                var p = new PointF(X(s), Y(s.Value!.Value));
                if (last is null || s.Time - last.Value > gap) path.MoveTo(p);
                else path.LineTo(p);
                last = s.Time;
            }
            canvas.DrawPath(path);
        }
        else
        {
            // Long logs: about one bucket per pixel column. Each bucket's min to max is a faint
            // band (so spikes and noise still show) and its average is the line, so a noisy
            // reading doesn't turn into a solid block.
            var buckets = TrendBucket.Group(withValues, t0, TimeSpan.FromSeconds(span / columns));
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
