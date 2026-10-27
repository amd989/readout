using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Readout.Core.Logging;
using Readout.Services;
using Microsoft.Extensions.Logging;

namespace Readout.ViewModels;

/// <summary>One saved session: its graph, statistics and notes. Opened with <c>?id=</c>.</summary>
public sealed partial class SessionDetailViewModel(SessionStore store, ILogger<SessionDetailViewModel> logger)
    : ObservableObject, IQueryAttributable
{
    private SessionInfo? _info;

    /// <summary>Raised when the samples for the graph are loaded.</summary>
    public event EventHandler? SamplesLoaded;

    /// <summary>The readings in the session's main mode, which the graph plots.</summary>
    public IReadOnlyList<Sample> GraphSamples { get; private set; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _samplesText = "";
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private string _minText = "–";
    [ObservableProperty] private string _maxText = "–";
    [ObservableProperty] private string _averageText = "–";
    [ObservableProperty] private string _peakToPeakText = "–";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOtherModes))]
    private string _otherModesNote = "";

    public bool HasOtherModes => OtherModesNote.Length > 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _ = LoadAsync(id);
    }

    private async Task LoadAsync(Guid id)
    {
        try
        {
            _info = (await store.ListAsync()).FirstOrDefault(s => s.Id == id);
            if (_info is null) return;
            Show(_info);

            var samples = await store.LoadSamplesAsync(id);
            GraphSamples = [.. samples.Where(s => s.Mode == _info.MainMode)];
            var others = samples.Count - GraphSamples.Count;
            OtherModesNote = others > 0
                ? $"The graph shows the {_info.ModeName.ToLowerInvariant()} readings. {others:N0} readings in other modes are in the CSV."
                : "";
            SamplesLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "loading session failed");
        }
    }

    private void Show(SessionInfo info)
    {
        Name = info.Name;
        Subtitle = $"{info.ModeName} · {info.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        SamplesText = info.SampleCount.ToString("N0", CultureInfo.CurrentCulture);
        DurationText = Format.Duration(info.Duration);
        Notes = info.Notes;
        if (info.Statistics is { } s)
        {
            MinText = Format.Value(s.Min, info.Unit);
            MaxText = Format.Value(s.Max, info.Unit);
            AverageText = Format.Value(s.Average, info.Unit);
            PeakToPeakText = Format.Value(s.PeakToPeak, info.Unit);
        }
    }

    /// <summary>Saves the notes if they changed (the page calls this when it goes away).</summary>
    public async Task SaveNotesAsync()
    {
        if (_info is null || _info.Notes == Notes) return;
        _info = _info with { Notes = Notes };
        try { await store.UpdateAsync(_info); }
        catch (Exception ex) { logger.LogError(ex, "saving notes failed"); }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_info is null) return;
        var name = await Shell.Current.CurrentPage.DisplayPromptAsync("Rename session", null, "Save", "Cancel",
            initialValue: _info.Name, maxLength: 80);
        if (string.IsNullOrWhiteSpace(name)) return;

        _info = _info with { Name = name.Trim(), Notes = Notes };
        Name = _info.Name;
        await store.UpdateAsync(_info);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_info is null) return;
        await SaveNotesAsync();
        try
        {
            await CsvExporter.ShareAsync(store, _info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "export failed");
            await Shell.Current.CurrentPage.DisplayAlertAsync("Export failed", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_info is null) return;
        if (!await Shell.Current.CurrentPage.DisplayAlertAsync("Delete this session?",
                $"\"{_info.Name}\" and its {_info.SampleCount:N0} readings will be gone.", "Delete", "Cancel"))
            return;

        store.Delete(_info.Id);
        _info = null;
        await Shell.Current.GoToAsync("..");
    }
}
