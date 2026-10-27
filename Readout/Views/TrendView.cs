using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Drawing;

namespace Readout.Views;

/// <summary>
/// The trend graph as a control. With <see cref="Interactive"/> on: pinch to zoom, drag to
/// pan, double-tap to show everything again.
/// </summary>
public sealed class TrendView : GraphicsView
{
    private readonly TrendDrawable _drawable = new();
    private readonly TrendWindow _window = new();
    private bool _interactive;
    private double _lastPanX;

    public TrendView()
    {
        _drawable.Window = _window;
        Drawable = _drawable;
        ApplyTheme();
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => { ApplyTheme(); Invalidate(); };
    }

    public IReadOnlyList<Sample> Samples
    {
        get => _drawable.Samples;
        set
        {
            _drawable.Samples = value;
            Invalidate();
        }
    }

    /// <summary>Above zero: show only the newest this-many seconds (see <see cref="TrendDrawable.RecentSeconds"/>).</summary>
    public double RecentSeconds
    {
        get => _drawable.RecentSeconds;
        set
        {
            _drawable.RecentSeconds = value;
            Invalidate();
        }
    }

    public bool Interactive
    {
        get => _interactive;
        set
        {
            if (_interactive == value || !value) return;
            _interactive = true;

            var pinch = new PinchGestureRecognizer();
            pinch.PinchUpdated += OnPinch;
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += OnPan;
            var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleTap.Tapped += (_, _) => { _window.Reset(); Invalidate(); };

            GestureRecognizers.Add(pinch);
            GestureRecognizers.Add(pan);
            GestureRecognizers.Add(doubleTap);
        }
    }

    /// <summary>Back to showing everything (a new recording, a different session).</summary>
    public void ResetZoom()
    {
        _window.Reset();
        Invalidate();
    }

    private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running) return;
        _window.Zoom(e.Scale, e.ScaleOrigin.X, TrendDrawable.DataSpan(Samples));
        Invalidate();
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanX = 0;
                break;
            case GestureStatus.Running when Width > 0:
                _window.Pan((e.TotalX - _lastPanX) / Width, TrendDrawable.DataSpan(Samples));
                _lastPanX = e.TotalX;
                Invalidate();
                break;
        }
    }

    private void ApplyTheme()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _drawable.TextColor = dark ? Color.FromArgb("#A9B0B5") : Color.FromArgb("#5C6368");
        _drawable.GridColor = dark ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#22000000");
    }
}
