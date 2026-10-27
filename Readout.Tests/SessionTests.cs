using System.Globalization;
using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Core.Protocol;

namespace Readout.Tests;

public sealed class SessionTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "readout-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Sample S(double seconds, double? value, MeterMode mode = MeterMode.DcVolts, string unit = "V",
        string display = "1 V", string sub = "") =>
        new(T0.AddSeconds(seconds), mode, value, unit, display, sub);

    [Fact]
    public void Csv_RoundTripsEverything()
    {
        Sample[] samples =
        [
            S(0, 12.345, display: "12.345 V", sub: "23.5 °C"),
            S(0.25, null, MeterMode.Resistance, "Ω", "OFL MΩ"),
            S(1.5, -0.000123, MeterMode.DcMicroamps, "A", "-123.00 µA", "a, \"quoted\" sub"),
        ];

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        SampleCsv.Write(samples, writer);
        using var reader = new StringReader(writer.ToString());

        Assert.Equal(samples, SampleCsv.Read(reader));
    }

    [Fact]
    public void Statistics_IncludePeakToPeak()
    {
        var stats = MeasurementLog.Statistics([S(0, -2), S(1, 3), S(2, null)])!;
        Assert.Equal(5.0, stats.PeakToPeak, 9);
    }

    [Fact]
    public void Describe_UsesTheModeWithMostReadings()
    {
        var info = SessionSummary.Describe(Guid.NewGuid(), "x", "",
            [S(0, 1), S(1, 100, MeterMode.Resistance, "Ω"), S(2, 200, MeterMode.Resistance, "Ω"), S(3, 2)
             , S(4, 300, MeterMode.Resistance, "Ω")]);

        Assert.Equal(MeterMode.Resistance, info.MainMode);
        Assert.Equal("Ω", info.Unit);
        Assert.Equal((3, 100.0, 300.0), (info.Statistics!.Count, info.Statistics.Min, info.Statistics.Max));
        Assert.Equal(TimeSpan.FromSeconds(4), info.Duration);
        Assert.Equal(5, info.SampleCount);
    }

    [Fact]
    public async Task Store_SavesListsLoadsUpdatesAndDeletes()
    {
        var store = new SessionStore(_dir);
        var older = await store.SaveAsync("First", [S(0, 1), S(1, 2)]);
        var newer = await store.SaveAsync("Second", [S(100, 5), S(101, 6), S(102, 7)]);

        var list = await store.ListAsync();
        Assert.Equal(["Second", "First"], list.Select(s => s.Name));
        Assert.Equal(newer, list[0]);

        Assert.Equal(3, (await store.LoadSamplesAsync(newer.Id)).Count);

        await store.UpdateAsync(older with { Name = "Renamed", Notes = "probe on R12" });
        var updated = (await store.ListAsync()).Single(s => s.Id == older.Id);
        Assert.Equal(("Renamed", "probe on R12"), (updated.Name, updated.Notes));

        store.Delete(newer.Id);
        Assert.Equal([older.Id], (await store.ListAsync()).Select(s => s.Id));
        Assert.False(File.Exists(store.CsvPath(newer.Id)));
    }

    [Fact]
    public async Task Store_SkipsADamagedFile()
    {
        var store = new SessionStore(_dir);
        await store.SaveAsync("Good", [S(0, 1)]);
        await File.WriteAllTextAsync(Path.Combine(_dir, "broken.json"), "{ not json");

        Assert.Equal("Good", Assert.Single(await store.ListAsync()).Name);
    }

    [Fact]
    public async Task Store_EmptyFolder_ListsNothing() =>
        Assert.Empty(await new SessionStore(_dir).ListAsync());

    [Fact]
    public void Autosave_AppendsAndRestoresAcrossLaunches()
    {
        var path = Path.Combine(_dir, "recording.csv");
        var log = new MeasurementLog();
        var autosave = new RecordingAutosave(log, path);

        log.AddRange([S(0, 1), S(1, 2)]);
        autosave.Flush();
        log.AddRange([S(2, 3)]);
        autosave.Flush();
        autosave.Flush(); // nothing new

        var relaunched = new MeasurementLog();
        new RecordingAutosave(relaunched, path).Restore();

        Assert.Equal(log.Samples, relaunched.Samples);
    }

    [Fact]
    public void Autosave_AfterClear_StartsTheFileOver()
    {
        var path = Path.Combine(_dir, "recording.csv");
        var log = new MeasurementLog();
        var autosave = new RecordingAutosave(log, path);
        log.AddRange([S(0, 1), S(1, 2)]);
        autosave.Flush();

        log.Clear();
        autosave.Flush();
        Assert.False(File.Exists(path));

        log.AddRange([S(5, 9)]);
        autosave.Flush();
        var relaunched = new MeasurementLog();
        new RecordingAutosave(relaunched, path).Restore();
        Assert.Equal(9.0, Assert.Single(relaunched.Samples).Value);
    }

    [Fact]
    public void Autosave_ClearThenMoreReadingsThanWereWritten_StillStartsOver()
    {
        var path = Path.Combine(_dir, "recording.csv");
        var log = new MeasurementLog();
        var autosave = new RecordingAutosave(log, path);
        log.AddRange([S(0, 1)]);
        autosave.Flush();

        // Cleared, and the new recording outgrew the old one before the next flush.
        log.Clear();
        log.AddRange([S(5, 7), S(6, 8), S(7, 9)]);
        autosave.Flush();

        var relaunched = new MeasurementLog();
        new RecordingAutosave(relaunched, path).Restore();
        Assert.Equal([7.0, 8.0, 9.0], relaunched.Samples.Select(x => x.Value!.Value));
    }

    [Fact]
    public void Autosave_RestoreWithoutAFile_LeavesTheLogEmpty()
    {
        var log = new MeasurementLog();
        new RecordingAutosave(log, Path.Combine(_dir, "none.csv")).Restore();
        Assert.Equal(0, log.Count);
    }
}

public class TrendWindowTests
{
    [Fact]
    public void Unzoomed_ShowsEverything()
    {
        var w = new TrendWindow();
        Assert.Equal((0.0, 100.0), w.Resolve(100));
        Assert.False(w.IsZoomed);
    }

    [Fact]
    public void ZoomAtTheRightEdge_FollowsNewReadings()
    {
        var w = new TrendWindow();
        w.Zoom(4, anchor: 1, dataSpan: 100);

        Assert.Equal((75.0, 100.0), w.Resolve(100));
        Assert.True(w.FollowsLatest);
        Assert.Equal((95.0, 120.0), w.Resolve(120));
    }

    [Fact]
    public void ZoomInTheMiddle_StaysPutAsDataGrows()
    {
        var w = new TrendWindow();
        w.Zoom(2, anchor: 0.5, dataSpan: 100);

        Assert.Equal((25.0, 75.0), w.Resolve(100));
        Assert.False(w.FollowsLatest);
        Assert.Equal((25.0, 75.0), w.Resolve(200));
    }

    [Fact]
    public void Pan_StopsAtTheEdgesAndRejoinsTheLatest()
    {
        var w = new TrendWindow();
        w.Zoom(2, anchor: 0.5, dataSpan: 100);   // 25..75

        w.Pan(10, 100);                           // far right drag: earliest
        Assert.Equal((0.0, 50.0), w.Resolve(100));

        w.Pan(-10, 100);                          // far left drag: newest
        Assert.Equal((50.0, 100.0), w.Resolve(100));
        Assert.True(w.FollowsLatest);
    }

    [Fact]
    public void ZoomingOutPastEverything_Resets()
    {
        var w = new TrendWindow();
        w.Zoom(2, 0.5, 100);
        w.Zoom(0.25, 0.5, 100);
        Assert.False(w.IsZoomed);
    }

    [Fact]
    public void Zoom_NeverGoesBelowTheMinimumSpan()
    {
        var w = new TrendWindow();
        w.Zoom(1000, 0.5, 100);
        var (from, to) = w.Resolve(100);
        Assert.Equal(TrendWindow.MinSpan, to - from, 9);
    }

    [Fact]
    public void Pan_WhenNotZoomed_DoesNothing()
    {
        var w = new TrendWindow();
        w.Pan(0.5, 100);
        Assert.Equal((0.0, 100.0), w.Resolve(100));
    }
}

public class TrendBucketTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Sample At(double seconds, double value) =>
        new(T0.AddSeconds(seconds), MeterMode.DcVolts, value, "V", "", "");

    [Fact]
    public void Groups_readings_by_time_slot_with_min_max_and_average()
    {
        var samples = new[] { At(0, 1), At(0.5, 3), At(1.25, 10), At(1.75, 20) };

        var buckets = TrendBucket.Group(samples, T0, TimeSpan.FromSeconds(1));

        Assert.Equal(2, buckets.Count);
        Assert.Equal((1.0, 3.0, 2.0), (buckets[0].Min, buckets[0].Max, buckets[0].Average));
        Assert.Equal((10.0, 20.0, 15.0), (buckets[1].Min, buckets[1].Max, buckets[1].Average));
        Assert.Equal(T0.AddSeconds(1.5), buckets[1].Middle);
    }

    [Fact]
    public void A_range_leaves_out_readings_without_a_value()
    {
        var samples = new[] { At(0, 1), At(0.1, 99), new Sample(T0.AddSeconds(0.2), MeterMode.DcVolts, null, "V", "OFL", ""), At(0.3, 3) };

        var bucket = Assert.Single(TrendBucket.Group(samples, 2, 4, T0, TimeSpan.FromSeconds(1)));

        Assert.Equal((3.0, 3.0, 3.0), (bucket.Min, bucket.Max, bucket.Average));
        Assert.Equal(T0.AddSeconds(0.3), bucket.First);
    }

    [Fact]
    public void A_pause_leaves_no_bucket_spanning_it()
    {
        var samples = new[] { At(0, 1), At(0.9, 1), At(60, 2), At(60.5, 2) };

        var buckets = TrendBucket.Group(samples, T0, TimeSpan.FromSeconds(1));

        Assert.Equal(2, buckets.Count);
        Assert.Equal(T0.AddSeconds(0.9), buckets[0].Last);
        Assert.Equal(T0.AddSeconds(60), buckets[1].First);
    }
}
