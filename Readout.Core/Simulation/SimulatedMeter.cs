using Readout.Core.Protocol;

namespace Readout.Core.Simulation;

/// <summary>
/// A pretend 121GW: produces plausible packets for a few modes and reacts to MODE, RANGE,
/// HOLD, REL and MIN/MAX. Used on simulators (no Bluetooth there), in tests, and as the
/// app's demo mode.
/// </summary>
public sealed class SimulatedMeter
{
    private sealed record SimMode(
        MeterMode Mode, int Range, BarScale BarScale, double FullScale,
        Coupling Coupling, Func<double, double> Signal);

    private static readonly SimMode[] Modes =
    [
        // A 12 V supply with a little ripple and drift: 50 V range, "12.345".
        new(MeterMode.DcVolts, 1, BarScale.Fifty, 50, Coupling.Dc,
            t => 12.0 + 0.35 * Math.Sin(t / 4.0) + 0.02 * Math.Sin(t * 7.3)),
        // Mains: 500 V range, "230.12", with its frequency on the sub display.
        new(MeterMode.AcVolts, 2, BarScale.FiveHundred, 500, Coupling.Ac,
            t => 230.0 + 1.5 * Math.Sin(t / 6.0)),
        // A 4.7 kΩ resistor warming up: 50 kΩ range ("kk" notation), "4.7000".
        new(MeterMode.Resistance, 2, BarScale.Five, 5, Coupling.None,
            t => 4.7 + 0.004 * Math.Sin(t / 9.0)),
        // A current draw on the 5 mA range: "1.2500".
        new(MeterMode.DcMilliamps, 0, BarScale.Five, 5, Coupling.Dc,
            t => 1.25 + 0.4 * Math.Sin(t / 2.5)),
        // Room temperature: "23.4", one decimal.
        new(MeterMode.TemperatureC, 0, BarScale.Five, 5, Coupling.None,
            t => 23.4 + 0.3 * Math.Sin(t / 20.0)),
    ];

    private readonly Random _random;
    private int _modeIndex;
    private bool _autoRange = true;
    private bool _relative;
    private double _relativeOffset;
    private HoldState _hold;
    private double? _heldValue;
    private MinMaxState _minMax;
    private double _lastLive;
    private int _setupPage;

    /// <summary>
    /// What SETUP steps through, in a real meter's order, after the mode's own sub reading
    /// (page 0). The buzzer, ID and interval pages come with sub mode 0 and no value, as the
    /// real meter sends them.
    /// </summary>
    private static readonly MeterMode[] SetupPages =
    [
        MeterMode.SetupTemperatureC, MeterMode.Battery, MeterMode.AutoPowerOffOn, MeterMode.LowZ,
        MeterMode.LcdContrast, MeterMode.Year, MeterMode.Date, MeterMode.Time, MeterMode.LowZ, MeterMode.LowZ,
    ];

    /// <summary>Real time, which the meter's clock runs from.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>How far the meter's clock is from <see cref="Clock"/>; setting the clock changes it.</summary>
    public TimeSpan ClockOffset { get; set; }

    /// <summary>The meter's clock, as its year, date and time pages show it.</summary>
    public DateTime MeterTime => Clock() + ClockOffset;

    // Editing a clock page: which field flashes, and its value. Null when not editing.
    private int? _editField;
    private int _editValue;

    public SimulatedMeter(int seed = 121) => _random = new Random(seed);

    public int Serial { get; set; } = 12100;
    public MeterMode CurrentMode => Modes[_modeIndex].Mode;

    /// <summary>The packet the meter would send <paramref name="elapsed"/> after it was switched on.</summary>
    public byte[] PacketAt(TimeSpan elapsed)
    {
        var sim = Modes[_modeIndex];
        var t = elapsed.TotalSeconds;
        var live = sim.Signal(t) + (_random.NextDouble() - 0.5) * sim.FullScale * 1e-4;
        var value = _heldValue ?? live;
        if (_relative) value -= _relativeOffset;

        var integerDigits = Display.ModeInfo.For(sim.Mode)!.IntegerDigits[sim.Range];
        var scale = Math.Pow(10, 5 - integerDigits);
        var raw = (int)Math.Round(Math.Abs(value) * scale);
        var builder = new PacketBuilder
        {
            Serial = Serial,
            Mode = sim.Mode == MeterMode.TemperatureC ? MeterMode.Temperature : sim.Mode,
            MainCelsius = sim.Mode == MeterMode.TemperatureC,
            MainRangeIndex = sim.Range,
            MainRawValue = raw,
            MainNegative = value < 0,
            BarOn = sim.Mode != MeterMode.TemperatureC,
            BarScale = sim.BarScale,
            BarNegative = value < 0,
            BarValue = (int)Math.Clamp(Math.Round(Math.Abs(value) / sim.FullScale * 25), 0, 25),
            Coupling = sim.Coupling,
            AutoRange = _autoRange,
            Relative = _relative,
            Hold = _hold,
            MinMax = _minMax,
        };

        // Page 0 is the mode's own reading; in modes where that's the meter temperature,
        // SETUP's temperature page is skipped, as on the meter.
        var pages = sim.Mode == MeterMode.AcVolts ? SetupPages : SetupPages[1..];
        if (_setupPage > 0)
            SetupPage(builder, pages[(_setupPage - 1) % pages.Length], t);
        else if (sim.Mode == MeterMode.AcVolts)
        {
            // Mains frequency on the sub display: "50.00 Hz".
            builder.SubMode = MeterMode.Frequency;
            builder.SubDecimals = 2;
            builder.SubRawValue = (int)Math.Round((50.0 + 0.02 * Math.Sin(t / 3.0)) * 100);
            builder.SubHertz = true;
        }
        else
        {
            // The meter's own temperature, "23.5 °C": the SETUP temperature page, which is
            // where a real meter's small display rests.
            builder.SubMode = MeterMode.SetupTemperatureC;
            builder.SubDecimals = 1;
            builder.SubRawValue = (int)Math.Round((23.5 + 0.1 * Math.Sin(t / 30.0)) * 10);
        }

        _lastLive = live;
        return builder.Build();
    }

    private MeterMode CurrentPage
    {
        get
        {
            if (_setupPage == 0) return MeterMode.LowZ;
            var pages = Modes[_modeIndex].Mode == MeterMode.AcVolts ? SetupPages : SetupPages[1..];
            return pages[(_setupPage - 1) % pages.Length];
        }
    }

    /// <summary>The clock fields a page edits, in order: year; month and day; hour and minute.</summary>
    private static int[] ClockFields(MeterMode page, DateTime time) => page switch
    {
        MeterMode.Year => [time.Year % 100],
        MeterMode.Date => [time.Month, time.Day],
        MeterMode.Time => [time.Hour, time.Minute],
        _ => [],
    };

    /// <summary>
    /// SETUP held on a clock page: starts editing its first field, or saves the flashing field
    /// and moves to the next. As on the meter, the packet keeps showing the running clock, not
    /// the value being edited.
    /// </summary>
    private void HoldSetupOnClockPage(MeterMode page)
    {
        if (_editField is { } field)
        {
            var time = MeterTime;
            var saved = (page, field) switch
            {
                (MeterMode.Year, _) => new DateTime(2000 + _editValue, time.Month, Math.Min(time.Day, DateTime.DaysInMonth(2000 + _editValue, time.Month)), time.Hour, time.Minute, time.Second),
                (MeterMode.Date, 0) => new DateTime(time.Year, _editValue, Math.Min(time.Day, DateTime.DaysInMonth(time.Year, _editValue)), time.Hour, time.Minute, time.Second),
                (MeterMode.Date, _) => new DateTime(time.Year, time.Month, Math.Min(_editValue, DateTime.DaysInMonth(time.Year, time.Month)), time.Hour, time.Minute, time.Second),
                (MeterMode.Time, 0) => new DateTime(time.Year, time.Month, time.Day, _editValue, time.Minute, time.Second),
                _ => new DateTime(time.Year, time.Month, time.Day, time.Hour, _editValue, 0),
            };
            ClockOffset = saved - Clock();
            var fields = ClockFields(page, MeterTime);
            _editField = field + 1 < fields.Length ? field + 1 : null;
            if (_editField is { } next) _editValue = fields[next];
        }
        else
        {
            _editField = 0;
            _editValue = ClockFields(page, MeterTime)[0];
        }
    }

    private void StepEditValue(MeterMode page, int step)
    {
        var (min, max) = (page, _editField) switch
        {
            (MeterMode.Year, _) => (0, 99),
            (MeterMode.Date, 0) => (1, 12),
            (MeterMode.Date, _) => (1, 31),
            (MeterMode.Time, 0) => (0, 23),
            _ => (0, 59),
        };
        _editValue = _editValue + step > max ? min : _editValue + step < min ? max : _editValue + step;
    }

    private void SetupPage(PacketBuilder builder, MeterMode page, double t)
    {
        var now = MeterTime;
        builder.SubMode = page;
        (builder.SubDecimals, builder.SubRawValue) = page switch
        {
            MeterMode.SetupTemperatureC => (1, (int)Math.Round((23.5 + 0.1 * Math.Sin(t / 30.0)) * 10)),
            MeterMode.Battery => (1, 62),
            MeterMode.AutoPowerOffOn => (2, 0),
            MeterMode.LcdContrast => (0, 4),
            MeterMode.Year => (0, now.Year % 100),
            MeterMode.Date => (0, now.Month << 8 | now.Day),
            MeterMode.Time => (0, now.Hour << 8 | now.Minute),
            _ => (0, 0),
        };
    }

    /// <summary>Reacts to a button the way the real meter roughly does.</summary>
    public void Press(MeterKey key, bool longPress = false)
    {
        var page = CurrentPage;
        if (page is MeterMode.Year or MeterMode.Date or MeterMode.Time)
        {
            if (key == MeterKey.Setup && longPress)
            {
                HoldSetupOnClockPage(page);
                return;
            }
            if (_editField is not null)
            {
                // ▲ and ▼ change the flashing value; a short SETUP drops the edit and moves on.
                if (key is MeterKey.Rel or MeterKey.Peak && !longPress)
                {
                    StepEditValue(page, key == MeterKey.Rel ? 1 : -1);
                    return;
                }
                _editField = null;
            }
        }

        switch (key)
        {
            case MeterKey.Setup when !longPress:
                // Past the last page, back to the mode's own reading.
                var count = (Modes[_modeIndex].Mode == MeterMode.AcVolts ? SetupPages.Length : SetupPages.Length - 1) + 1;
                _setupPage = (_setupPage + 1) % count;
                break;
            case MeterKey.Mode:
                _modeIndex = (_modeIndex + 1) % Modes.Length;
                _setupPage = 0;
                _editField = null;
                _relative = false;
                _hold = HoldState.Off;
                _heldValue = null;
                _minMax = MinMaxState.Off;
                break;
            case MeterKey.Range:
                _autoRange = longPress || !_autoRange;
                break;
            case MeterKey.Hold:
                // As the manual has it: HOLD, then A-HOLD, then live again.
                _hold = _hold switch
                {
                    HoldState.Off => HoldState.Hold,
                    HoldState.Hold => HoldState.AutoHold,
                    _ => HoldState.Off,
                };
                _heldValue = _hold == HoldState.Off ? null : _lastLive;
                break;
            case MeterKey.Rel:
                _relative = !_relative;
                _relativeOffset = _lastLive;
                break;
            case MeterKey.MinMax:
                // MAX, MIN, AVG, then all three; holding the button leaves.
                _minMax = longPress ? MinMaxState.Off : _minMax switch
                {
                    MinMaxState.Off => MinMaxState.Max,
                    MinMaxState.Max => MinMaxState.Min,
                    MinMaxState.Min => MinMaxState.Average,
                    MinMaxState.Average => MinMaxState.All,
                    _ => MinMaxState.Max,
                };
                break;
        }
    }
}
