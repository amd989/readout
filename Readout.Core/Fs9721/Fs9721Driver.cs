using Readout.Core.Connection;
using Readout.Core.Display;

namespace Readout.Core.Fs9721;

/// <summary>An FS9721 meter through a Bluetooth bridge that forwards the chip's serial bytes as they come.</summary>
public sealed class Fs9721Driver(IReadOnlyList<string>? userSymbols = null) : IMeterDriver
{
    private readonly Fs9721Framer _framer = new();

    public MeterModel Model => MeterModel.Fs9721;

    public IReadOnlyList<DriverReading> Receive(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        var frames = _framer.Push(data);
        if (frames.Count == 0)
            return [];

        var readings = new List<DriverReading>(frames.Count);
        foreach (var frame in frames)
            readings.Add(new DriverReading(Fs9721Decoder.Decode(frame, now, userSymbols)));
        return readings;
    }

    public void Reset() => _framer.Reset();
}
