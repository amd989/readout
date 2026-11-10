using Readout.Core.Display;

namespace Readout.Core.Protocol;

/// <summary>
/// How a Readout bridge shows up over Bluetooth LE: an ESP32 wired to a meter chip's serial port
/// that forwards the bytes as they come (firmware in <c>firmware/readout-bridge</c>). The app
/// frames and decodes them, so supporting another chip needs no change to what the bridge does
/// with the bytes. Two-way bridges (the DSO150's) also pass what the app writes to the chip.
/// </summary>
public static class BridgeBluetooth
{
    /// <summary>The bridge's service, advertised so a scan can find it.</summary>
    public static readonly Guid ServiceUuid = Guid.Parse("1AC73563-3941-40BC-90D4-47FB89F88271");

    /// <summary>Notifies the meter's bytes as they arrive.</summary>
    public static readonly Guid DataCharacteristicUuid = Guid.Parse("4D19A30C-94D7-404E-A69D-B513188F13DC");

    /// <summary>
    /// Readable text, one <c>key=value</c> per line: <c>chip</c> (FS9721, DSO150), <c>symbols</c>
    /// (an FS9721 meter's maker-chosen symbols, comma separated), <c>baud</c> and <c>firmware</c>.
    /// </summary>
    public static readonly Guid InfoCharacteristicUuid = Guid.Parse("6DAA6868-77CD-4539-8B02-6A82B3C6BC0C");

    /// <summary>
    /// Writable (with or without response) on two-way bridges only: the bytes go out the chip's
    /// serial input unchanged. Receive-only bridges (FS9721) don't have it.
    /// </summary>
    public static readonly Guid RxCharacteristicUuid = Guid.Parse("3065C3CA-B78A-4B98-96B6-280CF0784EC4");

    /// <summary>Bridges advertise as "Readout &lt;chip&gt; &lt;id&gt;", for example "Readout FS9721 A1B2".</summary>
    public const string NamePrefix = "Readout ";

    /// <summary>The kind of meter behind a bridge, from its advertised name; null when it isn't a bridge.</summary>
    public static MeterModel? ModelFromName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var chip = name[NamePrefix.Length..].Split(' ', 2)[0];
        if (chip.Equals("FS9721", StringComparison.OrdinalIgnoreCase)) return MeterModel.Fs9721;
        if (chip.Equals("DSO150", StringComparison.OrdinalIgnoreCase)) return MeterModel.Dso150;
        return null;
    }

    /// <summary>The ATT MTU the app and the bridge ask for: 244-byte notifications instead of 20.</summary>
    public const int PreferredMtu = 247;
}
