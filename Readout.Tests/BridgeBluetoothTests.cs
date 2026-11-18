using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Tests;

public class BridgeBluetoothTests
{
    [Theory]
    [InlineData("Readout FS9721 A1B2", MeterModel.Fs9721)]
    [InlineData("readout fs9721 00FF", MeterModel.Fs9721)]
    [InlineData("Readout FS9721", MeterModel.Fs9721)]
    [InlineData("Readout DSO150 A1B2", MeterModel.Dso150)]
    [InlineData("readout dso150 00ff", MeterModel.Dso150)]
    public void ModelFromName_Bridge_IsItsChip(string name, MeterModel model) =>
        Assert.Equal(model, BridgeBluetooth.ModelFromName(name));

    [Theory]
    [InlineData("121GW")]
    [InlineData("Readout ES51922 A1B2")]
    [InlineData("Readout DSO1501 A1B2")]
    [InlineData("DSO150")]
    [InlineData("Readout")]
    [InlineData("")]
    [InlineData(null)]
    public void ModelFromName_NotAKnownBridge_IsNull(string? name) =>
        Assert.Null(BridgeBluetooth.ModelFromName(name));

    [Theory]
    [InlineData("Readout FS9721 A1B2")]
    [InlineData("Readout DSO150 A1B2")]
    public void BridgeNames_AreNotTakenFor121GWs(string name) =>
        Assert.False(MeterBluetooth.IsMeterName(name));

    [Fact]
    public void Uuids_AreDistinct()
    {
        Guid[] uuids = [BridgeBluetooth.ServiceUuid, BridgeBluetooth.DataCharacteristicUuid, BridgeBluetooth.InfoCharacteristicUuid,
                        BridgeBluetooth.RxCharacteristicUuid, MeterBluetooth.DataCharacteristicUuid];
        Assert.Equal(uuids.Length, uuids.Distinct().Count());
    }

    [Fact]
    public void Firmware_UsesTheSameUuids()
    {
        // The firmware can't share this file, so check it hasn't drifted.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Readout.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var firmware = File.ReadAllText(Path.Combine(dir.FullName, "firmware", "readout-bridge", "src", "main.cpp"));

        foreach (var uuid in new[] { BridgeBluetooth.ServiceUuid, BridgeBluetooth.DataCharacteristicUuid, BridgeBluetooth.InfoCharacteristicUuid, BridgeBluetooth.RxCharacteristicUuid })
            Assert.Contains(uuid.ToString(), firmware, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Readout %s %02X%02X\"", firmware, StringComparison.Ordinal);

        // Every chip it builds for is one the app recognises by name.
        foreach (var chip in new[] { "FS9721", "DSO150" })
        {
            Assert.Contains($"#define CHIP_NAME \"{chip}\"", firmware, StringComparison.Ordinal);
            Assert.NotNull(BridgeBluetooth.ModelFromName($"Readout {chip} A1B2"));
        }
        Assert.Contains($"PREFERRED_MTU = {BridgeBluetooth.PreferredMtu};", firmware, StringComparison.Ordinal);
    }
}
