using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

// The head only runs on Linux (the GTK backend's APIs say so too).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace Readout.Linux;

/// <summary>The GTK application: it builds the same MauiApp as every other platform.</summary>
public sealed class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp()
    {
        // Gaps in the GTK backend (preview 13) that the app's pages run into
        PageRelayout.Register();
        GestureWiring.Register();
        ShellHeaderBar.Register();
        TrendMouse.Register();
        KeyButtonPress.Register();
        return MauiProgram.CreateMauiApp();
    }

    public static void Main(string[] args) => new Program().Run(args);
}
