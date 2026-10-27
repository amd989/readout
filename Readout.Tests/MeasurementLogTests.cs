using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Core.Protocol;
using Readout.Core.Simulation;

namespace Readout.Tests;

public class MeasurementLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static MeterReading Reading(MeterMode mode, int raw, double seconds, int range = 1) =>
        ReadingDecoder.Decode(
            new PacketBuilder { Mode = mode, MainRangeIndex = range, MainRawValue = raw, SubMode = MeterMode.Frequency, SubRawValue = 5000, SubDecimals = 2 }.BuildPacket(),
            T0.AddSeconds(seconds));

    [Fact]
    public void WriteCsv_UsesInvariantNumbersWhateverTheCulture()
    {
        var log = new MeasurementLog();
        log.Add(Reading(MeterMode.DcVolts, 12345, 0));
        log.Add(Reading(MeterMode.DcVolts, 12346, 0.25));

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        string csv;
        try
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            log.WriteCsv(writer);
            csv = writer.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("time_utc,elapsed_s,mode,value,unit,display,sub_display", lines[0]);
        Assert.Equal("2026-10-27T12:00:00.000Z,0.000,DcVolts,12.345,V,12.345 V,50.00 Hz", lines[1]);
        Assert.Equal("2026-10-27T12:00:00.250Z,0.250,DcVolts,12.346,V,12.346 V,50.00 Hz", lines[2]);
    }

    [Fact]
    public void WriteCsv_OverloadHasAnEmptyValue()
    {
        var log = new MeasurementLog();
        log.Add(ReadingDecoder.Decode(new PacketBuilder { Mode = MeterMode.Resistance, MainOverload = true }.BuildPacket(), T0));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        log.WriteCsv(writer);

        Assert.Contains(",Resistance,,Ω,OFL Ω,", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentRun_IsOnlyTheNewestStretchInThatMode()
    {
        var log = new MeasurementLog();
        log.Add(Reading(MeterMode.DcVolts, 1, 0));
        log.Add(Reading(MeterMode.Resistance, 2, 1));
        log.Add(Reading(MeterMode.DcVolts, 3, 2));
        log.Add(Reading(MeterMode.DcVolts, 4, 3));

        var run = log.CurrentRun(MeterMode.DcVolts);

        Assert.Equal([3e-3, 4e-3], run.Select(s => Math.Round(s.Value!.Value, 6)));
    }

    [Fact]
    public void Statistics_SkipsOverloads()
    {
        var log = new MeasurementLog();
        log.Add(Reading(MeterMode.DcVolts, 10000, 0));
        log.Add(Reading(MeterMode.DcVolts, 30000, 1));
        log.Add(ReadingDecoder.Decode(new PacketBuilder { Mode = MeterMode.DcVolts, MainOverload = true }.BuildPacket(), T0));

        var stats = MeasurementLog.Statistics(log.Samples)!;

        Assert.Equal(2, stats.Count);
        Assert.Equal(10.0, stats.Min, 9);
        Assert.Equal(30.0, stats.Max, 9);
        Assert.Equal(20.0, stats.Average, 9);
    }

    [Fact]
    public void Statistics_NoValues_IsNull() => Assert.Null(MeasurementLog.Statistics([]));
}
