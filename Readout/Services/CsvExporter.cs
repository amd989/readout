using Readout.Core.Logging;

namespace Readout.Services;

/// <summary>Opens the share sheet for a saved session's CSV (on Linux, a save dialog).</summary>
public static class CsvExporter
{
    public static async Task ShareAsync(SessionStore store, SessionInfo session)
    {
        // A copy with a readable name: the stored file is named by id.
        var name = $"readout-{Format.FileName(session.Name)}.csv";
#if LINUX
        await Readout.Linux.FileSaveDialog.SaveCopyAsync(store.CsvPath(session.Id), name, $"Export \"{session.Name}\"");
#else
        var path = Path.Combine(FileSystem.CacheDirectory, name);
        File.Copy(store.CsvPath(session.Id), path, overwrite: true);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Export \"{session.Name}\"",
            File = new ShareFile(path, "text/csv"),
        });
#endif
    }
}
