using Readout.Core.Display;
using Readout.Core.Fs9721;

namespace Readout.Core.Dtm0660;

/// <summary>
/// Decodes DTM0660 frames into what the LCD shows. The first 14 bytes are an FS9721 frame with
/// each byte's segment bits in the opposite order, so they're turned around and read by
/// <see cref="Fs9721Decoder"/>; the 15th byte adds MAX, MIN and auto power-off.
/// </summary>
public static class Dtm0660Decoder
{
    /// <summary>
    /// Byte 13's symbols once turned around, bit 0 first: the two maker-chosen symbols (not
    /// printed: their meaning differs between meters), then °C and °F.
    /// </summary>
    private static readonly IReadOnlyList<string> Symbols = ["", "", "°C", "°F"];

    public static MeterReading Decode(Dtm0660Frame frame, DateTimeOffset receivedAt)
    {
        var reading = Fs9721Decoder.Decode(frame.ToFs9721(), receivedAt, Symbols);
        return reading with
        {
            Model = MeterModel.Dtm0660,
            Raw = frame.Bytes.ToArray(),
            Icons = reading.Icons with
            {
                AutoPowerOff = frame.Bit(14, 0),
                Min = frame.Bit(14, 1),
                Max = frame.Bit(14, 3),
            },
        };
    }
}
