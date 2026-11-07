using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Protocol;
using Microsoft.Extensions.Logging;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace Readout.Services;

/// <summary>Bluetooth is off, or the app may not use it.</summary>
public sealed class BluetoothUnavailableException(BluetoothState state)
    : Exception($"Bluetooth is not available ({state}).")
{
    public BluetoothState State { get; } = state;
}

/// <summary>
/// Meters over Bluetooth LE, through Plugin.BLE (Android, iOS, Mac Catalyst, Windows): the 121GW
/// and Readout bridges (<see cref="BridgeBluetooth"/>). Subscribes to the meter's data
/// characteristic and passes on what arrives; the session's driver frames it.
/// </summary>
public sealed class BleMeterConnection : IMeterConnection, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private readonly ILogger<BleMeterConnection> _logger;
    private readonly Dictionary<Guid, IDevice> _seen = [];

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _rssiCts;
    private IDevice? _device;
    private ICharacteristic? _data;
    private bool _disconnecting;

    public BleMeterConnection(IBluetoothLE ble, ILogger<BleMeterConnection> logger)
    {
        _ble = ble;
        _adapter = ble.Adapter;
        _logger = logger;

        _adapter.ScanMode = ScanMode.LowLatency;
        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.DeviceConnectionLost += (_, e) => OnLinkDropped(e.Device, e.ErrorMessage);
        _adapter.DeviceDisconnected += (_, e) => OnLinkDropped(e.Device, null);
    }

    public ConnectionState State { get; private set; }
    public DiscoveredMeter? Meter { get; private set; }
    public int? SignalStrength { get; private set; }
    public BluetoothState BluetoothState => _ble.State;

    public event EventHandler? StateChanged;
    public event EventHandler<DiscoveredMeter>? MeterDiscovered;
    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler? ConnectionLost;

    public Task StartScanAsync(CancellationToken ct = default)
    {
        if (State is ConnectionState.Scanning or ConnectionState.Connected or ConnectionState.Connecting)
            return Task.CompletedTask;
        if (_ble.State != BluetoothState.On)
            throw new BluetoothUnavailableException(_ble.State);

        _logger.LogInformation("scan start");
        _seen.Clear();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(ConnectionState.Scanning);
        _ = ScanLoopAsync(_scanCts.Token);
        return Task.CompletedTask;
    }

    // Plugin.BLE ends each scan after Adapter.ScanTimeout (10 s), so keep restarting it until stopped.
    private async Task ScanLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _adapter.StartScanningForDevicesAsync(
                    deviceFilter: d => IsMeter(d.Name),
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "scan failed, retrying");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public async Task StopScanAsync()
    {
        if (_scanCts is null) return;

        await _scanCts.CancelAsync();
        _scanCts.Dispose();
        _scanCts = null;
        try { await _adapter.StopScanningForDevicesAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "scan stop failed"); }

        if (State == ConnectionState.Scanning)
            SetState(ConnectionState.Disconnected);
        _logger.LogInformation("scan stopped");
    }

    public async Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default)
    {
        if (State is ConnectionState.Connected or ConnectionState.Connecting) return;
        if (!_seen.TryGetValue(Guid.Parse(meter.Id), out var device))
            throw new InvalidOperationException("That meter hasn't been seen in this scan.");

        await StopScanAsync();
        Meter = meter;
        SetState(ConnectionState.Connecting);
        _logger.LogInformation("connect {Name} ({Id})", meter.Name, meter.Id);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await _adapter.ConnectToDeviceAsync(device, new ConnectParameters(autoConnect: false, forceBleTransport: true), timeout.Token);
            _device = device;

            var data = await FindDataCharacteristicAsync(device, timeout.Token)
                ?? throw new InvalidOperationException("This device has no meter data characteristic.");

            data.ValueUpdated += OnValueUpdated;
            await data.StartUpdatesAsync(timeout.Token);
            _data = data;

            SignalStrength = device.Rssi;
            SetState(ConnectionState.Connected);
            _logger.LogInformation("connected, data on {Characteristic}", data.Id);
            StartRssiPolling(device);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "connect failed");
            await CloseLinkAsync();
            Meter = null;
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    /// <summary>
    /// The data characteristic by UUID, else the first one that can notify outside the standard services.
    /// Windows can answer with no services right after connecting, before it has read them from the
    /// meter, so ask a few times.
    /// </summary>
    private async Task<ICharacteristic?> FindDataCharacteristicAsync(IDevice device, CancellationToken ct)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            var (data, fallback) = await SearchServicesAsync(device, ct);
            if (data is not null) return data;
            if (attempt == attempts)
            {
                if (fallback is not null)
                    _logger.LogWarning("data characteristic not found by UUID, using {Characteristic}", fallback.Id);
                return fallback;
            }
            _logger.LogInformation("data characteristic not found yet (try {Attempt}), asking again", attempt);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private static async Task<(ICharacteristic? Data, ICharacteristic? Fallback)> SearchServicesAsync(IDevice device, CancellationToken ct)
    {
        ICharacteristic? fallback = null;
        foreach (var service in await device.GetServicesAsync(ct))
        {
            var characteristics = await service.GetCharacteristicsAsync();
            foreach (var characteristic in characteristics)
            {
                if (characteristic.Id == MeterBluetooth.DataCharacteristicUuid ||
                    characteristic.Id == BridgeBluetooth.DataCharacteristicUuid)
                    return (characteristic, null);
                if (fallback is null && characteristic.CanUpdate && !MeterBluetooth.StandardServices.Contains(service.Id))
                    fallback = characteristic;
            }
        }
        return (null, fallback);
    }

    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Scanning)
        {
            await StopScanAsync();
            return;
        }

        _disconnecting = true;
        try
        {
            await CloseLinkAsync();
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
        if (_data is null) return;
        // Bridges only listen to their meter; there's nothing to press.
        if (Meter?.Model != MeterModel.Eevblog121Gw) return;
        var command = MeterCommands.KeyPress(key, longPress);
        _logger.LogInformation("key {Key} (long: {Long})", key, longPress);
        await _data.WriteAsync(command, ct);
    }

    // The signal strength isn't pushed; ask for it every few seconds while connected.
    private void StartRssiPolling(IDevice device)
    {
        _rssiCts?.Cancel();
        _rssiCts = new CancellationTokenSource();
        var ct = _rssiCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    if (await device.UpdateRssiAsync())
                        SignalStrength = device.Rssi;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger.LogDebug(ex, "rssi update failed"); }
            }
        }, ct);
    }

    private async Task CloseLinkAsync()
    {
        _rssiCts?.Cancel();
        _rssiCts = null;
        SignalStrength = null;
        var data = _data;
        var device = _device;
        _data = null;
        _device = null;

        if (data is not null)
        {
            data.ValueUpdated -= OnValueUpdated;
            try { await data.StopUpdatesAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "stop updates failed"); }
        }
        if (device is not null)
        {
            try { await _adapter.DisconnectDeviceAsync(device); }
            catch (Exception ex) { _logger.LogDebug(ex, "disconnect failed"); }
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        var device = e.Device;
        if (!IsMeter(device.Name)) return;

        _seen[device.Id] = device;
        var model = BridgeBluetooth.ModelFromName(device.Name) ?? MeterModel.Eevblog121Gw;
        MeterDiscovered?.Invoke(this, new DiscoveredMeter(device.Id.ToString(), device.Name, device.Rssi, model));
    }

    private static bool IsMeter(string? name) =>
        MeterBluetooth.IsMeterName(name) || BridgeBluetooth.ModelFromName(name) is not null;

    private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var value = e.Characteristic.Value;
        if (value is null || value.Length == 0) return;

        // Copied: the platform may reuse the buffer, and the session reads it later on the main thread.
        DataReceived?.Invoke(this, value.ToArray());
    }

    private void OnLinkDropped(IDevice device, string? error)
    {
        if (_disconnecting || _device is null || device.Id != _device.Id) return;

        _logger.LogWarning("link lost: {Error}", error ?? "disconnected");
        if (_data is not null)
            _data.ValueUpdated -= OnValueUpdated;
        _rssiCts?.Cancel();
        _rssiCts = null;
        SignalStrength = null;
        _data = null;
        _device = null;
        Meter = null;
        SetState(ConnectionState.Disconnected);
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _scanCts?.Dispose();
        _rssiCts?.Dispose();
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
