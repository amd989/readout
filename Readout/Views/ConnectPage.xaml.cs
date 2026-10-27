using Readout.ViewModels;

namespace Readout.Views;

public partial class ConnectPage : ContentPage
{
    private readonly ConnectViewModel _viewModel;

    public ConnectPage(ConnectViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.StartAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Stop();
    }
}
