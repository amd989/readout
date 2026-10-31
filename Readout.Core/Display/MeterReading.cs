using Readout.Core.Protocol;

namespace Readout.Core.Display;

/// <summary>Everything the meter's LCD shows for one packet, decoded for display and logging.</summary>
public sealed record MeterReading
{
    public required MeterPacket Packet { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }

    public required MeterMode Mode { get; init; }
    /// <summary>"DC voltage", "Resistance"…</summary>
    public required string ModeName { get; init; }

    /// <summary>The main digits as the LCD shows them, decimal point included ("12.106", "0.0005"), or "OFL".</summary>
    public required string MainText { get; init; }
    public required bool MainNegative { get; init; }
    public required bool MainOverload { get; init; }
    /// <summary>The unit next to the main digits ("mV", "kΩ", "°C").</summary>
    public required string MainUnit { get; init; }

    /// <summary>The main reading in <see cref="MainUnit"/>; null on overload or an unknown range.</summary>
    public required double? Value { get; init; }
    /// <summary>The main reading in <see cref="BaseUnit"/> ("12.106 mV" is 0.012106 V); null when <see cref="Value"/> is.</summary>
    public required double? BaseValue { get; init; }
    /// <summary>The SI unit for logging and graphs ("V", "Ω", "A").</summary>
    public required string BaseUnit { get; init; }

    public required SubDisplay Sub { get; init; }
    public required BarGraph Bar { get; init; }
    public required Annunciators Icons { get; init; }

    /// <summary>
    /// Right after a mode change the meter's display is blank until its first measurement,
    /// while the packets say 0. Such a reading shows nothing and isn't logged.
    /// </summary>
    public bool Blank { get; init; }

    public int Serial => Packet.Serial;

    /// <summary>The main reading as one line: "-12.106 mV", "OFL kΩ".</summary>
    public string MainLine =>
        $"{(MainNegative && !MainOverload ? "-" : "")}{MainText.Trim()} {MainUnit}".TrimEnd();
}

/// <summary>The small display at the top right of the LCD.</summary>
/// <param name="Text">The digits, decimal point included, or "OFL".</param>
/// <param name="Negative">Whether the minus sign is lit.</param>
/// <param name="Unit">"Hz", "°C", "mA"… or empty.</param>
/// <param name="Label">What a SETUP page shows ("Battery", "Year"); null for live readings.</param>
public sealed record SubDisplay(string Text, bool Negative, string Unit, string? Label)
{
    public string Line => $"{(Negative ? "-" : "")}{Text} {Unit}".TrimEnd();
}

/// <summary>The analog bar graph under the main digits.</summary>
/// <param name="On">Whether the bar graph shows at all.</param>
/// <param name="Segments">Lit segments, 0 to <see cref="TotalSegments"/>.</param>
/// <param name="Negative">Whether the minus sign at its left is lit.</param>
/// <param name="Scale">Full scale: 5, 50, 500 or 1000.</param>
/// <param name="Ticks150">Tick labels 0–150 instead of 0–5.</param>
public sealed record BarGraph(bool On, int Segments, bool Negative, BarScale Scale, bool Ticks150)
{
    public const int TotalSegments = 26;

    public int FullScale => Scale switch
    {
        BarScale.Five => 5,
        BarScale.Fifty => 50,
        BarScale.FiveHundred => 500,
        _ => 1000,
    };
}

/// <summary>The LCD's symbols and labels around the digits.</summary>
public sealed record Annunciators
{
    public bool LowZ { get; init; }
    public bool Ac { get; init; }
    public bool Dc { get; init; }
    public bool AcPlusDc { get; init; }
    public bool Auto { get; init; }
    public bool AutoPowerOff { get; init; }
    public bool LowBattery { get; init; }
    public bool Bluetooth { get; init; }
    public bool Relative { get; init; }
    public bool Decibels { get; init; }
    public bool Max { get; init; }
    public bool Min { get; init; }
    public bool Average { get; init; }
    public bool Test { get; init; }
    public bool Memory { get; init; }
    public bool Hold { get; init; }
    /// <summary>The "A-" before HOLD: auto-hold.</summary>
    public bool AutoHold { get; init; }
    public bool Beep { get; init; }
    public bool Diode { get; init; }
    /// <summary>The "1kHz" low-pass filter symbol.</summary>
    public bool LowPassFilter { get; init; }
    /// <summary>The "1ms" PEAK symbol.</summary>
    public bool OneMsPeak { get; init; }
    public bool Arrow { get; init; }
}
