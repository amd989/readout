using Readout.Core.Display;
using Readout.Core.Fs9721;

namespace Readout.Core.Connection;

/// <summary>A reading from a driver, with what else the meter's data told it.</summary>
/// <param name="InferredKey">A key pressed on the meter itself, as worked out from the display.</param>
/// <param name="BatteryVolts">The meter's battery, when this reading showed it.</param>
public sealed record DriverReading(MeterReading Reading, MeterKeyPress? InferredKey = null, double? BatteryVolts = null);

/// <summary>
/// The protocol side of one kind of meter: turns the bytes its connection receives into
/// readings. Connections only move bytes; a driver knows the format. Used on one thread.
/// </summary>
public interface IMeterDriver
{
    MeterModel Model { get; }

    /// <summary>Adds bytes as they arrived and returns the readings they complete, oldest first.</summary>
    /// <param name="now">When the bytes arrived, local time.</param>
    IReadOnlyList<DriverReading> Receive(ReadOnlySpan<byte> data, DateTimeOffset now);

    /// <summary>A new link: drops any partly received packet.</summary>
    void Reset();
}

public static class MeterDrivers
{
    public static IMeterDriver For(MeterModel model) => model switch
    {
        MeterModel.Fs9721 => new Fs9721Driver(),
        _ => new Eevblog121GwDriver(),
    };
}
