using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Core.Protocol;

namespace Readout.Mcp;

/// <summary>
/// The MCP tools: what an AI assistant can do with the meter. Everything that touches the
/// session or its log runs through <see cref="IUiThread"/>.
/// </summary>
public sealed class MeterTools(
    MeterSession session,
    SessionStore store,
    RecordingAutosave? autosave,
    IUiThread ui,
    TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>How long press_button waits for the meter to react before reporting the reading.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(800);

    public const string Instructions =
        "Tools for an EEVblog 121GW multimeter connected to the Readout app over Bluetooth. " +
        "get_reading returns what the meter shows right now; values are also given in SI base units. " +
        "The dial on the meter picks the measurement group (V, mV, Hz, Ω, A…) and can only be turned by hand; " +
        "press_button MODE cycles within the dial position (for example DC, AC, AC+DC on V). " +
        "The app logs every reading while connected; get_recent_readings and save_session work on that log.";

    public IReadOnlyList<McpServerTool> CreateTools() =>
    [
        McpServerTool.Create(GetStatusAsync, new McpServerToolCreateOptions
        {
            Name = "get_status",
            Title = "Meter status",
            Description = "Whether a meter is connected, its signal strength and battery, whether logging is on, and the latest reading.",
            ReadOnly = true,
        }),
        McpServerTool.Create(GetReadingAsync, new McpServerToolCreateOptions
        {
            Name = "get_reading",
            Title = "Read the meter",
            Description = "The meter's current reading: the main display (value and unit), the small display, the mode, the lit LCD symbols (AUTO, HOLD, REL, MIN/MAX…) and the raw packet in hex (see the protocol notes). On a SETUP page, setup_page names it and sub_display has its value. Fails when no meter is connected.",
            ReadOnly = true,
        }),
        McpServerTool.Create(GetRecentReadingsAsync, new McpServerToolCreateOptions
        {
            Name = "get_recent_readings",
            Title = "Recent readings",
            Description = "Readings from the app's log over the last few seconds or minutes, with min, max, average and peak-to-peak for the current mode. Long stretches are thinned to maxPoints evenly spaced readings. Only logged readings are included: nothing is logged while logging is paused.",
            ReadOnly = true,
        }),
        McpServerTool.Create(PressButtonAsync, new McpServerToolCreateOptions
        {
            Name = "press_button",
            Title = "Press a meter button",
            Description = "Presses one of the meter's buttons remotely, as if pressed on the meter: RANGE, HOLD, REL, PEAK (1ms PEAK), MODE, MINMAX, MEM or SETUP. longPress holds it (RANGE back to auto range, REL turns the 1 kHz low-pass filter on in AC, MIN/MAX leaves MIN/MAX mode). Holding PEAK is refused: it would turn the meter's Bluetooth off and end the connection. Returns the reading shortly after the press.",
            Destructive = false,
            Idempotent = false,
        }),
        McpServerTool.Create(SetLoggingAsync, new McpServerToolCreateOptions
        {
            Name = "set_logging",
            Title = "Pause or resume logging",
            Description = "Turns the app's logging of readings on or off. The display keeps updating either way.",
            Idempotent = true,
        }),
        McpServerTool.Create(ClearLogAsync, new McpServerToolCreateOptions
        {
            Name = "clear_log",
            Title = "Clear the log",
            Description = "Deletes the readings logged so far and starts a new recording. Save them first with save_session to keep them.",
            Destructive = true,
        }),
        McpServerTool.Create(SaveSessionAsync, new McpServerToolCreateOptions
        {
            Name = "save_session",
            Title = "Save the recording",
            Description = "Saves the logged readings as a named session (it appears in the app's Sessions tab) and starts a new recording.",
        }),
        McpServerTool.Create(ListSessionsAsync, new McpServerToolCreateOptions
        {
            Name = "list_sessions",
            Title = "Saved sessions",
            Description = "The saved sessions, newest first, with their mode, length and statistics.",
            ReadOnly = true,
        }),
        McpServerTool.Create(GetSessionAsync, new McpServerToolCreateOptions
        {
            Name = "get_session",
            Title = "A saved session",
            Description = "One saved session's details and readings, thinned to maxPoints evenly spaced readings when it's longer.",
            ReadOnly = true,
        }),
    ];

    public Task<string> GetStatusAsync() => ui.InvokeAsync(() =>
    {
        var reading = session.Latest is { } r && session.State == ConnectionState.Connected ? ToResult(r) : null;
        var rssi = session.SignalStrength;
        return McpResultJson.Serialize(new StatusResult(
            session.State.ToString(),
            session.Meter?.Name,
            session.IsDemo,
            rssi,
            rssi is { } dbm ? SignalQuality.Describe(dbm) : null,
            session.BatteryVolts,
            session.Latest?.Packet.LowBattery ?? false,
            session.IsLogging,
            session.Log.Count,
            reading));
    });

    public Task<string> GetReadingAsync() => ui.InvokeAsync(() =>
        McpResultJson.Serialize(ToResult(RequireReading())));

    public Task<string> GetRecentReadingsAsync(
        [Description("How far back to look, in seconds (1 to 86400).")] double seconds = 30,
        [Description("At most this many readings are returned (10 to 5000).")] int maxPoints = 300) => ui.InvokeAsync(() =>
    {
        seconds = Math.Clamp(seconds, 1, 86400);
        maxPoints = Math.Clamp(maxPoints, 10, 5000);

        var samples = session.Log.Samples;
        var since = _time.GetUtcNow().AddSeconds(-seconds);
        var start = samples.Count;
        while (start > 0 && samples[start - 1].Time >= since)
            start--;
        var window = new List<Sample>(samples.Count - start);
        for (var i = start; i < samples.Count; i++)
            window.Add(samples[i]);

        StatsResult? stats = null;
        if (window.Count > 0)
        {
            var mode = window[^1].Mode;
            var inMode = window.Where(s => s.Mode == mode).ToList();
            stats = ToResult(MeasurementLog.Statistics(inMode), inMode[^1].Unit);
        }

        var thinned = Thin(window, maxPoints);
        return McpResultJson.Serialize(new RecentReadingsResult(
            seconds, session.IsLogging, window.Count, thinned.Count < window.Count, stats, [.. thinned.Select(ToResult)]));
    });

    public async Task<string> PressButtonAsync(
        [Description("RANGE, HOLD, REL, PEAK, MODE, MINMAX, MEM or SETUP.")] string button,
        [Description("Hold the button instead of tapping it.")] bool longPress = false,
        CancellationToken ct = default)
    {
        var key = ParseButton(button);
        if (key == MeterKey.Peak && longPress)
            throw new McpException("Holding 1ms PEAK turns the meter's Bluetooth off, which would end this connection. Press it briefly instead, or turn Bluetooth off on the meter itself.");

        await ui.InvokeAsync(async () =>
        {
            if (session.State != ConnectionState.Connected)
                throw new McpException("No meter is connected. Connect one in the Readout app first.");
            await session.SendKeyAsync(key, longPress, ct);
            return true;
        });

        await Task.Delay(SettleTime, _time, ct);
        var after = await ui.InvokeAsync(() => session.Latest is { } r ? ToResult(r) : null);
        return McpResultJson.Serialize(new PressResult(key.ToString().ToUpperInvariant(), longPress, after));
    }

    public Task<string> SetLoggingAsync(
        [Description("true to log readings, false to pause.")] bool enabled) => ui.InvokeAsync(() =>
    {
        session.IsLogging = enabled;
        return McpResultJson.Serialize(new LoggingResult(session.IsLogging, session.Log.Count));
    });

    public Task<string> ClearLogAsync() => ui.InvokeAsync(() =>
    {
        session.Log.Clear();
        autosave?.Flush();
        return McpResultJson.Serialize(new LoggingResult(session.IsLogging, session.Log.Count));
    });

    public Task<string> SaveSessionAsync(
        [Description("A name for the session; leave out for one based on the mode and time.")] string? name = null,
        CancellationToken ct = default) => ui.InvokeAsync(async () =>
    {
        var log = session.Log;
        if (log.Count == 0)
            throw new McpException("Nothing has been logged yet, so there's nothing to save.");

        var title = string.IsNullOrWhiteSpace(name) ? SessionSummary.SuggestedName(log.Samples) : name.Trim();
        if (title.Length > 80) title = title[..80];
        var info = await store.SaveRecordingAsync(title, log, autosave, ct);
        return McpResultJson.Serialize(ToResult(info));
    });

    public async Task<string> ListSessionsAsync(
        [Description("At most this many sessions, newest first (1 to 500).")] int limit = 50,
        CancellationToken ct = default)
    {
        var sessions = await store.ListAsync(ct);
        return McpResultJson.Serialize(new SessionListResult([.. sessions.Take(Math.Clamp(limit, 1, 500)).Select(ToResult)]));
    }

    public async Task<string> GetSessionAsync(
        [Description("The session's id, from list_sessions or save_session.")] string id,
        [Description("At most this many readings are returned (10 to 5000).")] int maxPoints = 500,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new McpException($"\"{id}\" isn't a session id. Use list_sessions to find one.");
        var info = (await store.ListAsync(ct)).FirstOrDefault(s => s.Id == guid)
            ?? throw new McpException($"No saved session has the id {id}.");

        var samples = await store.LoadSamplesAsync(guid, ct);
        var thinned = Thin(samples, Math.Clamp(maxPoints, 10, 5000));
        return McpResultJson.Serialize(new SessionDetailResult(ToResult(info), thinned.Count < samples.Count, [.. thinned.Select(ToResult)]));
    }

    private MeterReading RequireReading()
    {
        if (session.State != ConnectionState.Connected)
            throw new McpException("No meter is connected. Connect one in the Readout app first.");
        return session.Latest ?? throw new McpException("The meter is connected but hasn't sent a reading yet. Try again in a moment.");
    }

    internal static MeterKey ParseButton(string button)
    {
        var name = (button ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal).Replace("/", "", StringComparison.Ordinal);
        return name switch
        {
            "RANGE" => MeterKey.Range,
            "HOLD" => MeterKey.Hold,
            "REL" or "RELATIVE" or "UP" => MeterKey.Rel,
            "PEAK" or "1MSPEAK" or "1MS" or "DOWN" => MeterKey.Peak,
            "MODE" => MeterKey.Mode,
            "MINMAX" => MeterKey.MinMax,
            "MEM" or "MEMORY" => MeterKey.Mem,
            "SETUP" => MeterKey.Setup,
            _ => throw new McpException($"\"{button}\" isn't a meter button. Use RANGE, HOLD, REL, PEAK, MODE, MINMAX, MEM or SETUP."),
        };
    }

    /// <summary>At most <paramref name="max"/> evenly spaced items, always including the last.</summary>
    internal static List<T> Thin<T>(IReadOnlyList<T> items, int max)
    {
        if (items.Count <= max) return [.. items];
        var result = new List<T>(max);
        for (var k = 0; k < max; k++)
            result.Add(items[(int)((long)k * (items.Count - 1) / (max - 1))]);
        return result;
    }

    private ReadingResult ToResult(MeterReading r) => new(
        r.ReceivedAt,
        Math.Round(Math.Max(0, (_time.GetUtcNow() - r.ReceivedAt).TotalSeconds), 1),
        r.ModeName,
        r.MainLine,
        r.BaseValue,
        r.BaseUnit,
        r.MainOverload,
        r.Sub.Text.Length > 0 ? r.Sub.Line : null,
        r.Sub.Label,
        Symbols(r.Icons),
        Convert.ToHexString(r.Packet.Bytes));

    private static List<string> Symbols(Annunciators i)
    {
        var lit = new List<string>();
        void Add(bool on, string name) { if (on) lit.Add(name); }
        Add(i.Auto, "AUTO");
        Add(i.AcPlusDc, "AC+DC");
        Add(i.Ac && !i.AcPlusDc, "AC");
        Add(i.Dc && !i.AcPlusDc, "DC");
        Add(i.LowZ, "LowZ");
        Add(i.Hold && !i.AutoHold, "HOLD");
        Add(i.AutoHold, "A-HOLD");
        Add(i.Relative, "REL");
        Add(i.Min, "MIN");
        Add(i.Max, "MAX");
        Add(i.Average, "AVG");
        Add(i.OneMsPeak, "1ms PEAK");
        Add(i.LowPassFilter, "1kHz filter");
        Add(i.Decibels, "dBm");
        Add(i.Memory, "MEM");
        Add(i.LowBattery, "low battery");
        Add(i.AutoPowerOff, "APO");
        Add(i.Bluetooth, "BT");
        Add(i.Test, "TEST");
        return lit;
    }

    private static SampleResult ToResult(Sample s) =>
        new(s.Time, ReadingDecoder.ModeName(s.Mode), s.Value, s.Unit, s.Display);

    private static StatsResult? ToResult(LogStatistics? s, string unit) =>
        s is null ? null : new StatsResult(s.Count, s.Min, s.Max, s.Average, s.PeakToPeak, unit);

    private static SessionResult ToResult(SessionInfo i) => new(
        i.Id.ToString("N"), i.Name, i.Notes, i.StartedAt, i.EndedAt, i.SampleCount, i.ModeName, i.Unit,
        ToResult(i.Statistics, i.Unit));
}
