namespace Readout.Core.Connection;

/// <summary>Turns a Bluetooth signal strength into bars, like a phone's signal icon.</summary>
public static class SignalQuality
{
    public const int MaxBars = 4;

    /// <summary>
    /// Bars for <paramref name="rssi"/> in dBm: −60 or better is full, then one bar less every
    /// 10 dB, down to none below −90. Bluetooth LE readings jump a few dB, so these are rough.
    /// </summary>
    public static int Bars(int rssi) => rssi switch
    {
        >= -60 => 4,
        >= -70 => 3,
        >= -80 => 2,
        >= -90 => 1,
        _ => 0,
    };

    public static string Describe(int rssi) => Bars(rssi) switch
    {
        4 => "excellent",
        3 => "good",
        2 => "fair",
        1 => "weak",
        _ => "very weak",
    };
}
