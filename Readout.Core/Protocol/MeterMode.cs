namespace Readout.Core.Protocol;

/// <summary>
/// What the meter is measuring. The main display uses 0–26. The sub display uses the same
/// values, plus 100 and up for the SETUP pages (battery, date, auto power-off and so on).
/// Values and names follow the official EEVblog app's <c>Packet121GW.eMode</c>.
/// </summary>
public enum MeterMode : byte
{
    LowZ = 0,
    DcVolts = 1,
    AcVolts = 2,
    DcMillivolts = 3,
    AcMillivolts = 4,
    /// <summary>Temperature before the °C/°F bits are applied; <see cref="MeterPacket.Mode"/> resolves it.</summary>
    Temperature = 5,
    Frequency = 6,
    /// <summary>Pulse width, in milliseconds.</summary>
    PulseWidth = 7,
    Duty = 8,
    Resistance = 9,
    Continuity = 10,
    Diode = 11,
    Capacitance = 12,
    AcMicroVoltAmps = 13,
    AcMilliVoltAmps = 14,
    AcVoltAmps = 15,
    AcMicroamps = 16,
    DcMicroamps = 17,
    AcMilliamps = 18,
    DcMilliamps = 19,
    AcAmps = 20,
    DcAmps = 21,
    DcMicroVoltAmps = 22,
    DcMilliVoltAmps = 23,
    DcVoltAmps = 24,
    TemperatureC = 25,
    TemperatureF = 26,

    // Sub display only: the SETUP pages.
    SetupTemperatureC = 100,
    SetupTemperatureF = 105,
    Battery = 110,
    AutoPowerOffOn = 120,
    AutoPowerOffOff = 125,
    Year = 130,
    Date = 135,
    Time = 140,
    BurdenVoltage = 150,
    LcdContrast = 160,
    Dbm = 180,
    Interval = 190,
}

/// <summary>The AC/DC annunciators, from status byte 1.</summary>
public enum Coupling : byte
{
    None = 0,
    Dc = 1,
    Ac = 2,
    AcPlusDc = 3,
}

/// <summary>Full scale of the bar graph, from the bar status byte.</summary>
public enum BarScale : byte
{
    Five = 0,
    Fifty = 1,
    FiveHundred = 2,
    OneThousand = 3,
}

/// <summary>MIN/MAX state, from status byte 2.</summary>
public enum MinMaxState : byte
{
    Off = 0,
    Max = 1,
    Min = 2,
    Average = 3,
    /// <summary>The official app lights MAX, MIN and AVG together for this value.</summary>
    All = 4,
}

/// <summary>HOLD state, from status byte 3.</summary>
public enum HoldState : byte
{
    Off = 0,
    /// <summary>Auto-hold: "A-HOLD" on the LCD.</summary>
    AutoHold = 1,
    Hold = 2,
}
