namespace Readout.Core.Protocol;

/// <summary>
/// One 19-byte status packet from the 121GW. The meter sends one several times a second.
/// </summary>
/// <remarks>
/// Layout (see docs/PROTOCOL.md):
/// <code>
///  0     0xF2 start byte
///  1     year - 2000
///  2     month (high nibble), serial digit 1 (low nibble)
///  3-4   serial digits 2-5, one per nibble
///  5     main mode (bits 0-4), main value bits 16-17 (bits 6-7)
///  6     main flags (high nibble: overload, negative, °C, °F), main range (low nibble)
///  7-8   main value bits 0-15, big-endian
///  9     sub mode
///  10    sub flags (high nibble: overload, negative, k, Hz), sub decimal places (low nibble)
///  11-12 sub value, big-endian
///  13    bar status
///  14    bar value (bits 0-4)
///  15-17 status bytes 1-3 (symbols)
///  18    XOR of bytes 0-17
/// </code>
/// Ported from the official EEVblog 121GW app (Copyright (c) 2018 David Ledger, MIT),
/// <c>121GW.Core/Controls/Multimeter/Packet121GW.cs</c> on its PrivatePostRelease branch.
/// </remarks>
public sealed class MeterPacket
{
    public const int Length = 19;
    public const byte StartByte = 0xF2;

    private readonly byte[] _data;

    private MeterPacket(byte[] data) => _data = data;

    /// <summary>The packet's bytes, start byte and checksum included.</summary>
    public ReadOnlySpan<byte> Bytes => _data;

    /// <summary>Parses one whole packet; false when the length, start byte or checksum is wrong.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out MeterPacket packet)
    {
        packet = null!;
        if (frame.Length != Length || frame[0] != StartByte || !ChecksumMatches(frame))
            return false;

        packet = new MeterPacket(frame.ToArray());
        return true;
    }

    /// <summary>True when byte 18 is the XOR of bytes 0–17.</summary>
    public static bool ChecksumMatches(ReadOnlySpan<byte> frame) =>
        frame.Length == Length && Checksum(frame[..^1]) == frame[^1];

    /// <summary>XOR of every byte.</summary>
    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (var b in bytes)
            sum ^= b;
        return sum;
    }

    private int High(int index) => _data[index] >> 4;
    private int Low(int index) => _data[index] & 0x0F;

    // ── Identity ────────────────────────────────────────────────────────────
    // The meter sends a fixed year and month (not today's date), and the serial number
    // the user set in SETUP.

    public int Year => _data[1] + 2000;
    public int Month => High(2);
    public int Serial => Low(2) * 10000 + High(3) * 1000 + Low(3) * 100 + High(4) * 10 + Low(4);

    // ── Main display ────────────────────────────────────────────────────────

    /// <summary>The mode bits as sent, before the °C/°F bits are applied.</summary>
    public MeterMode RawMode => (MeterMode)(_data[5] & 0x1F);

    /// <summary>The main mode, with plain <see cref="MeterMode.Temperature"/> resolved to °C or °F.</summary>
    public MeterMode Mode
    {
        get
        {
            var mode = RawMode;
            if (mode == MeterMode.Temperature)
            {
                if (MainCelsius) return MeterMode.TemperatureC;
                if (MainFahrenheit) return MeterMode.TemperatureF;
            }
            return mode;
        }
    }

    public bool MainOverload => (High(6) & 0x8) != 0;
    public bool MainNegative => (High(6) & 0x4) != 0;
    public bool MainCelsius => (High(6) & 0x2) != 0;
    public bool MainFahrenheit => (High(6) & 0x1) != 0;
    public int MainRangeIndex => Low(6);

    /// <summary>The main reading without its decimal point: 18 bits, so up to 262143.</summary>
    public int MainRawValue => ((_data[5] >> 6) & 0x3) << 16 | _data[7] << 8 | _data[8];

    // ── Sub display ─────────────────────────────────────────────────────────

    public MeterMode SubMode => (MeterMode)_data[9];
    public bool SubOverload => (High(10) & 0x8) != 0;
    public bool SubNegative => (High(10) & 0x4) != 0;
    /// <summary>The sub display's "k" annunciator.</summary>
    public bool SubKilo => (High(10) & 0x2) != 0;
    public bool SubHertz => (High(10) & 0x1) != 0;
    /// <summary>Digits after the sub display's decimal point (0–4; bit 3 is always 0).</summary>
    public int SubDecimals => _data[10] & 0x07;
    public int SubRawValue => _data[11] << 8 | _data[12];

    // ── Bar graph ───────────────────────────────────────────────────────────

    /// <summary>The bar graph shows when this bit is clear.</summary>
    /// <remarks>
    /// EEVblog's packet document labels this bit "USE", which reads as the opposite. The
    /// shipped app treats set as off, and others have confirmed that on real meters.
    /// </remarks>
    public bool BarOn => (_data[13] & 0x10) == 0;
    /// <summary>The 0-150 tick labels instead of 0-5.</summary>
    public bool BarTicks150 => (_data[13] & 0x08) != 0;
    /// <remarks>
    /// The official app decodes this bit inverted and then swaps the two LCD layers, so the
    /// net effect is "set = negative". This is that net effect.
    /// </remarks>
    public bool BarNegative => (_data[13] & 0x04) != 0;
    public BarScale BarScale => (BarScale)(_data[13] & 0x03);
    /// <summary>0–31. The LCD lights this many segments plus one, out of 26.</summary>
    public int BarValue => _data[14] & 0x1F;

    // ── Status 1 ────────────────────────────────────────────────────────────

    public byte Status1 => _data[15];
    /// <summary>The °C symbol (bit 7; from EEVblog's "Revised Packet Format Blob V2").</summary>
    public bool CelsiusIcon => (Status1 & 0x80) != 0;
    /// <summary>The "1kHz" annunciator: the AC low-pass filter, not a unit.</summary>
    public bool LowPassFilter => (Status1 & 0x40) != 0;
    /// <summary>The "1ms" annunciator: 1 ms PEAK capture.</summary>
    public bool OneMsPeak => (Status1 & 0x20) != 0;
    public Coupling Coupling => (Coupling)((Status1 >> 3) & 0x3);
    public bool AutoRange => (Status1 & 0x04) != 0;
    public bool AutoPowerOff => (Status1 & 0x02) != 0;
    public bool LowBattery => (Status1 & 0x01) != 0;

    // ── Status 2 ────────────────────────────────────────────────────────────

    public byte Status2 => _data[16];
    /// <summary>The °F symbol (bit 7; from EEVblog's "Revised Packet Format Blob V2").</summary>
    public bool FahrenheitIcon => (Status2 & 0x80) != 0;
    public bool Bluetooth => (Status2 & 0x40) != 0;
    public bool Arrow => (Status2 & 0x20) != 0;
    public bool Relative => (Status2 & 0x10) != 0;
    public bool Decibels => (Status2 & 0x08) != 0;
    public MinMaxState MinMax => (MinMaxState)(Status2 & 0x07);

    // ── Status 3 ────────────────────────────────────────────────────────────

    public byte Status3 => _data[17];
    public bool Test => (Status3 & 0x40) != 0;
    /// <summary>0 when memory is off, 1–3 otherwise.</summary>
    public int Memory => (Status3 & 0x30) >> 4;
    public HoldState Hold => (HoldState)((Status3 >> 2) & 0x3);
    public bool AcIcon => (Status3 & 0x02) != 0;
    public bool DcIcon => (Status3 & 0x01) != 0;

    public override string ToString() => Convert.ToHexString(_data);
}
