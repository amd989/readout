using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Readout.Core.Display;
using Readout.Core.Protocol;

namespace Readout.Core.Logging;

/// <summary>A saved recording.</summary>
/// <param name="MainMode">The mode with the most readings; the graph and statistics are for it.</param>
/// <param name="Unit">The SI unit of <paramref name="MainMode"/>.</param>
/// <param name="Statistics">Of the <paramref name="MainMode"/> readings; null when none had a value.</param>
public sealed record SessionInfo(
    Guid Id,
    string Name,
    string Notes,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int SampleCount,
    MeterMode MainMode,
    string Unit,
    LogStatistics? Statistics)
{
    [JsonIgnore] public TimeSpan Duration => EndedAt - StartedAt;
    [JsonIgnore] public string ModeName => ReadingDecoder.ModeName(MainMode);
}

/// <summary>Summarises samples the way a saved session shows them.</summary>
public static class SessionSummary
{
    /// <summary>The mode with the most readings that have a value (else the most readings at all).</summary>
    public static MeterMode MainMode(IReadOnlyList<Sample> samples)
    {
        if (samples.Count == 0) return MeterMode.DcVolts;
        return samples
            .GroupBy(s => s.Mode)
            .OrderByDescending(g => g.Count(s => s.Value is not null))
            .ThenByDescending(g => g.Count())
            .First().Key;
    }

    /// <summary>A name for a new session: its main mode and when it started ("DC voltage, Oct 27 14:05").</summary>
    public static string SuggestedName(IReadOnlyList<Sample> samples) =>
        samples.Count == 0 ? "Recording"
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0}, {1:MMM d HH:mm}",
                ReadingDecoder.ModeName(MainMode(samples)), samples[0].Time.ToLocalTime());

    public static SessionInfo Describe(Guid id, string name, string notes, IReadOnlyList<Sample> samples)
    {
        if (samples.Count == 0)
            throw new ArgumentException("A session needs at least one sample.", nameof(samples));

        var mode = MainMode(samples);
        var inMode = samples.Where(s => s.Mode == mode).ToList();
        return new SessionInfo(
            id, name, notes,
            samples[0].Time, samples[^1].Time,
            samples.Count, mode,
            inMode.Count > 0 ? inMode[0].Unit : "",
            MeasurementLog.Statistics(inMode));
    }
}

/// <summary>
/// Saved sessions on disk: each is <c>{id}.csv</c> (the samples, in the export format) plus
/// <c>{id}.json</c> (name, notes and a summary, so the list loads without reading samples).
/// </summary>
public sealed class SessionStore(string directory)
{
    public string Directory { get; } = directory;

    public string CsvPath(Guid id) => Path.Combine(Directory, $"{id:N}.csv");
    private string InfoPath(Guid id) => Path.Combine(Directory, $"{id:N}.json");

    /// <summary>Saves a copy of <paramref name="samples"/>; the caller may keep changing its own list.</summary>
    public async Task<SessionInfo> SaveAsync(string name, IReadOnlyList<Sample> samples, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var copy = samples.ToArray();
        var info = SessionSummary.Describe(Guid.NewGuid(), name, "", copy);

        await Task.Run(async () =>
        {
            var csv = new StringBuilder();
            using (var writer = new StringWriter(csv, System.Globalization.CultureInfo.InvariantCulture))
                SampleCsv.Write(copy, writer);
            await File.WriteAllTextAsync(CsvPath(info.Id), csv.ToString(), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        await WriteInfoAsync(info, ct).ConfigureAwait(false);
        return info;
    }

    /// <summary>
    /// Saves the live recording as a session and starts a new one: the log is cleared and the
    /// autosave file follows. Call it on the thread that owns the log; it comes back there.
    /// </summary>
    public async Task<SessionInfo> SaveRecordingAsync(string name, MeasurementLog log, RecordingAutosave? autosave, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        var info = await SaveAsync(name, log.Samples, ct);
        log.Clear();
        autosave?.Flush();
        return info;
    }

    /// <summary>Every saved session, newest first. Files that can't be read are skipped.</summary>
    public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct = default)
    {
        if (!System.IO.Directory.Exists(Directory)) return [];

        var sessions = new List<SessionInfo>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                if (await JsonSerializer.DeserializeAsync(stream, SessionJson.Default.SessionInfo, ct).ConfigureAwait(false) is { } info)
                    sessions.Add(info);
            }
            catch (JsonException)
            {
                // A damaged file: leave it, show the rest.
            }
        }
        return [.. sessions.OrderByDescending(s => s.StartedAt)];
    }

    public async Task<IReadOnlyList<Sample>> LoadSamplesAsync(Guid id, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(CsvPath(id), ct).ConfigureAwait(false);
        using var reader = new StringReader(text);
        return SampleCsv.Read(reader);
    }

    /// <summary>Saves a changed name or notes.</summary>
    public Task UpdateAsync(SessionInfo info, CancellationToken ct = default) => WriteInfoAsync(info, ct);

    public void Delete(Guid id)
    {
        File.Delete(CsvPath(id));
        File.Delete(InfoPath(id));
    }

    private async Task WriteInfoAsync(SessionInfo info, CancellationToken ct)
    {
        // Write then move, so a crash mid-write can't leave a half-written file.
        var path = InfoPath(info.Id);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, info, SessionJson.Default.SessionInfo, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}

// Source-generated, so it works with the trimming iOS and Android release builds use.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SessionInfo))]
internal sealed partial class SessionJson : JsonSerializerContext;
