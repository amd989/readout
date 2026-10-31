namespace Readout.Linux;

/// <summary>
/// The window's opening height, within the screen. The app opens 1000 px tall so the meter page
/// fits without scrolling, but a laptop at 1280×800 (the Fedora test laptop) cut its bottom off
/// behind the panel. GTK doesn't say where the panels are, so this leaves room for one.
/// </summary>
internal static class WindowFit
{
    // A desktop panel or dock (KDE's is about 45 px), plus a little air.
    private const int PanelRoom = 72;

    public static double Height(double wanted, double minimum)
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is not { } monitors || monitors.GetNItems() == 0)
            return wanted;
        // The window hasn't opened yet, so it isn't on a screen: the first is the likeliest.
        if (monitors.GetObject(0) is not Gdk.Monitor monitor) return wanted;
        monitor.GetGeometry(out var screen);
        return Math.Clamp(screen.Height - PanelRoom, minimum, wanted);
    }
}
