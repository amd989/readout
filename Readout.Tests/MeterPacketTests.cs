using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Tests;

public class MeterPacketTests
{
    /// <summary>
    /// Built by hand from the byte layout, not with PacketBuilder, so a shared mistake in the
    /// builder and the parser can't hide here.
    /// </summary>
    internal static readonly byte[] HandBuilt =
    [
        0xF2,       // start
        0x11,       // 2017
        0xA1,       // month 10, serial digit 1
        0x23, 0x45, // serial digits 2345
        0x01,       // DC volts, value bits 16-17 = 0
        0x41,       // negative, range 1
        0x30, 0x39, // 12345
        0x05,       // sub: temperature
        0x01,       // sub: one decimal
        0x00, 0xEB, // sub: 235
        0x01,       // bar on, scale 50
        0x06,       // bar value 6
        0x0C,       // status 1: DC, auto
        0x40,       // status 2: Bluetooth
        0x08,       // status 3: HOLD
        0xC1,       // XOR of the above
    ];

    [Fact]
    public void Checksum_OfHandBuiltPacket_IsItsLastByte() =>
        Assert.Equal(0xC1, MeterPacket.Checksum(HandBuilt.AsSpan(0, 18)));

    [Fact]
    public void TryParse_HandBuiltPacket_DecodesEveryField()
    {
        Assert.True(MeterPacket.TryParse(HandBuilt, out var p));

        Assert.Equal(2017, p.Year);
        Assert.Equal(10, p.Month);
        Assert.Equal(12345, p.Serial);
        Assert.Equal(MeterMode.DcVolts, p.Mode);
        Assert.True(p.MainNegative);
        Assert.False(p.MainOverload);
        Assert.Equal(1, p.MainRangeIndex);
        Assert.Equal(12345, p.MainRawValue);
        Assert.Equal(MeterMode.Temperature, p.SubMode);
        Assert.Equal(1, p.SubDecimals);
        Assert.Equal(235, p.SubRawValue);
        Assert.True(p.BarOn);
        Assert.Equal(BarScale.Fifty, p.BarScale);
        Assert.Equal(6, p.BarValue);
        Assert.Equal(Coupling.Dc, p.Coupling);
        Assert.True(p.AutoRange);
        Assert.True(p.Bluetooth);
        Assert.Equal(HoldState.Hold, p.Hold);
        Assert.False(p.Relative);
        Assert.Equal(MinMaxState.Off, p.MinMax);
    }

    [Fact]
    public void TryParse_BadChecksum_IsRejected()
    {
        var bytes = (byte[])HandBuilt.Clone();
        bytes[8] ^= 0x01;
        Assert.False(MeterPacket.TryParse(bytes, out _));
    }

    [Fact]
    public void TryParse_WrongStartByte_IsRejected()
    {
        var bytes = (byte[])HandBuilt.Clone();
        bytes[0] = 0xF3;
        bytes[18] ^= 0xF2 ^ 0xF3; // keep the checksum right, so only the start byte is wrong
        Assert.False(MeterPacket.TryParse(bytes, out _));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(20)]
    public void TryParse_WrongLength_IsRejected(int length) =>
        Assert.False(MeterPacket.TryParse(new byte[length], out _));

    [Fact]
    public void MainRawValue_UsesTheTwoHighBitsInTheModeByte()
    {
        var p = new PacketBuilder { Mode = MeterMode.Resistance, MainRawValue = 0x3FFFF }.BuildPacket();
        Assert.Equal(0x3FFFF, p.MainRawValue);
        Assert.Equal(MeterMode.Resistance, p.Mode);
    }

    [Theory]
    [InlineData(true, false, MeterMode.TemperatureC)]
    [InlineData(false, true, MeterMode.TemperatureF)]
    [InlineData(false, false, MeterMode.Temperature)]
    public void Mode_Temperature_ResolvesFromTheUnitBits(bool celsius, bool fahrenheit, MeterMode expected)
    {
        var p = new PacketBuilder { Mode = MeterMode.Temperature, MainCelsius = celsius, MainFahrenheit = fahrenheit }.BuildPacket();
        Assert.Equal(expected, p.Mode);
        Assert.Equal(MeterMode.Temperature, p.RawMode);
    }

    [Fact]
    public void BuilderAndParser_RoundTripEveryFlag()
    {
        var p = new PacketBuilder
        {
            Serial = 98765, Year = 2024, Month = 3,
            Mode = MeterMode.AcMilliamps, MainRangeIndex = 1, MainRawValue = 4321, MainOverload = true,
            SubMode = MeterMode.Battery, SubRawValue = 891, SubDecimals = 4, SubOverload = true, SubNegative = true, SubKilo = true, SubHertz = true,
            BarOn = false, BarTicks150 = true, BarNegative = true, BarScale = BarScale.OneThousand, BarValue = 31,
            CelsiusIcon = true, FahrenheitIcon = true, LowPassFilter = true, OneMsPeak = true, Coupling = Coupling.AcPlusDc, AutoPowerOff = true, LowBattery = true,
            Bluetooth = false, Arrow = true, Relative = true, Decibels = true, MinMax = MinMaxState.All,
            Test = true, Memory = 3, Hold = HoldState.AutoHold, AcIcon = true, DcIcon = true,
        }.BuildPacket();

        Assert.Equal((98765, 2024, 3), (p.Serial, p.Year, p.Month));
        Assert.Equal((MeterMode.AcMilliamps, 1, 4321, true), (p.Mode, p.MainRangeIndex, p.MainRawValue, p.MainOverload));
        Assert.Equal((MeterMode.Battery, 891, 4), (p.SubMode, p.SubRawValue, p.SubDecimals));
        Assert.True(p.SubOverload && p.SubNegative && p.SubKilo && p.SubHertz);
        Assert.False(p.BarOn);
        Assert.True(p.BarTicks150 && p.BarNegative);
        Assert.Equal((BarScale.OneThousand, 31), (p.BarScale, p.BarValue));
        Assert.True(p.CelsiusIcon && p.FahrenheitIcon);
        Assert.True(p.LowPassFilter && p.OneMsPeak && p.AutoPowerOff && p.LowBattery);
        Assert.Equal(Coupling.AcPlusDc, p.Coupling);
        Assert.False(p.AutoRange || p.Bluetooth);
        Assert.True(p.Arrow && p.Relative && p.Decibels);
        Assert.Equal(MinMaxState.All, p.MinMax);
        Assert.True(p.Test && p.AcIcon && p.DcIcon);
        Assert.Equal((3, HoldState.AutoHold), (p.Memory, p.Hold));
    }
}
