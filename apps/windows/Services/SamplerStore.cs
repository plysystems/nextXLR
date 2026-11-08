using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
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
}

/// <summary>
/// One playback voice (MediaPlayer). Pads play into the GoXLR's "Sample" playback device, so
/// they land on the Samples channel and its routing like on the original sampler; previews in
/// the browser use the Windows default device.
/// </summary>
public sealed class SamplePlayer : IDisposable
{
    private readonly MediaPlayer _player = new() { AutoPlay = false };
    private static Task<DeviceInformation?>? _sampleDevice;

    public event Action<bool>? PlayingChanged;
    public bool IsPlaying { get; private set; }

    public SamplePlayer(bool toSampleChannel)
    {
        _player.MediaEnded += (_, _) => SetPlaying(false);
        _player.MediaFailed += (_, _) => SetPlaying(false);
        if (toSampleChannel) _ = RouteToSampleChannel();
    }

    private async Task RouteToSampleChannel()
    {
        _sampleDevice ??= FindSampleDevice();
        if (await _sampleDevice is { } d) _player.AudioDevice = d;
    }

    private static async Task<DeviceInformation?> FindSampleDevice()
    {
        try
        {
            var all = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            return all.FirstOrDefault(d => d.Name.StartsWith("Sample", StringComparison.OrdinalIgnoreCase)
                                           && d.Name.Contains("GoXLR", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    /// <summary>Elapsed position inside the file (seconds).</summary>
    public double Position => _player.PlaybackSession?.Position.TotalSeconds ?? 0;

    public void Play(string file, double start = 0, double end = 0)
    {
        var source = MediaSource.CreateFromUri(new Uri(file));
        var from = TimeSpan.FromSeconds(Math.Max(0, start));
        _player.Source = end > start
            ? new MediaPlaybackItem(source, from, TimeSpan.FromSeconds(end - start))
            : new MediaPlaybackItem(source, from);
        _player.Play();
        SetPlaying(true);
    }

    public void Stop()
    {
        if (!IsPlaying) return;
        _player.Pause();
        _player.Source = null;
        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        PlayingChanged?.Invoke(playing);
    }

    public void Dispose() => _player.Dispose();
}
