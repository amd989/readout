using Readout.ViewModels;

namespace Readout.Views;

public partial class GraphPage : ContentPage
{
    private readonly MeterViewModel _viewModel;

    public GraphPage(MeterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // The view model is shared with the main page and outlives this one: subscribe only while shown.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.TrendChanged += OnTrendChanged;
        Trend.Samples = _viewModel.TrendSamples;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.TrendChanged -= OnTrendChanged;
    }

    private void OnTrendChanged(object? sender, EventArgs e) => Trend.Samples = _viewModel.TrendSamples;
}
