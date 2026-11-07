using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Connection;

/// <summary>
/// The EEVblog 121GW: frames its packets, decodes them, and works out from one packet to the
/// next what the packets don't say: keys pressed on the meter, its blank display after a mode
/// change, and its battery voltage when the SETUP page shows it.
/// </summary>
public sealed class Eevblog121GwDriver : IMeterDriver
{
    /// <summary>How long after a mode change a 0 can be the meter's blank display.</summary>
    private static readonly TimeSpan BlankAfterModeChange = TimeSpan.FromSeconds(2.5);

    private readonly PacketFramer _framer = new();
    private MeterReading? _latest;
    private DateTimeOffset? _modeChangedAt;

    public MeterModel Model => MeterModel.Eevblog121Gw;

    public IReadOnlyList<DriverReading> Receive(ReadOnlySpan<byte> data, DateTimeOffset now)
    {
        var packets = _framer.Push(data);
        if (packets.Count == 0)
            return [];

        var readings = new List<DriverReading>(packets.Count);
        foreach (var packet in packets)
            readings.Add(Decode(packet, now));
        return readings;
    }

    public void Reset() => _framer.Reset();

    private DriverReading Decode(MeterPacket packet, DateTimeOffset now)
    {
        var reading = ReadingDecoder.Decode(packet, now, _latest?.Sub.Label);
        var last = _latest?.Packet;
        var pressed = last is not null ? KeyInference.Infer(last, packet) : null;

        // After a mode change the meter's display stays blank until it has measured, but its
        // packets say 0 (a real meter, on temperature, for about two seconds). The same after
        // each range step while autoranging (on Ω, stepping up to OFL), with a bar that jumps.
        if (last is not null && (last.Mode != packet.Mode ||
            (last.Coupling == Coupling.AcPlusDc) != (packet.Coupling == Coupling.AcPlusDc) ||
            (packet.AutoRange && last.MainRangeIndex != packet.MainRangeIndex)))
            _modeChangedAt = now;
        if (_modeChangedAt is { } changed)
        {
            if (!packet.MainOverload && packet.MainRawValue == 0 && now - changed < BlankAfterModeChange)
                reading = reading with
                {
                    Blank = true, MainText = "", MainNegative = false, MainUnit = "", Value = null, BaseValue = null,
                    Bar = reading.Bar with { Segments = 0 },
                };
            else
                _modeChangedAt = null;
        }
        _latest = reading;

        double? battery = packet.SubMode == MeterMode.Battery && !packet.SubOverload
            ? packet.SubRawValue / Math.Pow(10, packet.SubDecimals)
            : null;
        return new DriverReading(reading, pressed, battery);
    }
}
