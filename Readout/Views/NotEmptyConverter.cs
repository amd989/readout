using System.Globalization;

namespace Readout.Views;

/// <summary>True for a non-empty string: hides labels that have nothing to say.</summary>
public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
