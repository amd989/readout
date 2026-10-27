using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Protocol;
using Readout.Core.Simulation;
using Microsoft.Extensions.Time.Testing;

namespace Readout.Tests;

/// <summary>A Bluetooth connection the test drives by hand.</summary>
internal sealed class FakeBluetooth : IMeterConnection
{
    public ConnectionState State { get; private set; }
    public DiscoveredMeter? Meter { get; private set; }
    public int? SignalStrength { get; set; }
    public List<(MeterKey Key, bool Long)> Keys { get; } = [];
    public int ScanStarts { get; private set; }
    public bool FailNextConnect { get; set; }

    public event EventHandler? StateChanged;
    public event EventHandler<DiscoveredMeter>? MeterDiscovered;
    public event EventHandler<MeterPacket>? PacketReceived;
    public event EventHandler? ConnectionLost;

    public Task StartScanAsync(CancellationToken ct = default)
    {
        ScanStarts++;
        Set(ConnectionState.Scanning);
        return Task.CompletedTask;
    }

    public Task StopScanAsync()
    {
        if (State == ConnectionState.Scanning) Set(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default)
    {
        if (FailNextConnect)
        {
            FailNextConnect = false;
            Set(ConnectionState.Disconnected);
            throw new InvalidOperationException("connect failed");
        }
        Meter = meter;
        Set(ConnectionState.Connected);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Meter = null;
        Set(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default)
    {
        Keys.Add((key, longPress));
        return Task.CompletedTask;
    }

    public void Discover(DiscoveredMeter meter) => MeterDiscovered?.Invoke(this, meter);
    public void Send(MeterPacket packet) => PacketReceived?.Invoke(this, packet);

    public void Drop()
    {
        Meter = null;
        Set(ConnectionState.Disconnected);
        ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    private void Set(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class MeterSessionTests : IDisposable
{
    private static readonly DiscoveredMeter Meter = new("aa:bb", "121GW", -60);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeBluetooth _bluetooth = new();
    private readonly SimulatedMeterConnection _demo;
    private readonly MeterSession _session;

    public MeterSessionTests()
    {
        _demo = new SimulatedMeterConnection(_time);
        _session = new MeterSession(_bluetooth, _demo, _time);
    }

    public void Dispose() => _demo.Dispose();

    [Fact]
    public async Task Connect_ThenPackets_AreDecodedAndLogged()
    {
        var readings = new List<MeterReading>();
        _session.ReadingReceived += (_, r) => readings.Add(r);

        await _session.ConnectAsync(Meter);
        _bluetooth.Send(new PacketBuilder { MainRangeIndex = 1, MainRawValue = 12345 }.BuildPacket());

        Assert.Equal(ConnectionState.Connected, _session.State);
        Assert.Equal("12.345 V", Assert.Single(readings).MainLine);
        Assert.Equal(1, _session.Log.Count);
        Assert.Same(readings[0], _session.Latest);
    }

    [Fact]
    public async Task Connect_RemembersTheMeter()
    {
        DiscoveredMeter? remembered = null;
        _session.PreferredMeterChanged += (_, m) => remembered = m;

        await _session.ConnectAsync(Meter);

        Assert.Equal(Meter.Id, _session.PreferredMeterId);
        Assert.Equal(Meter, remembered);
    }

    [Fact]
    public async Task LoggingOff_UpdatesTheReadingButNotTheLog()
    {
        await _session.ConnectAsync(Meter);
        _session.IsLogging = false;

        _bluetooth.Send(new PacketBuilder { MainRawValue = 1 }.BuildPacket());

        Assert.NotNull(_session.Latest);
        Assert.Equal(0, _session.Log.Count);
    }

    [Fact]
    public async Task UserScan_SeeingThePreferredMeter_WaitsForAPick()
    {
        _session.PreferredMeterId = Meter.Id;
        await _session.StartScanAsync();

        _bluetooth.Discover(Meter);

        Assert.Equal(ConnectionState.Scanning, _session.State);
        Assert.Equal(Meter, Assert.Single(_session.Discovered));
    }

    [Fact]
    public async Task DisconnectThenUserScan_DoesNotReconnectByItself()
    {
        await _session.ConnectAsync(Meter);
        await _session.DisconnectAsync();

        await _session.StartScanAsync();
        _bluetooth.Discover(Meter);

        Assert.Equal(ConnectionState.Scanning, _session.State);
    }

    [Fact]
    public async Task UserScan_DuringABackgroundScan_KeepsTheMetersAlreadySeen()
    {
        await _session.StartScanAsync(autoConnect: true);
        _bluetooth.Discover(new DiscoveredMeter("other", "121GW", -70));

        await _session.StartScanAsync();

        Assert.Equal("other", Assert.Single(_session.Discovered).Id);
    }

    [Fact]
    public async Task AutoConnectScan_SeeingThePreferredMeter_ConnectsByItself()
    {
        _session.PreferredMeterId = Meter.Id;
        await _session.StartScanAsync(autoConnect: true);

        _bluetooth.Discover(new DiscoveredMeter("other", "121GW", -40));
        Assert.Equal(ConnectionState.Scanning, _session.State);

        _bluetooth.Discover(Meter);
        Assert.Equal(ConnectionState.Connected, _session.State);
        Assert.Equal(Meter, _session.Meter);
    }

    [Fact]
    public async Task Scan_ListsMetersStrongestFirst()
    {
        await _session.StartScanAsync();
        _bluetooth.Discover(new DiscoveredMeter("far", "121GW", -90));
        _bluetooth.Discover(new DiscoveredMeter("near", "121GW", -40));
        _bluetooth.Discover(new DiscoveredMeter("far", "121GW", -95));

        Assert.Equal(["near", "far"], _session.Discovered.Select(m => m.Id));
    }

    [Fact]
    public async Task ConnectionLost_ScansAndReconnectsWhenTheMeterIsBack()
    {
        await _session.ConnectAsync(Meter);
        var scans = _bluetooth.ScanStarts;

        _bluetooth.Drop();
        Assert.Equal(scans + 1, _bluetooth.ScanStarts);
        Assert.Equal(ConnectionState.Scanning, _session.State);

        _bluetooth.Discover(Meter);
        Assert.Equal(ConnectionState.Connected, _session.State);
    }

    [Fact]
    public async Task AutoReconnectOff_ConnectionLost_DoesNotRescan()
    {
        _session.AutoReconnect = false;
        await _session.ConnectAsync(Meter);
        var scans = _bluetooth.ScanStarts;

        _bluetooth.Drop();

        Assert.Equal(scans, _bluetooth.ScanStarts);
        Assert.Equal(ConnectionState.Disconnected, _session.State);
    }

    [Fact]
    public async Task BatteryPage_IsRemembered()
    {
        await _session.ConnectAsync(Meter);
        Assert.Null(_session.BatteryVolts);

        _bluetooth.Send(new PacketBuilder { SubMode = MeterMode.Battery, SubRawValue = 570, SubDecimals = 2 }.BuildPacket());
        _bluetooth.Send(new PacketBuilder { SubMode = MeterMode.Temperature, SubRawValue = 235, SubDecimals = 1 }.BuildPacket());

        Assert.Equal(5.70, _session.BatteryVolts!.Value, 9);
    }

    [Fact]
    public async Task SignalStrength_IsTheActiveConnections()
    {
        _bluetooth.SignalStrength = -63;
        await _session.ConnectAsync(Meter);
        Assert.Equal(-63, _session.SignalStrength);

        await _session.StartDemoAsync();
        Assert.Equal(SimulatedMeterConnection.DemoMeter.Rssi, _session.SignalStrength);
    }

    [Fact]
    public async Task UserDisconnect_DoesNotRescan()
    {
        await _session.ConnectAsync(Meter);
        await _session.DisconnectAsync();
        var scans = _bluetooth.ScanStarts;

        _bluetooth.Drop();

        Assert.Equal(scans, _bluetooth.ScanStarts);
    }

    [Fact]
    public async Task AutoConnectFailure_GoesBackToScanning()
    {
        _session.PreferredMeterId = Meter.Id;
        await _session.StartScanAsync(autoConnect: true);
        _bluetooth.FailNextConnect = true;

        _bluetooth.Discover(Meter);

        Assert.Equal(ConnectionState.Scanning, _session.State);
    }

    [Fact]
    public async Task Keys_GoToTheConnectedMeterOnly()
    {
        await _session.SendKeyAsync(MeterKey.Hold);
        Assert.Empty(_bluetooth.Keys);

        await _session.ConnectAsync(Meter);
        await _session.SendKeyAsync(MeterKey.Mode, longPress: true);

        Assert.Equal((MeterKey.Mode, true), Assert.Single(_bluetooth.Keys));
    }

    [Fact]
    public async Task Demo_StreamsReadingsAndLeavesBluetoothAlone()
    {
        await _session.StartDemoAsync();
        _demo.Tick();
        _demo.Tick();

        Assert.True(_session.IsDemo);
        Assert.Equal(ConnectionState.Connected, _session.State);
        Assert.Equal(2, _session.Log.Count);
        Assert.Equal("DC voltage", _session.Latest!.ModeName);

        _bluetooth.Send(new PacketBuilder().BuildPacket());
        Assert.Equal(2, _session.Log.Count);
    }

    [Fact]
    public async Task Demo_ModeKey_ChangesTheReading()
    {
        await _session.StartDemoAsync();
        await _session.SendKeyAsync(MeterKey.Mode);
        _demo.Tick();

        Assert.Equal("AC voltage", _session.Latest!.ModeName);
        Assert.Equal("Hz", _session.Latest.Sub.Unit);
    }

    [Fact]
    public async Task Scan_AfterDemo_SwitchesBackToBluetooth()
    {
        await _session.StartDemoAsync();
        await _session.StartScanAsync();

        Assert.False(_session.IsDemo);
        Assert.Equal(ConnectionState.Disconnected, _demo.State);
        Assert.Equal(ConnectionState.Scanning, _session.State);
    }
}

public class SimulatedMeterTests
{
    [Fact]
    public void EveryMode_DecodesToAValue()
    {
        var meter = new SimulatedMeter();
        var seen = new HashSet<MeterMode>();

        for (var i = 0; i < 5; i++)
        {
            Assert.True(MeterPacket.TryParse(meter.PacketAt(TimeSpan.FromSeconds(i)), out var packet));
            var reading = ReadingDecoder.Decode(packet, DateTimeOffset.UnixEpoch);
            Assert.NotNull(reading.Value);
            Assert.NotEqual("", reading.MainUnit);
            seen.Add(reading.Mode);
            meter.Press(MeterKey.Mode);
        }

        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Hold_FreezesTheReading()
    {
        var meter = new SimulatedMeter();
        meter.PacketAt(TimeSpan.FromSeconds(1));
        meter.Press(MeterKey.Hold);

        Assert.True(MeterPacket.TryParse(meter.PacketAt(TimeSpan.FromSeconds(5)), out var a));
        Assert.True(MeterPacket.TryParse(meter.PacketAt(TimeSpan.FromSeconds(9)), out var b));

        Assert.Equal(a.MainRawValue, b.MainRawValue);
        Assert.Equal(HoldState.Hold, b.Hold);
    }
}
