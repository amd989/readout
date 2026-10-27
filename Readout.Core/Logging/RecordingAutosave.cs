using System.Globalization;
using System.Text;

namespace Readout.Core.Logging;

/// <summary>
/// Keeps the live log on disk as it grows, so closing the app (or a crash) doesn't lose an
/// unsaved recording. <see cref="Flush"/> appends only the samples added since the last call;
/// <see cref="Restore"/> reads them back at launch.
/// </summary>
/// <remarks>Call both on the thread that owns the log (the app's main thread).</remarks>
public sealed class RecordingAutosave(MeasurementLog log, string path)
{
    private int _written;

    public string Path { get; } = path;

    /// <summary>Loads a recording left from last time into the (empty) log.</summary>
    public void Restore()
    {
        if (log.Count > 0 || !File.Exists(Path)) return;

        using var reader = new StreamReader(Path);
        var samples = SampleCsv.Read(reader);
        if (samples.Count == 0) return;

        log.AddRange(samples);
        _written = samples.Count;
    }

    /// <summary>Writes what's new; rewrites the file if the log was cleared since.</summary>
    public void Flush()
    {
        var samples = log.Samples;
        if (samples.Count < _written)
        {
            // Cleared (or saved and cleared): start the file over.
            _written = 0;
            if (File.Exists(Path)) File.Delete(Path);
        }
        if (samples.Count == _written) return;

        var text = new StringBuilder();
        using (var writer = new StringWriter(text, CultureInfo.InvariantCulture))
        {
            if (_written == 0) writer.WriteLine(SampleCsv.Header);
            SampleCsv.WriteRows(samples, _written, samples[0].Time, writer);
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.AppendAllText(Path, text.ToString());
        _written = samples.Count;
    }
}
