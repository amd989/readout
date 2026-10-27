using Readout.Core.Feedback;

namespace Readout.Services;

/// <summary>A continuous beep that plays until stopped, like a meter's continuity beeper.</summary>
public interface IContinuityTone
{
    void Play();
    void Silence();
}

#if IOS || MACCATALYST
/// <summary>Loops a short generated tone with AVAudioPlayer. Ambient: it follows the silent switch and mixes with other audio.</summary>
public sealed class ContinuityTone : IContinuityTone, IDisposable
{
    private AVFoundation.AVAudioPlayer? _player;

    public void Play()
    {
        if (_player is null)
        {
            AVFoundation.AVAudioSession.SharedInstance().SetCategory(AVFoundation.AVAudioSessionCategory.Ambient);
            _player = AVFoundation.AVAudioPlayer.FromData(Foundation.NSData.FromArray(ToneWave.Create()));
            if (_player is null) return;
            _player.NumberOfLoops = -1;
            _player.PrepareToPlay();
        }
        _player.CurrentTime = 0;
        _player.Play();
    }

    public void Silence() => _player?.Stop();

    public void Dispose() => _player?.Dispose();
}
#elif ANDROID
/// <summary>Loops a short generated tone with a static AudioTrack (sonification, so it respects the system's sound settings).</summary>
public sealed class ContinuityTone : IContinuityTone, IDisposable
{
    private Android.Media.AudioTrack? _track;
    private int _frames;

    public void Play()
    {
        if (_track is null)
        {
            var wav = ToneWave.Create();
            var pcm = wav[44..];
            _frames = pcm.Length / 2;
            _track = new Android.Media.AudioTrack.Builder()
                .SetAudioAttributes(new Android.Media.AudioAttributes.Builder()
                    .SetUsage(Android.Media.AudioUsageKind.AssistanceSonification)!
                    .SetContentType(Android.Media.AudioContentType.Sonification)!
                    .Build()!)
                .SetAudioFormat(new Android.Media.AudioFormat.Builder()
                    .SetEncoding(Android.Media.Encoding.Pcm16bit)!
                    .SetSampleRate(ToneWave.SampleRate)!
                    .SetChannelMask(Android.Media.ChannelOut.Mono)!
                    .Build()!)
                .SetTransferMode(Android.Media.AudioTrackMode.Static)
                .SetBufferSizeInBytes(pcm.Length)
                .Build();
            _track.Write(pcm, 0, pcm.Length);
            _track.SetLoopPoints(0, _frames, -1);
        }
        _track.Play();
    }

    public void Silence()
    {
        if (_track is null || _track.PlayState != Android.Media.PlayState.Playing) return;
        _track.Pause();
        _track.SetPlaybackHeadPosition(0);
        _track.SetLoopPoints(0, _frames, -1);
    }

    public void Dispose() => _track?.Release();
}
#else
/// <summary>No beep on this platform yet (Windows).</summary>
public sealed class ContinuityTone : IContinuityTone
{
    public void Play() { }
    public void Silence() { }
}
#endif
