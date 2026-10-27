namespace Readout;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell())
        {
            Title = "Readout",
            // Desktop: a window shaped like a phone in portrait, wide enough for the LCD.
            MinimumWidth = 360,
            MinimumHeight = 560,
            Width = 480,
            Height = 900,
        };
}
