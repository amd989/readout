using Microsoft.Extensions.Time.Testing;
using Readout.Core.Display;
using Readout.Core.Protocol;
using Readout.Linux.BlueZ;

namespace Readout.Tests;

public class BlueZNamesTests
{
    private const string Data = "e7add780-b042-4876-aae1-112855353cc1";
    private const string SerialService = "0bd51666-e7cb-469b-8e4d-2742f1ba77cc";
    private const string Gap = "00001800-0000-1000-8000-00805f9b34fb";
    private const string Battery = "0000180f-0000-1000-8000-00805f9b34fb";
    private const string Device = "/org/bluez/hci0/dev_88_6B_0F_12_34_56";

    [Theory]
    [InlineData("/org/bluez/hci0/dev_88_6B_0F_12_34_56", "88:6B:0F:12:34:56")]
    [InlineData("/org/bluez/hci0/dev_88_6b_0f_12_34_56/service0010/char0011", "88:6B:0F:12:34:56")]
    [InlineData("/org/bluez/hci0", null)]
    [InlineData("/org/bluez/hci0/dev_88_6B", null)]
    [InlineData("/org/bluez/hci0/dev_GG_6B_0F_12_34_56", null)]
    [InlineData(null, null)]
    public void AddressFromPath_ReadsTheDevicePart(string? path, string? address) =>
        Assert.Equal(address, BlueZNames.AddressFromPath(path));

    [Fact]
    public void DevicePath_IsTheInverse()
    {
        var path = BlueZNames.DevicePath("/org/bluez/hci1", "88:6b:0f:12:34:56");

        Assert.Equal("/org/bluez/hci1/dev_88_6B_0F_12_34_56", path);
        Assert.Equal("88:6B:0F:12:34:56", BlueZNames.AddressFromPath(path));
    }

    [Fact]
    public void IsUnder_TakesServicesAndCharacteristics_NotADeviceWithALongerAddress()
    {
        Assert.True(BlueZNames.IsUnder(Device + "/service0010/char0011", Device));
        Assert.False(BlueZNames.IsUnder(Device, Device));
        Assert.False(BlueZNames.IsUnder(Device + "7/service0010", Device));
    }

    [Theory]
    [InlineData("121GW", "121GW", "121GW")]
    [InlineData("Bluegiga", null, "Bluegiga")]
    [InlineData(null, "121GW", "121GW")]          // only the alias (renamed, or the name not seen yet)
    [InlineData("Kitchen", "Kitchen", null)]
    [InlineData(null, "88-6B-0F-12-34-56", null)] // no name: BlueZ's alias is the address
    [InlineData("Readout FS9721 B1CC", null, "Readout FS9721 B1CC")] // a bridge
    public void MeterName_ComesFromTheNameOrAlias(string? name, string? alias, string? expected) =>
        Assert.Equal(expected, BlueZNames.MeterName(name, alias));

    [Theory]
    [InlineData("121GW", MeterModel.Eevblog121Gw)]
    [InlineData("Bluegiga", MeterModel.Eevblog121Gw)]
    [InlineData("Readout FS9721 B1CC", MeterModel.Fs9721)]
    [InlineData("Readout DSO150 B1CC", MeterModel.Dso150)]
    public void Model_ComesFromTheName(string name, MeterModel model) =>
        Assert.Equal(model, BlueZNames.ModelFromName(name));

    [Fact]
    public void BridgeDataCharacteristic_IsFoundByUuid()
    {
        var picked = BlueZNames.PickDataCharacteristic(
        [
            new(Device + "/service0010/char0011", BridgeBluetooth.ServiceUuid.ToString(), BridgeBluetooth.InfoCharacteristicUuid.ToString(), ["read"]),
            new(Device + "/service0010/char0013", BridgeBluetooth.ServiceUuid.ToString(), BridgeBluetooth.DataCharacteristicUuid.ToString(), ["notify"]),
        ]);

        Assert.Equal(Device + "/service0010/char0013", picked?.Path);
    }

    [Fact]
    public void TwoWayBridge_RxCharacteristic_IsFoundByUuid()
    {
        GattCharacteristicInfo[] characteristics =
        [
            new(Device + "/service0010/char0011", BridgeBluetooth.ServiceUuid.ToString(), BridgeBluetooth.DataCharacteristicUuid.ToString(), ["notify"]),
            new(Device + "/service0010/char0016", BridgeBluetooth.ServiceUuid.ToString(), BridgeBluetooth.RxCharacteristicUuid.ToString().ToUpperInvariant(), ["write", "write-without-response"]),
        ];

        Assert.Equal(Device + "/service0010/char0016", BlueZNames.PickRxCharacteristic(characteristics)?.Path);
        // Data is still the notifying one, not the writable one.
        Assert.Equal(Device + "/service0010/char0011", BlueZNames.PickDataCharacteristic(characteristics)?.Path);
    }

    [Fact]
    public void ReceiveOnlyBridge_HasNoRxCharacteristic() =>
        Assert.Null(BlueZNames.PickRxCharacteristic(
            [new(Device + "/service0010/char0013", BridgeBluetooth.ServiceUuid.ToString(), BridgeBluetooth.DataCharacteristicUuid.ToString(), ["notify"])]));

    [Fact]
    public void DataCharacteristic_IsFoundByUuid_InAnyCase()
    {
        var picked = BlueZNames.PickDataCharacteristic(
        [
            new(Device + "/service0001/char0002", Gap, "00002a00-0000-1000-8000-00805f9b34fb", ["read"]),
            new(Device + "/service0010/char0013", SerialService, "11111111-2222-3333-4444-555555555555", ["notify"]),
            new(Device + "/service0010/char0011", SerialService, Data.ToUpperInvariant(), ["indicate", "write"]),
        ]);

        Assert.Equal(Device + "/service0010/char0011", picked?.Path);
    }

    [Fact]
    public void WithoutTheUuid_TheFirstUpdatingCharacteristicOutsideStandardServicesIsTaken()
    {
        var picked = BlueZNames.PickDataCharacteristic(
        [
            new(Device + "/service0020/char0022", SerialService, "22222222-0000-0000-0000-000000000000", ["indicate"]),
            new(Device + "/service000c/char000d", Battery, "00002a19-0000-1000-8000-00805f9b34fb", ["read", "notify"]),
            new(Device + "/service0020/char0021", SerialService, "11111111-0000-0000-0000-000000000000", ["read"]),
        ]);

        // The battery level notifies too, but it's a standard service; char0021 can't update.
        Assert.Equal(Device + "/service0020/char0022", picked?.Path);
    }

    [Fact]
    public void NothingThatUpdates_PicksNothing() =>
        Assert.Null(BlueZNames.PickDataCharacteristic(
            [new(Device + "/service0001/char0002", Gap, "00002a00-0000-1000-8000-00805f9b34fb", ["read"])]));

    [Theory]
    [InlineData("public", new byte[] { 0x06 }, true)]  // LE General Discoverable, BR/EDR not supported
    [InlineData("random", new byte[] { 0x1A }, true)]  // an iPhone: dual-mode flags, but a random address
    [InlineData("public", new byte[] { 0x1A }, false)] // a Mac: public address, LE and BR/EDR
    [InlineData("public", new byte[0], false)]
    [InlineData(null, null, false)]
    public void IsLeOnly_ReadsTheAddressTypeAndFlags(string? addressType, byte[]? flags, bool leOnly) =>
        Assert.Equal(leOnly, BlueZNames.IsLeOnly(addressType, flags));

    [Theory]
    [InlineData(new[] { "indicate", "write", "write-without-response" }, "request")]
    [InlineData(new[] { "notify", "write-without-response" }, "command")]
    [InlineData(new[] { "notify" }, "request")]
    public void WriteType_PrefersAcknowledgedWrites(string[] flags, string expected) =>
        Assert.Equal(expected, BlueZNames.WriteType(flags));
}

public class RecentRssiTests
{
    [Fact]
    public void AValue_LastsUntilItIsStale()
    {
        var time = new FakeTimeProvider();
        var rssi = new RecentRssi(time);
        Assert.Null(rssi.Value);

        rssi.Set(-58);
        time.Advance(RecentRssi.FreshFor);
        Assert.Equal(-58, rssi.Value);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(rssi.Value);
    }

    [Fact]
    public void ANewValue_StartsAfresh()
    {
        var time = new FakeTimeProvider();
        var rssi = new RecentRssi(time);
        rssi.Set(-58);
        time.Advance(TimeSpan.FromSeconds(25));

        rssi.Set(-70);
        time.Advance(TimeSpan.FromSeconds(25));

        Assert.Equal(-70, rssi.Value);
    }

    [Fact]
    public void Clear_ForgetsTheValue()
    {
        var rssi = new RecentRssi(new FakeTimeProvider());
        rssi.Set(-58);

        rssi.Clear();

        Assert.Null(rssi.Value);
    }
}
