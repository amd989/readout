using Readout.Core.Connection;

namespace Readout.Drawing;

/// <summary>Signal strength as rising bars.</summary>
public sealed class SignalBarsView : GraphicsView, IDrawable
{
    public static readonly BindableProperty BarsProperty =
        BindableProperty.Create(nameof(Bars), typeof(int), typeof(SignalBarsView), 0,
            propertyChanged: (view, _, _) => ((SignalBarsView)view).Invalidate());

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(SignalBarsView), Colors.Gray,
            propertyChanged: (view, _, _) => ((SignalBarsView)view).Invalidate());

    public SignalBarsView()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
    }

    public int Bars
    {
        get => (int)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
    {
        const int count = SignalQuality.MaxBars;
        var gap = (float)Width * 0.12f;
        var barWidth = ((float)Width - gap * (count - 1)) / count;
        var height = (float)Height;
        for (var i = 0; i < count; i++)
        {
            var barHeight = height * (i + 1) / count;
            canvas.FillColor = i < Bars ? Color : Color.WithAlpha(0.25f);
            canvas.FillRoundedRectangle(i * (barWidth + gap), height - barHeight, barWidth, barHeight, 1);
        }
    }
}
