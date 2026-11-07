using Readout.Core.Display;

namespace Readout.Drawing;

/// <summary>
/// The LCD of an FS9721 meter, laid out like the Miyako 890G's glass: symbols along the top,
/// AC and DC left of four digits, and a grid of units on the right.
/// </summary>
public sealed partial class LcdDrawable
{
    private const float Fs9721Height = 170;

    /// <summary>The unit grid, row by row, with each symbol's x position.</summary>
    private static readonly (string Symbol, float X)[][] Fs9721Units =
    [
        [("hFE", 304), ("°F", 368)],
        [("n", 304), ("%", 330), ("°C", 368)],
        [("µ", 304), ("m", 322), ("A", 346), ("F", 368), ("V", 388)],
        [("k", 304), ("M", 322), ("Ω", 346), ("Hz", 368)],
    ];

    private static void DrawFs9721(ICanvas canvas, MeterReading? r, Annunciators i)
    {
        // Along the top. RS232 is lit whenever the meter is sending, which it is if there's a reading.
        const float top = 16;
        Label(canvas, "AUTO", 18, top, 12, i.Auto, HorizontalAlignment.Left, 40);
        Label(canvas, "RS232", 64, top, 12, r is not null, HorizontalAlignment.Left, 46);
        DiodeSymbol.Draw(canvas, new RectF(122, top + 4, 18, 10), i.Diode ? Ink : Ghost);
        Label(canvas, "·))", 150, top, 12, i.Beep, HorizontalAlignment.Left, 24);
        Label(canvas, "HOLD", 196, top, 12, i.Hold, HorizontalAlignment.Left, 42);
        Label(canvas, "Δ", 248, top, 14, i.Relative, HorizontalAlignment.Left, 16);
        Battery(canvas, 274, top + 2, i.LowBattery);

        // AC over the minus sign over DC, left of the digits.
        Label(canvas, "AC", 16, 52, 12, i.Ac, HorizontalAlignment.Left, 26);
        canvas.FillColor = r is not null && r.MainNegative && !r.MainOverload ? Ink : Ghost;
        canvas.FillRoundedRectangle(18, 92, 20, 7, 2);
        Label(canvas, "DC", 16, 124, 12, i.Dc, HorizontalAlignment.Left, 26);

        SevenSegment.DrawText(canvas, r?.MainText ?? "", new RectF(48, 46, 240, 100), 4, Ink, Ghost);

        var (prefix, unit) = SplitUnit(r?.MainUnit ?? "");
        for (var row = 0; row < Fs9721Units.Length; row++)
        {
            foreach (var (symbol, x) in Fs9721Units[row])
            {
                var lit = symbol == unit || symbol == prefix;
                Label(canvas, symbol, x, 46 + row * 26, 15, lit, HorizontalAlignment.Left, 36);
            }
        }
    }

    /// <summary>"mV" into ("m", "V"); units without a prefix ("Hz", "°C", "hFE") whole.</summary>
    private static (string Prefix, string Unit) SplitUnit(string unit) =>
        unit.Length > 1 && "nµmkM".Contains(unit[0], StringComparison.Ordinal) && unit is not ("hFE" or "Hz")
            ? (unit[..1], unit[1..])
            : ("", unit);
}
