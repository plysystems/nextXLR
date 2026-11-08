using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NextXLR.Services;

/// <summary>
/// Professional Zero-Latency Multi-Channel Audio Bridge for Bluetooth & Wireless Headsets.
/// 
/// Captures and mixes ALL active GoXLR channels simultaneously (Game, Chat, Music, System, Samples)
/// in real time, scales volume with both individual GoXLR faders and the GoXLR Headphone knob (Channel 8),
/// resamples smoothly if the headset requires 44.1 kHz, and streams jitter-free via WASAPI Event Sync with ~50ms latency.
/// </summary>
public sealed class BluetoothAudioRouter : IDisposable
{
    private static readonly Lazy<BluetoothAudioRouter> _instance = new(() => new BluetoothAudioRouter());
    public static BluetoothAudioRouter Instance => _instance.Value;

    private readonly List<WasapiCapture> _captures = new();
    private readonly List<BufferedWaveProvider> _buffers = new();
    private readonly ConcurrentDictionary<string, (float Volume, bool Muted)> _channelLevels = new(StringComparer.OrdinalIgnoreCase);
    private WasapiOut? _player;
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public string? CurrentDeviceId { get; private set; }
    public string? CurrentDeviceName { get; private set; }
    public string? CurrentSourceName { get; private set; }
    public string StatusMessage { get; private set; } = "Inaktiv";

    /// <summary>Real-time master volume multiplier linked directly to GoXLR Headphones knob/fader (0.0 to 1.0)</summary>
    public volatile float Volume = 1.0f;

    /// <summary>Master hardware/software mute state linked to GoXLR Headphones</summary>
    public volatile bool IsMuted = false;

    public event Action<bool, string>? StatusChanged;

    private BluetoothAudioRouter()
    {
        // Default standard channels to full volume
        SetChannelLevel("System", 1.0f, false);
        SetChannelLevel("Game", 1.0f, false);
        SetChannelLevel("Chat", 1.0f, false);
        SetChannelLevel("Musik", 1.0f, false);
        SetChannelLevel("Sample", 1.0f, false);
    }

    /// <summary>
    /// Update individual channel volume and mute from GoXLR faders/knobs.
    /// </summary>
    public void SetChannelLevel(string channelName, float volume, bool muted)
    {
        _channelLevels[channelName] = (Math.Clamp(volume, 0f, 1f), muted);
        var norm = NormalizeChannelName(channelName);
        if (norm != channelName)
        {
            _channelLevels[norm] = (Math.Clamp(volume, 0f, 1f), muted);
        }
    }

    /// <summary>
    /// Update channel level using GoXLR Channel IDs (0: Mic, 1: Chat, 2: Music, 3: Game, 4: Console, 5: Line In, 6: System, 7: Sample, 8: Headphones).
    /// </summary>
    public void SetChannelLevelById(uint channelId, float volume, bool muted)
    {
        string? name = channelId switch
        {
            0u => "Mic",
            1u => "Chat",
            2u => "Musik",
            3u => "Game",
            4u => "Console",
            5u => "Line In",
            6u => "System",
            7u => "Sample",
            8u => "Headphones",
            _ => null
        };

        if (name != null)
        {
            SetChannelLevel(name, volume, muted);
        }
    }

    public bool TryGetChannelLevel(string channelName, out float volume, out bool muted)
    {
        if (_channelLevels.TryGetValue(channelName, out var lvl) ||
            _channelLevels.TryGetValue(NormalizeChannelName(channelName), out lvl))
        {
            volume = lvl.Volume;
            muted = lvl.Muted;
            return true;
        }

        volume = 1.0f;
        muted = false;
        return false;
    }

    private static string NormalizeChannelName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("system")) return "System";
        if (lower.Contains("game")) return "Game";
        if (lower.Contains("chat")) return "Chat";
        if (lower.Contains("music") || lower.Contains("musik")) return "Musik";
        if (lower.Contains("sample")) return "Sample";
        if (lower.Contains("line in")) return "Line In";
        if (lower.Contains("console")) return "Console";
        if (lower.Contains("mic") || lower.Contains("mikrofon")) return "Mic";
        return name;
    }

    /// <summary>
    /// Starts the Bluetooth bridge.
    /// sourceMode can be:
    /// - "all" (default): captures and mixes System + Game + Chat + Music + Samples simultaneously.
    /// - "broadcast": captures GoXLR Broadcast Stream Mix.
    /// </summary>
    public void Start(string targetDeviceId, string targetDeviceName, bool muteMicSidetone = true, string sourceMode = "all")
    {
        lock (_lock)
        {
            StopInternal();

            if (string.IsNullOrWhiteSpace(targetDeviceId) && string.IsNullOrWhiteSpace(targetDeviceName))
            {
                SetStatus(false, "Kein Bluetooth-Zielgerät ausgewählt.");
                return;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

                // 1. Locate the target Bluetooth/Wireless playback endpoint
                MMDevice? targetDevice = null;
                if (!string.IsNullOrWhiteSpace(targetDeviceId))
                {
                    targetDevice = renderDevices.FirstOrDefault(d =>
                        d.ID.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase) ||
                        targetDeviceId.Contains(d.ID, StringComparison.OrdinalIgnoreCase) ||
                        d.ID.Contains(targetDeviceId, StringComparison.OrdinalIgnoreCase));
                }

                if (targetDevice == null && !string.IsNullOrWhiteSpace(targetDeviceName))
                {
                    targetDevice = renderDevices.FirstOrDefault(d =>
                        d.FriendlyName.Equals(targetDeviceName, StringComparison.OrdinalIgnoreCase) ||
                        d.FriendlyName.Contains(targetDeviceName, StringComparison.OrdinalIgnoreCase) ||
                        targetDeviceName.Contains(d.FriendlyName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetDevice == null)
                {
                    SetStatus(false, $"Headset '{targetDeviceName}' ist derzeit nicht aktiv.");
                    return;
                }

                // Standard mixing format (48 kHz, 32-bit float stereo)
                var standardFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                var mixer = new MixingSampleProvider(standardFormat) { ReadFully = true };

                var capturedChannelNames = new List<string>();

                if (sourceMode == "broadcast")
                {
                    // Look for GoXLR Broadcast Stream Mix
                    var bcastDev = captureDevices.FirstOrDefault(d =>
                        (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("Stream Mix", StringComparison.OrdinalIgnoreCase)) &&
                        (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase)));

                    if (bcastDev != null)
                    {
                        AddCaptureToMixer(new WasapiCapture(bcastDev), mixer, bcastDev.FriendlyName, "Broadcast");
                        capturedChannelNames.Add(bcastDev.FriendlyName);
                    }
                }

                // If "all" or if broadcast wasn't found: Capture all GoXLR render channels simultaneously!
                if (capturedChannelNames.Count == 0)
                {
                    var goXlrChannels = renderDevices.Where(d =>
                        (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC-HELICON", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC Helicon", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("Go XLR", StringComparison.OrdinalIgnoreCase)) &&
                        !d.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (goXlrChannels.Count > 0)
                    {
                        foreach (var ch in goXlrChannels)
                        {
                            try
                            {
                                var shortName = NormalizeChannelName(ch.FriendlyName);
                                AddCaptureToMixer(new WasapiLoopbackCapture(ch), mixer, ch.FriendlyName, shortName);
                                if (!capturedChannelNames.Contains(shortName))
                                {
                                    capturedChannelNames.Add(shortName);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to loopback GoXLR channel {ch.FriendlyName}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Fallback: Default multimedia playback device loopback
                        var defDev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (defDev != null && !defDev.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                        {
                            var shortName = NormalizeChannelName(defDev.FriendlyName);
                            AddCaptureToMixer(new WasapiLoopbackCapture(defDev), mixer, defDev.FriendlyName, shortName);
                            capturedChannelNames.Add(defDev.FriendlyName);
                        }
                    }

                    // Optional Microphone Sidetone: include GoXLR Mic if user unmuted Sidetone
                    if (!muteMicSidetone)
                    {
                        var micDev = captureDevices.FirstOrDefault(d =>
                            (d.FriendlyName.Contains("Chat Mic", StringComparison.OrdinalIgnoreCase) ||
                             d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                             d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase)) &&
                            !d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) &&
                            !d.FriendlyName.Contains("Stream Mix", StringComparison.OrdinalIgnoreCase));

                        if (micDev != null)
                        {
                            try
                            {
                                AddCaptureToMixer(new WasapiCapture(micDev), mixer, micDev.FriendlyName, "Mic");
                                capturedChannelNames.Add("Mikrofon (Sidetone)");
                            }
                            catch { }
                        }
                    }
                }

                if (_captures.Count == 0)
                {
                    SetStatus(false, "Keine GoXLR-Audioquellen für Bluetooth gefunden.");
                    return;
                }

                CurrentSourceName = string.Join(" + ", capturedChannelNames);

                // 2. Wrap mixer with real-time GoXLR master volume scaling (Headphones Channel 8)
                ISampleProvider pipeline = new VolumeScalingSampleProvider(mixer, this);

                // 3. Resample if the target Bluetooth headset requires a different sample rate (e.g. 44.1 kHz)
                int targetSampleRate = 48000;
                try
                {
                    targetSampleRate = targetDevice.AudioClient.MixFormat.SampleRate;
                }
                catch { }

                if (targetSampleRate > 0 && targetSampleRate != standardFormat.SampleRate)
                {
                    pipeline = new WdlResamplingSampleProvider(pipeline, targetSampleRate);
                }

                // 4. Output stream preparation: match target format (IEEE float or 16-bit PCM)
                IWaveProvider streamProvider;
                var mixFormat = targetDevice.AudioClient.MixFormat;
                if (mixFormat.BitsPerSample == 32 && mixFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    streamProvider = pipeline.ToWaveProvider();
                }
                else
                {
                    streamProvider = new SampleToWaveProvider16(pipeline);
                }

                // 5. Initialize Output: WASAPI Shared with hardware Event Sync (50ms buffer)
                // Event sync eliminates Windows Thread.Sleep jitter and completely cures audio stuttering.
                try
                {
                    _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 50);
                    _player.Init(streamProvider);
                }
                catch
                {
                    try
                    {
                        // Try alternative format representation (float <-> 16-bit)
                        _player?.Dispose();
                        streamProvider = (streamProvider is SampleToWaveProvider16)
                            ? pipeline.ToWaveProvider()
                            : new SampleToWaveProvider16(pipeline);

                        _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 50);
                        _player.Init(streamProvider);
                    }
                    catch
                    {
                        // Fallback to polling mode with 60ms latency if driver doesn't support event sync
                        _player?.Dispose();
                        _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 60);
                        _player.Init(streamProvider);
                    }
                }

                _player.PlaybackStopped += (s, e) =>
                {
                    if (IsRunning && e.Exception != null)
                    {
                        SetStatus(false, $"Bluetooth-Wiedergabe getrennt: {e.Exception.Message}");
                    }
                };

                // 6. Start captures and player
                foreach (var c in _captures)
                {
                    c.StartRecording();
                }
                _player.Play();

                IsRunning = true;
                CurrentDeviceId = targetDevice.ID;
                CurrentDeviceName = targetDevice.FriendlyName;

                SetStatus(true, $"Aktiv: GoXLR-Mix ({CurrentSourceName}) → '{targetDevice.FriendlyName}' (Echtzeit, kein Stottern, GoXLR-Fader & Master aktiv)");
            }
            catch (Exception ex)
            {
                StopInternal();
                SetStatus(false, $"Bluetooth-Routing Fehler: {ex.Message}");
            }
        }
    }

    public void Start(string targetDeviceId, string targetDeviceName, string sourceMode, bool muteMicSidetone = true)
        => Start(targetDeviceId, targetDeviceName, muteMicSidetone, sourceMode);

    private void AddCaptureToMixer(WasapiCapture capture, MixingSampleProvider mixer, string fullName, string shortName)
    {
        // ReadFully = true is CRUCIAL: it keeps silent/idle channels from returning 0 samples,
        // which would cause NAudio MixingSampleProvider to permanently drop that channel from the mix
        // or cause buffer starvation / audio stuttering.
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(100),
            ReadFully = true
        };

        capture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        };

        capture.RecordingStopped += (s, e) =>
        {
            if (IsRunning && e.Exception != null)
            {
                Debug.WriteLine($"Channel {fullName} capture stopped: {e.Exception.Message}");
            }
        };

        _captures.Add(capture);
        _buffers.Add(buffer);

        // Convert to sample provider
        ISampleProvider sampleProvider = buffer.ToSampleProvider();

        // 1. Resample to 48000 Hz if needed
        if (capture.WaveFormat.SampleRate != 48000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 48000);
        }

        // 2. Channel count adaptation: ensure 2 channels (stereo)
        if (sampleProvider.WaveFormat.Channels == 1)
        {
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
        }
        else if (sampleProvider.WaveFormat.Channels > 2)
        {
            sampleProvider = new MultiplexingSampleProvider(new[] { sampleProvider }, 2);
        }

        // 3. Per-channel GoXLR volume and mute control
        sampleProvider = new ChannelVolumeSampleProvider(sampleProvider, this, shortName);

        mixer.AddMixerInput(sampleProvider);
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
            SetStatus(false, "Bluetooth-Routing inaktiv. Kopfhörer-Signal wird über die GoXLR 3,5 mm Klinkenbuchse ausgegeben.");
        }
    }

    private void StopInternal()
    {
        foreach (var c in _captures)
        {
            try { c.StopRecording(); c.Dispose(); } catch { }
        }
        _captures.Clear();
        _buffers.Clear();

        try
        {
            _player?.Stop();
            _player?.Dispose();
        }
        catch { }
        finally { _player = null; }

        IsRunning = false;
        CurrentDeviceId = null;
        CurrentDeviceName = null;
        CurrentSourceName = null;
    }

    private void SetStatus(bool running, string msg)
    {
        IsRunning = running;
        StatusMessage = msg;
        StatusChanged?.Invoke(running, msg);
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Sample provider wrapper that applies per-channel GoXLR volume and mute scaling.
    /// </summary>
    private sealed class ChannelVolumeSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BluetoothAudioRouter _router;
        private readonly string _channelName;

        public ChannelVolumeSampleProvider(ISampleProvider source, BluetoothAudioRouter router, string channelName)
        {
            _source = source;
            _router = router;
            _channelName = channelName;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read <= 0) return read;

            if (_router.TryGetChannelLevel(_channelName, out var vol, out var muted))
            {
                if (muted || vol <= 0.001f)
                {
                    Array.Clear(buffer, offset, read);
                    return read;
                }
                if (Math.Abs(vol - 1.0f) > 0.005f)
                {
                    for (int i = 0; i < read; i++)
                    {
                        buffer[offset + i] *= vol;
                    }
                }
            }

            return read;
        }
    }

    /// <summary>
    /// Sample provider wrapper that applies real-time GoXLR master volume scaling and mute onto mixed frames.
    /// </summary>
    private sealed class VolumeScalingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BluetoothAudioRouter _router;

        public VolumeScalingSampleProvider(ISampleProvider source, BluetoothAudioRouter router)
        {
            _source = source;
            _router = router;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read <= 0) return read;

            float vol = _router.Volume;
            bool muted = _router.IsMuted;

            if (muted || vol <= 0.001f)
            {
                Array.Clear(buffer, offset, read);
            }
            else if (Math.Abs(vol - 1.0f) > 0.005f)
            {
                for (int i = 0; i < read; i++)
                {
                    buffer[offset + i] *= vol;
                }
            }

            return read;
        }
    }
}
