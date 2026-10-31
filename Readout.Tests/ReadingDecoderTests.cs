using Readout.Core.Display;
using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Tests;

public class ReadingDecoderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static MeterReading Decode(PacketBuilder builder) => ReadingDecoder.Decode(builder.BuildPacket(), Now);

    [Fact]
    public void Decode_HandBuiltPacket_ReadsLikeTheLcd()
    {
        Assert.True(MeterPacket.TryParse(MeterPacketTests.HandBuilt, out var packet));
        var r = ReadingDecoder.Decode(packet, Now);

        Assert.Equal("DC voltage", r.ModeName);
        Assert.Equal("12.345", r.MainText);
        Assert.Equal("V", r.MainUnit);
        Assert.Equal(-12.345, r.Value!.Value, 9);
        Assert.Equal(-12.345, r.BaseValue!.Value, 9);
        Assert.Equal("-12.345 V", r.MainLine);
        Assert.Equal("23.5 °C", r.Sub.Line);
        Assert.Null(r.Sub.Label);
        Assert.Equal(new BarGraph(true, 7, false, BarScale.Fifty, false), r.Bar);
        Assert.True(r.Icons.Dc && r.Icons.Auto && r.Icons.Bluetooth && r.Icons.Hold);
        Assert.False(r.Icons.Ac || r.Icons.AutoHold || r.Icons.Relative);
        Assert.Equal(12345, r.Serial);
    }

    [Fact]
    public void Decode_AcAndDcIconBits_LightTheStackedAcDc()
    {
        var ac = Decode(new PacketBuilder { Coupling = Coupling.None, AcIcon = true });
        var dc = Decode(new PacketBuilder { Coupling = Coupling.None, DcIcon = true });
        var none = Decode(new PacketBuilder { Coupling = Coupling.None });

        Assert.True(ac.Icons.Ac && !ac.Icons.Dc);
        Assert.True(dc.Icons.Dc && !dc.Icons.Ac);
        Assert.False(none.Icons.Ac || none.Icons.Dc);
    }

    [Fact]
    public void Decode_MinMaxAll_LightsMinMaxAndAvg()
    {
        var all = Decode(new PacketBuilder { MinMax = MinMaxState.All }).Icons;
        var avg = Decode(new PacketBuilder { MinMax = MinMaxState.Average }).Icons;

        Assert.True(all.Min && all.Max && all.Average);
        Assert.True(avg.Average && !avg.Min && !avg.Max);
    }

    [Fact]
    public void Decode_ArrowBit_LightsTheArrow() =>
        Assert.True(Decode(new PacketBuilder { Arrow = true }).Icons.Arrow);

    [Theory]
    [InlineData(12106, 2, "12.106")]
    [InlineData(5, 1, "0.0005")]
    [InlineData(123, 4, "  12.3")]
    [InlineData(0, 1, "0.0000")]
    [InlineData(50, 3, "  0.50")]
    [InlineData(99999, 5, "99999")]
    [InlineData(123456, 2, "123.456")]
    public void FormatMain_PlacesThePointLikeTheLcd(int raw, int integerDigits, string expected) =>
        Assert.Equal(expected, ReadingDecoder.FormatMain(raw, integerDigits));

    [Theory]
    [InlineData(5000, 3, "5.000")]
    [InlineData(5, 2, "0.05")]
    [InlineData(235, 1, "23.5")]
    [InlineData(42, 0, "42")]
    public void FormatSub_PlacesThePoint(int raw, int decimals, string expected) =>
        Assert.Equal(expected, ReadingDecoder.FormatSub(raw, decimals));

    [Theory]
    // mode, range, raw, main text, unit, value in base units
    [InlineData(MeterMode.Resistance, 2, 47000, "4.7000", "kΩ", 4700.0)]
    [InlineData(MeterMode.Resistance, 6, 12345, "12.345", "MΩ", 12345000.0)]
    [InlineData(MeterMode.Capacitance, 0, 10000, "100.00", "nF", 1e-7)]
    [InlineData(MeterMode.DcMicroamps, 1, 12345, "123.45", "µA", 123.45e-6)]
    [InlineData(MeterMode.DcMilliamps, 1, 12345, "12.345", "mA", 0.012345)]
    [InlineData(MeterMode.DcAmps, 0, 12345, "123.45", "mA", 0.12345)]
    [InlineData(MeterMode.DcAmps, 1, 12345, "1.2345", "A", 1.2345)]
    [InlineData(MeterMode.PulseWidth, 0, 12345, "1.2345", "ms", 0.0012345)]
    [InlineData(MeterMode.Frequency, 3, 50000, "50.000", "kHz", 50000.0)]
    [InlineData(MeterMode.DcMillivolts, 0, 12345, "12.345", "mV", 0.012345)]
    [InlineData(MeterMode.AcMicroVoltAmps, 0, 12345, "123.45", "µVA", 123.45e-6)]
    [InlineData(MeterMode.DcVoltAmps, 2, 12345, "12.345", "VA", 12.345)]
    public void Decode_ScalesEachModeAndRange(MeterMode mode, int range, int raw, string text, string unit, double baseValue)
    {
        var r = Decode(new PacketBuilder { Mode = mode, MainRangeIndex = range, MainRawValue = raw });

        Assert.Equal(text, r.MainText);
        Assert.Equal(unit, r.MainUnit);
        Assert.Equal(baseValue, r.BaseValue!.Value, baseValue * 1e-9);
    }

    [Fact]
    public void Decode_Fahrenheit_UsesTheFahrenheitUnit()
    {
        var r = Decode(new PacketBuilder { Mode = MeterMode.Temperature, MainFahrenheit = true, MainRawValue = 721 });
        Assert.Equal(MeterMode.TemperatureF, r.Mode);
        Assert.Equal("  72.1", r.MainText);
        Assert.Equal("°F", r.MainUnit);
        Assert.Equal("°F", r.BaseUnit);
    }

    [Fact]
    public void Decode_Overload_ShowsOflAndHasNoValue()
    {
        var r = Decode(new PacketBuilder { Mode = MeterMode.Resistance, MainRangeIndex = 6, MainOverload = true, MainNegative = true });
        Assert.Equal("OFL", r.MainText);
        Assert.Null(r.Value);
        Assert.Null(r.BaseValue);
        Assert.Equal("OFL MΩ", r.MainLine);
    }

    [Fact]
    public void Decode_RangeTheTableDoesNotKnow_ShowsDigitsWithoutAValue()
    {
        var r = Decode(new PacketBuilder { Mode = MeterMode.DcVolts, MainRangeIndex = 9, MainRawValue = 123 });
        Assert.Equal("123", r.MainText);
        Assert.Null(r.Value);
        Assert.Equal("", r.MainUnit);
    }

    [Fact]
    public void Decode_ModeTheTableDoesNotKnow_IsNamedByNumber()
    {
        var r = Decode(new PacketBuilder { Mode = (MeterMode)30, MainRawValue = 1 });
        Assert.Equal("Mode 30", r.ModeName);
        Assert.Null(r.Value);
    }

    [Theory]
    [InlineData(false, "°C")]
    [InlineData(true, "°F")]
    public void Decode_SubTemperature_TakesItsUnitFromTheFahrenheitSymbol(bool fahrenheit, string unit)
    {
        var r = Decode(new PacketBuilder { SubMode = MeterMode.Temperature, FahrenheitIcon = fahrenheit, SubRawValue = 721, SubDecimals = 1 });
        Assert.Equal($"72.1 {unit}", r.Sub.Line);
    }

    [Fact]
    public void Decode_SubFrequencyWithKilo_IsKilohertz()
    {
        var r = Decode(new PacketBuilder { SubMode = MeterMode.Frequency, SubKilo = true, SubRawValue = 1234, SubDecimals = 3 });
        Assert.Equal("1.234 kHz", r.Sub.Line);
    }

    [Fact]
    public void Decode_SubCurrentDuringMilliPower_ShowsMilliamps()
    {
        var r = Decode(new PacketBuilder
        {
            Mode = MeterMode.DcVoltAmps, MainRangeIndex = 0, MainRawValue = 100,
            SubMode = MeterMode.DcAmps, SubRawValue = 1500, SubDecimals = 1,
        });
        Assert.Equal("mA", r.Sub.Unit);
    }

    [Fact]
    public void Decode_SubCurrentOutsidePower_HasNoMilli()
    {
        var r = Decode(new PacketBuilder { Mode = MeterMode.DcVolts, SubMode = MeterMode.DcAmps, SubRawValue = 15 });
        Assert.Equal("A", r.Sub.Unit);
    }

    [Theory]
    [InlineData(MeterMode.Battery, "Battery", "V")]
    [InlineData(MeterMode.Year, "Year", "")]
    [InlineData(MeterMode.AutoPowerOffOn, "Auto power-off: on", "")]
    [InlineData(MeterMode.Dbm, "dBm", "dBm")]
    [InlineData((MeterMode)156, "Burden voltage", "V")]
    public void Decode_SetupPages_AreLabelled(MeterMode subMode, string label, string unit)
    {
        var r = Decode(new PacketBuilder { SubMode = subMode, SubRawValue = 42 });
        Assert.Equal(label, r.Sub.Label);
        Assert.Equal(unit, r.Sub.Unit);
    }

    // Packets recorded from a real 121GW (firmware of 2025) going through SETUP.
    [Theory]
    [InlineData("F21780000000000000780200000B0002400056", "APO.on", "Auto power-off: on")]
    [InlineData("F21780000000000000820000130B00024000BD", "2019", "Year")]
    [InlineData("F2178000000000000087000A0E0B00024000AF", "10-14", "Date")]
    [InlineData("F217800000000000008C00102A0B000240009A", "16-42", "Time")]
    [InlineData("F21780000000000000A00000040B0002400088", "4", "LCD contrast")]
    public void Decode_RecordedSetupPages(string hex, string text, string label)
    {
        Assert.True(MeterPacket.TryParse(Convert.FromHexString(hex), out var packet));
        var r = ReadingDecoder.Decode(packet, DateTimeOffset.UnixEpoch);
        Assert.Equal(text, r.Sub.Text);
        Assert.Equal(label, r.Sub.Label);
    }

    [Theory]
    [InlineData("Auto power-off: on", ReadingDecoder.BuzzerPage)]
    [InlineData(ReadingDecoder.BuzzerPage, ReadingDecoder.BuzzerPage)]
    [InlineData("Time", ReadingDecoder.IdOrIntervalPage)]
    [InlineData(null, ReadingDecoder.IdOrIntervalPage)]
    public void Decode_UnnumberedSetupPage_NamedByThePageBefore(string? previous, string expected)
    {
        // Sub mode 0 with no value: what the meter sends for the buzzer, ID and interval pages.
        Assert.True(MeterPacket.TryParse(Convert.FromHexString("F21780000000000000000000000B000240002C"), out var packet));
        var r = ReadingDecoder.Decode(packet, DateTimeOffset.UnixEpoch, previous);
        Assert.Equal(expected, r.Sub.Label);
        Assert.Equal("", r.Sub.Text);
        Assert.Equal("", r.Sub.Unit);
    }

    [Fact]
    public void Decode_AcPlusDc_IsNamedSo()
    {
        Assert.True(MeterPacket.TryParse(Convert.FromHexString("F21780000002000000640100F600001E4000AA"), out var packet));
        Assert.Equal("AC+DC voltage", ReadingDecoder.Decode(packet, DateTimeOffset.UnixEpoch).ModeName);
    }

    [Fact]
    public void Decode_BarOff_HasNoSegments()
    {
        var r = Decode(new PacketBuilder { BarOn = false, BarValue = 12 });
        Assert.False(r.Bar.On);
        Assert.Equal(0, r.Bar.Segments);
    }

    [Fact]
    public void Decode_BarValueAboveTheLcd_IsCapped()
    {
        var r = Decode(new PacketBuilder { BarValue = 31 });
        Assert.Equal(BarGraph.TotalSegments, r.Bar.Segments);
    }

    [Theory]
    [InlineData(MinMaxState.Max, true, false, false)]
    [InlineData(MinMaxState.Min, false, true, false)]
    [InlineData(MinMaxState.Average, false, false, true)]
    [InlineData(MinMaxState.All, true, true, true)]
    public void Decode_MinMax_LightsTheRightLabels(MinMaxState state, bool max, bool min, bool avg)
    {
        var icons = Decode(new PacketBuilder { MinMax = state }).Icons;
        Assert.Equal((max, min, avg), (icons.Max, icons.Min, icons.Average));
    }

    [Fact]
    public void Decode_AcPlusDc_LightsAllThree()
    {
        var icons = Decode(new PacketBuilder { Mode = MeterMode.DcVolts, Coupling = Coupling.AcPlusDc }).Icons;
        Assert.True(icons.Ac && icons.Dc && icons.AcPlusDc);
    }

    [Fact]
    public void Decode_AutoHold_LightsHoldAndTheA()
    {
        var icons = Decode(new PacketBuilder { Hold = HoldState.AutoHold }).Icons;
        Assert.True(icons.Hold && icons.AutoHold);
    }

    [Fact]
    public void SimulatedMeter_SetupStepsThroughThePages()
    {
        var meter = new Readout.Core.Simulation.SimulatedMeter { Clock = () => new DateTime(2026, 10, 31, 9, 5, 0) };
        var labels = new List<string?>();
        string? previous = null;
        for (var i = 0; i < 10; i++)
        {
            meter.Press(MeterKey.Setup);
            Assert.True(MeterPacket.TryParse(meter.PacketAt(TimeSpan.FromSeconds(i)), out var packet));
            var r = ReadingDecoder.Decode(packet, DateTimeOffset.UnixEpoch, previous);
            labels.Add(r.Sub.Label);
            previous = r.Sub.Label;
            if (r.Sub.Label == "Time") Assert.Equal("09-05", r.Sub.Text);
        }
        Assert.Equal(new string?[]
        {
            "Battery", "Auto power-off: on", ReadingDecoder.BuzzerPage, "LCD contrast", "Year", "Date", "Time",
            ReadingDecoder.IdOrIntervalPage, ReadingDecoder.IdOrIntervalPage, ReadingDecoder.MeterTemperaturePage,
        }, labels);
    }
}
