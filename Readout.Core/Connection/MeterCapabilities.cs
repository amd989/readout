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

    public static MeterCapabilities For(MeterModel model) => model switch
    {
        MeterModel.Fs9721 => Fs9721,
        _ => Eevblog121Gw,
    };
}
