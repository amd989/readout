using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Fs9721;

/// <summary>
/// Decodes FS9721 frames into what the LCD shows. Each frame is the LCD's segment map, so this
/// reads the digits back from their segments and the mode from the unit symbols.
/// </summary>
/// <remarks>
/// Byte layout (bit 3 to bit 0): byte 0 AC, DC, AUTO, RS232; bytes 1–8 four digits, two bytes
/// each, with bit 3 of the first byte the minus sign (digit 1) or the decimal point before the
/// digit (digits 2–4); byte 9 µ, n, k, diode; byte 10 m, %, M, beep; byte 11 F, Ω, REL, HOLD;
/// byte 12 A, V, Hz, low battery; byte 13 four symbols each meter maker chooses.
/// </remarks>
public static class Fs9721Decoder
{
    /// <summary>
    /// The four maker-chosen symbols in byte 13, bit 0 first, as on the Miyako 890G: °F, hFE, °C,
    /// and a fourth position that its LCD doesn't print. Other FS9721 meters use these bits for
    /// other symbols (MAX and MIN on some), so they're a setting per meter model.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultUserSymbols = ["°F", "hFE", "°C", ""];

    /// <param name="userSymbols">What the meter's byte-13 symbols mean, bit 0 first; see <see cref="DefaultUserSymbols"/>.</param>
    public static MeterReading Decode(Fs9721Frame frame, DateTimeOffset receivedAt, IReadOnlyList<string>? userSymbols = null)
    {
        userSymbols ??= DefaultUserSymbols;
        var symbol = UserSymbol(frame, userSymbols);

        var (text, overload) = Digits(frame);
        var negative = frame.Bit(1, 3);
        var (prefix, multiplier) = Prefix(frame);
        var mode = Mode(frame, symbol, prefix);

        var baseUnit = mode switch
        {
            MeterMode.DcVolts or MeterMode.AcVolts or MeterMode.DcMillivolts or MeterMode.AcMillivolts
                or MeterMode.Diode => "V",
            MeterMode.DcMicroamps or MeterMode.AcMicroamps or MeterMode.DcMilliamps or MeterMode.AcMilliamps
                or MeterMode.DcAmps or MeterMode.AcAmps => "A",
            MeterMode.Resistance or MeterMode.Continuity => "Ω",
            MeterMode.Capacitance => "F",
            MeterMode.Frequency => "Hz",
            MeterMode.Duty => "%",
            MeterMode.TemperatureC or MeterMode.TemperatureF => symbol,
            _ => "",
        };
        // hFE, temperature and duty never take a prefix; the others show the one lit next to the unit.
        var plain = mode is MeterMode.Hfe or MeterMode.TemperatureC or MeterMode.TemperatureF or MeterMode.Duty;
        var unit = mode == MeterMode.Hfe ? "hFE" : plain ? baseUnit : prefix + baseUnit;
        if (plain)
            multiplier = 1;

        double? value = null;
        if (!overload && double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var magnitude))
            value = negative ? -magnitude : magnitude;

        return new MeterReading
        {
            Model = MeterModel.Fs9721,
            Raw = frame.Bytes.ToArray(),
            ReceivedAt = receivedAt,
            Mode = mode,
            ModeName = ReadingDecoder.ModeName(mode),
            MainText = overload ? "OL" : text,
            MainNegative = negative,
            MainOverload = overload,
            MainUnit = unit,
            Value = value,
            BaseValue = value * multiplier,
            BaseUnit = baseUnit,
            Sub = SubDisplay.None,
            Bar = BarGraph.Off,
            Icons = new Annunciators
            {
                Ac = frame.Bit(0, 3),
                Dc = frame.Bit(0, 2),
                Auto = frame.Bit(0, 1),
                Hold = frame.Bit(11, 0),
                Relative = frame.Bit(11, 1),
                LowBattery = frame.Bit(12, 0),
                Beep = frame.Bit(10, 0),
                Diode = frame.Bit(9, 0),
            },
        };
    }

    /// <summary>
    /// The mode, from the unit symbols. The hFE and temperature positions light DC and one of the
    /// maker's symbols, so that symbol is checked first. Prefixes pick the 121GW's separate
    /// millivolt and current modes, so names, logs and graphs match.
    /// </summary>
    private static MeterMode Mode(Fs9721Frame frame, string symbol, string prefix)
    {
        var ac = frame.Bit(0, 3);
        var ohms = frame.Bit(11, 2);
        return symbol switch
        {
            "hFE" => MeterMode.Hfe,
            "°C" => MeterMode.TemperatureC,
            "°F" => MeterMode.TemperatureF,
            _ when frame.Bit(9, 0) => MeterMode.Diode,
            _ when ohms && frame.Bit(10, 0) => MeterMode.Continuity,
            _ when ohms => MeterMode.Resistance,
            _ when frame.Bit(11, 3) => MeterMode.Capacitance,
            _ when frame.Bit(12, 1) => MeterMode.Frequency,
            _ when frame.Bit(10, 2) => MeterMode.Duty,
            _ when frame.Bit(12, 3) => (prefix, ac) switch
            {
                ("µ", false) => MeterMode.DcMicroamps,
                ("µ", true) => MeterMode.AcMicroamps,
                ("m", false) => MeterMode.DcMilliamps,
                ("m", true) => MeterMode.AcMilliamps,
                (_, false) => MeterMode.DcAmps,
                (_, true) => MeterMode.AcAmps,
            },
            _ when frame.Bit(12, 2) => (prefix, ac) switch
            {
                ("m", false) => MeterMode.DcMillivolts,
                ("m", true) => MeterMode.AcMillivolts,
                (_, false) => MeterMode.DcVolts,
                (_, true) => MeterMode.AcVolts,
            },
            _ => MeterMode.Unknown,
        };
    }

    /// <summary>Segment patterns (bits 6–0) for what the LCD can show in a digit.</summary>
    private static char DigitChar(int segments) => segments switch
    {
        0x7D => '0',
        0x05 => '1',
        0x5B => '2',
        0x1F => '3',
        0x27 => '4',
        0x3E => '5',
        0x7E => '6',
        0x15 => '7',
        0x7F => '8',
        0x3F => '9',
        0x68 => 'L',
        0x00 => ' ',
        _ => '?',
    };

    /// <summary>
    /// The four digits with their decimal point, blanks trimmed ("04.98", "0000"). The overload
    /// display is "0L" somewhere in the digits, its position depending on the range.
    /// </summary>
    private static (string Text, bool Overload) Digits(Fs9721Frame frame)
    {
        var text = new System.Text.StringBuilder(5);
        for (var digit = 0; digit < 4; digit++)
        {
            var first = 1 + digit * 2;
            if (digit > 0 && frame.Bit(first, 3))
                text.Append('.');
            var segments = ((frame.Segments(first) & 0x7) << 4) | frame.Segments(first + 1);
            text.Append(DigitChar(segments));
        }
        var s = text.ToString().Trim();
        return (s, s.Contains('L', StringComparison.Ordinal));
    }

    private static (string Symbol, double Multiplier) Prefix(Fs9721Frame frame) =>
        frame.Bit(9, 2) ? ("n", 1e-9)
        : frame.Bit(9, 3) ? ("µ", 1e-6)
        : frame.Bit(10, 3) ? ("m", 1e-3)
        : frame.Bit(9, 1) ? ("k", 1e3)
        : frame.Bit(10, 1) ? ("M", 1e6)
        : ("", 1);

    /// <summary>The first lit maker-chosen symbol in byte 13 that the meter's LCD prints.</summary>
    private static string UserSymbol(Fs9721Frame frame, IReadOnlyList<string> symbols)
    {
        for (var bit = 0; bit < 4 && bit < symbols.Count; bit++)
        {
            if (frame.Bit(13, bit) && symbols[bit].Length > 0)
                return symbols[bit];
        }
        return "";
    }
}
