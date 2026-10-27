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
    private readonly ILogger<MeterViewModel> _logger;
    private bool _triedAutoConnect;
    private long _lastLogRefresh;

    // The log summary, stats and graph walk the current run, so they refresh at most this often.
    private static readonly TimeSpan LogRefreshInterval = TimeSpan.FromMilliseconds(500);

    public MeterViewModel(MeterSession session, BluetoothAccess access, ILogger<MeterViewModel> logger)
    {
        _session = session;
        _access = access;
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

    /// <summary>The samples the graph plots: the newest run in the current mode.</summary>
    public IReadOnlyList<Sample> TrendSamples =>
        _session.Latest is { } r ? _session.Log.CurrentRun(r.Mode) : [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _primaryActionText = "Connect";

    [ObservableProperty]
    private bool _isConnected;

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
            await _session.StartScanAsync();
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
        if (await page.DisplayAlertAsync("Clear the log?", $"This deletes {_session.Log.Count:N0} readings. Export them first if you want to keep them.", "Clear", "Cancel"))
        {
            _session.Log.Clear();
            RefreshLog();
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_session.Log.Count == 0) return;
        try
        {
            await CsvExporter.ShareAsync(_session.Log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "export failed");
            await Shell.Current.CurrentPage.DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }

    private void OnReading(MeterReading reading)
    {
        ModeName = reading.Sub.Label is { } label ? $"{reading.ModeName} · SETUP: {label}" : reading.ModeName;
        ReadingDescription = reading.Sub.Text.Length > 0
            ? $"{reading.MainLine}, {reading.Sub.Line}"
            : reading.MainLine;
        DisplayChanged?.Invoke(this, EventArgs.Empty);
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
            return;
        }

        var span = log.Samples[^1].Time - log.Samples[0].Time;
        LogSummary = string.Format(CultureInfo.CurrentCulture, "{0:N0} readings over {1}{2}",
            log.Count, FormatSpan(span), _session.IsLogging ? "" : " (paused)");

        // Statistics for the run on the graph, not the whole log (it may mix units).
        if (_session.Latest is { } latest && MeasurementLog.Statistics(log.CurrentRun(latest.Mode)) is { } stats)
        {
            var unit = latest.BaseUnit;
            Statistics = $"Min {SiFormat.Format(stats.Min, unit, provider: CultureInfo.CurrentCulture)}   Max {SiFormat.Format(stats.Max, unit, provider: CultureInfo.CurrentCulture)}   Avg {SiFormat.Format(stats.Average, unit, provider: CultureInfo.CurrentCulture)}";
        }
        else
            Statistics = "";
    }

    private static string FormatSpan(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => $"{span.TotalSeconds:0} s",
        < 3600 => $"{(int)span.TotalMinutes} min {span.Seconds} s",
        _ => $"{(int)span.TotalHours} h {span.Minutes} min",
    };
}
