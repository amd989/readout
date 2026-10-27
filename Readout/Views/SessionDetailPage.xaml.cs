using Readout.ViewModels;

namespace Readout.Views;

public partial class SessionDetailPage : ContentPage
{
    private readonly SessionDetailViewModel _viewModel;

    public SessionDetailPage(SessionDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.SamplesLoaded += OnSamplesLoaded;
    }

    private void OnSamplesLoaded(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => Trend.Samples = _viewModel.GraphSamples);

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.SaveNotesAsync();
    }
}
