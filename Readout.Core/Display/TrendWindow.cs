namespace Readout.Core.Display;

/// <summary>
/// Which stretch of time the trend graph shows, and how pinch and drag change it. Times are
/// seconds since the first sample. Unzoomed it shows everything; zoomed in at the newest end
/// it keeps following new readings; zoomed elsewhere it stays put.
/// </summary>
public sealed class TrendWindow
{
    /// <summary>The narrowest window a pinch can reach.</summary>
    public const double MinSpan = 1.0;

    private double? _span;
    private double _from;

    public bool IsZoomed => _span is not null;

    /// <summary>True when the window's right edge sticks to the newest reading.</summary>
    public bool FollowsLatest { get; private set; } = true;

    /// <summary>The visible range for data spanning 0 to <paramref name="dataSpan"/> seconds.</summary>
    public (double From, double To) Resolve(double dataSpan)
    {
        if (_span is not { } span || span >= dataSpan)
            return (0, dataSpan);

        var from = FollowsLatest ? dataSpan - span : Math.Clamp(_from, 0, dataSpan - span);
        return (from, from + span);
    }

    /// <summary>Zooms by <paramref name="scale"/> (above 1 zooms in) around a point of the window.</summary>
    /// <param name="anchor">Where the pinch is, across the window: 0 left edge, 1 right edge.</param>
    public void Zoom(double scale, double anchor, double dataSpan)
    {
        if (scale <= 0 || dataSpan <= 0) return;

        var (from, to) = Resolve(dataSpan);
        var span = to - from;
        var newSpan = Math.Clamp(span / scale, Math.Min(MinSpan, dataSpan), dataSpan);
        if (newSpan >= dataSpan)
        {
            Reset();
            return;
        }

        anchor = Math.Clamp(anchor, 0, 1);
        var newFrom = Math.Clamp(from + anchor * span - anchor * newSpan, 0, dataSpan - newSpan);
        Set(newFrom, newSpan, dataSpan);
    }

    /// <summary>Moves the window; positive <paramref name="fraction"/> (a drag to the right) shows earlier readings.</summary>
    /// <param name="fraction">How far, as a fraction of the window's width.</param>
    public void Pan(double fraction, double dataSpan)
    {
        if (_span is null) return;

        var (from, to) = Resolve(dataSpan);
        var span = to - from;
        Set(Math.Clamp(from - fraction * span, 0, dataSpan - span), span, dataSpan);
    }

    public void Reset()
    {
        _span = null;
        _from = 0;
        FollowsLatest = true;
    }

    private void Set(double from, double span, double dataSpan)
    {
        _span = span;
        _from = from;
        FollowsLatest = from + span >= dataSpan - 1e-6;
    }
}
