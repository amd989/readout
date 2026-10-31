using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Connection;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout.ViewModels;

/// <summary>
/// A meter in the scan list. Updated in place each time the meter is heard again, so its row
/// stays put: replacing the rows on every advertisement rebuilt them on Linux, and a tap that
/// straddled a rebuild was lost.
/// </summary>
public sealed partial class MeterItem(DiscoveredMeter meter) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(SignalBars), nameof(Detail), nameof(SignalDescription))]
    private DiscoveredMeter _meter = meter;

    public string Name => Meter.Name;
    public int SignalBars => SignalQuality.Bars(Meter.Rssi);

    /// <summary>"Excellent signal".</summary>
    public string Detail
    {
        get
        {
            var words = SignalQuality.Describe(Meter.Rssi);
            return $"{char.ToUpperInvariant(words[0])}{words[1..]} signal";
        }
    }

    /// <summary>For screen readers and the tooltip, with the number: "Signal excellent, -50 dBm".</summary>
    public string SignalDescription => $"Signal {SignalQuality.Describe(Meter.Rssi)}, {Meter.Rssi} dBm";
}

/// <summary>
/// The connect screen: scans while it's open and lists the meters it finds. Transient: the
/// page calls <see cref="Stop"/> when it goes away, which unsubscribes from the session.
/// </summary>
public sealed partial class ConnectViewModel : ObservableObject
{
    private readonly MeterSession _session;
    private readonly BluetoothAccess _access;
    private readonly ILogger<ConnectViewModel> _logger;
    private bool _connecting;

    public ConnectViewModel(MeterSession session, BluetoothAccess access, ILogger<ConnectViewModel> logger)
    {
        _session = session;
        _access = access;
        _logger = logger;
    }

    public ObservableCollection<MeterItem> Meters { get; } = [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isScanning;

    public async Task StartAsync()
    {
        _session.DiscoveredChanged += OnDiscoveredChanged;
        _session.StateChanged += OnStateChanged;

        if (await _access.EnsureReadyAsync() is { } problem)
        {
            StatusText = problem;
            return;
        }

        try
        {
            await _session.StartScanAsync();
            StatusText = "Looking for meters. Turn on Bluetooth on the 121GW: hold 1ms PEAK.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scan failed");
            StatusText = "Couldn't start looking for meters. Check that Bluetooth is on.";
        }
        Refresh();
    }

    public void Stop()
    {
        _session.DiscoveredChanged -= OnDiscoveredChanged;
        _session.StateChanged -= OnStateChanged;
        if (!_connecting && _session.State == ConnectionState.Scanning)
            _ = _session.StopScanAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync(MeterItem? item)
    {
        if (item is null || _connecting) return;
        _connecting = true;
        StatusText = $"Connecting to {item.Name}…";
        try
        {
            await _session.ConnectAsync(item.Meter);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "connect failed");
            StatusText = $"Couldn't connect to {item.Name}. Move closer and try again.";
            try { await _session.StartScanAsync(); }
            catch (Exception scanEx) { _logger.LogWarning(scanEx, "rescan failed"); }
        }
        finally
        {
            _connecting = false;
        }
    }

    [RelayCommand]
    private async Task DemoAsync()
    {
        _connecting = true;
        try
        {
            await _session.StartDemoAsync();
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _connecting = false;
        }
    }

    private void OnDiscoveredChanged(object? sender, EventArgs e) => Refresh();

    // Only the user's tap closes this page. (Closing it from here as well raced the page's own
    // opening animation and could leave Shell's top bar behind.)
    private void OnStateChanged(object? sender, EventArgs e) =>
        IsScanning = _session.State == ConnectionState.Scanning;

    // Brings the list in step with the session's, keeping each meter's row (see MeterItem).
    private void Refresh()
    {
        IsScanning = _session.State == ConnectionState.Scanning;
        var discovered = _session.Discovered;
        for (var i = 0; i < discovered.Count; i++)
        {
            var meter = discovered[i];
            var at = -1;
            for (var j = i; j < Meters.Count && at < 0; j++)
                if (Meters[j].Meter.Id == meter.Id) at = j;

            if (at < 0)
            {
                Meters.Insert(i, new MeterItem(meter));
                continue;
            }
            if (at != i) Meters.Move(at, i);
            if (Meters[i].Meter != meter) Meters[i].Meter = meter;
        }
        while (Meters.Count > discovered.Count)
            Meters.RemoveAt(Meters.Count - 1);
    }
}
