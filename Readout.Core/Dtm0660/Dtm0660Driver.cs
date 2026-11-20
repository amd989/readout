using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Fs9721;

namespace Readout.Core.Dtm0660;

/// <summary>A DTM0660 meter through a Bluetooth bridge that forwards the chip's serial bytes as they come.</summary>
public sealed class Dtm0660Driver : IMeterDriver
{
    private readonly NumberedFramer _framer = new(Dtm0660Frame.Length);

    public MeterModel Model => MeterModel.Dtm0660;

    public IReadOnlyList<DriverReading> Receive(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        var frames = _framer.Push(data);
        if (frames.Count == 0)
            return [];

        var readings = new List<DriverReading>(frames.Count);
        foreach (var bytes in frames)
        {
            if (Dtm0660Frame.TryParse(bytes, out var frame))
                readings.Add(new DriverReading(Dtm0660Decoder.Decode(frame, now)));
        }
        return readings;
    }

    public void Reset() => _framer.Reset();
}
