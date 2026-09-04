using System.IO;
using System.Media;

namespace K162.App.Services;

/// <summary>
/// The alert ping: a 0.4s 880Hz sine with exponential decay, synthesized once as an
/// in-memory WAV (matching the prototype's WebAudio oscillator). Kill alerts play it twice.
/// </summary>
public sealed class SoundService
{
    private readonly SoundPlayer _ping;
    public Func<bool> SoundEnabled { get; set; } = () => true;

    public SoundService()
    {
        _ping = new SoundPlayer(BuildPingWav(frequency: 880, seconds: 0.4, amplitude: 0.12));
        _ping.Load();
    }

    public void Ping()
    {
        if (!SoundEnabled()) return;
        try { _ping.Play(); } catch (Exception) { /* audio device issues are non-fatal */ }
    }

    public async void DoublePing()
    {
        Ping();
        await Task.Delay(250);
        Ping();
    }

    private static MemoryStream BuildPingWav(double frequency, double seconds, double amplitude)
    {
        const int sampleRate = 44100;
        var sampleCount = (int)(sampleRate * seconds);
        var dataSize = sampleCount * 2;
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + dataSize); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataSize);
            for (var i = 0; i < sampleCount; i++)
            {
                var t = (double)i / sampleRate;
                // gain 0.12 → 0.001 exponential ramp over the duration
                var envelope = amplitude * Math.Pow(0.001 / amplitude, t / seconds);
                var sample = Math.Sin(2 * Math.PI * frequency * t) * envelope;
                w.Write((short)(sample * short.MaxValue));
            }
        }
        ms.Position = 0;
        return ms;
    }
}
