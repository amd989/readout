namespace Readout.Core.Feedback;

/// <summary>Makes the beep: a sine tone as a 16-bit mono WAV file that loops without a click.</summary>
public static class ToneWave
{
    public const int SampleRate = 44100;

    /// <param name="frequency">Hz; the tone is a whole number of cycles long, so it loops cleanly.</param>
    /// <param name="cycles">Length in cycles of the tone.</param>
    /// <param name="volume">0 to 1.</param>
    public static byte[] Create(double frequency = 2400, int cycles = 1200, double volume = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycles);

        var samples = (int)Math.Round(SampleRate * cycles / frequency);
        var dataBytes = samples * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(stream);

        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                 // fmt chunk size
        w.Write((short)1);           // PCM
        w.Write((short)1);           // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);     // bytes per second
        w.Write((short)2);           // block align
        w.Write((short)16);          // bits per sample
        w.Write("data"u8);
        w.Write(dataBytes);

        var amplitude = short.MaxValue * Math.Clamp(volume, 0, 1);
        for (var i = 0; i < samples; i++)
            w.Write((short)Math.Round(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate)));

        w.Flush();
        return stream.ToArray();
    }
}
