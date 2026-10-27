namespace Readout.Drawing;

/// <summary>Draws seven-segment characters, slanted like the meter's LCD.</summary>
internal static class SevenSegment
{
    // Bits: a=1 (top), b=2 (top right), c=4 (bottom right), d=8 (bottom), e=16 (bottom left),
    // f=32 (top left), g=64 (middle).
    private static readonly Dictionary<char, int> Glyphs = new()
    {
        ['0'] = 0x3F, ['1'] = 0x06, ['2'] = 0x5B, ['3'] = 0x4F, ['4'] = 0x66,
        ['5'] = 0x6D, ['6'] = 0x7D, ['7'] = 0x07, ['8'] = 0x7F, ['9'] = 0x6F,
        ['-'] = 0x40, [' '] = 0x00, ['_'] = 0x08,
        ['A'] = 0x77, ['b'] = 0x7C, ['C'] = 0x39, ['c'] = 0x58, ['d'] = 0x5E,
        ['E'] = 0x79, ['F'] = 0x71, ['H'] = 0x76, ['h'] = 0x74, ['L'] = 0x38,
        ['n'] = 0x54, ['O'] = 0x3F, ['o'] = 0x5C, ['P'] = 0x73, ['r'] = 0x50,
        ['t'] = 0x78, ['U'] = 0x3E, ['u'] = 0x1C, ['y'] = 0x6E,
    };

    private const float Slant = 0.08f;

    /// <summary>
    /// Splits LCD text into cells: each character plus whether the decimal point after it is lit.
    /// A point never takes a cell of its own.
    /// </summary>
    public static List<(char Glyph, bool Point)> Cells(string text)
    {
        var cells = new List<(char, bool)>();
        foreach (var ch in text)
        {
            if (ch == '.' && cells.Count > 0)
                cells[^1] = (cells[^1].Item1, true);
            else if (ch == '.')
                cells.Add((' ', true));
            else
                cells.Add((ch, false));
        }
        return cells;
    }

    /// <summary>
    /// Draws <paramref name="text"/> right-aligned into <paramref name="count"/> digit cells.
    /// Unlit segments are drawn in <paramref name="off"/> so the display reads like a real LCD.
    /// </summary>
    public static void DrawText(ICanvas canvas, string text, RectF area, int count, Color on, Color off)
    {
        var cells = Cells(text);
        while (cells.Count < count)
            cells.Insert(0, (' ', false));
        if (cells.Count > count)
            cells = cells[^count..];

        var cellWidth = area.Width / count;
        for (var i = 0; i < count; i++)
        {
            var (glyph, point) = cells[i];
            var cell = new RectF(area.X + i * cellWidth, area.Y, cellWidth, area.Height);
            DrawDigit(canvas, glyph, point, cell, on, off);
        }
    }

    private static void DrawDigit(ICanvas canvas, char glyph, bool point, RectF cell, Color on, Color off)
    {
        var bits = Glyphs.GetValueOrDefault(glyph, 0);

        // The digit inside its cell, leaving room for the point at the bottom right.
        var w = cell.Width * 0.68f;
        var h = cell.Height;
        var x = cell.X + cell.Width * 0.08f;
        var y = cell.Y;
        var t = Math.Max(1.5f, w * 0.17f);   // segment thickness
        var gap = t * 0.18f;
        var mid = y + h / 2;

        void Segment(int bit, float x1, float y1, float x2, float y2, bool horizontal)
        {
            canvas.FillColor = (bits & bit) != 0 ? on : off;
            var path = new PathF();
            if (horizontal)
            {
                var half = t / 2;
                path.MoveTo(Skew(x1 + gap, y1, y, h));
                path.LineTo(Skew(x1 + gap + half, y1 - half, y, h));
                path.LineTo(Skew(x2 - gap - half, y2 - half, y, h));
                path.LineTo(Skew(x2 - gap, y2, y, h));
                path.LineTo(Skew(x2 - gap - half, y2 + half, y, h));
                path.LineTo(Skew(x1 + gap + half, y1 + half, y, h));
            }
            else
            {
                var half = t / 2;
                path.MoveTo(Skew(x1, y1 + gap, y, h));
                path.LineTo(Skew(x1 + half, y1 + gap + half, y, h));
                path.LineTo(Skew(x2 + half, y2 - gap - half, y, h));
                path.LineTo(Skew(x2, y2 - gap, y, h));
                path.LineTo(Skew(x2 - half, y2 - gap - half, y, h));
                path.LineTo(Skew(x1 - half, y1 + gap + half, y, h));
            }
            path.Close();
            canvas.FillPath(path);
        }

        var left = x + t / 2;
        var right = x + w - t / 2;
        var top = y + t / 2;
        var bottom = y + h - t / 2;

        Segment(0x01, left, top, right, top, true);          // a
        Segment(0x02, right, top, right, mid, false);        // b
        Segment(0x04, right, mid, right, bottom, false);     // c
        Segment(0x08, left, bottom, right, bottom, true);    // d
        Segment(0x10, left, mid, left, bottom, false);       // e
        Segment(0x20, left, top, left, mid, false);          // f
        Segment(0x40, left, mid, right, mid, true);          // g

        // Decimal point
        canvas.FillColor = point ? on : off;
        var dot = t * 1.1f;
        var dp = Skew(x + w + cell.Width * 0.07f, bottom, y, h);
        canvas.FillEllipse(dp.X - dot / 2, dp.Y - dot / 2, dot, dot);
    }

    // Leans the glyph to the right: points higher up move further right.
    private static PointF Skew(float px, float py, float top, float height) =>
        new(px + (top + height - py) * Slant, py);
}
