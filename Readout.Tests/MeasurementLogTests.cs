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

    private static Sample At(double seconds, MeterMode mode, double? value) =>
        new(T0.AddSeconds(seconds), mode, value, "V", "", "");

    [Fact]
    public void CurrentRunStatistics_MatchesAFullScanOfTheRun()
    {
        var log = new MeasurementLog();
        log.AddRange([At(0, MeterMode.DcVolts, 100), At(1, MeterMode.Resistance, 5)]);
        log.AddRange([At(2, MeterMode.DcVolts, 3), At(3, MeterMode.DcVolts, null), At(4, MeterMode.DcVolts, -1), At(5, MeterMode.DcVolts, 7)]);

        var expected = MeasurementLog.Statistics(log.CurrentRun(MeterMode.DcVolts));

        Assert.Equal(new LogStatistics(3, -1, 7, 3), expected);
        Assert.Equal(expected, log.CurrentRunStatistics(MeterMode.DcVolts));
        Assert.Null(log.CurrentRunStatistics(MeterMode.Resistance));
    }

    [Fact]
    public void CurrentRunStatistics_StartsOverOnAModeChangeAndOnClear()
    {
        var log = new MeasurementLog();
        log.AddRange([At(0, MeterMode.DcVolts, 50), At(1, MeterMode.Resistance, 5), At(2, MeterMode.DcVolts, 2)]);
        Assert.Equal(new LogStatistics(1, 2, 2, 2), log.CurrentRunStatistics(MeterMode.DcVolts));

        log.Clear();
        Assert.Null(log.CurrentRunStatistics(MeterMode.DcVolts));
        log.AddRange([At(3, MeterMode.DcVolts, null)]);
        Assert.Null(log.CurrentRunStatistics(MeterMode.DcVolts));
        log.AddRange([At(4, MeterMode.DcVolts, 4)]);
        Assert.Equal(new LogStatistics(1, 4, 4, 4), log.CurrentRunStatistics(MeterMode.DcVolts));
    }

    [Fact]
    public void CurrentRun_IsASnapshotThatSurvivesMoreReadingsAndAClear()
    {
        var log = new MeasurementLog();
        log.AddRange([At(0, MeterMode.DcVolts, 1), At(1, MeterMode.DcVolts, 2)]);
        var run = log.CurrentRun(MeterMode.DcVolts);

        log.AddRange([At(2, MeterMode.DcVolts, 3)]);
        log.Clear();

        Assert.Equal([1.0, 2.0], run.Select(x => x.Value!.Value));
        Assert.Throws<ArgumentOutOfRangeException>(() => run[2]);
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
