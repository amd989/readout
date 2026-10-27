using Readout.Core.Protocol;

namespace Readout.Core.Display;

/// <summary>
/// What each main mode measures and how its ranges are scaled.
/// </summary>
/// <param name="Name">Shown above the reading.</param>
/// <param name="BaseUnit">The SI unit logged values are converted to.</param>
/// <param name="ImpliedPrefix">A prefix the mode always has, which its range notation leaves out (µA, ms).</param>
/// <param name="IntegerDigits">Per range: how many of the 5 LCD digits come before the decimal point.</param>
/// <param name="Notation">Per range: the SI prefix ('n', 'u', 'm', 'k', 'M' or ' ').</param>
internal sealed record ModeInfo(string Name, string BaseUnit, char ImpliedPrefix, int[] IntegerDigits, string Notation)
{
    /// <summary>
    /// Indexed by <see cref="MeterMode"/> 0–26. The ranges are the official app's
    /// <c>mRangeLookup</c> from its shipped (PrivatePostRelease) branch.
    /// </summary>
    private static readonly ModeInfo[] Table =
    [
        new("Low-Z voltage", "V", ' ', [4], " "),                         // 0
        new("DC voltage", "V", ' ', [1, 2, 3, 4], "    "),                // 1
        new("AC voltage", "V", ' ', [1, 2, 3, 4], "    "),                // 2
        new("DC millivolts", "V", ' ', [2, 3], "mm"),                     // 3
        new("AC millivolts", "V", ' ', [2, 3], "mm"),                     // 4
        new("Temperature", "°C", ' ', [4], " "),                          // 5
        new("Frequency", "Hz", ' ', [2, 3, 1, 2, 3], "  kkk"),            // 6
        new("Pulse width", "s", 'm', [1, 2, 3], "   "),                   // 7
        new("Duty cycle", "%", ' ', [4], " "),                            // 8
        new("Resistance", "Ω", ' ', [2, 3, 1, 2, 3, 1, 2], "  kkkMM"),    // 9
        new("Continuity", "Ω", ' ', [3], " "),                            // 10
        new("Diode", "V", ' ', [1, 2], "  "),                             // 11
        new("Capacitance", "F", ' ', [3, 4, 2, 3, 4, 5], "nnuuuu"),       // 12
        new("AC power", "VA", 'u', [3, 4, 4, 5], "    "),                 // 13
        new("AC power", "VA", ' ', [2, 3, 3, 4], "mm  "),                 // 14
        new("AC power", "VA", ' ', [4, 5, 2, 3], "mm  "),                 // 15
        new("AC current", "A", 'u', [2, 3], "  "),                        // 16
        new("DC current", "A", 'u', [2, 3], "  "),                        // 17
        new("AC current", "A", ' ', [1, 2], "mm"),                        // 18
        new("DC current", "A", ' ', [1, 2], "mm"),                        // 19
        new("AC current", "A", ' ', [3, 1, 2], "m  "),                    // 20
        new("DC current", "A", ' ', [3, 1, 2], "m  "),                    // 21
        new("DC power", "VA", 'u', [3, 4, 4, 5], "    "),                 // 22
        new("DC power", "VA", ' ', [2, 3, 3, 4], "mm  "),                 // 23
        new("DC power", "VA", ' ', [4, 5, 2, 3], "mm  "),                 // 24
        new("Temperature", "°C", ' ', [4], " "),                          // 25
        new("Temperature", "°F", ' ', [4], " "),                          // 26
    ];

    public static ModeInfo? For(MeterMode mode) =>
        (int)mode < Table.Length ? Table[(int)mode] : null;

    public bool HasRange(int index) => index >= 0 && index < IntegerDigits.Length;

    /// <summary>The prefix for a range: its notation, or the mode's implied one.</summary>
    public char PrefixFor(int range) =>
        Notation[range] != ' ' ? Notation[range] : ImpliedPrefix;
}

internal static class SiPrefix
{
    public static string Symbol(char prefix) => prefix switch
    {
        'n' => "n",
        'u' => "µ",
        'm' => "m",
        'k' or 'K' => "k",
        'M' => "M",
        _ => "",
    };

    public static double Multiplier(char prefix) => prefix switch
    {
        'n' => 1e-9,
        'u' => 1e-6,
        'm' => 1e-3,
        'k' or 'K' => 1e3,
        'M' => 1e6,
        _ => 1,
    };
}
