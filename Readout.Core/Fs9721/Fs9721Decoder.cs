using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Fs9721;

/// <summary>What an FS9721 meter is measuring, worked out from the LCD's unit symbols.</summary>
public enum Fs9721Function
{
    Unknown,
    DcVoltage,
    AcVoltage,
    DcCurrent,
    AcCurrent,
    Resistance,
    Continuity,
    Diode,
    Capacitance,
    Frequency,
    Duty,
    Temperature,
    /// <summary>Transistor gain, from the hFE socket.</summary>
    Hfe,
}

/// <summary>What an FS9721 meter's LCD shows for one frame.</summary>
public sealed record Fs9721Reading
{
    public required Fs9721Frame Frame { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }

    public required Fs9721Function Function { get; init; }
    /// <summary>The 121GW mode that measures the same thing; null for hFE and unknown functions.</summary>
    public required MeterMode? Mode { get; init; }
    /// <summary>"DC voltage", "Resistance"…, named as for the 121GW.</summary>
    public required string ModeName { get; init; }

    /// <summary>The digits as the LCD shows them, leading zeros and decimal point included ("04.98"), or "OL".</summary>
    public required string MainText { get; init; }
    public required bool MainNegative { get; init; }
    public required bool MainOverload { get; init; }
    /// <summary>The unit next to the digits ("mV", "kΩ", "°C", "hFE").</summary>
    public required string MainUnit { get; init; }

    /// <summary>The reading in <see cref="MainUnit"/>; null on overload or a digit that isn't one.</summary>
    public required double? Value { get; init; }
    /// <summary>The reading in <see cref="BaseUnit"/>; null when <see cref="Value"/> is.</summary>
    public required double? BaseValue { get; init; }
    /// <summary>The SI unit for logging and graphs ("V", "Ω", "°C"; empty for hFE).</summary>
    public required string BaseUnit { get; init; }

    public required Annunciators Icons { get; init; }

    /// <summary>The reading as one line: "-12.34 mV", "OL MΩ".</summary>
    public string MainLine =>
        $"{(MainNegative && !MainOverload ? "-" : "")}{MainText} {MainUnit}".TrimEnd();
}

/// <summary>
/// Decodes FS9721 frames into what the LCD shows. Each frame is the LCD's segment map, so this
/// reads the digits back from their segments and the function from the unit symbols.
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
    public static Fs9721Reading Decode(Fs9721Frame frame, DateTimeOffset receivedAt, IReadOnlyList<string>? userSymbols = null)
    {
        userSymbols ??= DefaultUserSymbols;
        var symbol = UserSymbol(frame, userSymbols);

        var (text, overload) = Digits(frame);
        var negative = frame.Bit(1, 3);
        var (prefix, multiplier) = Prefix(frame);

        var volts = frame.Bit(12, 2);
        var amps = frame.Bit(12, 3);
        var ohms = frame.Bit(11, 2);
        var ac = frame.Bit(0, 3);

        // The hFE and temperature positions light DC and nothing else, so their symbol comes first.
        var function = symbol switch
        {
            "hFE" => Fs9721Function.Hfe,
            "°C" or "°F" => Fs9721Function.Temperature,
            _ when frame.Bit(9, 0) => Fs9721Function.Diode,
            _ when ohms && frame.Bit(10, 0) => Fs9721Function.Continuity,
            _ when ohms => Fs9721Function.Resistance,
            _ when frame.Bit(11, 3) => Fs9721Function.Capacitance,
            _ when frame.Bit(12, 1) => Fs9721Function.Frequency,
            _ when frame.Bit(10, 2) => Fs9721Function.Duty,
            _ when amps => ac ? Fs9721Function.AcCurrent : Fs9721Function.DcCurrent,
            _ when volts => ac ? Fs9721Function.AcVoltage : Fs9721Function.DcVoltage,
            _ => Fs9721Function.Unknown,
        };

        var baseUnit = function switch
        {
            Fs9721Function.DcVoltage or Fs9721Function.AcVoltage or Fs9721Function.Diode => "V",
            Fs9721Function.DcCurrent or Fs9721Function.AcCurrent => "A",
            Fs9721Function.Resistance or Fs9721Function.Continuity => "Ω",
            Fs9721Function.Capacitance => "F",
            Fs9721Function.Frequency => "Hz",
            Fs9721Function.Duty => "%",
            Fs9721Function.Temperature => symbol,
            _ => "",
        };
        // hFE and temperature have no prefix; the others show the one lit next to the unit.
        var unit = function switch
        {
            Fs9721Function.Hfe => "hFE",
            Fs9721Function.Temperature or Fs9721Function.Duty => baseUnit,
            _ => prefix + baseUnit,
        };
        if (function is Fs9721Function.Hfe or Fs9721Function.Temperature or Fs9721Function.Duty)
            multiplier = 1;

        double? value = null;
        if (!overload && double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var magnitude))
            value = negative ? -magnitude : magnitude;

        var mode = Mode(function, prefix, ac, symbol);
        return new Fs9721Reading
        {
            Frame = frame,
            ReceivedAt = receivedAt,
            Function = function,
            Mode = mode,
            ModeName = function switch
            {
                Fs9721Function.Hfe => "Transistor gain",
                Fs9721Function.Unknown => "Unknown",
                _ => ReadingDecoder.ModeName(mode!.Value),
            },
            MainText = overload ? "OL" : text,
            MainNegative = negative,
            MainOverload = overload,
            MainUnit = unit,
            Value = value,
            BaseValue = value * multiplier,
            BaseUnit = baseUnit,
            Icons = new Annunciators
            {
                Ac = ac,
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

    /// <summary>The 121GW mode for the same measurement, so names, logging and graphs can be shared.</summary>
    private static MeterMode? Mode(Fs9721Function function, string prefix, bool ac, string symbol) => function switch
    {
        Fs9721Function.DcVoltage => prefix == "m" ? MeterMode.DcMillivolts : MeterMode.DcVolts,
        Fs9721Function.AcVoltage => prefix == "m" ? MeterMode.AcMillivolts : MeterMode.AcVolts,
        Fs9721Function.DcCurrent or Fs9721Function.AcCurrent => (prefix, ac) switch
        {
            ("µ", false) => MeterMode.DcMicroamps,
            ("µ", true) => MeterMode.AcMicroamps,
            ("m", false) => MeterMode.DcMilliamps,
            ("m", true) => MeterMode.AcMilliamps,
            (_, false) => MeterMode.DcAmps,
            (_, true) => MeterMode.AcAmps,
        },
        Fs9721Function.Resistance => MeterMode.Resistance,
        Fs9721Function.Continuity => MeterMode.Continuity,
        Fs9721Function.Diode => MeterMode.Diode,
        Fs9721Function.Capacitance => MeterMode.Capacitance,
        Fs9721Function.Frequency => MeterMode.Frequency,
        Fs9721Function.Duty => MeterMode.Duty,
        Fs9721Function.Temperature => symbol == "°F" ? MeterMode.TemperatureF : MeterMode.TemperatureC,
        _ => null,
    };
}
