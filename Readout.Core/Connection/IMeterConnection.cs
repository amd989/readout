using Readout.Core.Protocol;

namespace Readout.Core.Connection;

public enum ConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
}

/// <summary>A meter seen while scanning.</summary>
/// <param name="Id">The platform's id for the device; stable across launches on the same phone.</param>
public sealed record DiscoveredMeter(string Id, string Name, int Rssi);

/// <summary>
/// One way of talking to a meter: Bluetooth on a given platform, or the simulated meter.
/// Events may be raised on any thread.
/// </summary>
public interface IMeterConnection
{
    ConnectionState State { get; }

    /// <summary>The meter being connected to or connected; null otherwise.</summary>
    DiscoveredMeter? Meter { get; }

    /// <summary>Signal strength of the connected meter in dBm (−40 strong, −90 weak); null when unknown.</summary>
    int? SignalStrength { get; }

    event EventHandler? StateChanged;

    /// <summary>A meter was seen while scanning (again, possibly, with a new signal strength).</summary>
    event EventHandler<DiscoveredMeter>? MeterDiscovered;

    /// <summary>The meter sent a valid packet.</summary>
    event EventHandler<MeterPacket>? PacketReceived;

    /// <summary>The link dropped without <see cref="DisconnectAsync"/> being called.</summary>
    event EventHandler? ConnectionLost;

    /// <summary>Scans until <see cref="StopScanAsync"/>, a connect, or cancellation.</summary>
    Task StartScanAsync(CancellationToken ct = default);

    Task StopScanAsync();

    Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default);

    Task DisconnectAsync();

    /// <summary>Presses one of the meter's buttons remotely.</summary>
    Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default);
}
