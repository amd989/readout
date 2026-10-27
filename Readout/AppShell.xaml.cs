using Readout.Views;

namespace Readout;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
#if ANDROID || WINDOWS
        // Branded bars where the system's own look doesn't apply.
        if (Application.Current?.Resources.TryGetValue("BrandShell", out var style) == true)
            Style = (Style)style;
#endif
        // Pages pushed on top of a tab.
        Routing.RegisterRoute(nameof(ConnectPage), typeof(ConnectPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(SessionDetailPage), typeof(SessionDetailPage));
        Routing.RegisterRoute(nameof(GraphPage), typeof(GraphPage));
    }
}
