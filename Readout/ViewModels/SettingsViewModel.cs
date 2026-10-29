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
        settings.Changed += (_, _) => OnPropertyChanged(nameof(McpAddress));
        _autoReconnect = settings.AutoReconnect;
        _continuitySound = settings.ContinuitySound;
        _continuityVibration = settings.ContinuityVibration;
        _onBelow = settings.ContinuityOnBelow.ToString("0.#", CultureInfo.CurrentCulture);
        _offAbove = settings.ContinuityOffAbove.ToString("0.#", CultureInfo.CurrentCulture);
    }

    public string Version { get; } = AppVersion.Text;

    public bool ShowVibration => AppSettings.CanVibrate;

    public string ContinuityHelp { get; } = AppSettings.CanVibrate
        ? "In continuity and resistance modes, the phone beeps and buzzes below the first value and stops above the second, so a wavering reading doesn't chatter."
        : "In continuity and resistance modes, the app beeps below the first value and stops above the second, so a wavering reading doesn't chatter.";

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

    /// <summary>The server's address, for clients set up by hand.</summary>
    public string McpAddress => $"http://127.0.0.1:{_settings.McpPort}/mcp";

    [RelayCommand]
    private Task CopyMcpAddressAsync() => CopyAsync(McpAddress, "The server's address is on the clipboard.");

    [RelayCommand]
    private Task CopyMcpHeaderAsync() => CopyAsync($"Authorization: Bearer {_settings.McpToken}",
        "The header, with the token, is on the clipboard. Treat it like a password.");

    /// <summary>Asks which assistant, then copies the setup it needs.</summary>
    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Uses the settings on desktop; does nothing on phones, where the section is hidden.")]
    private async Task CopyMcpSetupAsync()
    {
#if MACCATALYST || WINDOWS
        var choices = new (string Title, Readout.Mcp.McpClientKind Kind, string Where)[]
        {
            ("Claude Code", Readout.Mcp.McpClientKind.ClaudeCode,
                "Paste it in a terminal, then start a new Claude Code session."),
            ("Claude Desktop", Readout.Mcp.McpClientKind.ClaudeDesktop,
                "In Claude Desktop, open Settings → Developer → Edit Config and add it to claude_desktop_config.json (merge it with any servers already there), then quit and reopen Claude. It needs Node.js."),
            ("VS Code", Readout.Mcp.McpClientKind.VsCode,
                "In VS Code, run \"MCP: Open User Configuration\" and add it to mcp.json (merge it with any servers already there)."),
            ("Cursor", Readout.Mcp.McpClientKind.Cursor,
                "Add it to ~/.cursor/mcp.json (merge it with any servers already there), then restart Cursor."),
        };
        var picked = await Shell.Current.CurrentPage.DisplayActionSheetAsync(
            "Copy setup for", "Cancel", null, [.. choices.Select(c => c.Title)]);
        var choice = choices.FirstOrDefault(c => c.Title == picked);
        if (choice.Title is null) return;

        await CopyAsync(Readout.Mcp.McpSetup.For(choice.Kind, _settings.McpPort, _settings.McpToken),
            $"{choice.Where} It includes the token, so treat it like a password.");
#else
        await Task.CompletedTask;
#endif
    }

    private static async Task CopyAsync(string text, string message)
    {
        await Clipboard.Default.SetTextAsync(text);
        await Shell.Current.CurrentPage.DisplayAlertAsync("Copied", message, "OK");
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
