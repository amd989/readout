using Readout.Core.Display;
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
/// <param name="Model">What kind of meter it is, which picks the driver for its data.</param>
public sealed record DiscoveredMeter(string Id, string Name, int Rssi, MeterModel Model = MeterModel.Eevblog121Gw);

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

    /// <summary>
    /// Bytes from the meter, as they arrived: a packet may be split across several of these, or
    /// several packets may come in one. The session's driver for <see cref="Meter"/> frames them.
    /// </summary>
    event EventHandler<ReadOnlyMemory<byte>>? DataReceived;

    /// <summary>The link dropped without <see cref="DisconnectAsync"/> being called.</summary>
    event EventHandler? ConnectionLost;

    /// <summary>Scans until <see cref="StopScanAsync"/>, a connect, or cancellation.</summary>
    Task StartScanAsync(CancellationToken ct = default);

    Task StopScanAsync();

    Task ConnectAsync(DiscoveredMeter meter, CancellationToken ct = default);

    Task DisconnectAsync();

    /// <summary>Presses one of the meter's buttons remotely (the 121GW).</summary>
    Task SendKeyAsync(MeterKey key, bool longPress = false, CancellationToken ct = default);

    /// <summary>
    /// Sends bytes to the meter as they are: a two-way bridge writes them to its chip's serial
    /// input (<see cref="BridgeBluetooth.RxCharacteristicUuid"/>). Does nothing while not
    /// connected.
    /// </summary>
    /// <exception cref="NotSupportedException">The connected meter takes no bytes (a 121GW, an FS9721 bridge).</exception>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
