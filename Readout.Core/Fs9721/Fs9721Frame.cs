namespace Readout.Core.Fs9721;

/// <summary>
/// One frame from a Fortune FS9721_LP3 meter chip: 14 bytes at 2400 baud, 8N1, a few times a
/// second. Each byte is the LCD's state for one segment line: the high nibble numbers the byte
/// 1 to E, the low nibble holds four segments (bit 3 is COM4, bit 0 is COM1).
/// </summary>
/// <remarks>
/// Written from Fortune's FS9721_LP3 datasheet (section 14, "RS232 传输协议"). The chip only
/// sends while its ENTX pin is pulled to VSS, which many meters leave unconnected.
/// </remarks>
public sealed class Fs9721Frame
{
    public const int Length = 14;

    private readonly byte[] _bytes;

    private Fs9721Frame(byte[] bytes) => _bytes = bytes;

    /// <summary>The frame as received.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Checks the byte numbers and wraps the frame; false when any byte is out of place.</summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out Fs9721Frame frame)
    {
        frame = null!;
        if (bytes.Length != Length)
            return false;
        for (var i = 0; i < Length; i++)
        {
            if (bytes[i] >> 4 != i + 1)
                return false;
        }
        frame = new Fs9721Frame(bytes.ToArray());
        return true;
    }

    /// <summary>Parses a frame written as hex ("17 27 3d … e8"), as the capture tool logs it.</summary>
    public static bool TryParseHex(string hex, out Fs9721Frame frame)
    {
        frame = null!;
        try
        {
            return TryParse(Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal)), out frame);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The four segment bits of byte <paramref name="index"/> (0–13).</summary>
    public int Segments(int index) => _bytes[index] & 0x0F;

    /// <summary>Whether bit <paramref name="bit"/> (0–3) of byte <paramref name="index"/> is lit.</summary>
    public bool Bit(int index, int bit) => (_bytes[index] >> bit & 1) != 0;

    public override string ToString() => Convert.ToHexString(_bytes).ToLowerInvariant();
}
