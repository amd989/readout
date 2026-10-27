using System.Globalization;
using Readout.Core.Protocol;

namespace Readout.Core.Display;

/// <summary>
/// Decodes a packet into what the LCD shows. Follows the official app's
/// <c>MultimeterScreen.Update</c>, which lights LCD layers; here they become text.
/// </summary>
public static class ReadingDecoder
{
    private const int LcdDigits = 5;

    /// <summary>"DC voltage", "Resistance"… for a main mode.</summary>
    public static string ModeName(MeterMode mode) => ModeInfo.For(mode)?.Name ?? $"Mode {(int)mode}";

    public static MeterReading Decode(MeterPacket packet, DateTimeOffset receivedAt)
    {
        var mode = packet.Mode;
        var info = ModeInfo.For(mode);
        var range = packet.MainRangeIndex;
        var known = info is not null && info.HasRange(range);

        string mainText;
        double? value = null;
        double? baseValue = null;
        var unit = "";

        if (known)
        {
            var integerDigits = info!.IntegerDigits[range];
            var prefix = info.PrefixFor(range);
            unit = SiPrefix.Symbol(prefix) + info.BaseUnit;

            if (packet.MainOverload)
                mainText = "OFL";
            else
            {
                mainText = FormatMain(packet.MainRawValue, integerDigits);
                var magnitude = packet.MainRawValue / Math.Pow(10, LcdDigits - integerDigits);
                value = packet.MainNegative ? -magnitude : magnitude;
                baseValue = value * SiPrefix.Multiplier(prefix);
            }
        }
        else
        {
            // A mode or range this table doesn't know: show the digits, but don't guess the
            // decimal point, and don't log a number that may be off by a power of ten.
            mainText = packet.MainOverload ? "OFL" : packet.MainRawValue.ToString(CultureInfo.InvariantCulture);
        }

        return new MeterReading
        {
            Packet = packet,
            ReceivedAt = receivedAt,
            Mode = mode,
            ModeName = ModeName(mode),
            MainText = mainText,
            MainNegative = packet.MainNegative,
            MainOverload = packet.MainOverload,
            MainUnit = unit,
            Value = value,
            BaseValue = baseValue,
            BaseUnit = info?.BaseUnit ?? "",
            Sub = DecodeSub(packet),
            Bar = new BarGraph(
                packet.BarOn,
                packet.BarOn ? Math.Min(BarGraph.TotalSegments, packet.BarValue + 1) : 0,
                packet.BarNegative,
                packet.BarScale,
                packet.BarTicks150),
            Icons = DecodeIcons(packet),
        };
    }

    /// <summary>
    /// The main digits: right-aligned in 5 places, the decimal point after
    /// <paramref name="integerDigits"/> of them, zeros after the point (" 0.050", "12.106").
    /// </summary>
    internal static string FormatMain(int raw, int integerDigits)
    {
        var digits = raw.ToString(CultureInfo.InvariantCulture).PadLeft(LcdDigits, ' ');
        if (integerDigits >= LcdDigits)
            return digits;

        // Counted from the right, so a value past 99999 keeps the same number of decimals.
        var point = digits.Length - (LcdDigits - integerDigits);
        var whole = digits[..point];
        var fraction = digits[point..].Replace(' ', '0');
        if (whole.Length == 0 || whole[^1] == ' ')
            whole = whole[..^Math.Min(1, whole.Length)] + "0";
        return $"{whole}.{fraction}";
    }

    /// <summary>The sub display's digits: <paramref name="decimals"/> after the point, "0.05" style.</summary>
    internal static string FormatSub(int raw, int decimals)
    {
        var digits = raw.ToString(CultureInfo.InvariantCulture);
        if (decimals <= 0)
            return digits;

        digits = digits.PadLeft(decimals + 1, '0');
        return digits.Insert(digits.Length - decimals, ".");
    }

    private static SubDisplay DecodeSub(MeterPacket packet)
    {
        var mode = packet.SubMode;
        var milli = SubMilli(packet) ? "m" : "";

        var unit = mode switch
        {
            MeterMode.LowZ => "V",
            MeterMode.DcVolts or MeterMode.AcVolts => milli + "V",
            MeterMode.DcMillivolts or MeterMode.AcMillivolts => "mV",
            // Plain temperature says neither °C nor °F; the LCD's °F symbol does.
            MeterMode.Temperature => packet.FahrenheitIcon ? "°F" : "°C",
            MeterMode.TemperatureC or MeterMode.SetupTemperatureC => "°C",
            MeterMode.TemperatureF or MeterMode.SetupTemperatureF => "°F",
            MeterMode.Frequency => "Hz",
            MeterMode.PulseWidth => "ms",
            MeterMode.Duty => "%",
            MeterMode.Resistance => "Ω",
            MeterMode.Diode => "V",
            MeterMode.AcVoltAmps => "VA",
            MeterMode.AcMicroamps or MeterMode.DcMicroamps
                or MeterMode.AcMilliamps or MeterMode.DcMilliamps
                or MeterMode.AcAmps or MeterMode.DcAmps => milli + "A",
            MeterMode.DcMicroVoltAmps or MeterMode.DcMilliVoltAmps or MeterMode.DcVoltAmps => milli + "VA",
            MeterMode.Battery or MeterMode.BurdenVoltage => "V",
            MeterMode.Dbm => "dBm",
            MeterMode.Interval => "ms",
            _ => "",
        };
        if (packet.SubKilo)
            unit = "k" + unit;

        var label = mode switch
        {
            MeterMode.SetupTemperatureC or MeterMode.SetupTemperatureF => "Temperature",
            MeterMode.Battery => "Battery",
            MeterMode.AutoPowerOffOn => "Auto power-off: on",
            MeterMode.AutoPowerOffOff => "Auto power-off: off",
            MeterMode.Year => "Year",
            MeterMode.Date => "Date",
            MeterMode.Time => "Time",
            MeterMode.BurdenVoltage => "Burden voltage",
            MeterMode.LcdContrast => "LCD contrast",
            MeterMode.Dbm => "dBm reference",
            MeterMode.Interval => "Logging interval",
            _ => null,
        };

        var text = packet.SubOverload ? "OFL" : FormatSub(packet.SubRawValue, packet.SubDecimals);
        return new SubDisplay(text, packet.SubNegative && !packet.SubOverload, unit, label);
    }

    /// <summary>
    /// The sub display's "m", which the packet has no bit for. The official app lights it when
    /// the main display shows power and the sub display shows current in mA or µA, or the main
    /// range itself is milli or smaller.
    /// </summary>
    private static bool SubMilli(MeterPacket packet)
    {
        var main = packet.Mode;
        var isPower = main is MeterMode.AcMicroVoltAmps or MeterMode.AcMilliVoltAmps or MeterMode.AcVoltAmps
            or MeterMode.DcMicroVoltAmps or MeterMode.DcMilliVoltAmps or MeterMode.DcVoltAmps;
        var sub = packet.SubMode;
        var subIsCurrent = sub is MeterMode.AcAmps or MeterMode.AcMilliamps or MeterMode.AcMicroamps
            or MeterMode.DcAmps or MeterMode.DcMilliamps or MeterMode.DcMicroamps;
        if (!isPower || !subIsCurrent)
            return false;

        var info = ModeInfo.For(main);
        if (info is not null && info.HasRange(packet.MainRangeIndex) &&
            info.Notation[packet.MainRangeIndex] is 'm' or 'n' or 'u')
            return true;

        return sub is MeterMode.AcMilliamps or MeterMode.AcMicroamps
            or MeterMode.DcMilliamps or MeterMode.DcMicroamps;
    }

    private static Annunciators DecodeIcons(MeterPacket packet)
    {
        var coupling = packet.Coupling;
        var minMax = packet.MinMax;
        return new Annunciators
        {
            LowZ = packet.Mode == MeterMode.LowZ,
            // The LCD has one stacked AC/DC pair; the coupling bits and the separate AC and DC
            // icon bits both light it.
            Ac = coupling is Coupling.Ac or Coupling.AcPlusDc || packet.Mode == MeterMode.AcMillivolts || packet.AcIcon,
            Dc = coupling is Coupling.Dc or Coupling.AcPlusDc || packet.DcIcon,
            AcPlusDc = coupling == Coupling.AcPlusDc,
            Auto = packet.AutoRange,
            AutoPowerOff = packet.AutoPowerOff,
            LowBattery = packet.LowBattery,
            Bluetooth = packet.Bluetooth,
            Relative = packet.Relative,
            Decibels = packet.Decibels,
            Max = minMax is MinMaxState.Max or MinMaxState.All,
            Min = minMax is MinMaxState.Min or MinMaxState.All,
            Average = minMax is MinMaxState.Average or MinMaxState.All,
            Test = packet.Test,
            Memory = packet.Memory > 0,
            Hold = packet.Hold is HoldState.Hold or HoldState.AutoHold,
            AutoHold = packet.Hold == HoldState.AutoHold,
            Beep = packet.Mode == MeterMode.Continuity,
            Diode = packet.Mode == MeterMode.Diode,
            LowPassFilter = packet.LowPassFilter,
            OneMsPeak = packet.OneMsPeak,
            Arrow = packet.Arrow,
        };
    }
}
