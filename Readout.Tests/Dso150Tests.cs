using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Dso150;
using Readout.Core.Fs9721;
using Microsoft.Extensions.Time.Testing;

namespace Readout.Tests;

public class Dso150Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Driver_TakesBytes_AndMakesNoReadings()
    {
        var driver = new Dso150Driver();

        Assert.Equal(MeterModel.Dso150, driver.Model);
        Assert.Empty(driver.Receive("DSOShell,DSO150,1234,0.1\n"u8, Now));
        Assert.Empty(driver.Receive("EVT:KEY,down=0x01,up=0x00\n"u8, Now));
        Assert.Equal(51, driver.BytesReceived);

        driver.Reset();
        Assert.Equal(0, driver.BytesReceived);
    }

    [Theory]
    [InlineData(MeterModel.Eevblog121Gw, typeof(Eevblog121GwDriver))]
    [InlineData(MeterModel.Fs9721, typeof(Fs9721Driver))]
    [InlineData(MeterModel.Dso150, typeof(Dso150Driver))]
    public void EachModel_GetsItsOwnDriver(MeterModel model, Type driver)
    {
        var picked = MeterDrivers.For(model);
        Assert.IsType(driver, picked);
        Assert.Equal(model, picked.Model);
    }

    [Fact]
    public void EveryModel_HasADriverAndCapabilities()
    {
        // A new model must be added to both switches, not fall through to the 121GW's.
        foreach (var model in Enum.GetValues<MeterModel>())
        {
            Assert.Equal(model, MeterDrivers.For(model).Model);
            Assert.NotNull(MeterCapabilities.For(model));
        }
    }

    [Fact]
    public void UnknownModel_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterDrivers.For((MeterModel)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeterCapabilities.For((MeterModel)99));
    }

    [Fact]
    public void Capabilities_NoKeysClockOrLcdExtras()
    {
        var caps = MeterCapabilities.For(MeterModel.Dso150);

        Assert.Same(MeterCapabilities.Dso150, caps);
        Assert.False(caps.RemoteKeys);
        Assert.False(caps.ClockSync);
        Assert.False(caps.SubDisplay);
        Assert.False(caps.BarGraph);
    }

    [Fact]
    public async Task SimulatedConnection_TakesBytesOnlyWhileConnected()
    {
        using var demo = new SimulatedMeterConnection(new FakeTimeProvider(Now));
        var sent = new List<byte[]>();
        demo.DataSent += (_, data) => sent.Add(data.ToArray());

        await demo.SendAsync("*IDN?\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await demo.ConnectAsync(SimulatedMeterConnection.DemoMeter, TestContext.Current.CancellationToken);
        await demo.SendAsync("*IDN?\n"u8.ToArray(), TestContext.Current.CancellationToken);
        await demo.SendAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken);

        Assert.Equal("*IDN?\n"u8.ToArray(), Assert.Single(sent));
    }
}
