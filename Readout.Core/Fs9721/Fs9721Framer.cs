namespace Readout.Core.Fs9721;

/// <summary>
/// Turns an FS9721 byte stream into frames. The bytes can arrive in any chunks (a Bluetooth
/// bridge may split or join frames), and a frame is only taken when all 14 bytes are numbered
/// 1 to E in order, so the stream resynchronises on the next byte numbered 1.
/// </summary>
public sealed class Fs9721Framer
{
    private readonly NumberedFramer _framer = new(Fs9721Frame.Length);

    /// <summary>Bytes dropped because they came out of order.</summary>
    public int RejectedBytes => _framer.RejectedBytes;

    /// <summary>Adds bytes and returns every whole frame they complete, oldest first.</summary>
    public IReadOnlyList<Fs9721Frame> Push(ReadOnlySpan<byte> bytes)
    {
        var raw = _framer.Push(bytes);
        if (raw.Count == 0)
            return [];
        var frames = new List<Fs9721Frame>(raw.Count);
        foreach (var bytesOfFrame in raw)
        {
            if (Fs9721Frame.TryParse(bytesOfFrame, out var frame))
                frames.Add(frame);
        }
        return frames;
    }

    public void Reset() => _framer.Reset();
}
