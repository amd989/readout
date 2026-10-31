using Readout.Core.Protocol;

namespace Readout.Core.Connection;

/// <summary>
/// Sets the meter's clock to this device's, the only way the meter allows: through its
/// buttons, as a person would. On each clock page (year, date, time) it holds SETUP to edit,
/// steps the flashing value with ▲ (REL) and ▼ (1ms PEAK), and holds SETUP to save it and
/// move to the next. The packet keeps showing the saved clock while a value flashes, so each
/// page is checked after it's saved and done again if a press went missing.
/// </summary>
public sealed class MeterClockSync
{
    /// <summary>Between presses: the meter takes about five a second.</summary>
    public static readonly TimeSpan PressGap = TimeSpan.FromMilliseconds(300);
    /// <summary>After holding SETUP: a press sooner than this is lost.</summary>
    public static readonly TimeSpan EditSettle = TimeSpan.FromSeconds(1);
    /// <summary>Long enough for a fresh packet (the meter sends five a second).</summary>
    public static readonly TimeSpan PacketWait = TimeSpan.FromMilliseconds(700);

    private const int MaxPages = 14;
    private const int Attempts = 3;

    private readonly MeterSession _session;
    private readonly Func<DateTime> _now;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="now">This device's clock.</param>
    /// <param name="wait">Waits between steps; tests replace it to run without real time.</param>
    public MeterClockSync(MeterSession session, Func<DateTime>? now = null, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        _session = session;
        _now = now ?? (() => DateTime.Now);
        _wait = wait ?? ((delay, ct) => Task.Delay(delay, ct));
    }

    /// <summary>Sets the year, date and time, then goes back to the page the small display was on.</summary>
    /// <param name="progress">"Setting the year…" and so on, for the screen.</param>
    /// <exception cref="MeterClockSyncException">The meter disconnected, the dial moved, or a page wouldn't take its value.</exception>
    public async Task SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var start = Latest();
        var mode = start.Mode;
        var startPage = start.SubMode;

        progress?.Report("Setting the year…");
        await SetPageAsync(MeterMode.Year, mode, now => [now.Year % 100], ct).ConfigureAwait(false);
        progress?.Report("Setting the date…");
        await SetPageAsync(MeterMode.Date, mode, now => [now.Month, now.Day], ct).ConfigureAwait(false);
        progress?.Report("Setting the time…");
        await SetPageAsync(MeterMode.Time, mode, now => [now.Hour, now.Minute], ct).ConfigureAwait(false);

        // Back to what the small display showed. Not finding it isn't worth failing over.
        try { await GoToPageAsync(startPage, mode, ct).ConfigureAwait(false); }
        catch (MeterClockSyncException) { }
    }

    private async Task SetPageAsync(MeterMode page, MeterMode mode, Func<DateTime, int[]> target, CancellationToken ct)
    {
        await GoToPageAsync(page, mode, ct).ConfigureAwait(false);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var current = Fields(Latest());
            if (Matches(page, current, target(_now())))
                return;

            await PressAsync(MeterKey.Setup, longPress: true, EditSettle, ct).ConfigureAwait(false);
            for (var field = 0; field < current.Length; field++)
            {
                // The target is read again for each field, so the minute is the one when it's saved.
                var steps = target(_now())[field] - current[field];
                for (var i = 0; i < Math.Abs(steps); i++)
                    await PressAsync(steps > 0 ? MeterKey.Rel : MeterKey.Peak, longPress: false, PressGap, ct).ConfigureAwait(false);
                // The minute may have turned while stepping.
                if (page == MeterMode.Time && field == 1)
                {
                    var late = target(_now())[1] - (current[1] + steps);
                    for (var i = 0; i < late; i++)
                        await PressAsync(MeterKey.Rel, longPress: false, PressGap, ct).ConfigureAwait(false);
                }
                await PressAsync(MeterKey.Setup, longPress: true, EditSettle, ct).ConfigureAwait(false);
            }
            await _wait(PacketWait, ct).ConfigureAwait(false);
            CheckStill(mode);
        }

        if (!Matches(page, Fields(Latest()), target(_now())))
            throw new MeterClockSyncException($"The meter's {PageName(page)} didn't take the new value. Try again, and leave the meter's buttons alone meanwhile.");
    }

    /// <summary>Presses SETUP until the small display shows <paramref name="page"/>.</summary>
    private async Task GoToPageAsync(MeterMode page, MeterMode mode, CancellationToken ct)
    {
        for (var i = 0; i < MaxPages; i++)
        {
            CheckStill(mode);
            if (Latest().SubMode == page)
                return;
            await PressAsync(MeterKey.Setup, longPress: false, PacketWait, ct).ConfigureAwait(false);
        }
        throw new MeterClockSyncException($"Couldn't find the meter's {PageName(page)} page with SETUP.");
    }

    private async Task PressAsync(MeterKey key, bool longPress, TimeSpan after, CancellationToken ct)
    {
        if (_session.State != ConnectionState.Connected)
            throw new MeterClockSyncException("The meter disconnected.");
        await _session.SendKeyAsync(key, longPress, ct).ConfigureAwait(false);
        await _wait(after, ct).ConfigureAwait(false);
    }

    private MeterPacket Latest() =>
        _session.State == ConnectionState.Connected && _session.Latest is { } reading
            ? reading.Packet
            : throw new MeterClockSyncException("Connect to the meter first.");

    private void CheckStill(MeterMode mode)
    {
        if (Latest().Mode != mode)
            throw new MeterClockSyncException("The meter's mode changed. Leave the dial and buttons alone while the clock is set.");
    }

    /// <summary>The page's values as the packet has them: year; month, day; hour, minute.</summary>
    private static int[] Fields(MeterPacket packet)
    {
        var raw = packet.SubRawValue;
        return packet.SubMode == MeterMode.Year ? [raw % 100] : [raw >> 8, raw & 0xFF];
    }

    /// <summary>
    /// Whether the page shows the target. The time may be a minute out: the meter's seconds
    /// aren't in the packet, so it can turn the minute before or after this device does.
    /// </summary>
    private static bool Matches(MeterMode page, int[] current, int[] target)
    {
        if (page != MeterMode.Time)
            return current.SequenceEqual(target);
        var minutes = current[0] * 60 + current[1];
        var wanted = target[0] * 60 + target[1];
        return Math.Abs(minutes - wanted) <= 1;
    }

    private static string PageName(MeterMode page) => page switch
    {
        MeterMode.Year => "year",
        MeterMode.Date => "date",
        MeterMode.Time => "time",
        _ => "setup",
    };
}

/// <summary>Why the clock couldn't be set, in words for the user.</summary>
public sealed class MeterClockSyncException(string message) : Exception(message);
