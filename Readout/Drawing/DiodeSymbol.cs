namespace Readout.Drawing;

/// <summary>The schematic diode symbol: a lead, a filled triangle pointing at a bar, a lead.</summary>
public static class DiodeSymbol
{
    public static void Draw(ICanvas canvas, RectF rect, Color color)
    {
        var mid = rect.Center.Y;
        var half = rect.Height / 2;
        var anode = rect.X + rect.Width * 0.22f;
        var cathode = rect.X + rect.Width * 0.72f;

        canvas.StrokeColor = color;
        canvas.FillColor = color;
        canvas.StrokeSize = Math.Max(1, rect.Height * 0.14f);
        canvas.StrokeLineCap = LineCap.Butt;

        canvas.DrawLine(rect.X, mid, anode, mid);
        canvas.DrawLine(cathode, mid, rect.Right, mid);
        canvas.DrawLine(cathode, mid - half, cathode, mid + half);

        var triangle = new PathF();
        triangle.MoveTo(anode, mid - half);
        triangle.LineTo(cathode, mid);
        triangle.LineTo(anode, mid + half);
        triangle.Close();
        canvas.FillPath(triangle);
    }
}

/// <summary>A diode symbol as a control, for text-sized labels.</summary>
public sealed class DiodeView : GraphicsView, IDrawable
{
    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(DiodeView), Colors.Gray,
            propertyChanged: (view, _, _) => ((DiodeView)view).Invalidate());

    public DiodeView()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect) =>
        DiodeSymbol.Draw(canvas, new RectF(1, 1, (float)Width - 2, (float)Height - 2), Color);
}
