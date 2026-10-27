using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Logging;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout.ViewModels;

/// <summary>A saved session in the list.</summary>
public sealed record SessionItem(SessionInfo Info)
{
    public string Name => Info.Name;
    public string When => Info.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string Detail =>
        $"{Info.ModeName} · {Format.Duration(Info.Duration)} · {Info.SampleCount:N0} readings";
}

/// <summary>The list of saved sessions, newest first.</summary>
public sealed partial class SessionsViewModel(SessionStore store, ILogger<SessionsViewModel> logger) : ObservableObject
{
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    [ObservableProperty]
    private bool _isEmpty;

    public async Task LoadAsync()
    {
        try
        {
            var sessions = await store.ListAsync();
            Sessions.Clear();
            foreach (var s in sessions)
                Sessions.Add(new SessionItem(s));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "listing sessions failed");
        }
        IsEmpty = Sessions.Count == 0;
    }

    [RelayCommand]
    private Task OpenAsync(SessionItem? item) =>
        item is null ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{nameof(Views.SessionDetailPage)}?id={item.Info.Id:N}");
}
