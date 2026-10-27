using Readout.Drawing;
using Readout.ViewModels;

namespace Readout.Views;

public partial class MeterPage : ContentPage
{
    private readonly MeterViewModel _viewModel;
    private readonly LcdDrawable _lcd = new();
    private readonly TrendDrawable _trend = new();

    public MeterPage(MeterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        Lcd.Drawable = _lcd;
        Trend.Drawable = _trend;
        ApplyTheme();
        Application.Current!.RequestedThemeChanged += (_, _) => { ApplyTheme(); Trend.Invalidate(); };

        // The LCD keeps its 2:1 shape at any width.
        Lcd.SizeChanged += (_, _) =>
        {
            var height = Math.Round(Lcd.Width / 2);
            if (Lcd.Width > 0 && Math.Abs(Lcd.HeightRequest - height) > 1)
                Lcd.HeightRequest = height;
        };

        // The view model raises these on the main thread.
        _viewModel.DisplayChanged += (_, _) =>
        {
            _lcd.Reading = _viewModel.Reading;
            Lcd.Invalidate();
        };
        _viewModel.TrendChanged += (_, _) =>
        {
            _trend.Samples = _viewModel.TrendSamples;
            Trend.Invalidate();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.AutoConnectAsync();
    }

    private async void OnKeyPressed(object? sender, KeyPressedEventArgs e) =>
        await _viewModel.PressKeyAsync(e.Key, e.LongPress);

    private async void OnAboutClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(AboutPage));

    private void ApplyTheme()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _trend.TextColor = dark ? Color.FromArgb("#A9B0B5") : Color.FromArgb("#5C6368");
        _trend.GridColor = dark ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#22000000");
    }
}
