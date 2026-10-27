using Readout.Core.Connection;
using Readout.Core.Feedback;
using Microsoft.Extensions.Logging;

namespace Readout.Services;

/// <summary>
/// Beeps and vibrates the phone for continuity, following the user's settings. Lives for the
/// whole app (resolved at launch), listening to the meter session.
/// </summary>
public sealed class ContinuityFeedback
{
    private readonly MeterSession _session;
    private readonly AppSettings _settings;
    private readonly IContinuityTone _tone;
    private readonly ILogger<ContinuityFeedback> _logger;
    private readonly ContinuityAlert _alert = new();

    public ContinuityFeedback(MeterSession session, AppSettings settings, IContinuityTone tone, ILogger<ContinuityFeedback> logger)
    {
        _session = session;
        _settings = settings;
        _tone = tone;
        _logger = logger;

        ApplySettings();
        _settings.Changed += (_, _) => ApplySettings();
        _alert.Changed += (_, on) => OnAlertChanged(on);
        _session.ReadingReceived += (_, reading) =>
        {
            if (_settings.ContinuitySound || _settings.ContinuityVibration)
                _alert.Update(reading);
        };
        _session.StateChanged += (_, _) =>
        {
            if (_session.State != ConnectionState.Connected) _alert.Reset();
        };
    }

    private void ApplySettings()
    {
        _alert.OnBelowOhms = _settings.ContinuityOnBelow;
        _alert.OffAboveOhms = _settings.ContinuityOffAbove;
        if (!_settings.ContinuitySound) _tone.Silence();
        if (!_settings.ContinuitySound && !_settings.ContinuityVibration) _alert.Reset();
    }

    private void OnAlertChanged(bool on)
    {
        try
        {
            if (!on)
            {
                _tone.Silence();
                return;
            }
            if (_settings.ContinuitySound) _tone.Play();
            if (_settings.ContinuityVibration) Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(120));
        }
        catch (Exception ex)
        {
            // No vibrator, audio busy…: the readings matter more than the beep.
            _logger.LogWarning(ex, "continuity feedback failed");
        }
    }
}
