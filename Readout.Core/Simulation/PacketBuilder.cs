using Readout.Core.Protocol;

namespace Readout.Core.Simulation;

/// <summary>
/// Builds valid 19-byte packets field by field: the inverse of <see cref="MeterPacket"/>.
/// The simulated meter and the tests use it.
/// </summary>
public sealed class PacketBuilder
{
    public int Year { get; set; } = 2017;
    public int Month { get; set; } = 10;
    /// <summary>Five decimal digits, 0–99999.</summary>
    public int Serial { get; set; } = 12345;

    public MeterMode Mode { get; set; } = MeterMode.DcVolts;
    public int MainRangeIndex { get; set; }
    /// <summary>0–262143 (18 bits).</summary>
    public int MainRawValue { get; set; }
    public bool MainOverload { get; set; }
    public bool MainNegative { get; set; }
    public bool MainCelsius { get; set; }
    public bool MainFahrenheit { get; set; }

    public MeterMode SubMode { get; set; }
    public int SubRawValue { get; set; }
    public int SubDecimals { get; set; }
    public bool SubOverload { get; set; }
    public bool SubNegative { get; set; }
    public bool SubKilo { get; set; }
    public bool SubHertz { get; set; }

    public bool BarOn { get; set; } = true;
    public bool BarTicks150 { get; set; }
    public bool BarNegative { get; set; }
    public BarScale BarScale { get; set; }
    /// <summary>0–31.</summary>
    public int BarValue { get; set; }

    public bool CelsiusIcon { get; set; }
    public bool FahrenheitIcon { get; set; }
    public bool LowPassFilter { get; set; }
    public bool OneMsPeak { get; set; }
    public Coupling Coupling { get; set; }
    public bool AutoRange { get; set; }
    public bool AutoPowerOff { get; set; }
    public bool LowBattery { get; set; }

    public bool Bluetooth { get; set; } = true;
    public bool Arrow { get; set; }
    public bool Relative { get; set; }
    public bool Decibels { get; set; }
    public MinMaxState MinMax { get; set; }

    public bool Test { get; set; }
    /// <summary>0–3.</summary>
    public int Memory { get; set; }
    public HoldState Hold { get; set; }
    public bool AcIcon { get; set; }
    public bool DcIcon { get; set; }

    public byte[] Build()
    {
        var d = new byte[MeterPacket.Length];
        d[0] = MeterPacket.StartByte;
        d[1] = (byte)(Year - 2000);

        var serial = Serial.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        int Digit(int i) => serial[i] - '0';
        d[2] = (byte)(Month << 4 | Digit(0));
        d[3] = (byte)(Digit(1) << 4 | Digit(2));
        d[4] = (byte)(Digit(3) << 4 | Digit(4));

        d[5] = (byte)(((int)Mode & 0x1F) | ((MainRawValue >> 16) & 0x3) << 6);
        d[6] = (byte)(Flags(MainOverload, MainNegative, MainCelsius, MainFahrenheit) << 4 | (MainRangeIndex & 0x0F));
        d[7] = (byte)(MainRawValue >> 8);
        d[8] = (byte)MainRawValue;

        d[9] = (byte)SubMode;
        d[10] = (byte)(Flags(SubOverload, SubNegative, SubKilo, SubHertz) << 4 | (SubDecimals & 0x07));
        d[11] = (byte)(SubRawValue >> 8);
        d[12] = (byte)SubRawValue;

        d[13] = (byte)((BarOn ? 0 : 0x10) | (BarTicks150 ? 0x08 : 0) | (BarNegative ? 0x04 : 0) | ((int)BarScale & 0x3));
        d[14] = (byte)(BarValue & 0x1F);

        d[15] = (byte)((CelsiusIcon ? 0x80 : 0) | (LowPassFilter ? 0x40 : 0) | (OneMsPeak ? 0x20 : 0) | ((int)Coupling & 0x3) << 3
            | (AutoRange ? 0x04 : 0) | (AutoPowerOff ? 0x02 : 0) | (LowBattery ? 0x01 : 0));
        d[16] = (byte)((FahrenheitIcon ? 0x80 : 0) | (Bluetooth ? 0x40 : 0) | (Arrow ? 0x20 : 0) | (Relative ? 0x10 : 0)
            | (Decibels ? 0x08 : 0) | ((int)MinMax & 0x07));
        d[17] = (byte)((Test ? 0x40 : 0) | (Memory & 0x3) << 4 | ((int)Hold & 0x3) << 2
            | (AcIcon ? 0x02 : 0) | (DcIcon ? 0x01 : 0));

        d[18] = MeterPacket.Checksum(d.AsSpan(0, MeterPacket.Length - 1));
        return d;
    }

    public MeterPacket BuildPacket() =>
        MeterPacket.TryParse(Build(), out var packet)
            ? packet
            : throw new InvalidOperationException("PacketBuilder produced an invalid packet.");

    private static int Flags(bool b3, bool b2, bool b1, bool b0) =>
        (b3 ? 8 : 0) | (b2 ? 4 : 0) | (b1 ? 2 : 0) | (b0 ? 1 : 0);
}
