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

        if (sim.Mode == MeterMode.AcVolts)
        {
            // Mains frequency on the sub display: "50.00 Hz".
            builder.SubMode = MeterMode.Frequency;
            builder.SubDecimals = 2;
            builder.SubRawValue = (int)Math.Round((50.0 + 0.02 * Math.Sin(t / 3.0)) * 100);
            builder.SubHertz = true;
        }
        else
        {
            // The meter's own temperature: "23.5 °C".
            builder.SubMode = MeterMode.Temperature;
            builder.CelsiusIcon = true;
            builder.SubDecimals = 1;
            builder.SubRawValue = (int)Math.Round((23.5 + 0.1 * Math.Sin(t / 30.0)) * 10);
        }

        _lastLive = live;
        return builder.Build();
    }

    /// <summary>Reacts to a button the way the real meter roughly does.</summary>
    public void Press(MeterKey key, bool longPress = false)
    {
        switch (key)
        {
            case MeterKey.Mode:
                _modeIndex = (_modeIndex + 1) % Modes.Length;
                _relative = false;
                _hold = HoldState.Off;
                _heldValue = null;
                _minMax = MinMaxState.Off;
                break;
            case MeterKey.Range:
                _autoRange = longPress || !_autoRange;
                break;
            case MeterKey.Hold:
                if (_hold == HoldState.Off)
                {
                    _hold = longPress ? HoldState.AutoHold : HoldState.Hold;
                    _heldValue = _lastLive;
                }
                else
                {
                    _hold = HoldState.Off;
                    _heldValue = null;
                }
                break;
            case MeterKey.Rel:
                _relative = !_relative;
                _relativeOffset = _lastLive;
                break;
            case MeterKey.MinMax:
                _minMax = longPress ? MinMaxState.Off : _minMax switch
                {
                    MinMaxState.Off => MinMaxState.Max,
                    MinMaxState.Max => MinMaxState.Min,
                    MinMaxState.Min => MinMaxState.Average,
                    _ => MinMaxState.Off,
                };
                break;
        }
    }
}
