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

        // Stacked or side by side, depending on the window's shape.
        Scroller.SizeChanged += (_, _) => ApplyLayout();

        // The graph takes whatever height the page has spare, so the page fits the screen.
        Scroller.SizeChanged += (_, _) => QueueFitTrend();
        PageGrid.SizeChanged += (_, _) => QueueFitTrend();
        TrendCard.SizeChanged += (_, _) => QueueFitTrend();

        // The view model raises these on the main thread.
        _viewModel.KeyPressed += (_, press) =>
        {
            foreach (var button in KeyGrid.Children.OfType<KeyButton>())
                if (button.Key == press.Key)
                    button.PlayPress(press.LongPress);
        };
        _viewModel.DisplayChanged += (_, _) =>
        {
            _lcd.Reading = _viewModel.Reading;
            Lcd.Invalidate();
        };
        _viewModel.TrendChanged += (_, _) => Trend.Samples = _viewModel.TrendSamples;
    }

    /// <summary>The meter column's narrowest width in two columns: about a phone's, where the buttons' labels fit.</summary>
    private const double MinimumMeterColumnWidth = 340;

    private bool? _wide;
    private double _meterColumnWidth;

    /// <summary>
    /// Side by side when the window is wider than tall and wide enough for two readable
    /// columns: the meter in a narrow column on the left (a quarter of the width, at least a
    /// phone's), the trend card filling the rest. Otherwise stacked.
    /// </summary>
    private void ApplyLayout()
    {
        if (Scroller.Width <= 0 || Scroller.Height <= 0) return;
        var wide = Scroller.Width > Scroller.Height && Scroller.Width >= 640;

        if (wide)
        {
            var content = Scroller.Width - PageGrid.Padding.HorizontalThickness - PageGrid.ColumnSpacing;
            var meterWidth = Math.Floor(Math.Max(MinimumMeterColumnWidth, content / 4));
            if (wide == _wide && Math.Abs(meterWidth - _meterColumnWidth) < 1) return;
            _meterColumnWidth = meterWidth;

            PageGrid.ColumnDefinitions.Clear();
            PageGrid.RowDefinitions.Clear();
            PageGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(meterWidth)));
            PageGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            PageGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(TrendCard, 0);
            Grid.SetColumn(TrendCard, 1);
            PageGrid.MaximumWidthRequest = double.PositiveInfinity;
            MoveLogFooter(toRecordingCard: true);
        }
        else
        {
            if (wide == _wide) return;
            PageGrid.ColumnDefinitions.Clear();
            PageGrid.RowDefinitions.Clear();
            PageGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            PageGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            PageGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(TrendCard, 1);
            Grid.SetColumn(TrendCard, 0);
            PageGrid.MaximumWidthRequest = 720;
            MoveLogFooter(toRecordingCard: false);
        }
        _wide = wide;
        Grid.SetRow(MeterColumn, 0);
        Grid.SetColumn(MeterColumn, 0);
        QueueFitTrend();
    }

    /// <summary>
    /// Side by side, the statistics and Pause/Clear/Save move to a Recording card under the
    /// meter's buttons, as large tiles, and the graph card is just the graph. Stacked, they go
    /// back under the graph, as one line.
    /// </summary>
    private void MoveLogFooter(bool toRecordingCard)
    {
        var target = toRecordingCard ? (Layout)RecordingStack : TrendGrid;
        if (LogFooter.Parent == target) return;

        ((Layout)LogFooter.Parent).Remove(LogFooter);
        if (toRecordingCard)
        {
            RecordingStack.Add(LogFooter);
        }
        else
        {
            Grid.SetRow(LogFooter, 2);
            TrendGrid.Add(LogFooter);
        }
        RecordingCard.IsVisible = toRecordingCard;
        StatsTiles.IsVisible = toRecordingCard;
        StatsLine.IsVisible = !toRecordingCard;
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
