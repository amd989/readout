using Readout.Core.Connection;
using Readout.Core.Display;

namespace Readout.Core.Dso150;

/// <summary>
/// A DSO150 oscilloscope running DSOShell, through the two-way bridge. For now it only takes the
/// bytes and makes no readings: decoding <c>MEAS</c> records into readings (and waveforms after
/// that) is the next step. It exists so a DSO150 gets its own driver, never the 121GW's.
/// </summary>
public sealed class Dso150Driver : IMeterDriver
{
    public MeterModel Model => MeterModel.Dso150;

    /// <summary>Bytes received since the last <see cref="Reset"/>.</summary>
    public long BytesReceived { get; private set; }

    public IReadOnlyList<DriverReading> Receive(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        BytesReceived += data.Length;
        return [];
    }

    public void Reset() => BytesReceived = 0;
}
