using Readout.ViewModels;

namespace Readout.Views;

public partial class SessionsPage : ContentPage
{
    private readonly SessionsViewModel _viewModel;

    public SessionsPage(SessionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Every time: a session may have been renamed or deleted on its own page.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
