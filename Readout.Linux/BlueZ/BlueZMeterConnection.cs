using Microsoft.Extensions.Logging;
using Plugin.BLE.Abstractions.Contracts;
using Readout.Core.Connection;
using Readout.Core.Protocol;
using Readout.Services;
using Tmds.DBus.Protocol;

namespace Readout.Linux.BlueZ;

using Props = Dictionary<string, VariantValue>;

/// <summary>
/// The 121GW over BlueZ's D-Bus API, on Linux (Plugin.BLE has no Linux backend). BlueZ keeps
/// one object per device it has heard of; discovery reports advertisements as property changes
/// on those objects. A connect is Device1.Connect, then the GATT objects BlueZ resolves under
/// the device: StartNotify on the data characteristic (its indications arrive as Value
/// changes), WriteValue for key presses. A meter's id is its address.
/// </summary>
internal sealed class BlueZMeterConnection : IMeterConnection, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopTimeout    = TimeSpan.FromSeconds(3);
    private const int MaxConnectAttempts = 3;

    private readonly BlueZBus _bus;
    private readonly ILogger<BlueZMeterConnection> _logger;
    private readonly PacketFramer _framer = new();
    private readonly RecentRssi _rssi = new(TimeProvider.System);
    private readonly Lock _gate = new();
    private readonly Task<bool> _ready;

    // What each device last reported (BlueZ sends changes, not whole advertisements), and the
    // meter name it was last seen with: once connected, BlueZ may swap in the GAP device name.
    private readonly Dictionary<string, Props> _devices = [];
    private readonly Dictionary<string, string> _names = [];

    // What this scan has heard, for the log: a meter that never shows up is otherwise silent.
    private readonly HashSet<string> _heard = [];
    private readonly HashSet<string> _metersFound = [];
    private CancellationTokenSource? _scanReport;

    private string? _adapterPath;
    private bool _powered;
    private bool _discovering;
    private bool _disconnecting;
    private bool _preferredBearerWorks = true;

    // The open link: the device's path, its data characteristic and how to write to it.
    private string? _devicePath;
    private string? _dataPath;
    private string _writeType = "request";

    public BlueZMeterConnection(BlueZBus bus, ILogger<BlueZMeterConnection> logger)
    {
        _bus = bus;
        _logger = logger;
        _ready = StartAsync();
    }

    public ConnectionState State { get; private set; }
    public DiscoveredMeter? Meter { get; private set; }
    public int? SignalStrength => State == ConnectionState.Connected ? _rssi.Value : null;

    public event EventHandler? StateChanged;
    public event EventHandler<DiscoveredMeter>? MeterDiscovered;
    public event EventHandler<MeterPacket>? PacketReceived;
    public event EventHandler? ConnectionLost;

    /// <summary>On with a powered adapter, Off with an unpowered one, Unavailable with none (or no BlueZ).</summary>
    public async Task<BluetoothState> GetBluetoothStateAsync() =>
        !await _ready.ConfigureAwait(false) ? BluetoothState.Unavailable
        : _powered ? BluetoothState.On
        : BluetoothState.Off;

    /// <summary>Connects to the system bus and finds the adapter; false when there's no Bluetooth to use.</summary>
    private async Task<bool> StartAsync()
    {
        try
        {
            await _bus.EnsureConnectedAsync().ConfigureAwait(false);
            await _bus.WatchPropertiesAsync(OnPropertiesChanged).ConfigureAwait(false);
            await _bus.WatchObjectsAsync(OnInterfacesAdded, OnInterfacesRemoved).ConfigureAwait(false);

            if (await _bus.FindAdapterAsync().ConfigureAwait(false) is not { } adapter)
            {
                _logger.LogWarning("bluez: no Bluetooth adapter");
                return false;
            }
            _adapterPath = adapter.Path;
            _powered = adapter.Properties.Bool("Powered") ?? false;
            _logger.LogInformation("bluez: adapter {Path} {Address}, powered {Powered}",
                adapter.Path, adapter.Properties.String("Address"), _powered);

            // Remember what BlueZ already knows, without reporting it: these can be days old.
            foreach (var (path, interfaces) in await _bus.GetManagedObjectsAsync().ConfigureAwait(false))
                if (interfaces.TryGetValue(BlueZNames.Device1, out var props) && BlueZNames.AddressFromPath(path) is { } address)
                    lock (_gate) _devices[address] = new Props(props);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bluez: no system bus or no BlueZ, so no Bluetooth");
            return false;
        }
    }

    private string AdapterPath => _adapterPath ?? throw new InvalidOperationException("No Bluetooth adapter.");

    // ── Scanning ────────────────────────────────────────────────────────────

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        if (State is ConnectionState.Scanning or ConnectionState.Connected or ConnectionState.Connecting)
            return;
        var state = await GetBluetoothStateAsync().ConfigureAwait(false);
        if (state != BluetoothState.On)
            throw new BluetoothUnavailableException(state);

        _logger.LogInformation("scan start");
        SetState(ConnectionState.Scanning);
        try
        {
            await _bus.SetDiscoveryFilterAsync(AdapterPath).ConfigureAwait(false);
            await _bus.CallAsync(AdapterPath, BlueZNames.Adapter1, "StartDiscovery").ConfigureAwait(false);
        }
        catch (DBusErrorReplyException ex) when (ex.ErrorName == "org.bluez.Error.InProgress")
        {
            _logger.LogInformation("scan start: already discovering");
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            throw;
        }
        _discovering = true;
        lock (_gate)
        {
            _heard.Clear();
            _metersFound.Clear();
        }
        StopScanReport();
        _scanReport = new CancellationTokenSource();
        _ = ReportScanAsync(_scanReport.Token);

        // Meters with an RSSI are being heard right now (BlueZ drops it when discovery ends, so
        // another program is scanning too): list them at once rather than at their next advert.
        List<string> known;
        lock (_gate) known = [.. _devices.Where(d => d.Value.ContainsKey("RSSI")).Select(d => d.Key)];
        foreach (var address in known) Sighted(address);
    }

    public async Task StopScanAsync()
    {
        if (_discovering)
        {
            _discovering = false;
            StopScanReport();
            await WithStopTimeoutAsync(_bus.CallAsync(AdapterPath, BlueZNames.Adapter1, "StopDiscovery"), "scan stop").ConfigureAwait(false);
        }
        if (State == ConnectionState.Scanning)
            SetState(ConnectionState.Disconnected);
    }

    // ── Connection ──────────────────────────────────────────────────────────

    public async Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default)
    {
        if (State is ConnectionState.Connected or ConnectionState.Connecting) return;
        if (!await _ready.ConfigureAwait(false))
            throw new BluetoothUnavailableException(BluetoothState.Unavailable);

        await StopScanAsync().ConfigureAwait(false);
        Meter = meter;
        SetState(ConnectionState.Connecting);
        _logger.LogInformation("connect {Name} ({Id})", meter.Name, meter.Id);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await ConnectCoreAsync(meter, timeout.Token).ConfigureAwait(false);
            SetState(ConnectionState.Connected);
            _logger.LogInformation("connected in {Ms} ms, data on {Path}", sw.ElapsedMilliseconds, _dataPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "connect FAILED after {Ms} ms", sw.ElapsedMilliseconds);
            await CloseLinkAsync().ConfigureAwait(false);
            Meter = null;
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    private async Task ConnectCoreAsync(DiscoveredMeter meter, CancellationToken ct)
    {
        var devicePath = BlueZNames.DevicePath(AdapterPath, meter.Id);
        lock (_gate) _devicePath = devicePath;

        await PreferLeAsync(meter.Id, devicePath).ConfigureAwait(false);
        await WaitForScanToStopAsync(ct).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _bus.CallAsync(devicePath, BlueZNames.Device1, "Connect").WaitAsync(ct).ConfigureAwait(false);
                break;
            }
            catch (DBusErrorReplyException ex) when (ex.ErrorName == "org.bluez.Error.AlreadyConnected")
            {
                break;
            }
            catch (DBusErrorReplyException ex) when (ex.ErrorMessage?.StartsWith("br-", StringComparison.Ordinal) == true)
            {
                _logger.LogWarning("BlueZ tried Classic Bluetooth for the meter ({Error}), which it doesn't have; run bluetoothd with -E so the app can ask for LE", ex.ErrorMessage);
                throw;
            }
            // The kernel cancels an LE connect when a scan starts or stops under it (seen on the
            // Fedora laptop: the scan was still being torn down). Give it a moment and go again.
            catch (DBusErrorReplyException ex) when (ex.ErrorMessage == "le-connection-abort-by-local" && attempt < MaxConnectAttempts)
            {
                _logger.LogInformation("connect: BlueZ aborted it (attempt {Attempt}, adapter discovering: {Discovering}); trying again",
                    attempt, await IsDiscoveringAsync().ConfigureAwait(false));
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct).ConfigureAwait(false);
                await WaitForScanToStopAsync(ct).ConfigureAwait(false);
            }
        }

        // BlueZ discovers the meter's GATT database after connecting; its objects appear then.
        while (!await ServicesResolvedAsync(devicePath).ConfigureAwait(false))
            await Task.Delay(100, ct).ConfigureAwait(false);

        var data = BlueZNames.PickDataCharacteristic(await CharacteristicsAsync(devicePath).ConfigureAwait(false))
            ?? throw new InvalidOperationException("This device has no 121GW data characteristic.");
        if (!Guid.TryParse(data.Uuid, out var uuid) || uuid != MeterBluetooth.DataCharacteristicUuid)
            _logger.LogWarning("data characteristic not found by UUID, using {Uuid}", data.Uuid);

        lock (_gate)
        {
            _framer.Reset();
            _dataPath = data.Path;
            _writeType = BlueZNames.WriteType(data.Flags);
        }
        await _bus.CallAsync(data.Path, BlueZNames.GattCharacteristic1, "StartNotify").WaitAsync(ct).ConfigureAwait(false);

        // The strength from the scan, until (and unless) BlueZ reports a newer one.
        _rssi.Set(meter.Rssi);
    }

    /// <summary>
    /// Asks BlueZ to connect over LE. BlueZ connects a device it takes for dual-mode over Classic
    /// Bluetooth (Wavelength's Linux build saw that with a Mac on Fedora, where it ended in a
    /// Classic pairing). The meter is LE-only, so that can't pair, but the connect would fail if
    /// its advertisement doesn't say so. Device1.PreferredBearer (BlueZ 5.80+) exists only with
    /// bluetoothd's experimental interfaces on (-E); without it, this just says what to expect.
    /// </summary>
    private async Task PreferLeAsync(string address, string devicePath)
    {
        if (_preferredBearerWorks)
        {
            try
            {
                await _bus.SetStringPropertyAsync(devicePath, BlueZNames.Device1, "PreferredBearer", "le").ConfigureAwait(false);
                return;
            }
            catch (DBusErrorReplyException ex)
            {
                _preferredBearerWorks = false;
                _logger.LogInformation("bluez: can't ask for LE ({Error}); fine for a meter that advertises as LE-only", ex.ErrorName);
            }
        }

        string? addressType;
        byte[]? flags;
        lock (_gate)
        {
            _devices.TryGetValue(address, out var props);
            addressType = props?.String("AddressType");
            flags = props?.Bytes("AdvertisingFlags");
        }
        if (!BlueZNames.IsLeOnly(addressType, flags))
            _logger.LogWarning("connect {Address}: its advertisement doesn't say LE-only (address {Type}, flags {Flags}), so BlueZ may try Classic Bluetooth",
                address, addressType ?? "?", flags is { Length: > 0 } ? $"0x{flags[0]:X2}" : "none");
    }

    /// <summary>
    /// Waits (up to 2 s) for BlueZ to say the adapter has stopped scanning. StopDiscovery returns
    /// before the scan is down, and an LE connect started while the kernel is still scanning, or
    /// while it restarts the scan, is aborted (le-connection-abort-by-local). Another program
    /// scanning keeps the adapter discovering; then this just says so and goes on.
    /// </summary>
    private async Task WaitForScanToStopAsync(CancellationToken ct)
    {
        for (var waited = 0; waited < 2000; waited += 100)
        {
            if (!await IsDiscoveringAsync().ConfigureAwait(false)) return;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        _logger.LogInformation("connect: the adapter is still scanning (another program may be), connecting anyway");
    }

    private async Task<bool> IsDiscoveringAsync()
    {
        try
        {
            var value = await _bus.GetPropertyAsync(AdapterPath, BlueZNames.Adapter1, "Discovering").ConfigureAwait(false);
            return value.Unwrap() is { Type: VariantValueType.Bool } b && b.GetBool();
        }
        catch (DBusErrorReplyException)
        {
            return false;
        }
    }

    private async Task<bool> ServicesResolvedAsync(string devicePath)
    {
        var value = await _bus.GetPropertyAsync(devicePath, BlueZNames.Device1, "ServicesResolved").ConfigureAwait(false);
        return value.Unwrap() is { Type: VariantValueType.Bool } b && b.GetBool();
    }

    private async Task<List<GattCharacteristicInfo>> CharacteristicsAsync(string devicePath)
    {
        var objects = await _bus.GetManagedObjectsAsync().ConfigureAwait(false);
        var services = objects
            .Where(o => BlueZNames.IsUnder(o.Key, devicePath) && o.Value.ContainsKey(BlueZNames.GattService1))
            .ToDictionary(o => o.Key, o => o.Value[BlueZNames.GattService1].String("UUID") ?? "");
        return [.. objects
            .Where(o => BlueZNames.IsUnder(o.Key, devicePath) && o.Value.ContainsKey(BlueZNames.GattCharacteristic1))
            .Select(o =>
            {
                var props = o.Value[BlueZNames.GattCharacteristic1];
                var service = props.String("Service") ?? "";
                return new GattCharacteristicInfo(o.Key, services.GetValueOrDefault(service, ""),
                    props.String("UUID") ?? "", props.Strings("Flags") ?? []);
            })];
    }

    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Scanning)
        {
            await StopScanAsync().ConfigureAwait(false);
            return;
        }

        _disconnecting = true;
        try
        {
            await CloseLinkAsync().ConfigureAwait(false);
        }
        finally
        {
            _disconnecting = false;
        }
        Meter = null;
        SetState(ConnectionState.Disconnected);
        _logger.LogInformation("disconnected");
    }

    public async Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default)
    {
        string? dataPath;
        string writeType;
        lock (_gate) (dataPath, writeType) = (_dataPath, _writeType);
        if (dataPath is null) return;

        _logger.LogInformation("key {Key} (long: {Long})", key, longPress);
        await _bus.WriteValueAsync(dataPath, MeterCommands.KeyPress(key, longPress), writeType).WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task CloseLinkAsync()
    {
        string? devicePath, dataPath;
        lock (_gate)
        {
            (devicePath, dataPath) = (_devicePath, _dataPath);
            _devicePath = _dataPath = null;
        }
        _rssi.Clear();

        if (dataPath is not null)
            await WithStopTimeoutAsync(_bus.CallAsync(dataPath, BlueZNames.GattCharacteristic1, "StopNotify"), "stop notify").ConfigureAwait(false);
        if (devicePath is not null)
            await WithStopTimeoutAsync(_bus.CallAsync(devicePath, BlueZNames.Device1, "Disconnect"), "disconnect").ConfigureAwait(false);
    }

    private async Task WithStopTimeoutAsync(Task task, string what)
    {
        try
        {
            await task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) { _logger.LogWarning("{What}: no answer after {Seconds:0} s, moving on", what, StopTimeout.TotalSeconds); }
        catch (Exception ex) { _logger.LogInformation("{What} failed: {Error}", what, ex.Message); }
    }

    // ── BlueZ signals (on the bus's thread) ─────────────────────────────────

    private void OnInterfacesAdded(string path, Dictionary<string, Props> interfaces)
    {
        if (!interfaces.TryGetValue(BlueZNames.Device1, out var props) || BlueZNames.AddressFromPath(path) is not { } address)
            return;
        lock (_gate) _devices[address] = new Props(props);
        // A device appearing during discovery was just heard.
        if (_discovering && props.ContainsKey("RSSI")) Sighted(address);
    }

    private void OnInterfacesRemoved(string path, string[] interfaces)
    {
        if (interfaces.Contains(BlueZNames.Device1) && BlueZNames.AddressFromPath(path) is { } address)
            lock (_gate) _devices.Remove(address);
    }

    private void OnPropertiesChanged(string path, string @interface, Props changed, string[] invalidated)
    {
        switch (@interface)
        {
            case BlueZNames.Adapter1 when path == _adapterPath:
                if (changed.Bool("Powered") is { } powered)
                {
                    _logger.LogInformation("bluez: adapter powered {Powered}", powered);
                    _powered = powered;
                }
                break;

            case BlueZNames.Device1 when BlueZNames.AddressFromPath(path) is { } address:
                OnDeviceChanged(path, address, changed, invalidated);
                break;

            case BlueZNames.GattCharacteristic1 when changed.Bytes("Value") is { Length: > 0 } value:
                IReadOnlyList<MeterPacket> packets;
                lock (_gate)
                {
                    if (path != _dataPath) return;
                    packets = _framer.Push(value);
                }
                foreach (var packet in packets)
                    PacketReceived?.Invoke(this, packet);
                break;
        }
    }

    private void OnDeviceChanged(string path, string address, Props changed, string[] invalidated)
    {
        bool ours;
        lock (_gate)
        {
            if (!_devices.TryGetValue(address, out var props)) _devices[address] = props = [];
            foreach (var (name, value) in changed) props[name] = value;
            foreach (var name in invalidated) props.Remove(name);
            ours = path == _devicePath;
        }

        if (ours && changed.Int16("RSSI") is { } rssi) _rssi.Set(rssi);
        if (ours && changed.Bool("Connected") == false && State == ConnectionState.Connected && !_disconnecting)
            OnLinkDropped();

        // An advertisement: RSSI (or its data) changes only when one is heard.
        if (_discovering && (changed.ContainsKey("RSSI") || changed.ContainsKey("Name") || changed.ContainsKey("ManufacturerData")))
            Sighted(address);
    }

    private void OnLinkDropped()
    {
        _logger.LogWarning("link lost");
        lock (_gate) _devicePath = _dataPath = null;
        _rssi.Clear();
        Meter = null;
        SetState(ConnectionState.Disconnected);
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    private void Sighted(string address)
    {
        string? name;
        short? rssi;
        bool first;
        lock (_gate)
        {
            _heard.Add(address);
            if (!_devices.TryGetValue(address, out var props)) return;
            name = BlueZNames.MeterName(props.String("Name"), props.String("Alias"));
            if (name is not null) _names[address] = name;
            else if (!_names.TryGetValue(address, out name)) return;
            rssi = props.Int16("RSSI");
            first = rssi is not null && _metersFound.Add(address);
        }
        if (rssi is null) return;
        if (first) _logger.LogInformation("scan: found {Name} at {Address}, {Rssi} dBm", name, address, rssi);
        MeterDiscovered?.Invoke(this, new DiscoveredMeter(address, name, rssi.Value));
    }

    private void StopScanReport()
    {
        _scanReport?.Cancel();
        _scanReport?.Dispose();
        _scanReport = null;
    }

    public void Dispose() => StopScanReport();

    // While scanning: how many devices BlueZ has heard, and how many look like a meter. After
    // 15 s, then every 2 minutes (the Connect page can stay open for hours).
    private async Task ReportScanAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            while (true)
            {
                var first = DateTime.UtcNow - started < TimeSpan.FromSeconds(10);
                await Task.Delay(first ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
                int heard, meters;
                lock (_gate) (heard, meters) = (_heard.Count, _metersFound.Count);
                if (meters > 0)
                    _logger.LogInformation("scan: {Seconds:0} s, heard {Heard} devices, {Meters} of them meters", (DateTime.UtcNow - started).TotalSeconds, heard, meters);
                else
                    _logger.LogInformation("scan: {Seconds:0} s, heard {Heard} devices, none named 121GW or Bluegiga; is Bluetooth on at the meter (BT on its display), and not connected to a phone?",
                        (DateTime.UtcNow - started).TotalSeconds, heard);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
