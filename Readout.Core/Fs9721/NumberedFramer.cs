namespace Readout.Core.Fs9721;

/// <summary>
/// Frames a stream whose bytes carry their position in the frame in the high nibble, 1 to
/// <c>length</c>, as FS9721 and DTM0660 meters send them. The bytes can arrive in any chunks (a
/// Bluetooth bridge may split or join frames), and a frame is only taken when every byte is
/// numbered in order, so the stream resynchronises on the next byte numbered 1.
/// </summary>
internal sealed class NumberedFramer(int length)
{
    private readonly byte[] _buffer = new byte[length];
    private int _count;

    /// <summary>Bytes dropped because they came out of order.</summary>
    public int RejectedBytes { get; private set; }

    /// <summary>Adds bytes and returns every whole frame they complete, oldest first.</summary>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        List<byte[]>? frames = null;
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

            if (_count == _buffer.Length)
            {
                _count = 0;
                (frames ??= []).Add(_buffer.ToArray());
            }
        }
        return frames ?? (IReadOnlyList<byte[]>)[];
    }

    public void Reset() => _count = 0;
}
