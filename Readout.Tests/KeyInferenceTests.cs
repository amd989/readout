using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Tests;

/// <summary>Pairs of packets recorded from a real 121GW before and after a key press.</summary>
public sealed class KeyInferenceTests
{
    private const string AcVoltsAuto = "F2178000000200000006120000000016400025";

    private static MeterPacket P(string hex)
    {
        Assert.True(MeterPacket.TryParse(Convert.FromHexString(hex), out var packet));
        return packet;
    }

    [Theory]
    [InlineData(AcVoltsAuto, "F217800000020000C5061200000000124008EC", MeterKey.Hold, false)]
    [InlineData(AcVoltsAuto, "F2178000000200000006120000000012500031", MeterKey.Rel, false)]
    [InlineData(AcVoltsAuto, "F2178000000200000006120000000056400065", MeterKey.Rel, true)]
    [InlineData(AcVoltsAuto, "F2178000000200005906120000000012410079", MeterKey.MinMax, false)]
    [InlineData("F2178000000200013B0612000000001244001F", "F2178000000200015406120000000016400070", MeterKey.MinMax, true)]
    [InlineData(AcVoltsAuto, "F2178000000200000006120000000032410000", MeterKey.Peak, false)]
    [InlineData("F2178000000200000006120000000032410000", AcVoltsAuto, MeterKey.Peak, false)]
    [InlineData(AcVoltsAuto, "F21780000002000000640100F600001E4000AA", MeterKey.Mode, false)]
    [InlineData("F21780000000000000640100F60B00024000BF", "F217800000000000006E01003E0B000240007D", MeterKey.Setup, false)]
    [InlineData(AcVoltsAuto, "F217800000020001F5061200000000124000D5", MeterKey.Range, false)]
    [InlineData("F2178000000201000006120000010012400021", "F2178000000202000006120000020012400021", MeterKey.Range, false)]
    [InlineData("F21780000002030000061200000B0012400029", AcVoltsAuto, MeterKey.Range, true)]
    public void Infer_RecordedPress(string before, string after, MeterKey key, bool longPress) =>
        Assert.Equal(new MeterKeyPress(key, longPress), KeyInference.Infer(P(before), P(after)));

    [Theory]
    // The same packet, and a new reading only.
    [InlineData(AcVoltsAuto, AcVoltsAuto)]
    [InlineData("F2178000000200016506120000000016400041", "F2178000000200016606120000000016400042")]
    // The dial turned: AC V to DC mV.
    [InlineData(AcVoltsAuto, "F21780000003400030640100F705000E4000CF")]
    // The VA position's small display switching from amps to volts by itself.
    [InlineData("F2178000000F0000001402000000001640002A", "F2178000000F00000002040152000016400069")]
    // Auto-ranging on its own.
    [InlineData("F21780000009060000640100F701220640009D", "F21780000009000000640100F7012E06400097")]
    public void Infer_NoPress(string before, string after) =>
        Assert.Null(KeyInference.Infer(P(before), P(after)));
}
