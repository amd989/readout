using Readout.Core.Display;

namespace Readout.Core.Connection;

/// <summary>What a kind of meter can do beyond sending readings, so the app shows only what works.</summary>
/// <param name="RemoteKeys">Its buttons can be pressed from the app.</param>
/// <param name="ClockSync">It has a clock the app can set.</param>
/// <param name="SubDisplay">It has a small second display.</param>
/// <param name="BarGraph">It has an analog bar graph.</param>
/// <param name="Digits">Digits on its main display.</param>
public sealed record MeterCapabilities(bool RemoteKeys, bool ClockSync, bool SubDisplay, bool BarGraph, int Digits)
{
    public static readonly MeterCapabilities Eevblog121Gw = new(RemoteKeys: true, ClockSync: true, SubDisplay: true, BarGraph: true, Digits: 5);

    /// <summary>An FS9721 through a bridge: it only sends, 3¾ digits, nothing else.</summary>
    public static readonly MeterCapabilities Fs9721 = new(RemoteKeys: false, ClockSync: false, SubDisplay: false, BarGraph: false, Digits: 4);

    /// <summary>
    /// A DSO150 oscilloscope through a bridge: no buttons to press (it takes commands instead),
    /// no clock, no LCD extras. Its measurements come from an 8-bit ADC, so 4 digits is plenty.
    /// </summary>
    public static readonly MeterCapabilities Dso150 = new(RemoteKeys: false, ClockSync: false, SubDisplay: false, BarGraph: false, Digits: 4);

    public static MeterCapabilities For(MeterModel model) => model switch
    {
        MeterModel.Eevblog121Gw => Eevblog121Gw,
        MeterModel.Fs9721 => Fs9721,
        MeterModel.Dso150 => Dso150,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "No capabilities for this meter."),
    };
}
