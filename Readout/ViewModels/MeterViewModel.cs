using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Connection;
using Readout.Core.Display;
using Readout.Core.Logging;
using Readout.Core.Protocol;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout.ViewModels;

/// <summary>The main screen: the live reading, the remote keys, and the log with its graph.</summary>
public sealed partial class MeterViewModel : ObservableObject
{
    private readonly MeterSession _session;
    private readonly BluetoothAccess _access;
    private readonly SessionStore _store;
    private readonly RecordingAutosave _autosave;
    private readonly ILogger<MeterViewModel> _logger;
    private bool _triedAutoConnect;
    private long _lastLogRefresh;

    // The log summary, stats and graph walk the current run, so they refresh at most this often.
    private static readonly TimeSpan LogRefreshInterval = TimeSpan.FromMilliseconds(500);

    public MeterViewModel(MeterSession session, BluetoothAccess access, SessionStore store,
        RecordingAutosave autosave, ILogger<MeterViewModel> logger)
    {
        _session = session;
        _access = access;
        _store = store;
        _autosave = autosave;
        _logger = logger;

        _session.StateChanged += (_, _) => RefreshConnection();
        _session.ReadingReceived += (_, r) => OnReading(r);
        _session.Log.Changed += (_, _) => RefreshLogThrottled();
        RefreshConnection();
        RefreshLog();
    }

    /// <summary>Raised when the LCD should redraw (every reading).</summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Raised when the graph should redraw (at most twice a second).</summary>
    public event EventHandler? TrendChanged;

    public MeterReading? Reading => _session.Latest;

    /// <summary>The mode on the graph: the meter's, or (before any reading, after a relaunch) the log's last.</summary>
    private (MeterMode Mode, string Unit)? GraphMode =>
        _session.Latest is { } r ? (r.Mode, r.BaseUnit)
        : _session.Log.Count > 0 ? (_session.Log.Samples[^1].Mode, _session.Log.Samples[^1].Unit)
        : null;

    /// <summary>The samples the graph plots: the newest run in the current mode.</summary>
    public IReadOnlyList<Sample> TrendSamples =>
        GraphMode is { } m ? _session.Log.CurrentRun(m.Mode) : [];

    /// <summary>The full-screen graph's header.</summary>
    [ObservableProperty] private string _nowText = "–";
    [ObservableProperty] private string _minText = "–";
    [ObservableProperty] private string _maxText = "–";
    [ObservableProperty] private string _avgText = "–";
    [ObservableProperty] private string _peakToPeakText = "–";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _primaryActionText = "Connect";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectCard))]
    private bool _isConnected;

    /// <summary>The big connection card shows only until a meter is connected.</summary>
    public bool ShowConnectCard => !IsConnected;

    /// <summary>The slim status line while connected: "121GW · DC voltage".</summary>
    [ObservableProperty]
    private string _connectedLine = "";

    [ObservableProperty]
    private string _modeName = "No meter connected";

    [ObservableProperty]
    private string _readingDescription = "";

    [ObservableProperty]
    private string _logSummary = "";

    [ObservableProperty]
    private string _statistics = "";

    [ObservableProperty]
    private bool _hasLog;

    [ObservableProperty]
    private string _loggingActionText = "Pause";

    /// <summary>On first appearance: reconnect to the meter used last time, if there is one.</summary>
    public async Task AutoConnectAsync()
    {
        if (_triedAutoConnect || _session.PreferredMeterId is null) return;
        _triedAutoConnect = true;

        if (await _access.EnsureReadyAsync() is not null) return;
        try
        {
            await _session.StartScanAsync(autoConnect: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "auto-connect scan failed");
        }
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (_session.State == ConnectionState.Disconnected)
            await Shell.Current.GoToAsync(nameof(Views.ConnectPage));
        else
            await _session.DisconnectAsync();
    }

    public async Task PressKeyAsync(MeterKey key, bool longPress)
    {
        try
        {
            await _session.SendKeyAsync(key, longPress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "key {Key} failed", key);
        }
    }

    [RelayCommand]
    private void ToggleLogging()
    {
        _session.IsLogging = !_session.IsLogging;
        LoggingActionText = _session.IsLogging ? "Pause" : "Resume";
        RefreshLog();
    }

    [RelayCommand]
    private async Task ClearLogAsync()
    {
        if (_session.Log.Count == 0) return;
        var page = Shell.Current.CurrentPage;
        if (await page.DisplayAlertAsync("Clear the log?", $"This deletes {_session.Log.Count:N0} readings. Save them as a session first if you want to keep them.", "Clear", "Cancel"))
        {
            _session.Log.Clear();
            RefreshLog();
        }
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        var log = _session.Log;
        if (log.Count == 0) return;

        var page = Shell.Current.CurrentPage;
        var started = log.Samples[0].Time.ToLocalTime();
        var suggested = $"{ReadingDecoder.ModeName(SessionSummary.MainMode(log.Samples))}, {started:MMM d HH:mm}";
        var name = await page.DisplayPromptAsync("Save session", "Name it so you can find it later.",
            "Save", "Cancel", initialValue: suggested, maxLength: 80);
        if (name is null) return;
        if (string.IsNullOrWhiteSpace(name)) name = suggested;

        try
        {
            var info = await _store.SaveAsync(name.Trim(), log.Samples);
            log.Clear();
            _autosave.Flush();
            RefreshLog();

            if (await page.DisplayAlertAsync("Saved", $"\"{info.Name}\" is in Sessions. A new recording has started.", "View", "OK"))
                await Shell.Current.GoToAsync($"{nameof(Views.SessionsPage)}/{nameof(Views.SessionDetailPage)}?id={info.Id:N}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "save failed");
            await page.DisplayAlertAsync("Couldn't save", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private Task OpenSessionsAsync() => Shell.Current.GoToAsync(nameof(Views.SessionsPage));

    [RelayCommand]
    private Task OpenGraphAsync() => Shell.Current.GoToAsync(nameof(Views.GraphPage));

    private void OnReading(MeterReading reading)
    {
        ModeName = reading.Sub.Label is { } label ? $"{reading.ModeName} · SETUP: {label}" : reading.ModeName;
        ReadingDescription = reading.Sub.Text.Length > 0
            ? $"{reading.MainLine}, {reading.Sub.Line}"
            : reading.MainLine;
        RefreshConnectedLine();
        NowText = reading.MainLine;
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshConnectedLine()
    {
        var name = _session.IsDemo ? "Demo meter" : _session.Meter?.Name ?? "";
        ConnectedLine = _session.Latest is null ? name : $"{name} · {ModeName}";
    }

    private void RefreshConnection()
    {
        var meter = _session.Meter;
        (StatusText, PrimaryActionText) = _session.State switch
        {
            ConnectionState.Scanning => ("Looking for your meter…", "Stop"),
            ConnectionState.Connecting => ($"Connecting to {meter?.Name}…", "Cancel"),
            ConnectionState.Connected when _session.IsDemo => ("Demo meter (simulated readings)", "Disconnect"),
            ConnectionState.Connected => ($"Connected to {meter?.Name}", "Disconnect"),
            _ => ("Not connected", "Connect"),
        };
        IsConnected = _session.State == ConnectionState.Connected;
        RefreshConnectedLine();
        if (_session.State == ConnectionState.Disconnected && _session.Latest is null)
            ModeName = "No meter connected";
    }

    private void RefreshLogThrottled()
    {
        var now = Environment.TickCount64;
        if (now - _lastLogRefresh < LogRefreshInterval.TotalMilliseconds && _session.Log.Count > 1) return;
        RefreshLog();
    }

    private void RefreshLog()
    {
        _lastLogRefresh = Environment.TickCount64;
        TrendChanged?.Invoke(this, EventArgs.Empty);

        var log = _session.Log;
        HasLog = log.Count > 0;
        if (log.Count == 0)
        {
            LogSummary = _session.IsLogging ? "Readings are logged while connected." : "Logging is paused.";
            Statistics = "";
            MinText = MaxText = AvgText = PeakToPeakText = "–";
            return;
        }

        var span = log.Samples[^1].Time - log.Samples[0].Time;
        LogSummary = string.Format(CultureInfo.CurrentCulture, "{0:N0} readings over {1}{2}",
            log.Count, Format.Duration(span), _session.IsLogging ? "" : " (paused)");

        // Statistics for the run on the graph, not the whole log (it may mix units).
        if (GraphMode is { } m && MeasurementLog.Statistics(log.CurrentRun(m.Mode)) is { } stats)
        {
            Statistics = Format.Stats(stats, m.Unit);
            MinText = Format.Value(stats.Min, m.Unit);
            MaxText = Format.Value(stats.Max, m.Unit);
            AvgText = Format.Value(stats.Average, m.Unit);
            PeakToPeakText = Format.Value(stats.PeakToPeak, m.Unit);
        }
        else
        {
            Statistics = "";
            MinText = MaxText = AvgText = PeakToPeakText = "–";
        }
    }
}
