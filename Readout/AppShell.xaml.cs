using Readout.Views;

namespace Readout;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(ConnectPage), typeof(ConnectPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
    }
}
