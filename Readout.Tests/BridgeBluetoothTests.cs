using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Tests;

public class BridgeBluetoothTests
{
    [Theory]
    [InlineData("Readout FS9721 A1B2", MeterModel.Fs9721)]
    [InlineData("readout fs9721 00FF", MeterModel.Fs9721)]
    [InlineData("Readout FS9721", MeterModel.Fs9721)]
    public void ModelFromName_Bridge_IsItsChip(string name, MeterModel model) =>
        Assert.Equal(model, BridgeBluetooth.ModelFromName(name));

    [Theory]
    [InlineData("121GW")]
    [InlineData("Readout ES51922 A1B2")]
    [InlineData("Readout")]
    [InlineData("")]
    [InlineData(null)]
    public void ModelFromName_NotAKnownBridge_IsNull(string? name) =>
        Assert.Null(BridgeBluetooth.ModelFromName(name));

    [Fact]
    public void BridgeNames_AreNotTakenFor121GWs() =>
        Assert.False(MeterBluetooth.IsMeterName("Readout FS9721 A1B2"));

    [Fact]
    public void Firmware_UsesTheSameUuids()
    {
        // The firmware can't share this file, so check it hasn't drifted.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Readout.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var firmware = File.ReadAllText(Path.Combine(dir.FullName, "firmware", "fs9721-bridge", "src", "main.cpp"));

        foreach (var uuid in new[] { BridgeBluetooth.ServiceUuid, BridgeBluetooth.DataCharacteristicUuid, BridgeBluetooth.InfoCharacteristicUuid })
            Assert.Contains(uuid.ToString(), firmware, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Readout %s %02X%02X\"", firmware, StringComparison.Ordinal);
    }
}
