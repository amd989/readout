using Readout.Views;

namespace Readout;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(ConnectPage), typeof(ConnectPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(SessionsPage), typeof(SessionsPage));
        Routing.RegisterRoute(nameof(SessionDetailPage), typeof(SessionDetailPage));
        Routing.RegisterRoute(nameof(GraphPage), typeof(GraphPage));
    }
}
