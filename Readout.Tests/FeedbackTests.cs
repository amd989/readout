using System.Buffers.Binary;
using Readout.Core.Display;
using Readout.Core.Feedback;
using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Tests;

public class ContinuityAlertTests
{
    // Continuity range: 3 integer digits, no prefix, so raw / 100 = ohms ("12.34" Ω is 1234).
    private static MeterReading Ohms(double ohms, MeterMode mode = MeterMode.Continuity, bool overload = false) =>
        ReadingDecoder.Decode(new PacketBuilder
        {
            Mode = mode,
            MainRawValue = (int)Math.Round(ohms * 100),
            MainOverload = overload,
        }.BuildPacket(), DateTimeOffset.UnixEpoch);

    [Fact]
    public void BelowOn_TurnsOn_AboveOff_TurnsOff()
    {
        var alert = new ContinuityAlert { OnBelowOhms = 30, OffAboveOhms = 50 };
        var changes = new List<bool>();
        alert.Changed += (_, on) => changes.Add(on);

        alert.Update(Ohms(10));
        alert.Update(Ohms(12));
        alert.Update(Ohms(60));

        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void BetweenTheThresholds_KeepsTheLastState()
    {
        var alert = new ContinuityAlert { OnBelowOhms = 30, OffAboveOhms = 50 };

        alert.Update(Ohms(40));
        Assert.False(alert.IsOn);

        alert.Update(Ohms(10));
        alert.Update(Ohms(40));
        Assert.True(alert.IsOn);
    }

    [Fact]
    public void OverloadOrAnotherMode_TurnsOff()
    {
        var alert = new ContinuityAlert();
        alert.Update(Ohms(1));
        alert.Update(Ohms(0, overload: true));
        Assert.False(alert.IsOn);

        alert.Update(Ohms(1));
        alert.Update(ReadingDecoder.Decode(new PacketBuilder { Mode = MeterMode.DcVolts, MainRawValue = 1 }.BuildPacket(), DateTimeOffset.UnixEpoch));
        Assert.False(alert.IsOn);
    }

    [Fact]
    public void ResistanceMode_AlsoBeeps()
    {
        var alert = new ContinuityAlert();
        alert.Update(Ohms(5, MeterMode.Resistance));
        Assert.True(alert.IsOn);
    }

    [Fact]
    public void OffBelowOn_StillTurnsOffAboveOn()
    {
        var alert = new ContinuityAlert { OnBelowOhms = 30, OffAboveOhms = 10 };
        alert.Update(Ohms(5));
        alert.Update(Ohms(31));
        Assert.False(alert.IsOn);
    }
}

public class ToneWaveTests
{
    [Fact]
    public void Create_IsAValidPcmWav()
    {
        var wav = ToneWave.Create(frequency: 2400, cycles: 1200);

        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal(ToneWave.SampleRate, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        var dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        Assert.Equal(44 + dataBytes, wav.Length);
        Assert.Equal(ToneWave.SampleRate / 2 * 2, dataBytes); // half a second of 16-bit samples
    }

    [Fact]
    public void Create_StartsAndEndsNearZero_SoItLoopsWithoutAClick()
    {
        var wav = ToneWave.Create();
        var first = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44));
        var last = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(wav.Length - 2));

        Assert.Equal(0, first);
        Assert.InRange(Math.Abs(last), 0, short.MaxValue / 2);
    }
}
