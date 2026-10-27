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

    public SettingsViewModel(AppSettings settings, MeterSession session)
    {
        _settings = settings;
        _session = session;
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

    [RelayCommand]
    private Task OpenAboutAsync() => Shell.Current.GoToAsync(nameof(Views.AboutPage));
}
