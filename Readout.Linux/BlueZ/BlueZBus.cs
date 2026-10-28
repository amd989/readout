using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Readout.Linux.BlueZ;

using Props = Dictionary<string, VariantValue>;
using ManagedObjects = Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>;

/// <summary>
/// The system bus and the handful of BlueZ calls the meter connection makes. Hand-written over
/// Tmds.DBus.Protocol (which the GTK backend already uses) rather than generated proxies:
/// BlueZ's API here is a few methods. Connects on first use; a machine without a system bus
/// (a container) fails <see cref="EnsureConnectedAsync"/>, which the caller reports as no
/// Bluetooth. Taken from Wavelength's Linux head, central role only.
/// </summary>
internal sealed class BlueZBus(ILogger<BlueZBus> logger) : IDisposable
{
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private DBusConnection? _connection;

    public DBusConnection Connection => _connection ?? throw new InvalidOperationException("Not connected to the system bus.");

    public async Task<DBusConnection> EnsureConnectedAsync()
    {
        if (_connection is not null) return _connection;
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is not null) return _connection;
            var address = DBusAddress.System ?? throw new InvalidOperationException("No system bus address.");
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            logger.LogInformation("bluez: connected to the system bus as {Name}", connection.UniqueName);
            _connection = connection;
            return connection;
        }
        finally { _connectGate.Release(); }
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connectGate.Dispose();
    }

    /// <summary>The first Bluetooth adapter's path and properties, or null when there's none.</summary>
    public async Task<(string Path, Props Properties)?> FindAdapterAsync()
    {
        var objects = await GetManagedObjectsAsync().ConfigureAwait(false);
        foreach (var (path, interfaces) in objects.OrderBy(o => o.Key, StringComparer.Ordinal))
            if (interfaces.TryGetValue(BlueZNames.Adapter1, out var props))
                return (path, props);
        return null;
    }

    public Task<ManagedObjects> GetManagedObjectsAsync()
    {
        var connection = Connection;
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BlueZNames.BusName, "/", BlueZNames.ObjectManager, "GetManagedObjects", null, MessageFlags.None);
        return connection.CallMethodAsync(writer.CreateMessage(), (message, _) =>
        {
            var reader = message.GetBodyReader();
            return ReadManagedObjects(ref reader);
        }, null);
    }

    private static ManagedObjects ReadManagedObjects(ref Reader reader)
    {
        var objects = new ManagedObjects();
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            var path = reader.ReadObjectPathAsString();
            objects[path] = ReadInterfaces(ref reader);
        }
        return objects;
    }

    private static Dictionary<string, Props> ReadInterfaces(ref Reader reader)
    {
        var interfaces = new Dictionary<string, Props>();
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            var name = reader.ReadString();
            interfaces[name] = reader.ReadDictionaryOfStringToVariantValue();
        }
        return interfaces;
    }

    /// <summary>A call that takes no arguments and returns nothing (StartDiscovery, Connect, …).</summary>
    public Task CallAsync(string path, string @interface, string member)
    {
        var connection = Connection;
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BlueZNames.BusName, path, @interface, member, null, MessageFlags.None);
        return connection.CallMethodAsync(writer.CreateMessage());
    }

    /// <summary>
    /// LE only, and every advertisement reported (DuplicateData), so RSSI keeps arriving. No
    /// UUID filter: the meter is known by its name, and may not advertise its service.
    /// </summary>
    public Task SetDiscoveryFilterAsync(string adapterPath)
    {
        var connection = Connection;
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BlueZNames.BusName, adapterPath, BlueZNames.Adapter1, "SetDiscoveryFilter", "a{sv}", MessageFlags.None);
        var dict = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("Transport");
        writer.WriteVariantString("le");
        writer.WriteDictionaryEntryStart();
        writer.WriteString("DuplicateData");
        writer.WriteVariantBool(true);
        writer.WriteDictionaryEnd(dict);
        return connection.CallMethodAsync(writer.CreateMessage());
    }

    public Task<VariantValue> GetPropertyAsync(string path, string @interface, string name)
    {
        var connection = Connection;
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BlueZNames.BusName, path, BlueZNames.Properties, "Get", "ss", MessageFlags.None);
        writer.WriteString(@interface);
        writer.WriteString(name);
        return connection.CallMethodAsync(writer.CreateMessage(), (message, _) =>
        {
            var reader = message.GetBodyReader();
            return reader.ReadVariantValue();
        }, null);
    }

    /// <summary>GattCharacteristic1.WriteValue; <paramref name="type"/> is "request" or "command".</summary>
    public Task WriteValueAsync(string characteristicPath, byte[] value, string type)
    {
        var connection = Connection;
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BlueZNames.BusName, characteristicPath, BlueZNames.GattCharacteristic1, "WriteValue", "aya{sv}", MessageFlags.None);
        writer.WriteArray(value);
        var dict = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("type");
        writer.WriteVariantString(type);
        writer.WriteDictionaryEnd(dict);
        return connection.CallMethodAsync(writer.CreateMessage());
    }

    /// <summary>InterfacesAdded and InterfacesRemoved from BlueZ's object manager.</summary>
    public async Task WatchObjectsAsync(
        Action<string, Dictionary<string, Props>> added,
        Action<string, string[]> removed)
    {
        var connection = Connection;
        await connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = BlueZNames.BusName, Interface = BlueZNames.ObjectManager, Member = "InterfacesAdded" },
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                var path = reader.ReadObjectPathAsString();
                return (path, ReadInterfaces(ref reader));
            },
            n => { if (n.HasValue) Guard(() => added(n.Value.path, n.Value.Item2)); },
            emitOnCapturedContext: false, ObserverFlags.None, null).ConfigureAwait(false);
        await connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = BlueZNames.BusName, Interface = BlueZNames.ObjectManager, Member = "InterfacesRemoved" },
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadObjectPathAsString(), reader.ReadArrayOfString());
            },
            n => { if (n.HasValue) Guard(() => removed(n.Value.Item1, n.Value.Item2)); },
            emitOnCapturedContext: false, ObserverFlags.None, null).ConfigureAwait(false);
    }

    /// <summary>
    /// PropertiesChanged for every BlueZ object: (path, interface, changed, invalidated). BlueZ
    /// invalidates a device's RSSI when discovery stops.
    /// </summary>
    public Task WatchPropertiesAsync(Action<string, string, Props, string[]> changed) =>
        Connection.AddMatchAsync(
            new MatchRule
            {
                Type = MessageType.Signal, Sender = BlueZNames.BusName,
                Interface = BlueZNames.Properties, Member = "PropertiesChanged", PathNamespace = "/org/bluez",
            },
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                var @interface = reader.ReadString();
                var values = reader.ReadDictionaryOfStringToVariantValue();
                return (message.PathAsString ?? "", @interface, values, reader.ReadArrayOfString());
            },
            n => { if (n.HasValue) Guard(() => changed(n.Value.Item1, n.Value.@interface, n.Value.values, n.Value.Item4)); },
            emitOnCapturedContext: false, ObserverFlags.None, null).AsTask();

    // A handler that throws would take the bus's reader down with it.
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { logger.LogError(ex, "bluez: signal handler FAILED"); }
    }
}

/// <summary>Reading BlueZ's property values, which arrive as variants (sometimes nested).</summary>
internal static class BlueZValues
{
    public static VariantValue Unwrap(this VariantValue value)
    {
        while (value.Type == VariantValueType.Variant) value = value.GetVariantValue();
        return value;
    }

    public static string? String(this Props props, string name) =>
        props.TryGetValue(name, out var v) && v.Unwrap() is { Type: VariantValueType.String or VariantValueType.ObjectPath } s
            ? (s.Type == VariantValueType.String ? s.GetString() : s.GetObjectPathAsString())
            : null;

    public static bool? Bool(this Props props, string name) =>
        props.TryGetValue(name, out var v) && v.Unwrap() is { Type: VariantValueType.Bool } b ? b.GetBool() : null;

    public static short? Int16(this Props props, string name) =>
        props.TryGetValue(name, out var v) && v.Unwrap() is { Type: VariantValueType.Int16 } n ? n.GetInt16() : null;

    public static string[]? Strings(this Props props, string name) =>
        props.TryGetValue(name, out var v) && v.Unwrap() is { Type: VariantValueType.Array } a ? a.GetArray<string>() : null;

    public static byte[]? Bytes(this Props props, string name) =>
        props.TryGetValue(name, out var v) && v.Unwrap() is { Type: VariantValueType.Array, ItemType: VariantValueType.Byte } a
            ? a.GetArray<byte>()
            : null;
}
