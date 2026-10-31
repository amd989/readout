using Microsoft.Extensions.Time.Testing;
using Readout.Core.Connection;
using Readout.Core.Protocol;

namespace Readout.Tests;

/// <summary>
/// Sets the demo meter's clock through its buttons. The demo meter edits its clock the way a
/// real 121GW does: hold SETUP, ▲/▼, hold SETUP to save each field.
/// </summary>
public sealed class MeterClockSyncTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 13, 30, 0, TimeSpan.Zero));
    private readonly SimulatedMeterConnection _demo;
    private readonly MeterSession _session;
    private DateTime _now = new(2026, 10, 31, 9, 30, 20);
    private int _presses;

    public MeterClockSyncTests()
    {
        _demo = new SimulatedMeterConnection(_time);
        _demo.Simulator.Clock = () => _now;
        _session = new MeterSession(new FakeBluetooth(), _demo, _time);
    }

    public void Dispose() => _demo.Dispose();

    /// <summary>Each wait moves this device's clock on and has the meter send a packet.</summary>
    private MeterClockSync Sync() => new(_session, () => _now, (delay, _) =>
    {
        _now += delay;
        _demo.Tick();
        return Task.CompletedTask;
    });

    private async Task ConnectAsync()
    {
        await _session.StartDemoAsync();
        _demo.Tick();
    }

    private void CountPresses() => _demo.KeySent += (_, _) => _presses++;

    [Fact]
    public async Task Sync_SetsAWrongClockToThisDevicesTime()
    {
        await ConnectAsync();
        _demo.Simulator.ClockOffset = new DateTime(2019, 10, 14, 16, 42, 0) - _now;

        await Sync().SyncAsync(ct: TestContext.Current.CancellationToken);

        var meter = _demo.Simulator.MeterTime;
        Assert.Equal(_now.Date, meter.Date);
        Assert.InRange((meter - _now).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sync_GoesBackToThePageItStartedOn()
    {
        await ConnectAsync();
        _demo.Simulator.ClockOffset = TimeSpan.FromHours(-5);

        await Sync().SyncAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(MeterMode.SetupTemperatureC, _session.Latest!.Packet.SubMode);
    }

    [Fact]
    public async Task Sync_ClockAlreadyRight_EditsNothing()
    {
        await ConnectAsync();
        CountPresses();

        await Sync().SyncAsync(ct: TestContext.Current.CancellationToken);

        // Only short SETUP presses, to walk the pages and back: no edits.
        Assert.Equal(TimeSpan.Zero, _demo.Simulator.ClockOffset);
        Assert.InRange(_presses, 1, 14);
    }

    [Fact]
    public async Task Sync_NotConnected_SaysSo()
    {
        var error = await Assert.ThrowsAsync<MeterClockSyncException>(() => Sync().SyncAsync(ct: TestContext.Current.CancellationToken));
        Assert.Contains("Connect", error.Message);
    }
}
