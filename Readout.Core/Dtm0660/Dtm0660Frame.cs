using Readout.Core.Fs9721;

namespace Readout.Core.Dtm0660;

/// <summary>
/// One frame from a Dream Tech DTM0660 meter chip: 15 bytes at 2400 baud, 8N1, a few times a
/// second, once serial output is turned on in the meter's EEPROM. Like the FS9721 each byte is
/// one segment line of the LCD with its number (1 to F) in the high nibble, but the four segment
/// bits come in the opposite order, and a 15th byte adds MAX, MIN, MAX/MIN and auto power-off.
/// </summary>
/// <remarks>
/// Layout as libsigrok's <c>dtm0660.c</c> documents it (bit 0 to bit 3): byte 0 AC, DC, AUTO,
/// RS232; bytes 1–8 four digits, two bytes each, bit 0 of the first being the minus sign
/// (digit 1) or the decimal point before the digit (digits 2–4); byte 9 µ, n, k, diode; byte 10
/// m, %, M, beep; byte 11 F, Ω, REL, HOLD; byte 12 A, V, Hz, low battery; byte 13 °F, °C and
/// two symbols each maker chooses; byte 14 auto power-off, MIN, MAX/MIN, MAX.
/// </remarks>
public sealed class Dtm0660Frame
{
    public const int Length = 15;

    private readonly byte[] _bytes;

    private Dtm0660Frame(byte[] bytes) => _bytes = bytes;

    /// <summary>The frame as received.</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Checks the byte numbers and wraps the frame; false when any byte is out of place.</summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out Dtm0660Frame frame)
    {
        frame = null!;
        if (bytes.Length != Length)
            return false;
        for (var i = 0; i < Length; i++)
        {
            if (bytes[i] >> 4 != i + 1)
                return false;
        }
        frame = new Dtm0660Frame(bytes.ToArray());
        return true;
    }

    /// <summary>Parses a frame written as hex ("1e 21 3a … f8"), as the bridge logs it.</summary>
    public static bool TryParseHex(string hex, out Dtm0660Frame frame)
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

    /// <summary>Whether bit <paramref name="bit"/> (0–3) of byte <paramref name="index"/> is lit.</summary>
    public bool Bit(int index, int bit) => (_bytes[index] >> bit & 1) != 0;

    /// <summary>
    /// The first 14 bytes with each byte's segment bits turned around: exactly the frame an
    /// FS9721 sends for the same LCD, so <see cref="Fs9721Decoder"/> can read it. Byte 13's
    /// symbols land as °F on bit 3 and °C on bit 2.
    /// </summary>
    public Fs9721Frame ToFs9721()
    {
        var bytes = new byte[Fs9721Frame.Length];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(_bytes[i] & 0xF0 | ReversedNibble[_bytes[i] & 0x0F]);
        // The byte numbers carry over, and bytes 1–E are exactly what an FS9721 frame needs.
        if (!Fs9721Frame.TryParse(bytes, out var frame))
            throw new InvalidOperationException("A DTM0660 frame's first 14 bytes are always an FS9721 frame.");
        return frame;
    }

    private static readonly byte[] ReversedNibble = [0x0, 0x8, 0x4, 0xC, 0x2, 0xA, 0x6, 0xE, 0x1, 0x9, 0x5, 0xD, 0x3, 0xB, 0x7, 0xF];

    public override string ToString() => Convert.ToHexString(_bytes).ToLowerInvariant();
}
