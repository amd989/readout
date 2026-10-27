using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Logging;

namespace Readout.Drawing;

/// <summary>
/// A line graph of the current run of readings: value over time, scaled to fit, with the
/// range and time span as labels.
/// </summary>
public sealed class TrendDrawable : IDrawable
{
    public IReadOnlyList<Sample> Samples { get; set; } = [];

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
        var withValues = samples.Where(s => s.Value is not null).ToList();
        if (withValues.Count < 2)
        {
            canvas.DrawString(samples.Count == 0 ? "No readings yet" : "Waiting for readings…",
                plot, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

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

        var t0 = withValues[0].Time;
        var span = Math.Max((withValues[^1].Time - t0).TotalSeconds, 1e-3);

        float X(Sample s) => plot.X + (float)((s.Time - t0).TotalSeconds / span) * plot.Width;
        float Y(double v) => plot.Bottom - (float)((v - min) / (max - min)) * plot.Height;

        // Long logs: split into buckets of about one pixel column and draw each bucket's min
        // and max, in time order, so spikes survive and drawing stays fast.
        var path = new PathF();
        var columns = Math.Max(1, (int)plot.Width);
        var step = Math.Max(1, withValues.Count / columns);
        // A pause in the readings (disconnected, logging paused, overload) leaves a gap rather
        // than a straight line across it. Long logs draw buckets, so allow a few buckets apart.
        var gap = TimeSpan.FromSeconds(Math.Max(2, span / columns * 3));
        DateTimeOffset? last = null;
        void Add(Sample s)
        {
            var p = new PointF(X(s), Y(s.Value!.Value));
            if (last is null || s.Time - last.Value > gap) path.MoveTo(p);
            else path.LineTo(p);
            last = s.Time;
        }

        for (var i = 0; i < withValues.Count; i += step)
        {
            var end = Math.Min(withValues.Count, i + step);
            int lo = i, hi = i;
            for (var j = i + 1; j < end; j++)
            {
                if (withValues[j].Value < withValues[lo].Value) lo = j;
                if (withValues[j].Value > withValues[hi].Value) hi = j;
            }
            Add(withValues[Math.Min(lo, hi)]);
            if (lo != hi) Add(withValues[Math.Max(lo, hi)]);
        }

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 2;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);

        canvas.DrawString(SiFormat.Format(max, unit, provider: CultureInfo.CurrentCulture), plot.X, rect.Y + 2, plot.Width, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString(SiFormat.Format(min, unit, provider: CultureInfo.CurrentCulture), plot.X, plot.Bottom + 2, plot.Width / 2, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString(FormatSpan(span), plot.X + plot.Width / 2, plot.Bottom + 2, plot.Width / 2, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
    }

    private static string FormatSpan(double seconds) => seconds switch
    {
        < 120 => $"{seconds:0} s",
        < 7200 => $"{seconds / 60:0.#} min",
        _ => $"{seconds / 3600:0.#} h",
    };
}
