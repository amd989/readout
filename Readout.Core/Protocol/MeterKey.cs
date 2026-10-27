namespace Readout.Core.Protocol;

/// <summary>The meter's front-panel buttons, in the numbering its remote-key command uses.</summary>
public enum MeterKey : byte
{
    Range = 1,
    Hold = 2,
    /// <summary>REL, which is also ▲ inside SETUP.</summary>
    Rel = 3,
    /// <summary>1ms PEAK, which is also ▼ inside SETUP.</summary>
    Peak = 4,
    Mode = 5,
    MinMax = 6,
    Mem = 7,
    Setup = 8,
}

/// <summary>Builds the commands that press the meter's buttons remotely.</summary>
public static class MeterCommands
{
    /// <summary>
    /// A five-byte key press: <c>F4 k c k c</c>, where <c>k</c> is ASCII '0' for a short press
    /// or '8' for a long one, and <c>c</c> is the key's number as an ASCII digit.
    /// </summary>
    public static byte[] KeyPress(MeterKey key, bool longPress = false)
    {
        if (!Enum.IsDefined(key))
            throw new ArgumentOutOfRangeException(nameof(key), key, "Not a 121GW key.");

        var kind = (byte)(longPress ? '8' : '0');
        var code = (byte)('0' + (byte)key);
        return [0xF4, kind, code, kind, code];
    }
}
