using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace NextXLR.Services;

/// <summary>How a pad reacts to presses.</summary>
public enum PadMode
{
    /// <summary>Press = play, press again = stop.</summary>
    PlayStop,
    /// <summary>Press = play, press again = start over.</summary>
    PlayRestart,
    /// <summary>Plays only while held.</summary>
    Hold,
}

/// <summary>An imported audio file in the sampler library.</summary>
public sealed class SampleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Copy inside the library folder (the original may move or disappear).</summary>
    public string File { get; set; } = "";
    public double Duration { get; set; }
    /// <summary>Waveform overview: <see cref="SamplerStore.PeakCount"/> peaks, 0..255.</summary>
    public byte[] Peaks { get; set; } = [];
    public DateTime Added { get; set; } = DateTime.Now;
}

/// <summary>One pad on one page: its sample, trim points (seconds) and press behaviour.</summary>
public sealed class PadConfig
{
    public string? SampleId { get; set; }
    public double Start { get; set; }
    /// <summary>End point in seconds; 0 = end of the file.</summary>
    public double End { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PadMode Mode { get; set; } = PadMode.PlayStop;
    public double GainDb { get; set; } = 0.0;
}

/// <summary>
/// Sampler data on disk: %LOCALAPPDATA%\NextXLR\sampler\sampler.json (library + pad setup for
/// pages A–C) and the imported audio in …\sampler\samples. Imports are analysed once (duration
/// and a waveform overview), so the browser and the trim editor open instantly.
/// </summary>
public sealed class SamplerStore
{
    public const int Pages = 3, PadsPerPage = 4, PeakCount = 480;

    public static readonly string Dir = Path.Combine(ProfileLibrary.DataDir, "sampler");
    private static readonly string FilePath = Path.Combine(Dir, "sampler.json");
    public static readonly string SamplesDir = Path.Combine(Dir, "samples");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<SampleEntry> Library { get; set; } = [];
    /// <summary>[page][pad]</summary>
    public PadConfig[][] Pads { get; set; } = NewPads();

    private static PadConfig[][] NewPads() =>
        [.. Enumerable.Range(0, Pages).Select(_ => Enumerable.Range(0, PadsPerPage).Select(_ => new PadConfig()).ToArray())];

    public static SamplerStore Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<SamplerStore>(System.IO.File.ReadAllText(FilePath), Options) ?? new();
                if (s.Pads.Length != Pages || s.Pads.Any(p => p.Length != PadsPerPage)) s.Pads = NewPads();
                s.Library.RemoveAll(e => !System.IO.File.Exists(e.File));
                return s;
            }
        }
        catch { /* corrupt file: start empty */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* best effort */ }
    }

    public SampleEntry? Find(string? id) => id is null ? null : Library.FirstOrDefault(e => e.Id == id);

    public static readonly string[] Extensions = [".wav", ".mp3", ".flac", ".m4a", ".aac", ".wma"];

    /// <summary>Copies a file into the library and analyses it. Throws with a readable message.</summary>
    public async Task<SampleEntry> ImportAsync(string path)
    {
        Directory.CreateDirectory(SamplesDir);
        var entry = new SampleEntry { Name = Path.GetFileNameWithoutExtension(path) };
        entry.File = Path.Combine(SamplesDir, entry.Id + Path.GetExtension(path).ToLowerInvariant());
        System.IO.File.Copy(path, entry.File);
        try
        {
            (entry.Duration, entry.Peaks) = await AnalyzeAsync(entry.File);
        }
        catch
        {
            System.IO.File.Delete(entry.File);
            throw;
        }
        Library.Add(entry);
        Save();
        return entry;
    }

    public void Remove(SampleEntry e)
    {
        Library.Remove(e);
        foreach (var pad in Pads.SelectMany(p => p).Where(p => p.SampleId == e.Id)) pad.SampleId = null;
        try { System.IO.File.Delete(e.File); } catch { }
        Save();
    }

    /// <summary>
    /// Decodes any format Windows can read (Media Foundation) to 8 kHz mono PCM in a temp WAV and
    /// reduces it to <see cref="PeakCount"/> peaks.
    /// </summary>
    public static async Task<(double Duration, byte[] Peaks)> AnalyzeAsync(string path)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"nextxlr-{Guid.NewGuid():N}.wav");
        System.IO.File.WriteAllBytes(tmp, []);
        try
        {
            var src = await StorageFile.GetFileFromPathAsync(path);
            var dst = await StorageFile.GetFileFromPathAsync(tmp);
            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.Low);
            profile.Audio = AudioEncodingProperties.CreatePcm(8000, 1, 16);
            var prepared = await new MediaTranscoder().PrepareFileTranscodeAsync(src, dst, profile);
            if (!prepared.CanTranscode) throw new InvalidDataException(Loc.Get("SamplerUnsupported"));
            await prepared.TranscodeAsync();
            var samples = ReadPcm16(System.IO.File.ReadAllBytes(tmp));
            var peaks = new byte[PeakCount];
            if (samples.Length > 0)
            {
                for (int i = 0; i < PeakCount; i++)
                {
                    int from = (int)((long)samples.Length * i / PeakCount), to = (int)((long)samples.Length * (i + 1) / PeakCount);
                    int max = 0;
                    for (int j = from; j < Math.Max(to, from + 1) && j < samples.Length; j++) max = Math.Max(max, Math.Abs((int)samples[j]));
                    peaks[i] = (byte)Math.Min(255, max * 255 / 32768);
                }
            }
            return (samples.Length / 8000.0, peaks);
        }
        finally
        {
            try { System.IO.File.Delete(tmp); } catch { }
        }
    }

    private static short[] ReadPcm16(byte[] wav)
    {
        for (int pos = 12; pos + 8 <= wav.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data")
            {
                var n = Math.Min(size, wav.Length - pos - 8) / 2;
                var data = new short[n];
                Buffer.BlockCopy(wav, pos + 8, data, 0, n * 2);
                return data;
            }
            pos += 8 + size + (size & 1);
        }
        return [];
    }

    /// <summary>
    /// Normalizes the sample to target dBFS (default -1.0 dBFS) using 32-bit floating point PCM scaling.
    /// </summary>
    public static async Task<(bool Success, double GainDb)> NormalizeAudioAsync(SampleEntry entry, double targetDb = -1.0)
    {
        if (string.IsNullOrEmpty(entry.File) || !System.IO.File.Exists(entry.File)) return (false, 0.0);

        var tempOut = Path.Combine(Path.GetTempPath(), $"norm-{Guid.NewGuid():N}.wav");
        try
        {
            float maxPeak = 0f;
            int sampleRate;
            int channels;
            List<float> allSamples = [];

            using (var reader = new MediaFoundationReader(entry.File))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                channels = reader.WaveFormat.Channels;
                var sampleProvider = reader.ToSampleProvider();
                float[] buf = new float[sampleRate * channels];
                int read;
                while ((read = sampleProvider.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        float abs = Math.Abs(buf[i]);
                        if (abs > maxPeak) maxPeak = abs;
                        allSamples.Add(buf[i]);
                    }
                }
            }

            if (maxPeak < 0.0001f || allSamples.Count == 0) return (false, 0.0);

            float targetAmp = (float)Math.Pow(10.0, targetDb / 20.0);
            float multiplier = targetAmp / maxPeak;
            double gainDb = 20.0 * Math.Log10(multiplier);

            short[] pcm16 = new short[allSamples.Count];
            for (int i = 0; i < allSamples.Count; i++)
            {
                pcm16[i] = (short)Math.Clamp(Math.Round(allSamples[i] * multiplier * 32767.0), -32768, 32767);
            }

            WriteWav16(tempOut, pcm16, sampleRate, (short)channels);

            var targetPath = Path.ChangeExtension(entry.File, ".wav");
            if (targetPath != entry.File && System.IO.File.Exists(entry.File))
            {
                try { System.IO.File.Delete(entry.File); } catch { }
            }
            entry.File = targetPath;
            System.IO.File.Copy(tempOut, entry.File, overwrite: true);

            (entry.Duration, entry.Peaks) = await AnalyzeAsync(entry.File);
            return (true, gainDb);
        }
        catch
        {
            return (false, 0.0);
        }
        finally
        {
            try { System.IO.File.Delete(tempOut); } catch { }
        }
    }

    public static (double Start, double End) AutoTrimSilence(SampleEntry entry, double silenceThresholdDb = -36.0)
    {
        if (entry.Peaks.Length == 0 || entry.Duration <= 0) return (0, 0);

        byte thresh = (byte)Math.Clamp((int)(Math.Pow(10.0, silenceThresholdDb / 20.0) * 255.0), 3, 25);
        int startIdx = 0;
        while (startIdx < entry.Peaks.Length && entry.Peaks[startIdx] < thresh)
        {
            startIdx++;
        }

        int endIdx = entry.Peaks.Length - 1;
        while (endIdx > startIdx && entry.Peaks[endIdx] < thresh)
        {
            endIdx--;
        }

        double stepSec = entry.Duration / entry.Peaks.Length;
        double startSec = Math.Max(0.0, (startIdx * stepSec) - 0.02);
        double endSec = Math.Min(entry.Duration, ((endIdx + 1) * stepSec) + 0.04);

        return (Math.Round(startSec, 3), Math.Round(endSec, 3));
    }

    public static void WriteWav16(string filePath, short[] samples, int sampleRate = 48000, short channels = 2)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int byteRate = sampleRate * channels * 2;
        short blockAlign = (short)(channels * 2);
        int subchunk2Size = samples.Length * 2;
        int chunkSize = 36 + subchunk2Size;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(chunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(subchunk2Size);

        byte[] bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        bw.Write(bytes);
    }
}

/// <summary>
/// High-performance, low-latency audio playback voice for sampler pads (using NAudio WASAPI).
/// Pads play into the GoXLR's "Sample" playback device so they land on the Samples channel
/// and its routing matrix. Previews in the browser play into the Windows default multimedia device.
/// Completely avoids Windows System Media Transport Controls (SMTC) hidden message windows.
/// </summary>
public sealed class SamplePlayer : IDisposable
{
    private MediaFoundationReader? _reader;
    private WasapiOut? _output;
    private readonly bool _toSampleChannel;
    private readonly object _lock = new();
    private System.Threading.Timer? _trimTimer;
    private static MMDevice? _cachedSampleDevice;
    private static bool _deviceSearched;

    public event Action<bool>? PlayingChanged;
    public bool IsPlaying { get; private set; }

    public SamplePlayer(bool toSampleChannel)
    {
        _toSampleChannel = toSampleChannel;
    }

    private static MMDevice? FindSampleDevice()
    {
        if (_deviceSearched) return _cachedSampleDevice;
        _deviceSearched = true;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            _cachedSampleDevice = renderDevices.FirstOrDefault(d =>
                (d.FriendlyName.StartsWith("Sample", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("Sample", StringComparison.OrdinalIgnoreCase)) &&
                (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                 d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase)));
        }
        catch { }
        return _cachedSampleDevice;
    }

    /// <summary>Elapsed position inside the file (seconds).</summary>
    public double Position
    {
        get
        {
            lock (_lock)
            {
                return _reader?.CurrentTime.TotalSeconds ?? 0;
            }
        }
    }

    public void Play(string file, double start = 0, double end = 0, double gainDb = 0.0)
    {
        lock (_lock)
        {
            StopInternal();
            try
            {
                if (!System.IO.File.Exists(file)) return;

                _reader = new MediaFoundationReader(file);
                if (start > 0 && start < _reader.TotalTime.TotalSeconds)
                {
                    _reader.CurrentTime = TimeSpan.FromSeconds(start);
                }

                float vol = (float)Math.Pow(10.0, gainDb / 20.0);
                IWaveProvider provider = Math.Abs(gainDb) > 0.01 ? new VolumeProvider(_reader, vol) : _reader;

                MMDevice? targetDevice = null;
                if (_toSampleChannel)
                {
                    targetDevice = FindSampleDevice();
                }

                if (targetDevice != null)
                {
                    try
                    {
                        _output = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 20);
                        _output.Init(provider);
                    }
                    catch
                    {
                        _output?.Dispose();
                        _output = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 30);
                        _output.Init(provider);
                    }
                }
                else
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var defDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (defDev != null)
                    {
                        try
                        {
                            _output = new WasapiOut(defDev, AudioClientShareMode.Shared, true, 20);
                            _output.Init(provider);
                        }
                        catch
                        {
                            _output?.Dispose();
                            _output = new WasapiOut(defDev, AudioClientShareMode.Shared, false, 30);
                            _output.Init(provider);
                        }
                    }
                    else
                    {
                        _output = new WasapiOut(AudioClientShareMode.Shared, 30);
                        _output.Init(provider);
                    }
                }

                _output.PlaybackStopped += (s, e) =>
                {
                    lock (_lock)
                    {
                        SetPlaying(false);
                    }
                };

                _output.Play();
                SetPlaying(true);

                if (end > start)
                {
                    double durationSeconds = end - start;
                    _trimTimer = new System.Threading.Timer(_ =>
                    {
                        lock (_lock)
                        {
                            StopInternal();
                        }
                    }, null, TimeSpan.FromSeconds(durationSeconds), Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SamplePlayer Play error: {ex.Message}");
                StopInternal();
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _trimTimer?.Dispose();
        _trimTimer = null;

        if (_output != null)
        {
            try { _output.Stop(); } catch { }
            try { _output.Dispose(); } catch { }
            _output = null;
        }

        if (_reader != null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }

        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        PlayingChanged?.Invoke(playing);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }
}

public sealed class VolumeProvider : IWaveProvider
{
    private readonly IWaveProvider _source;
    public float Volume { get; set; } = 1.0f;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public VolumeProvider(IWaveProvider source, float volume = 1.0f)
    {
        _source = source;
        Volume = volume;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (Math.Abs(Volume - 1.0f) < 0.005f || read <= 0) return read;

        if (WaveFormat.BitsPerSample == 16)
        {
            for (int i = 0; i < read; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, offset + i);
                short scaled = (short)Math.Clamp(Math.Round(sample * Volume), short.MinValue, short.MaxValue);
                buffer[offset + i] = (byte)(scaled & 0xFF);
                buffer[offset + i + 1] = (byte)((scaled >> 8) & 0xFF);
            }
        }
        else if (WaveFormat.BitsPerSample == 32 && WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i < read; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, offset + i);
                float scaled = sample * Volume;
                byte[] bytes = BitConverter.GetBytes(scaled);
                Buffer.BlockCopy(bytes, 0, buffer, offset + i, 4);
            }
        }
        return read;
    }
}
