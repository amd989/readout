using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Connection;
using Readout.Services;

namespace Readout.ViewModels;

/// <summary>The Settings tab. Changes save as soon as they're made.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MeterSession _session;

#if MACCATALYST || WINDOWS
    private readonly McpHost _mcp;

    public SettingsViewModel(AppSettings settings, MeterSession session, McpHost mcp)
    {
        _mcp = mcp;
        _mcpStatus = mcp.Status;
        mcp.StatusChanged += (_, _) => McpStatus = mcp.Status;
#else
    public SettingsViewModel(AppSettings settings, MeterSession session)
    {
#endif
        _settings = settings;
        _session = session;
        _mcpEnabled = settings.McpEnabled;
        _mcpPort = settings.McpPort.ToString(CultureInfo.InvariantCulture);
        _autoReconnect = settings.AutoReconnect;
        _continuitySound = settings.ContinuitySound;
        _continuityVibration = settings.ContinuityVibration;
        _onBelow = settings.ContinuityOnBelow.ToString("0.#", CultureInfo.CurrentCulture);
        _offAbove = settings.ContinuityOffAbove.ToString("0.#", CultureInfo.CurrentCulture);
    }

    public string Version { get; } = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private bool _continuitySound;
    [ObservableProperty] private bool _continuityVibration;
    [ObservableProperty] private string _onBelow;
    [ObservableProperty] private string _offAbove;

    partial void OnAutoReconnectChanged(bool value)
    {
        _settings.AutoReconnect = value;
        _session.AutoReconnect = value;
    }

    partial void OnContinuitySoundChanged(bool value) => _settings.ContinuitySound = value;
    partial void OnContinuityVibrationChanged(bool value) => _settings.ContinuityVibration = value;

    partial void OnOnBelowChanged(string value)
    {
        if (TryOhms(value, out var ohms)) _settings.ContinuityOnBelow = ohms;
    }

    partial void OnOffAboveChanged(string value)
    {
        if (TryOhms(value, out var ohms)) _settings.ContinuityOffAbove = ohms;
    }

    private static bool TryOhms(string text, out double ohms) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out ohms) && ohms is >= 0 and <= 10_000;

    /// <summary>The AI assistant (MCP) section: Mac and Windows only.</summary>
    public bool ShowMcp { get; } = DeviceInfo.Idiom == DeviceIdiom.Desktop;

    [ObservableProperty] private bool _mcpEnabled;
    [ObservableProperty] private string _mcpPort;
    [ObservableProperty] private string _mcpStatus = "Off";

    partial void OnMcpEnabledChanged(bool value) => _settings.McpEnabled = value;

    partial void OnMcpPortChanged(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1024 and <= 65535)
            _settings.McpPort = port;
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Uses the settings on desktop; does nothing on phones, where the section is hidden.")]
    private async Task CopyMcpCommandAsync()
    {
#if MACCATALYST || WINDOWS
        await Clipboard.Default.SetTextAsync(Readout.Mcp.McpSetup.ClaudeCodeCommand(_settings.McpPort, _settings.McpToken));
        await Shell.Current.CurrentPage.DisplayAlertAsync("Copied",
            "Paste it in a terminal to add Readout to Claude Code. It includes the token, so treat it like a password.", "OK");
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    private async Task NewMcpTokenAsync()
    {
        if (!await Shell.Current.CurrentPage.DisplayAlertAsync("Make a new token?",
                "Assistants set up with the current token will stop working until you set them up again.", "New token", "Cancel"))
            return;
        _settings.NewMcpToken();
    }

    [RelayCommand]
    private Task OpenAboutAsync() => Shell.Current.GoToAsync(nameof(Views.AboutPage));
}
