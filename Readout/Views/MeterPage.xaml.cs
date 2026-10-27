using Readout.Drawing;
using Readout.ViewModels;

namespace Readout.Views;

public partial class MeterPage : ContentPage
{
    private readonly MeterViewModel _viewModel;
    private readonly LcdDrawable _lcd = new();

    public MeterPage(MeterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        Lcd.Drawable = _lcd;
        Trend.Samples = _viewModel.TrendSamples;   // a recording restored from last time

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
        _viewModel.TrendChanged += (_, _) => Trend.Samples = _viewModel.TrendSamples;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.AutoConnectAsync();
    }

    private async void OnKeyPressed(object? sender, KeyPressedEventArgs e) =>
        await _viewModel.PressKeyAsync(e.Key, e.LongPress);
}
