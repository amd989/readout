using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Core.Protocol;

namespace Readout.Core.Connection;

/// <summary>
/// The app's one meter: picks Bluetooth or the demo meter, decodes packets into readings,
/// logs them, and reconnects to the remembered meter when it's seen again or the link drops.
/// </summary>
/// <remarks>
/// Connection events can arrive on any thread; the session raises its own events through
/// <see cref="Dispatch"/>, which the app points at the main thread. The log is only touched
/// there.
/// </remarks>
public sealed class MeterSession
{
    private readonly IMeterConnection _bluetooth;
    private readonly IMeterConnection _demo;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, DiscoveredMeter> _discovered = [];
    private bool _userDisconnected;
    private bool _autoConnect;

    public MeterSession(IMeterConnection bluetooth, IMeterConnection demo, TimeProvider? time = null)
    {
        _bluetooth = bluetooth;
        _demo = demo;
        _time = time ?? TimeProvider.System;
        Active = bluetooth;

        foreach (var connection in new[] { bluetooth, demo })
        {
            connection.StateChanged += (s, _) => { if (s == Active) Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty)); };
            connection.PacketReceived += (s, p) => { if (s == Active) Dispatch(() => OnPacket(p)); };
        }
        bluetooth.MeterDiscovered += (_, m) => Dispatch(() => OnDiscovered(m));
        bluetooth.ConnectionLost += (_, _) => Dispatch(OnConnectionLost);
    }

    /// <summary>Runs an action on the UI thread. Defaults to running it inline.</summary>
    public Action<Action> Dispatch { get; set; } = action => action();

    /// <summary>The meter to connect to by itself when a scan sees it (the last one used).</summary>
    public string? PreferredMeterId { get; set; }

    /// <summary>Raised when the app should remember a meter as <see cref="PreferredMeterId"/>.</summary>
    public event EventHandler<DiscoveredMeter>? PreferredMeterChanged;

    public IMeterConnection Active { get; private set; }
    public bool IsDemo => Active == _demo;
    public ConnectionState State => Active.State;
    public DiscoveredMeter? Meter => Active.Meter;

    public MeasurementLog Log { get; } = new();

    /// <summary>When false, readings still update the display but aren't logged.</summary>
    public bool IsLogging
    {
        get => _isLogging;
        set
        {
            if (_isLogging == value) return;
            _isLogging = value;
            LoggingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private bool _isLogging = true;

    /// <summary>Raised when <see cref="IsLogging"/> changes (from the app or an AI assistant).</summary>
    public event EventHandler? LoggingChanged;

    public MeterReading? Latest { get; private set; }

    /// <summary>
    /// Reconnect by itself: at launch (the app asks) and when the link drops. Off, the user
    /// connects by hand every time.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// The meter's battery in volts, as last shown on its SETUP battery page; null until the
    /// meter has shown it (it isn't in the regular packets).
    /// </summary>
    public double? BatteryVolts { get; private set; }

    /// <summary>Signal strength of the connected meter in dBm; null when unknown.</summary>
    public int? SignalStrength => Active.SignalStrength;

    /// <summary>Meters seen in the current scan, strongest signal first.</summary>
    public IReadOnlyList<DiscoveredMeter> Discovered =>
        [.. _discovered.Values.OrderByDescending(m => m.Rssi)];

    public event EventHandler? StateChanged;
    public event EventHandler? DiscoveredChanged;
    public event EventHandler<MeterReading>? ReadingReceived;

    /// <summary>Starts looking for meters.</summary>
    /// <param name="autoConnect">
    /// Connect by itself when the remembered meter shows up. For background reconnects (at
    /// launch, after the link drops); a scan the user starts to pick a meter leaves it off.
    /// </param>
    public async Task StartScanAsync(bool autoConnect = false, CancellationToken ct = default)
    {
        await LeaveDemoAsync().ConfigureAwait(false);
        _autoConnect = autoConnect;
        // A scan already running (a background reconnect) keeps its list: Bluetooth stacks
        // report each device once per scan, so what it found won't be reported again.
        if (_bluetooth.State != ConnectionState.Scanning)
        {
            _discovered.Clear();
            DiscoveredChanged?.Invoke(this, EventArgs.Empty);
        }
        _userDisconnected = false;
        await _bluetooth.StartScanAsync(ct).ConfigureAwait(false);
    }

    public Task StopScanAsync() => _bluetooth.StopScanAsync();

    public async Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default)
    {
        await LeaveDemoAsync().ConfigureAwait(false);
        _userDisconnected = false;
        await _bluetooth.StopScanAsync().ConfigureAwait(false);
        await _bluetooth.ConnectAsync(meter, ct).ConfigureAwait(false);
        if (_bluetooth.State == ConnectionState.Connected && meter.Id != PreferredMeterId)
        {
            PreferredMeterId = meter.Id;
            PreferredMeterChanged?.Invoke(this, meter);
        }
    }

    /// <summary>Switches to the simulated meter (leaving any Bluetooth meter).</summary>
    public async Task StartDemoAsync()
    {
        _userDisconnected = true;
        await _bluetooth.StopScanAsync().ConfigureAwait(false);
        if (_bluetooth.State != ConnectionState.Disconnected)
            await _bluetooth.DisconnectAsync().ConfigureAwait(false);

        Active = _demo;
        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
        await _demo.ConnectAsync(SimulatedMeterConnection.DemoMeter).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        _userDisconnected = true;
        if (Active.State == ConnectionState.Scanning)
            await Active.StopScanAsync().ConfigureAwait(false);
        else
            await Active.DisconnectAsync().ConfigureAwait(false);
    }

    public Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default) =>
        Active.State == ConnectionState.Connected
            ? Active.SendKeyAsync(key, longPress, ct)
            : Task.CompletedTask;

    private async Task LeaveDemoAsync()
    {
        if (!IsDemo) return;
        await _demo.DisconnectAsync().ConfigureAwait(false);
        Active = _bluetooth;
        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnPacket(MeterPacket packet)
    {
        var reading = ReadingDecoder.Decode(packet, _time.GetLocalNow());
        Latest = reading;
        if (packet.SubMode == MeterMode.Battery && !packet.SubOverload)
            BatteryVolts = packet.SubRawValue / Math.Pow(10, packet.SubDecimals);
        if (IsLogging)
            Log.Add(reading);
        ReadingReceived?.Invoke(this, reading);
    }

    private void OnDiscovered(DiscoveredMeter meter)
    {
        _discovered[meter.Id] = meter;
        DiscoveredChanged?.Invoke(this, EventArgs.Empty);

        if (_autoConnect && meter.Id == PreferredMeterId && !IsDemo && _bluetooth.State == ConnectionState.Scanning)
            _ = ConnectQuietlyAsync(meter);
    }

    private void OnConnectionLost()
    {
        // Out of range or switched off: keep looking, and the remembered meter reconnects
        // by itself when it's seen again.
        if (AutoReconnect && !_userDisconnected && !IsDemo)
            _ = ScanQuietlyAsync();
    }

    private async Task ConnectQuietlyAsync(DiscoveredMeter meter)
    {
        try { await ConnectAsync(meter).ConfigureAwait(false); }
        catch (Exception) when (_bluetooth.State != ConnectionState.Connected)
        {
            // Back to scanning; the next sighting tries again.
            await ScanQuietlyAsync().ConfigureAwait(false);
        }
    }

    private async Task ScanQuietlyAsync()
    {
        _autoConnect = true;
        try { await _bluetooth.StartScanAsync().ConfigureAwait(false); }
        catch (Exception)
        {
            // Bluetooth off or not allowed: the user starts the next scan.
        }
    }
}
