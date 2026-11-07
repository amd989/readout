namespace Readout.Core.Fs9721;

/// <summary>
/// Turns an FS9721 byte stream into frames. The bytes can arrive in any chunks (a Bluetooth
/// bridge may split or join frames), and a frame is only taken when all 14 bytes are numbered
/// 1 to E in order, so the stream resynchronises on the next byte numbered 1.
/// </summary>
public sealed class Fs9721Framer
{
    private readonly byte[] _buffer = new byte[Fs9721Frame.Length];
    private int _count;

    /// <summary>Partial frames dropped because a byte came out of order.</summary>
    public int RejectedBytes { get; private set; }

    /// <summary>Adds bytes and returns every whole frame they complete, oldest first.</summary>
    public IReadOnlyList<Fs9721Frame> Push(ReadOnlySpan<byte> bytes)
    {
        List<Fs9721Frame>? frames = null;
        foreach (var b in bytes)
        {
            var number = b >> 4;
            if (number == _count + 1)
            {
                _buffer[_count++] = b;
            }
            else
            {
                RejectedBytes++;
                // Out of step: a byte numbered 1 starts a new frame, anything else is dropped.
                _count = 0;
                if (number == 1)
                    _buffer[_count++] = b;
            }

            if (_count == Fs9721Frame.Length)
            {
                _count = 0;
                if (Fs9721Frame.TryParse(_buffer, out var frame))
                    (frames ??= []).Add(frame);
            }
        }
        return frames ?? (IReadOnlyList<Fs9721Frame>)[];
    }

    public void Reset() => _count = 0;
}
