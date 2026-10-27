using System.Globalization;
using System.Text;
using Readout.Core.Logging;

namespace Readout.Services;

/// <summary>Writes the log to a CSV file and opens the share sheet for it.</summary>
public static class CsvExporter
{
    public static async Task ShareAsync(MeasurementLog log)
    {
        var started = log.StartedAt ?? DateTimeOffset.Now;
        var name = $"readout-{started.ToLocalTime():yyyyMMdd-HHmmss}.csv";
        var path = Path.Combine(FileSystem.CacheDirectory, name);

        // Written on the caller's (main) thread: the log is only touched there.
        var builder = new StringBuilder();
        using (var writer = new StringWriter(builder, CultureInfo.InvariantCulture))
            log.WriteCsv(writer);
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export measurements",
            File = new ShareFile(path, "text/csv"),
        });
    }
}
