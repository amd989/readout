using Readout.Core.Protocol;

namespace Readout.Core.Display;

/// <summary>A key press on the meter: which key, and whether it was held.</summary>
public readonly record struct MeterKeyPress(MeterKey Key, bool LongPress);

/// <summary>
/// Works out which key was pressed on the meter itself from how the display changed. The
/// meter never reports its keys, but nearly every press leaves a mark: HOLD and REL light their
/// symbols, MIN/MAX steps MAX, MIN, AVG, SETUP moves the small display to another page, and
/// MODE moves to another function on the same dial position. Turning the dial changes the mode
/// too, so a mode change only counts as MODE within one dial position.
/// </summary>
public static class KeyInference
{
    /// <summary>The key the change from <paramref name="before"/> to <paramref name="after"/> shows, or null.</summary>
    public static MeterKeyPress? Infer(MeterPacket before, MeterPacket after)
    {
        if (Function(before) != Function(after))
            return DialPosition(before.Mode) is { } position && position == DialPosition(after.Mode)
                ? new MeterKeyPress(MeterKey.Mode, false)
                : null;

        // These also switch AUTO off, so they come before RANGE.
        if (before.Hold != after.Hold)
            return new MeterKeyPress(MeterKey.Hold, false);
        if (before.LowPassFilter != after.LowPassFilter)
            return new MeterKeyPress(MeterKey.Rel, true);
        if (before.Relative != after.Relative)
            return new MeterKeyPress(MeterKey.Rel, false);
        // 1ms PEAK lights MAX too, so it comes before MIN/MAX.
        if (before.OneMsPeak != after.OneMsPeak)
            return new MeterKeyPress(MeterKey.Peak, false);
        if (before.MinMax != after.MinMax)
            return new MeterKeyPress(MeterKey.MinMax, after.MinMax == MinMaxState.Off);
        if (before.Memory != after.Memory)
            return new MeterKeyPress(MeterKey.Mem, false);
        if (before.SubMode != after.SubMode && (IsSetupPage(before.SubMode) || IsSetupPage(after.SubMode)))
            return new MeterKeyPress(MeterKey.Setup, false);

        if (before.AutoRange && !after.AutoRange)
            return new MeterKeyPress(MeterKey.Range, false);
        if (!before.AutoRange && after.AutoRange)
            return new MeterKeyPress(MeterKey.Range, true);
        if (!before.AutoRange && !after.AutoRange && before.MainRangeIndex != after.MainRangeIndex)
            return new MeterKeyPress(MeterKey.Range, false);
        return null;
    }

    /// <summary>The function MODE steps through: AC+DC on V is the AC mode with AC+DC coupling.</summary>
    private static (MeterMode, bool) Function(MeterPacket p) => (p.Mode, p.Coupling == Coupling.AcPlusDc);

    /// <summary>
    /// SETUP pages: numbered 100 and up, and the buzzer, ID and interval pages that come as 0.
    /// The VA position's small display switching between volts and amps by itself isn't one.
    /// </summary>
    private static bool IsSetupPage(MeterMode sub) => (int)sub >= 100 || sub == MeterMode.LowZ;

    /// <summary>The dial position a mode belongs to, as found on a real meter; null if unknown.</summary>
    private static int? DialPosition(MeterMode mode) => mode switch
    {
        MeterMode.LowZ => 0,
        MeterMode.DcVolts or MeterMode.AcVolts => 1,
        MeterMode.DcMillivolts or MeterMode.AcMillivolts or MeterMode.Temperature
            or MeterMode.TemperatureC or MeterMode.TemperatureF => 2,
        MeterMode.Frequency or MeterMode.PulseWidth or MeterMode.Duty => 3,
        MeterMode.Resistance or MeterMode.Continuity or MeterMode.Diode or MeterMode.Capacitance => 4,
        MeterMode.AcVoltAmps or MeterMode.DcVoltAmps => 5,
        MeterMode.AcMicroamps or MeterMode.DcMicroamps or MeterMode.AcMicroVoltAmps or MeterMode.DcMicroVoltAmps => 6,
        MeterMode.AcMilliamps or MeterMode.DcMilliamps or MeterMode.AcMilliVoltAmps or MeterMode.DcMilliVoltAmps => 7,
        MeterMode.AcAmps or MeterMode.DcAmps => 8,
        _ => null,
    };
}
