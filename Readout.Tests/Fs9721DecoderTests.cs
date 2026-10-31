using Readout.Core.Display;
using Readout.Core.Fs9721;
using Readout.Core.Protocol;

namespace Readout.Tests;

public class Fs9721DecoderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static MeterReading Decode(string hex)
    {
        Assert.True(Fs9721Frame.TryParseHex(hex, out var frame), hex);
        return Fs9721Decoder.Decode(frame, Now);
    }

    /// <summary>What each label in the capture file was measuring.</summary>
    private static MeterMode ExpectedMode(string label) => label switch
    {
        _ when label.StartsWith("mVDC", StringComparison.Ordinal) => MeterMode.DcMillivolts,
        _ when label.StartsWith("VDC", StringComparison.Ordinal) => MeterMode.DcVolts,
        _ when label.StartsWith("VAC", StringComparison.Ordinal) => MeterMode.AcVolts,
        _ when label.StartsWith("OHM/Diode", StringComparison.Ordinal) => MeterMode.Diode,
        _ when label.StartsWith("OHM/Continuity", StringComparison.Ordinal) => MeterMode.Continuity,
        _ when label.StartsWith("OHM", StringComparison.Ordinal) => MeterMode.Resistance,
        _ when label.StartsWith("CAP", StringComparison.Ordinal) => MeterMode.Capacitance,
        _ when label.StartsWith("hfe", StringComparison.Ordinal) => MeterMode.Hfe,
        _ when label.StartsWith("Temp F", StringComparison.Ordinal) => MeterMode.TemperatureF,
        _ when label.StartsWith("Temp C", StringComparison.Ordinal) => MeterMode.TemperatureC,
        _ when label.StartsWith("Freq", StringComparison.Ordinal) => MeterMode.Frequency,
        _ when label.StartsWith("Duty", StringComparison.Ordinal) => MeterMode.Duty,
        "uA" => MeterMode.DcMicroamps,
        "mA" => MeterMode.DcMilliamps,
        "A" => MeterMode.DcAmps,
        _ => throw new InvalidOperationException($"No expected mode for '{label}'"),
    };

    private static IEnumerable<(string Label, string Hex)> CapturedFrames()
    {
        var label = "";
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "fs9721-890g.txt")))
        {
            if (line.StartsWith('#'))
                label = line.TrimStart('#', ' ');
            else if (line.Length > 0)
                yield return (label, line);
        }
    }

    [Fact]
    public void Decode_EveryCapturedFrame_ReadsAsTheDialPosition()
    {
        var frames = CapturedFrames().ToList();
        Assert.True(frames.Count > 300);

        foreach (var (label, hex) in frames)
        {
            var r = Decode(hex);
            Assert.Equal(ExpectedMode(label), r.Mode);
            Assert.DoesNotContain('?', r.MainText);
            Assert.Equal(r.MainOverload, r.Value is null);
            Assert.Equal(MeterModel.Fs9721, r.Model);
            Assert.Equal(hex.Replace(" ", ""), Convert.ToHexString(r.Raw.Span), ignoreCase: true);
        }
    }

    [Theory]
    // Each a frame the meter sent, with what its LCD showed.
    [InlineData("17 25 3b 4f 5f 67 7f 87 9f a0 b0 c0 d4 e8", "2.888", "V", 2.888, 2.888, MeterMode.DcVolts)]
    [InlineData("17 2d 3b 4f 5f 67 7f 81 95 a0 b0 c0 d4 e8", "2.887", "V", -2.887, -2.887, MeterMode.DcVolts)]
    [InlineData("1b 25 3b 49 55 65 7b 82 97 a0 b0 c0 d4 e8", "2.724", "V", 2.724, 2.724, MeterMode.AcVolts)]
    [InlineData("15 2f 3d 47 5d 67 7d 8f 9d a0 b8 c0 d4 e8", "000.0", "mV", -0.0, -0.0, MeterMode.DcMillivolts)]
    [InlineData("13 20 35 40 55 63 7f 89 9f a0 b0 c4 d0 e8", "119.3", "Ω", 119.3, 119.3, MeterMode.Resistance)]
    [InlineData("11 27 3d 47 5d 61 7f 8a 97 a0 b1 c4 d0 e8", "003.4", "Ω", 3.4, 3.4, MeterMode.Continuity)]
    [InlineData("11 27 3d 4b 5e 67 7d 85 9b a1 b0 c0 d4 e8", "0.502", "V", 0.502, 0.502, MeterMode.Diode)]
    [InlineData("13 20 35 47 5e 69 75 82 97 a4 b0 c8 d0 e8", "16.74", "nF", 16.74, 16.74e-9, MeterMode.Capacitance)]
    [InlineData("11 27 3d 43 5e 63 7f 8b 9f a0 b0 c0 d2 e8", "059.9", "Hz", 59.9, 59.9, MeterMode.Frequency)]
    [InlineData("11 27 3d 42 57 61 75 8f 9f a0 b4 c0 d0 e8", "047.8", "%", 47.8, 47.8, MeterMode.Duty)]
    [InlineData("17 27 3d 47 5d 61 7f 89 9f a8 b0 c0 d8 e8", "003.3", "µA", 3.3, 3.3e-6, MeterMode.DcMicroamps)]
    [InlineData("17 27 3d 47 5d 6f 7d 80 95 a0 b8 c0 d8 e8", "00.01", "mA", 0.01, 0.01e-3, MeterMode.DcMilliamps)]
    [InlineData("17 27 3d 4f 5d 67 7d 87 9d a0 b0 c0 d8 e8", "0.000", "A", 0.0, 0.0, MeterMode.DcAmps)]
    [InlineData("15 27 3d 47 5d 61 75 81 95 a0 b0 c0 d0 e1", "0077", "°F", 77.0, 77.0, MeterMode.TemperatureF)]
    [InlineData("15 27 3d 47 5d 65 7b 81 9f a0 b0 c0 d0 e4", "0023", "°C", 23.0, 23.0, MeterMode.TemperatureC)]
    public void Decode_CapturedFrame_ReadsLikeTheLcd(string hex, string text, string unit, double value, double baseValue, MeterMode mode)
    {
        var r = Decode(hex);

        Assert.Equal(text, r.MainText);
        Assert.Equal(unit, r.MainUnit);
        Assert.Equal(value, r.Value!.Value, 9);
        Assert.Equal(baseValue, r.BaseValue!.Value, 15);
        Assert.Equal(mode, r.Mode);
        Assert.False(r.MainOverload);
    }

    [Fact]
    public void Decode_Hfe_HasItsOwnModeAndNoUnit()
    {
        var r = Decode("15 27 3d 47 5d 60 75 83 9f a0 b0 c0 d0 e2");

        Assert.Equal(MeterMode.Hfe, r.Mode);
        Assert.Equal("Transistor gain", r.ModeName);
        Assert.Equal("0019 hFE", r.MainLine);
        Assert.Equal(19.0, r.BaseValue);
        Assert.Equal("", r.BaseUnit);
    }

    [Fact]
    public void Readings_LogAndRoundTripThroughCsv()
    {
        var log = new Readout.Core.Logging.MeasurementLog();
        log.Add(Decode("13 20 35 47 5e 69 75 82 97 a4 b0 c8 d0 e8"));
        log.Add(Decode("15 27 3d 47 5d 60 75 83 9f a0 b0 c0 d0 e2"));

        var csv = new StringWriter();
        Readout.Core.Logging.SampleCsv.Write(log.Samples, csv);
        var back = Readout.Core.Logging.SampleCsv.Read(new StringReader(csv.ToString()));

        Assert.Equal([MeterMode.Capacitance, MeterMode.Hfe], back.Select(s => s.Mode));
        Assert.Equal(16.74e-9, back[0].Value!.Value, 15);
        Assert.Equal(("F", "16.74 nF", ""), (back[0].Unit, back[0].Display, back[0].Sub));
        Assert.Equal(("", "0019 hFE"), (back[1].Unit, back[1].Display));
    }

    [Theory]
    // The "0L" moves with the range: MΩ, kΩ and Ω.
    [InlineData("13 20 30 47 5d 6e 78 80 90 a0 b2 c4 d0 e8", "MΩ")]
    [InlineData("13 20 30 4f 5d 66 78 80 90 a2 b0 c4 d0 e8", "kΩ")]
    [InlineData("13 20 30 47 5d 66 78 88 90 a0 b0 c4 d0 e8", "Ω")]
    public void Decode_OpenLeadsOnOhms_IsOverload(string hex, string unit)
    {
        var r = Decode(hex);

        Assert.True(r.MainOverload);
        Assert.Equal($"OL {unit}", r.MainLine);
        Assert.Null(r.Value);
        Assert.Null(r.BaseValue);
    }

    [Fact]
    public void Decode_Icons_FollowTheirBits()
    {
        var auto = Decode("17 25 3b 4f 5f 67 7f 87 9f a0 b0 c0 d4 e8").Icons;
        var relative = Decode("15 27 3d 4f 5d 67 7d 87 9d a0 b0 c2 d4 e8").Icons;
        var continuity = Decode("11 20 30 47 5d 66 78 88 90 a0 b1 c4 d0 e8").Icons;

        Assert.True(auto.Dc && auto.Auto && !auto.Relative && !auto.Ac);
        Assert.True(relative.Relative && relative.Dc && !relative.Auto);
        Assert.True(continuity.Beep && !continuity.Diode);
    }

    [Fact]
    public void Decode_HoldAndLowBatteryBits_LightTheirIcons()
    {
        // 2.888 V with HOLD (byte 11, bit 0) and low battery (byte 12, bit 0) set by hand.
        var icons = Decode("17 25 3b 4f 5f 67 7f 87 9f a0 b0 c1 d5 e8").Icons;
        Assert.True(icons.Hold && icons.LowBattery);
    }

    [Fact]
    public void Decode_OtherUserSymbols_FollowTheGivenMap()
    {
        Assert.True(Fs9721Frame.TryParseHex("15 27 3d 47 5d 65 7b 81 9f a0 b0 c0 d0 e4", out var frame));
        // A meter whose bit 2 is °F rather than °C.
        var r = Fs9721Decoder.Decode(frame, Now, ["", "hFE", "°F", ""]);
        Assert.Equal("0023 °F", r.MainLine);
        Assert.Equal(MeterMode.TemperatureF, r.Mode);
    }

    [Fact]
    public void TryParse_ByteOutOfPlace_IsRejected()
    {
        Assert.False(Fs9721Frame.TryParseHex("17 25 3b 4f 5f 67 7f 87 9f a0 b0 c0 e8 d4", out _));
        Assert.False(Fs9721Frame.TryParseHex("17 25 3b", out _));
        Assert.False(Fs9721Frame.TryParseHex("not hex", out _));
    }
}

public class Fs9721FramerTests
{
    private static readonly byte[] Frame = Convert.FromHexString("17253b4f5f677f879fa0b0c0d4e8");

    [Fact]
    public void Push_FrameSplitAcrossChunks_ComesOutWhole()
    {
        var framer = new Fs9721Framer();

        Assert.Empty(framer.Push(Frame.AsSpan(0, 5)));
        Assert.Empty(framer.Push(Frame.AsSpan(5, 6)));
        var frame = Assert.Single(framer.Push(Frame.AsSpan(11)));

        Assert.Equal(Frame, frame.Bytes.ToArray());
    }

    [Fact]
    public void Push_SeveralFramesAtOnce_ComeOutInOrder() =>
        Assert.Equal(3, new Fs9721Framer().Push([.. Frame, .. Frame, .. Frame]).Count);

    [Fact]
    public void Push_StartedMidFrame_SyncsOnTheNextFrame()
    {
        // Listening started halfway through a frame, as when the bridge connects.
        var framer = new Fs9721Framer();
        var frames = framer.Push([.. Frame.AsSpan(6), .. Frame]);

        Assert.Single(frames);
        Assert.Equal(8, framer.RejectedBytes);
    }

    [Fact]
    public void Push_DroppedByte_CostsOnlyThatFrame()
    {
        var damaged = Frame.Where((_, i) => i != 4).ToArray();
        var frames = new Fs9721Framer().Push([.. damaged, .. Frame]);
        Assert.Single(frames);
    }
}
