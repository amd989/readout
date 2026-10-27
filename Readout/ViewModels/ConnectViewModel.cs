using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Connection;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout.ViewModels;

/// <summary>A meter in the scan list.</summary>
public sealed record MeterItem(DiscoveredMeter Meter)
{
    public string Name => Meter.Name;
    public string Detail => $"Signal {Meter.Rssi} dBm";
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
            await _session.StartScanAsync();
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

    private void OnStateChanged(object? sender, EventArgs e)
    {
        IsScanning = _session.State == ConnectionState.Scanning;

        // Connected by itself (the remembered meter showed up): nothing left to do here.
        if (_session.State == ConnectionState.Connected && !_connecting)
            _ = Shell.Current.GoToAsync("..");
    }

    private void Refresh()
    {
        IsScanning = _session.State == ConnectionState.Scanning;
        Meters.Clear();
        foreach (var meter in _session.Discovered)
            Meters.Add(new MeterItem(meter));
    }
}
