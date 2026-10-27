using System.Globalization;

namespace Readout.Core.Display;

/// <summary>Formats a value in an SI base unit with a readable prefix: 0.0123 V → "12.3 mV".</summary>
public static class SiFormat
{
    private static readonly (double Factor, string Symbol)[] Prefixes =
    [
        (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p"),
    ];

    // Units that never take a prefix.
    private static readonly HashSet<string> Plain = ["%", "°C", "°F", "dBm", ""];

    /// <param name="significant">Significant digits to show.</param>
    public static string Format(double value, string unit, int significant = 4, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return $"– {unit}".TrimEnd();

        if (Plain.Contains(unit) || value == 0)
            return $"{Round(value, significant).ToString("G" + significant, provider)} {unit}".TrimEnd();

        var magnitude = Math.Abs(value);
        var (factor, symbol) = Prefixes[^1];
        foreach (var prefix in Prefixes)
        {
            if (magnitude >= prefix.Factor * 0.9995)
            {
                (factor, symbol) = prefix;
                break;
            }
        }

        var scaled = Round(value / factor, significant);
        return $"{scaled.ToString("0.####", provider)} {symbol}{unit}";
    }

    private static double Round(double value, int significant)
    {
        if (value == 0) return 0;
        var digits = significant - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value)));
        return digits is >= 0 and <= 15 ? Math.Round(value, digits, MidpointRounding.AwayFromZero) : value;
    }
}
