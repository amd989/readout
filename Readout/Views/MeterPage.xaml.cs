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

        // The LCD keeps its shape at any width.
        Lcd.SizeChanged += (_, _) =>
        {
            var height = Math.Round(Lcd.Width / LcdDrawable.AspectRatio);
            if (Lcd.Width > 0 && Math.Abs(Lcd.HeightRequest - height) > 1)
                Lcd.HeightRequest = height;
        };

        // The graph takes whatever height the page has spare, so the page fits the screen.
        Scroller.SizeChanged += (_, _) => QueueFitTrend();
        PageGrid.SizeChanged += (_, _) => QueueFitTrend();
        TrendCard.SizeChanged += (_, _) => QueueFitTrend();

        // The view model raises these on the main thread.
        _viewModel.DisplayChanged += (_, _) =>
        {
            _lcd.Reading = _viewModel.Reading;
            Lcd.Invalidate();
        };
        _viewModel.TrendChanged += (_, _) => Trend.Samples = _viewModel.TrendSamples;
    }

    /// <summary>The graph's height never goes below this; smaller screens scroll instead.</summary>
    private const double MinimumTrendHeight = 110;

    private bool _fitQueued;

    /// <summary>Fits the graph once the current layout pass is over, not in the middle of it.</summary>
    private void QueueFitTrend()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _fitQueued = false;
            FitTrend();
        });
    }

    /// <summary>
    /// Sizes the graph to the height left below everything else. Done after layout rather
    /// than with a "*" row: inside a ScrollView a "*" row isn't stretched, and the graph's
    /// natural height differs between platforms.
    /// </summary>
    private void FitTrend()
    {
        if (Scroller.Height <= 0 || TrendCard.Height <= 0 || Trend.Height <= 0) return;

        // Mid-layout, the graph may not have its last requested height yet, and the sizes
        // below don't add up. The size change that follows queues another fit.
        if (Trend.HeightRequest >= 0 && Math.Abs(Trend.Height - Trend.HeightRequest) > 1) return;

        var restOfCard = TrendCard.Height - Trend.Height;
        var available = Scroller.Height - TrendCard.Y - restOfCard - PageGrid.Padding.Bottom;
        var height = Math.Max(MinimumTrendHeight, Math.Floor(available));
        if (Math.Abs(height - Trend.HeightRequest) > 1)
            Trend.HeightRequest = height;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.AutoConnectAsync();
    }

    private async void OnKeyPressed(object? sender, KeyPressedEventArgs e) =>
        await _viewModel.PressKeyAsync(e.Key, e.LongPress);
}
