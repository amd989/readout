namespace Readout.Core.Protocol;

/// <summary>
/// Turns the meter's byte stream into packets. A packet can arrive split across Bluetooth
/// notifications, or several in one, so bytes are buffered until a whole valid packet is there.
/// </summary>
/// <remarks>
/// Unlike the original app, a frame with a bad checksum doesn't throw the bytes after it
/// away: the search restarts one byte later, so the next good packet is found at once.
/// </remarks>
public sealed class PacketFramer
{
    // Far more than one packet; only reached when the stream is garbage.
    private const int MaxBuffered = 1024;

    private readonly List<byte> _buffer = new(MeterPacket.Length * 4);

    /// <summary>Frames that started with 0xF2 but failed the checksum.</summary>
    public int RejectedFrames { get; private set; }

    /// <summary>Adds bytes and returns every whole packet they complete, oldest first.</summary>
    public IReadOnlyList<MeterPacket> Push(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            _buffer.Add(b);

        if (_buffer.Count > MaxBuffered)
            _buffer.RemoveRange(0, _buffer.Count - MeterPacket.Length);

        List<MeterPacket>? packets = null;
        var start = 0;
        Span<byte> frame = stackalloc byte[MeterPacket.Length];

        while (true)
        {
            start = _buffer.IndexOf(MeterPacket.StartByte, start);
            if (start < 0)
            {
                _buffer.Clear();
                break;
            }
            if (_buffer.Count - start < MeterPacket.Length)
            {
                _buffer.RemoveRange(0, start);
                break;
            }

            for (var i = 0; i < MeterPacket.Length; i++)
                frame[i] = _buffer[start + i];

            if (MeterPacket.TryParse(frame, out var packet))
            {
                (packets ??= []).Add(packet);
                start += MeterPacket.Length;
            }
            else
            {
                RejectedFrames++;
                start++;
            }
        }

        return packets ?? (IReadOnlyList<MeterPacket>)[];
    }

    public void Reset() => _buffer.Clear();
}
