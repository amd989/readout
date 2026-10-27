using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Tests;

public class PacketFramerTests
{
    private static byte[] Packet(int value) => new PacketBuilder { MainRawValue = value }.Build();

    [Fact]
    public void Push_PacketSplitAcrossNotifications_ComesOutWhole()
    {
        var framer = new PacketFramer();
        var bytes = Packet(1);

        Assert.Empty(framer.Push(bytes.AsSpan(0, 5)));
        Assert.Empty(framer.Push(bytes.AsSpan(5, 10)));
        var packets = framer.Push(bytes.AsSpan(15));

        Assert.Equal(1, Assert.Single(packets).MainRawValue);
    }

    [Fact]
    public void Push_SeveralPacketsAtOnce_ComeOutInOrder()
    {
        var framer = new PacketFramer();
        var packets = framer.Push([.. Packet(1), .. Packet(2), .. Packet(3)]);
        Assert.Equal([1, 2, 3], packets.Select(p => p.MainRawValue));
    }

    [Fact]
    public void Push_GarbageBeforeAPacket_IsSkipped()
    {
        var framer = new PacketFramer();
        var packets = framer.Push([0x00, 0x13, 0xF2, 0x99, .. Packet(7)]);
        Assert.Equal(7, Assert.Single(packets).MainRawValue);
    }

    [Fact]
    public void Push_CorruptPacket_DoesNotCostTheNextOne()
    {
        var framer = new PacketFramer();
        var corrupt = Packet(1);
        corrupt[8] ^= 0xFF;

        var packets = framer.Push([.. corrupt, .. Packet(2)]);

        Assert.Equal(2, Assert.Single(packets).MainRawValue);
        Assert.True(framer.RejectedFrames >= 1);
    }

    [Fact]
    public void Push_PartialThenPacket_KeepsTheTailForLater()
    {
        var framer = new PacketFramer();
        var first = Packet(4);
        var second = Packet(5);

        var a = framer.Push([.. first, .. second.AsSpan(0, 3)]);
        var b = framer.Push(second.AsSpan(3));

        Assert.Equal(4, Assert.Single(a).MainRawValue);
        Assert.Equal(5, Assert.Single(b).MainRawValue);
    }

    [Fact]
    public void Push_EndlessGarbage_StaysBounded()
    {
        var framer = new PacketFramer();
        var noise = Enumerable.Repeat((byte)0xF2, 5000).ToArray();
        Assert.Empty(framer.Push(noise));
        Assert.Equal(9, Assert.Single(framer.Push(Packet(9))).MainRawValue);
    }
}

public class MeterCommandsTests
{
    [Fact]
    public void KeyPress_ShortHold_MatchesTheOfficialApp() =>
        Assert.Equal(new byte[] { 0xF4, 0x30, 0x32, 0x30, 0x32 }, MeterCommands.KeyPress(MeterKey.Hold));

    [Fact]
    public void KeyPress_LongSetup_MatchesTheOfficialApp() =>
        Assert.Equal(new byte[] { 0xF4, 0x38, 0x38, 0x38, 0x38 }, MeterCommands.KeyPress(MeterKey.Setup, longPress: true));

    [Fact]
    public void KeyPress_LongMode_UsesKeyFive() =>
        Assert.Equal(new byte[] { 0xF4, 0x38, 0x35, 0x38, 0x35 }, MeterCommands.KeyPress(MeterKey.Mode, longPress: true));

    [Fact]
    public void KeyPress_UnknownKey_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterCommands.KeyPress((MeterKey)9));

    [Theory]
    [InlineData("121GW")]
    [InlineData("EEVblog 121GW")]
    [InlineData("Bluegiga CR Demo")]
    public void IsMeterName_AcceptsTheMeter(string name) => Assert.True(MeterBluetooth.IsMeterName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AirPods")]
    public void IsMeterName_RejectsOthers(string? name) => Assert.False(MeterBluetooth.IsMeterName(name));
}
