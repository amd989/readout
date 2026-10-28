using Readout.Core.Logging;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout;

public partial class App : Application
{
    // How often the live recording is written to disk while the app runs.
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(10);

    private readonly RecordingAutosave _autosave;
    private readonly ILogger<App> _logger;

    public App(RecordingAutosave autosave, ContinuityFeedback continuity, ILogger<App> logger)
    {
        // Created now so it listens from the first reading on.
        _ = continuity;
        InitializeComponent();
        _autosave = autosave;
        _logger = logger;

        // A recording that wasn't saved before the app last closed comes back.
        try { _autosave.Restore(); }
        catch (Exception ex) { _logger.LogError(ex, "restoring the recording failed"); }

        Dispatcher.StartTimer(AutosaveInterval, () => { FlushRecording(); return true; });
    }

    // Desktop: a window shaped like a phone in portrait, wide enough for the LCD.
#if MACCATALYST
    // Tall enough that the meter page fits without scrolling, Connect card and all. Other
    // desktops keep 900 so the window fits a 1080p screen with a taskbar.
    internal static readonly Size WindowSize = new(480, 1000);
#else
    internal static readonly Size WindowSize = new(480, 900);
#endif
    internal static readonly Size MinimumWindowSize = new(360, 560);

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Readout",
            MinimumWidth = MinimumWindowSize.Width,
            MinimumHeight = MinimumWindowSize.Height,
            Width = WindowSize.Width,
            Height = WindowSize.Height,
        };
        window.Destroying += (_, _) => FlushRecording();
        return window;
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        FlushRecording();
    }

    private void FlushRecording()
    {
        try { _autosave.Flush(); }
        catch (Exception ex) { _logger.LogWarning(ex, "autosave failed"); }
    }
}
