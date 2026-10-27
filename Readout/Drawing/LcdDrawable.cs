using Readout.Core.Display;

namespace Readout.Drawing;

/// <summary>
/// A drawing of the 121GW's LCD: main and sub digits, units, the bar graph and the symbols
/// around them. Colors follow the official app's palette.
/// </summary>
public sealed class LcdDrawable : IDrawable
{
    // Virtual canvas; scaled to fit the view, keeping its proportions.
    private const float Width = 420;
    private const float Height = 210;

    private static readonly Color Glass = Color.FromArgb("#C5CCB9");
    private static readonly Color Ink = Color.FromArgb("#111111");
    private static readonly Color Ghost = Color.FromArgb("#111111").WithAlpha(0.07f);
    private static readonly Color Bezel = Color.FromArgb("#292F33");

    public MeterReading? Reading { get; set; }

    /// <summary>Shown in the main digits when there's no reading ("----").</summary>
    public string IdleText { get; set; } = "-----";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var scale = Math.Min(dirtyRect.Width / Width, dirtyRect.Height / Height);
        var offsetX = dirtyRect.X + (dirtyRect.Width - Width * scale) / 2;
        var offsetY = dirtyRect.Y + (dirtyRect.Height - Height * scale) / 2;

        canvas.SaveState();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);

        canvas.FillColor = Bezel;
        canvas.FillRoundedRectangle(0, 0, Width, Height, 14);
        canvas.FillColor = Glass;
        canvas.FillRoundedRectangle(8, 8, Width - 16, Height - 16, 9);

        var r = Reading;
        var icons = r?.Icons ?? new Annunciators();

        DrawTopRow(canvas, icons);
        DrawSub(canvas, r);
        DrawMain(canvas, r, icons);
        DrawBar(canvas, r?.Bar);

        canvas.RestoreState();
    }

    private static void Label(ICanvas canvas, string text, float x, float y, float size, bool lit, HorizontalAlignment align = HorizontalAlignment.Left, float width = 60)
    {
        canvas.FontColor = lit ? Ink : Ghost;
        canvas.FontSize = size;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(text, x, y, width, size * 1.3f, align, VerticalAlignment.Center);
    }

    private static void DrawTopRow(ICanvas canvas, Annunciators i)
    {
        var x = 18f;
        void Item(string text, bool lit, float width)
        {
            Label(canvas, text, x, 14, 10, lit, HorizontalAlignment.Left, width);
            x += width;
        }

        Item("AUTO", i.Auto, 34);
        Item("APO", i.AutoPowerOff, 28);
        Item("BAT", i.LowBattery, 28);
        Item("BT", i.Bluetooth, 20);
        Item("TEST", i.Test, 34);
        Item("MEM", i.Memory, 30);
        Item(i.AutoHold ? "A-HOLD" : "HOLD", i.Hold, 44);
    }

    private static void DrawSub(ICanvas canvas, MeterReading? r)
    {
        var sub = r?.Sub;
        var area = new RectF(270, 30, 104, 34);

        if (sub is not null && sub.Negative)
            Label(canvas, "-", area.X - 14, area.Y + 8, 16, true);

        SevenSegment.DrawText(canvas, sub?.Text ?? "", area, 5, Ink, Ghost);
        Label(canvas, sub?.Unit ?? "", area.Right + 2, area.Y + 10, 12, true, HorizontalAlignment.Left, 40);

        if (sub?.Label is { } label)
            Label(canvas, label.ToUpperInvariant(), 150, area.Y + 10, 10, true, HorizontalAlignment.Right, 112);
    }

    private static void DrawMain(ICanvas canvas, MeterReading? r, Annunciators i)
    {
        // Left column: coupling and mode symbols.
        var couplingY = 78f;
        Label(canvas, i.AcPlusDc ? "AC+DC" : i.Ac ? "AC" : "DC", 16, couplingY, 13, i.Ac || i.Dc);
        Label(canvas, "LoZ", 16, couplingY + 20, 11, i.LowZ);
        Label(canvas, "REL", 16, couplingY + 36, 11, i.Relative);
        Label(canvas, "1ms", 16, couplingY + 52, 10, i.OneMsPeak);
        Label(canvas, "1kHz", 16, couplingY + 66, 10, i.LowPassFilter);

        var minMax = i.Max && i.Min ? "MAX MIN" : i.Max ? "MAX" : i.Min ? "MIN" : i.Average ? "AVG" : "MAX";
        Label(canvas, minMax, 150, 50, 11, i.Max || i.Min || i.Average, HorizontalAlignment.Left, 80);
        Label(canvas, "·))", 218, 50, 11, i.Beep);
        DiodeSymbol.Draw(canvas, new RectF(240, 52, 18, 10), i.Diode ? Ink : Ghost);

        // Minus sign and digits.
        var digits = new RectF(78, 68, 250, 82);
        canvas.FillColor = r is not null && r.MainNegative && !r.MainOverload ? Ink : Ghost;
        canvas.FillRoundedRectangle(56, digits.Y + digits.Height / 2 - 4, 20, 8, 2);

        var text = r?.MainText ?? "";
        SevenSegment.DrawText(canvas, text.Length == 0 ? "" : text, digits, 5, Ink, Ghost);

        Label(canvas, r?.MainUnit ?? "", 334, 112, 24, true, HorizontalAlignment.Left, 80);
        if (i.Decibels)
            Label(canvas, "dBm", 334, 86, 14, true);
    }

    private static void DrawBar(ICanvas canvas, BarGraph? bar)
    {
        const int total = BarGraph.TotalSegments;
        var left = 50f;
        var top = 162f;
        var width = 330f;
        var segment = width / total;
        var lit = bar?.Segments ?? 0;

        // The sign at the bar's left: "-" when negative, "+" otherwise.
        canvas.FillColor = bar is { On: true } ? Ink : Ghost;
        canvas.FillRectangle(24, top + 6, 14, 4);
        canvas.FillColor = bar is { On: true, Negative: false } ? Ink : Ghost;
        canvas.FillRectangle(29, top + 1, 4, 14);

        for (var s = 0; s < total; s++)
        {
            canvas.FillColor = s < lit ? Ink : Ghost;
            canvas.FillRectangle(left + s * segment + 1, top, segment - 3, 16);
        }

        // Scale: six labels across the bar, 0 to full scale.
        if (bar is { On: true })
        {
            var full = bar.FullScale;
            for (var k = 0; k <= 5; k++)
            {
                var x = left + width * k / 5f;
                canvas.FillColor = Ink;
                canvas.FillRectangle(x - 0.5f, top + 18, 1, 4);
                Label(canvas, (full * k / 5).ToString(System.Globalization.CultureInfo.InvariantCulture), x - 20, top + 22, 9, true, HorizontalAlignment.Center, 40);
            }
        }
    }
}
