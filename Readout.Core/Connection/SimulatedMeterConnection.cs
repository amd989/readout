using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Core.Connection;

/// <summary>
/// A connection to <see cref="SimulatedMeter"/>: a scan finds it at once, and once connected
/// it sends a packet every <see cref="Interval"/>, split across two "notifications" the way
/// Bluetooth delivers them, so the framing path is the real one.
/// </summary>
public sealed class SimulatedMeterConnection : IMeterConnection, IDisposable
{
    public static readonly DiscoveredMeter DemoMeter = new("demo", "121GW (demo)", -50);

    private readonly SimulatedMeter _meter = new();
    private readonly PacketFramer _framer = new();
    private readonly TimeProvider _time;
    private CancellationTokenSource? _streamCts;
    private DateTimeOffset _poweredOn;

    public SimulatedMeterConnection(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The simulated meter itself, for tests that set up its state (its clock, say).</summary>
    public SimulatedMeter Simulator => _meter;

    /// <summary>A key press reached the simulated meter (for tests).</summary>
    public event EventHandler<MeterKey>? KeySent;

    public ConnectionState State { get; private set; }
    public DiscoveredMeter? Meter { get; private set; }

    /// <summary>A strong, steady signal while connected.</summary>
    public int? SignalStrength => State == ConnectionState.Connected ? DemoMeter.Rssi : null;

    public event EventHandler? StateChanged;
    public event EventHandler<DiscoveredMeter>? MeterDiscovered;
    public event EventHandler<MeterPacket>? PacketReceived;
    public event EventHandler? ConnectionLost;

    public Task StartScanAsync(CancellationToken ct = default)
    {
        if (State != ConnectionState.Disconnected) return Task.CompletedTask;
        SetState(ConnectionState.Scanning);
        MeterDiscovered?.Invoke(this, DemoMeter);
        return Task.CompletedTask;
    }

    public Task StopScanAsync()
    {
        if (State == ConnectionState.Scanning) SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default)
    {
        if (State == ConnectionState.Connected) return Task.CompletedTask;

        Meter = DemoMeter;
        SetState(ConnectionState.Connecting);
        _poweredOn = _time.GetUtcNow();
        _framer.Reset();
        _streamCts = new CancellationTokenSource();
        SetState(ConnectionState.Connected);
        _ = StreamAsync(_streamCts.Token);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        StopStream();
        Meter = null;
        SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default)
    {
        if (State == ConnectionState.Connected)
        {
            _meter.Press(key, longPress);
            KeySent?.Invoke(this, key);
        }
        return Task.CompletedTask;
    }

    /// <summary>Makes the next packet arrive now (tests drive the meter with this instead of waiting).</summary>
    public void Tick()
    {
        var bytes = _meter.PacketAt(_time.GetUtcNow() - _poweredOn);
        Deliver(bytes.AsSpan(0, 7));
        Deliver(bytes.AsSpan(7));
    }

    /// <summary>Pretends the link dropped (for tests of reconnecting).</summary>
    public void SimulateConnectionLost()
    {
        StopStream();
        Meter = null;
        SetState(ConnectionState.Disconnected);
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    private async Task StreamAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(Interval, _time, ct).ConfigureAwait(false);
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // disconnected
        }
    }

    private void Deliver(ReadOnlySpan<byte> notification)
    {
        foreach (var packet in _framer.Push(notification))
            PacketReceived?.Invoke(this, packet);
    }

    private void StopStream()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => StopStream();
}
