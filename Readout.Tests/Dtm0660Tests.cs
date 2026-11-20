using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Dtm0660;
using Readout.Core.Fs9721;
using Readout.Core.Protocol;

namespace Readout.Tests;

/// <summary>
/// No DTM0660 captures yet: these frames are built by hand from the layout libsigrok documents
/// (its digit table and flag bits), and checked against the FS9721 captures, which the DTM0660
/// sends with each byte's segment bits turned around.
/// </summary>
public class Dtm0660Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    // -1.234 V DC on auto range, MAX on: digits 1, 2, 3, 4 are sigrok's 0x0a, 0xad, 0x8f, 0x4e.
    private const string MinusVoltsMax = "1e 21 3a 4b 5d 68 7f 84 9e a0 b0 c0 d2 e0 f8";

    // "0L" kΩ: sigrok's overload digit bytes 00 eb 61 00.
    private const string OverloadKiloohms = "1c 20 30 4e 5b 66 71 80 90 a4 b0 c2 d0 e0 f0";

    // 23 °C.
    private const string TwentyThreeCelsius = "18 20 30 40 50 6a 7d 88 9f a0 b0 c0 d0 e2 f0";

    private static MeterReading Decode(string hex)
    {
        Assert.True(Dtm0660Frame.TryParseHex(hex, out var frame), hex);
        return Dtm0660Decoder.Decode(frame, Now);
    }

    [Fact]
    public void Decode_NegativeVolts_ReadsDigitsSignPointAndMax()
    {
        var r = Decode(MinusVoltsMax);

        Assert.Equal((MeterModel.Dtm0660, MeterMode.DcVolts, "1.234", "V", "-1.234 V"), (r.Model, r.Mode, r.MainText, r.MainUnit, r.MainLine));
        Assert.Equal(-1.234, r.Value!.Value, 6);
        Assert.True(r.Icons.Dc);
        Assert.True(r.Icons.Auto);
        Assert.False(r.Icons.Ac);
        Assert.True(r.Icons.Max);
        Assert.False(r.Icons.Min);
        Assert.Equal(Convert.FromHexString(MinusVoltsMax.Replace(" ", "", StringComparison.Ordinal)), r.Raw.ToArray());
    }

    [Fact]
    public void Decode_Overload_IsOlWithTheRangeUnit()
    {
        var r = Decode(OverloadKiloohms);

        Assert.True(r.MainOverload);
        Assert.Null(r.Value);
        Assert.Equal((MeterMode.Resistance, "kΩ", "OL kΩ"), (r.Mode, r.MainUnit, r.MainLine));
    }

    [Fact]
    public void Decode_Celsius_IsTemperature()
    {
        var r = Decode(TwentyThreeCelsius);

        Assert.Equal((MeterMode.TemperatureC, "°C", 23.0), (r.Mode, r.MainUnit, r.Value));
    }

    [Theory]
    [InlineData(0x1, false, false, true)]
    [InlineData(0x2, true, false, false)]
    [InlineData(0x8, false, true, false)]
    public void Decode_ByteFourteen_IsMinMaxAndAutoPowerOff(int nibble, bool min, bool max, bool autoPowerOff)
    {
        var r = Decode(MinusVoltsMax[..^2] + $"f{nibble:x}");

        Assert.Equal((min, max, autoPowerOff), (r.Icons.Min, r.Icons.Max, r.Icons.AutoPowerOff));
    }

    /// <summary>
    /// Every FS9721 capture, sent the DTM0660's way (segment bits turned around, a 15th byte
    /// added), reads the same. Byte 13 holds the 890G's own symbols, which the DTM0660 has
    /// elsewhere, so frames lighting °F, hFE or °C are left out and the rest send it blank (the
    /// 890G lights its fourth, unprinted symbol all the time).
    /// </summary>
    [Fact]
    public void Decode_FsCapturesSentTheDtm0660Way_ReadTheSame()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "fs9721-890g.txt");
        var checkedFrames = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            Assert.True(Fs9721Frame.TryParseHex(line, out var fs));
            if ((fs.Bytes[13] & 0x07) != 0)
                continue;

            var dtm = new byte[Dtm0660Frame.Length];
            for (var i = 0; i < Fs9721Frame.Length; i++)
                dtm[i] = (byte)(fs.Bytes[i] & 0xF0 | Reverse(fs.Bytes[i] & 0x0F));
            dtm[13] = 0xE0;
            dtm[14] = 0xF0;
            Assert.True(Dtm0660Frame.TryParse(dtm, out var frame));

            var expected = Fs9721Decoder.Decode(fs, Now);
            var actual = Dtm0660Decoder.Decode(frame, Now);
            Assert.Equal((expected.Mode, expected.MainLine, expected.Value, expected.Icons), (actual.Mode, actual.MainLine, actual.Value, actual.Icons));
            checkedFrames++;
        }
        Assert.True(checkedFrames > 200, $"only {checkedFrames} frames");

        static int Reverse(int n) => (n & 1) << 3 | (n & 2) << 1 | (n & 4) >> 1 | (n & 8) >> 3;
    }

    [Fact]
    public void Driver_FramesSplitAcrossChunks_AfterNoise_GiveOneReadingEach()
    {
        var driver = MeterDrivers.For(MeterModel.Dtm0660);
        var frame = Convert.FromHexString(MinusVoltsMax.Replace(" ", "", StringComparison.Ordinal));

        Assert.Empty(driver.Receive([0xF0, 0x33, .. frame[..6]], Now));
        var readings = driver.Receive([.. frame[6..], .. frame[..9]], Now);
        Assert.Equal("-1.234 V", Assert.Single(readings).Reading.MainLine);
        readings = driver.Receive(frame.AsSpan(9), Now);
        Assert.Equal(MeterModel.Dtm0660, Assert.Single(readings).Reading.Model);
    }

    [Fact]
    public void Driver_FsNineTwentyOneFrames_GiveNothing()
    {
        // A meter whose EEPROM picks the 14-byte FS9721 format needs the FS9721 bridge build.
        var driver = new Dtm0660Driver();
        var fs = Convert.FromHexString("17253b4f5f677f879fa0b0c0d4e8");

        Assert.Empty(driver.Receive([.. fs, .. fs, .. fs], Now));
    }

    [Fact]
    public void TryParse_WrongLengthOrNumbering_IsFalse()
    {
        Assert.False(Dtm0660Frame.TryParseHex("17253b4f5f677f879fa0b0c0d4e8", out _));
        Assert.False(Dtm0660Frame.TryParseHex("1e 21 3a 4b 5d 68 7f 84 9e a0 b0 c0 d2 e0 08", out _));
        Assert.False(Dtm0660Frame.TryParseHex("not hex", out _));
    }

    [Fact]
    public void Capabilities_OnlySend()
    {
        var c = MeterCapabilities.For(MeterModel.Dtm0660);

        Assert.False(c.RemoteKeys || c.ClockSync || c.SubDisplay || c.BarGraph);
        Assert.Equal(4, c.Digits);
    }
}
