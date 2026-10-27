using System.Globalization;
using Readout.Core.Display;

namespace Readout.Tests;

public class SiFormatTests
{
    [Theory]
    [InlineData(0.0123, "V", "12.3 mV")]
    [InlineData(12.345, "V", "12.35 V")]
    [InlineData(4700.0, "Ω", "4.7 kΩ")]
    [InlineData(12345000.0, "Ω", "12.35 MΩ")]
    [InlineData(1e-7, "F", "100 nF")]
    [InlineData(-0.5, "A", "-500 mA")]
    [InlineData(0.0, "V", "0 V")]
    [InlineData(23.45, "°C", "23.45 °C")]
    [InlineData(50.0, "%", "50 %")]
    [InlineData(0.9999, "V", "0.9999 V")]
    public void Format_PicksAReadablePrefix(double value, string unit, string expected) =>
        Assert.Equal(expected, SiFormat.Format(value, unit, provider: CultureInfo.InvariantCulture));

    [Fact]
    public void Format_NaN_IsADash() =>
        Assert.Equal("– V", SiFormat.Format(double.NaN, "V", provider: CultureInfo.InvariantCulture));
}
