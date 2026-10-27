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
    private const float Height = 220;

    /// <summary>Width over height, for sizing the view so the drawing fills it.</summary>
    public const float AspectRatio = Width / Height;

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
        DrawStatusRow(canvas, r, icons);
        DrawLeftColumn(canvas, icons);
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

    /// <summary>A symbol printed light-on-dark in a box, as AUTO, MEM, A- and HOLD are on the meter.</summary>
    private static void Boxed(ICanvas canvas, string text, float x, float y, float width, bool lit)
    {
        const float height = 13;
        canvas.FillColor = lit ? Ink : Ghost;
        canvas.FillRoundedRectangle(x, y, width, height, 4);
        canvas.FontColor = lit ? Glass : Ghost;
        canvas.FontSize = 10;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(text, x, y, width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // The layout follows the meter's LCD: symbols top left, the sub display top right, a row
    // of boxed symbols above the main digits, a column of symbols left of them, units right,
    // and the chevron bar graph with its scale along the bottom.
    private static void DrawTopRow(ICanvas canvas, Annunciators i)
    {
        // MIN MAX AVG LowZ: separate symbols, so "all three" lights all three.
        Label(canvas, "MIN", 18, 18, 10, i.Min, HorizontalAlignment.Left, 26);
        Label(canvas, "MAX", 44, 18, 10, i.Max, HorizontalAlignment.Left, 28);
        Label(canvas, "AVG", 72, 18, 10, i.Average, HorizontalAlignment.Left, 28);
        Label(canvas, "LowZ", 100, 18, 10, i.LowZ, HorizontalAlignment.Left, 34);

        // Diode, beep, the 1 kHz low-pass filter, Bluetooth.
        DiodeSymbol.Draw(canvas, new RectF(18, 38, 16, 9), i.Diode ? Ink : Ghost);
        Label(canvas, "·))", 38, 36, 10, i.Beep, HorizontalAlignment.Left, 20);
        LowPassSymbol(canvas, 60, 35, i.LowPassFilter ? Ink : Ghost);
        Label(canvas, "BT", 96, 34, 13, i.Bluetooth, HorizontalAlignment.Left, 24);

        // AC over a bar over DC, beside the sub display.
        Label(canvas, "AC", 156, 17, 9, i.Ac, HorizontalAlignment.Center, 22);
        canvas.FillColor = i.Ac || i.Dc ? Ink : Ghost;
        canvas.FillRectangle(160, 31, 14, 2);
        Label(canvas, "DC", 156, 35, 9, i.Dc, HorizontalAlignment.Center, 22);
    }

    /// <summary>"1kHz" under a line that drops at the end: the low-pass filter symbol.</summary>
    private static void LowPassSymbol(ICanvas canvas, float x, float y, Color color)
    {
        canvas.FontColor = color;
        canvas.FontSize = 9;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString("1kHz", x, y + 3, 30, 12, HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.StrokeColor = color;
        canvas.StrokeSize = 1.4f;
        canvas.DrawLine(x + 1, y + 1, x + 24, y + 1);
        canvas.DrawLine(x + 24, y + 1, x + 30, y + 14);
    }

    private static void DrawStatusRow(ICanvas canvas, MeterReading? r, Annunciators i)
    {
        const float y = 57;
        Boxed(canvas, "AUTO", 18, y, 34, i.Auto);
        // DC+AC: each part lights on its own, as on the meter.
        Label(canvas, "DC", 56, y - 1, 10, i.AcPlusDc, HorizontalAlignment.Left, 18);
        Label(canvas, "+", 72, y - 1, 10, i.AcPlusDc, HorizontalAlignment.Left, 10);
        Label(canvas, "AC", 81, y - 1, 10, i.AcPlusDc, HorizontalAlignment.Left, 18);

        // What a SETUP page is about ("Battery"), which the meter only hints at in the digits.
        if (r?.Sub.Label is { } label)
            Label(canvas, label.ToUpperInvariant(), 104, y - 1, 9, true, HorizontalAlignment.Right, 120);

        Boxed(canvas, "MEM", 230, y, 30, i.Memory);
        Boxed(canvas, "A-", 264, y, 18, i.AutoHold);
        Boxed(canvas, "HOLD", 284, y, 34, i.Hold);
        Label(canvas, "1ms", 322, y - 1, 11, i.OneMsPeak, HorizontalAlignment.Left, 30);
    }

    private static void DrawLeftColumn(ICanvas canvas, Annunciators i)
    {
        // Battery: a filled cell with + and -.
        var lit = i.LowBattery ? Ink : Ghost;
        canvas.FillColor = lit;
        canvas.FillRoundedRectangle(18, 78, 24, 12, 2);
        canvas.FillRectangle(42, 81, 3, 6);
        canvas.FontColor = i.LowBattery ? Glass : Ghost;
        canvas.FontSize = 10;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString("+ -", 18, 78, 24, 12, HorizontalAlignment.Center, VerticalAlignment.Center);

        Label(canvas, "Apo", 16, 94, 11, i.AutoPowerOff, HorizontalAlignment.Left, 34);
        Label(canvas, "REL", 16, 110, 10, i.Relative, HorizontalAlignment.Left, 34);
        Label(canvas, "TEST", 16, 124, 9, i.Test, HorizontalAlignment.Left, 34);

        var arrow = new PathF();
        arrow.MoveTo(22, 154);
        arrow.LineTo(28, 144);
        arrow.LineTo(34, 154);
        arrow.Close();
        canvas.FillColor = i.Arrow ? Ink : Ghost;
        canvas.FillPath(arrow);
    }

    private static void DrawSub(ICanvas canvas, MeterReading? r)
    {
        var sub = r?.Sub;
        var area = new RectF(196, 16, 132, 34);

        canvas.FillColor = sub is not null && sub.Negative ? Ink : Ghost;
        canvas.FillRectangle(area.X - 12, area.Y + area.Height / 2 - 1.5f, 9, 3);

        SevenSegment.DrawText(canvas, sub?.Text ?? "", area, 5, Ink, Ghost);
        Label(canvas, sub?.Unit ?? "", area.Right + 4, area.Y + 12, 13, true, HorizontalAlignment.Left, 64);
    }

    private static void DrawMain(ICanvas canvas, MeterReading? r, Annunciators i)
    {
        // Minus sign and digits.
        var digits = new RectF(78, 78, 250, 80);
        canvas.FillColor = r is not null && r.MainNegative && !r.MainOverload ? Ink : Ghost;
        canvas.FillRoundedRectangle(56, digits.Y + digits.Height / 2 - 4, 20, 8, 2);

        var text = r?.MainText ?? "";
        SevenSegment.DrawText(canvas, text.Length == 0 ? "" : text, digits, 5, Ink, Ghost);

        Label(canvas, r?.MainUnit ?? "", 334, 120, 24, true, HorizontalAlignment.Left, 80);
        if (i.Decibels)
            Label(canvas, "dBm", 334, 92, 14, true);
    }

    private static void DrawBar(ICanvas canvas, BarGraph? bar)
    {
        const int total = BarGraph.TotalSegments;
        const float left = 50f, top = 188f, width = 340f, height = 14f;
        var segment = width / total;
        var lit = bar?.Segments ?? 0;

        // The sign at the bar's left: "-" when negative, "+" otherwise.
        canvas.FillColor = bar is { On: true } ? Ink : Ghost;
        canvas.FillRectangle(24, top + 5, 14, 4);
        canvas.FillColor = bar is { On: true, Negative: false } ? Ink : Ghost;
        canvas.FillRectangle(29, top, 4, 14);

        // Chevrons, as on the meter.
        for (var s = 0; s < total; s++)
        {
            var x = left + s * segment;
            var w = segment - 1.5f;
            var notch = w * 0.4f;
            var chevron = new PathF();
            chevron.MoveTo(x, top);
            chevron.LineTo(x + w - notch, top);
            chevron.LineTo(x + w, top + height / 2);
            chevron.LineTo(x + w - notch, top + height);
            chevron.LineTo(x, top + height);
            if (s > 0) chevron.LineTo(x + notch, top + height / 2);
            chevron.Close();
            canvas.FillColor = s < lit ? Ink : Ghost;
            canvas.FillPath(chevron);
        }

        // Scale above the bar: six labels, 0 to full scale.
        if (bar is { On: true })
        {
            var full = bar.FullScale;
            for (var k = 0; k <= 5; k++)
            {
                var x = left + width * k / 5f;
                canvas.FillColor = Ink;
                canvas.FillRectangle(x - 0.5f, top - 6, 1, 4);
                Label(canvas, (full * k / 5).ToString(System.Globalization.CultureInfo.InvariantCulture), x - 20, top - 19, 9, true, HorizontalAlignment.Center, 40);
            }
        }
    }
}
