using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Linux.BlueZ;

/// <summary>A characteristic BlueZ resolved on a device, with the service it belongs to.</summary>
internal sealed record GattCharacteristicInfo(string Path, string ServiceUuid, string Uuid, IReadOnlyList<string> Flags)
{
    public bool CanUpdate => Flags.Contains("notify") || Flags.Contains("indicate");
}

/// <summary>
/// BlueZ's names for things, and the choices the meter connection makes from what BlueZ
/// reports. No D-Bus here, so the test project compiles it. A device is known by its address
/// (<c>AA:BB:CC:DD:EE:FF</c>), which is also the meter's id on Linux.
/// </summary>
internal static class BlueZNames
{
    public const string BusName             = "org.bluez";
    public const string Adapter1            = "org.bluez.Adapter1";
    public const string Device1             = "org.bluez.Device1";
    public const string GattService1        = "org.bluez.GattService1";
    public const string GattCharacteristic1 = "org.bluez.GattCharacteristic1";
    public const string ObjectManager       = "org.freedesktop.DBus.ObjectManager";
    public const string Properties          = "org.freedesktop.DBus.Properties";

    private const string DevicePrefix = "/dev_";

    /// <summary>The address in a device's object path (or a path under it), or null.</summary>
    public static string? AddressFromPath(string? path)
    {
        if (path is null) return null;
        var start = path.IndexOf(DevicePrefix, StringComparison.Ordinal);
        if (start < 0) return null;
        start += DevicePrefix.Length;
        var end = path.IndexOf('/', start);
        var part = end < 0 ? path[start..] : path[start..end];
        return part.Length == 17 && part.Split('_').All(b => b.Length == 2 && b.All(Uri.IsHexDigit))
            ? part.Replace('_', ':').ToUpperInvariant()
            : null;
    }

    /// <summary>The object path BlueZ gives a device with this address on this adapter.</summary>
    public static string DevicePath(string adapterPath, string address) =>
        $"{adapterPath}{DevicePrefix}{address.ToUpperInvariant().Replace(':', '_')}";

    /// <summary>Whether <paramref name="path"/> is an object under the device (a service or characteristic).</summary>
    public static bool IsUnder(string path, string devicePath) =>
        path.StartsWith(devicePath + "/", StringComparison.Ordinal);

    /// <summary>
    /// The name a device advertised, if it looks like a meter: a 121GW, or a Readout bridge
    /// ("Readout FS9721 A1B2"). BlueZ's Alias is the name unless the user renamed the device, or
    /// the address (with dashes) when it has none.
    /// </summary>
    public static string? MeterName(string? name, string? alias) =>
        IsMeter(name) ? name
        : IsMeter(alias) ? alias
        : null;

    /// <summary>The kind of meter a name says: a bridge's chip, else a 121GW.</summary>
    public static MeterModel ModelFromName(string name) =>
        BridgeBluetooth.ModelFromName(name) ?? MeterModel.Eevblog121Gw;

    private static bool IsMeter(string? name) =>
        MeterBluetooth.IsMeterName(name) || BridgeBluetooth.ModelFromName(name) is not null;

    /// <summary>The 121GW's or a bridge's data characteristic.</summary>
    public static bool IsKnownDataCharacteristic(string uuid) =>
        Guid.TryParse(uuid, out var id) &&
        (id == MeterBluetooth.DataCharacteristicUuid || id == BridgeBluetooth.DataCharacteristicUuid);

    /// <summary>
    /// Whether BlueZ can only connect over LE: the device advertises from a random address
    /// (Classic needs the public one), or its flags say "BR/EDR not supported" (bit 2). Otherwise
    /// BlueZ may take it for a dual-mode device and try Classic Bluetooth. The 121GW's radio is
    /// an LE-only Bluegiga module, so a Classic attempt can only fail, not pair.
    /// </summary>
    public static bool IsLeOnly(string? addressType, byte[]? advertisingFlags) =>
        addressType == "random" ||
        (advertisingFlags is { Length: > 0 } && (advertisingFlags[0] & 0x04) != 0);

    /// <summary>
    /// The meter's data characteristic: by UUID (the 121GW's or a bridge's), else the first one that can notify or indicate
    /// outside the standard services, as on the other platforms. In BlueZ's path order, which is
    /// the device's handle order.
    /// </summary>
    public static GattCharacteristicInfo? PickDataCharacteristic(IEnumerable<GattCharacteristicInfo> characteristics)
    {
        var ordered = characteristics.OrderBy(c => c.Path, StringComparer.Ordinal).ToList();
        return ordered.FirstOrDefault(c => IsKnownDataCharacteristic(c.Uuid))
            ?? ordered.FirstOrDefault(c => c.CanUpdate &&
                !(Guid.TryParse(c.ServiceUuid, out var service) && MeterBluetooth.StandardServices.Contains(service)));
    }

    /// <summary>
    /// WriteValue's "type": a write request (acknowledged) when the characteristic takes one,
    /// else a write command.
    /// </summary>
    public static string WriteType(IReadOnlyList<string> flags) =>
        flags.Contains("write") || !flags.Contains("write-without-response") ? "request" : "command";
}
