using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NextXLR.Services;

/// <summary>
/// Plays a clean 1000 Hz sine wave censor tone while the Bleep button is held in the app.
/// Uses Win32 winmm.dll PlaySound for zero-dependency, low-latency playback on the default audio endpoint.
/// </summary>
public static class BleepSound
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_LOOP = 0x0008;
    private const uint SND_PURGE = 0x0040;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private static byte[] _waveBytes;
    private static readonly object Lock = new();
    private static bool _isPlaying;
    private static double _volume = 1.0;

    static BleepSound()
    {
        _waveBytes = Create1KhzSineWave(1.0, 1.0);
    }

    public static double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            lock (Lock)
            {
                if (Math.Abs(_volume - clamped) < 0.001) return;
                _volume = clamped;
                _waveBytes = Create1KhzSineWave(1.0, _volume);
                if (_isPlaying && _volume > 0.001)
                {
                    try
                    {
                        PlaySound(_waveBytes, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_LOOP | SND_NODEFAULT);
                    }
                    catch { }
                }
                else if (_isPlaying && _volume <= 0.001)
                {
                    try { PlaySound(null, IntPtr.Zero, SND_PURGE); } catch { }
                }
            }
        }
    }

    public static void Start()
    {
        lock (Lock)
        {
            if (_isPlaying) return;
            _isPlaying = true;
            try
            {
                if (_volume > 0.001)
                {
                    PlaySound(_waveBytes, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_LOOP | SND_NODEFAULT);
                }
            }
            catch { }
        }
    }

    public static void Stop()
    {
        lock (Lock)
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            try
            {
                PlaySound(null, IntPtr.Zero, SND_PURGE);
            }
            catch { }
        }
    }

    private static byte[] Create1KhzSineWave(double durationSeconds, double volume)
    {
        const int sampleRate = 44100;
        const int frequency = 1000;
        // 44.1 samples per cycle: 441 samples = exactly 10 cycles, zero click/pop on loop
        int numSamples = (int)(sampleRate * durationSeconds);
        numSamples = (numSamples / 441) * 441;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + numSamples * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);             // Subchunk1Size (16 for PCM)
            writer.Write((short)1);        // AudioFormat (1 = PCM)
            writer.Write((short)1);        // NumChannels (1 = Mono)
            writer.Write(sampleRate);     // SampleRate
            writer.Write(sampleRate * 2); // ByteRate (SampleRate * NumChannels * BitsPerSample/8)
            writer.Write((short)2);        // BlockAlign (NumChannels * BitsPerSample/8)
            writer.Write((short)16);       // BitsPerSample (16 bits)
            writer.Write("data"u8);
            writer.Write(numSamples * 2); // Subchunk2Size

            // Amplitude scaled by volume (max ~16000, -6 dBFS)
            double amplitude = Math.Clamp(16000.0 * volume, 0.0, 32767.0);
            const double twoPiFreq = 2.0 * Math.PI * frequency;
            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                short sample = (short)(Math.Sin(twoPiFreq * t) * amplitude);
                writer.Write(sample);
            }
        }
        return stream.ToArray();
    }
}
