using Readout.Core.Logging;

namespace Readout.Services;

/// <summary>Opens the share sheet for a saved session's CSV.</summary>
public static class CsvExporter
{
    public static async Task ShareAsync(SessionStore store, SessionInfo session)
    {
        // A copy with a readable name: the stored file is named by id.
        var name = $"readout-{Format.FileName(session.Name)}.csv";
        var path = Path.Combine(FileSystem.CacheDirectory, name);
        File.Copy(store.CsvPath(session.Id), path, overwrite: true);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Export \"{session.Name}\"",
            File = new ShareFile(path, "text/csv"),
        });
    }
}
