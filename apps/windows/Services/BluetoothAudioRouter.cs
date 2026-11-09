using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    private readonly List<RealtimeJitterBuffer> _jitterBuffers = new();
    private readonly ConcurrentDictionary<string, (float Volume, bool Muted)> _channelLevels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _channelRouting = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _muteMicSidetone = true;
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    private WasapiOut? _player;
    private readonly object _lock = new();
    private System.Threading.Timer? _reconnectTimer;
    private string? _lastTargetDeviceId;
    private string? _lastTargetDeviceName;
    private bool _lastMuteMicSidetone;
    private string _lastSourceMode = "all";

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

    /// <summary>
    /// Checks whether this channel is enabled in the Wireless Kopfhörer routing matrix.
    /// </summary>
    public bool IsChannelRouted(string channelName)
    {
        var norm = NormalizeChannelName(channelName);
        if (norm.Equals("Mic", StringComparison.OrdinalIgnoreCase) ||
            norm.Equals("Mikrofon", StringComparison.OrdinalIgnoreCase))
        {
            if (_muteMicSidetone)
            {
                return false;
            }
        }

        if (_channelRouting.TryGetValue(norm, out var routed))
        {
            return routed;
        }
        if (_channelRouting.TryGetValue(channelName, out routed))
        {
            return routed;
        }

        return AppSettings.Load().IsWirelessChannelRouted(norm);
    }

    /// <summary>
    /// Updates the routing state for a channel on the Wireless Kopfhörer output.
    /// </summary>
    public void SetChannelRouted(string channelName, bool isRouted)
    {
        var norm = NormalizeChannelName(channelName);
        _channelRouting[norm] = isRouted;
        _channelRouting[channelName] = isRouted;
    }

    private static string NormalizeChannelName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("mic") || lower.Contains("mikrofon")) return "Mic";
        if (lower.Contains("system")) return "System";
        if (lower.Contains("game")) return "Game";
        if (lower.Contains("chat")) return "Chat";
        if (lower.Contains("music") || lower.Contains("musik")) return "Musik";
        if (lower.Contains("sample")) return "Sample";
        if (lower.Contains("line in")) return "Line In";
        if (lower.Contains("console")) return "Console";
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
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;

            _lastTargetDeviceId = targetDeviceId;
            _lastTargetDeviceName = targetDeviceName;
            _lastMuteMicSidetone = muteMicSidetone;
            _lastSourceMode = sourceMode;

            _muteMicSidetone = muteMicSidetone;

            var settings = AppSettings.Load();
            if (settings.WirelessRouting != null)
            {
                foreach (var kvp in settings.WirelessRouting)
                {
                    SetChannelRouted(kvp.Key, kvp.Value);
                }
            }

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
                    SetStatus(false, $"Headset '{targetDeviceName}' ist derzeit nicht aktiv. Warte auf Verbindung...");
                    ScheduleReconnect();
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
                        AddCaptureToMixer(new WasapiCapture(bcastDev, false, 20), mixer, bcastDev.FriendlyName, "Broadcast");
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
                        !d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) &&
                        !d.FriendlyName.Contains("Stream Mix", StringComparison.OrdinalIgnoreCase) &&
                        !d.FriendlyName.Contains("Mic", StringComparison.OrdinalIgnoreCase) &&
                        !d.FriendlyName.Contains("Mikrofon", StringComparison.OrdinalIgnoreCase) &&
                        !d.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (goXlrChannels.Count > 0)
                    {
                        foreach (var ch in goXlrChannels)
                        {
                            try
                            {
                                var shortName = NormalizeChannelName(ch.FriendlyName);
                                AddCaptureToMixer(new LowLatencyWasapiLoopbackCapture(ch, 20), mixer, ch.FriendlyName, shortName);
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
                            AddCaptureToMixer(new LowLatencyWasapiLoopbackCapture(defDev, 20), mixer, defDev.FriendlyName, shortName);
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
                                AddCaptureToMixer(new WasapiCapture(micDev, false, 20), mixer, micDev.FriendlyName, "Mic");
                                capturedChannelNames.Add("Mikrofon (Sidetone)");
                            }
                            catch { }
                        }
                    }
                }

                if (_captures.Count == 0)
                {
                    SetStatus(false, "Keine GoXLR-Audioquellen für externe Ausgabe gefunden.");
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
                bool isFloat = mixFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                               (mixFormat is WaveFormatExtensible we && we.SubFormat == SubtypeIeeeFloat);

                if (isFloat && mixFormat.BitsPerSample == 32)
                {
                    streamProvider = pipeline.ToWaveProvider();
                }
                else
                {
                    streamProvider = new SampleToWaveProvider16(pipeline);
                }

                // 5. Initialize Output: WASAPI Shared with hardware Event Sync (35ms buffer for low latency)
                // Event sync provides low hardware latency; if the target device doesn't support event sync,
                // fallback to low-latency polling mode with 40ms buffer.
                int bufferLatencyMs = 35;
                try
                {
                    _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, bufferLatencyMs);
                    _player.Init(streamProvider);
                }
                catch
                {
                    try
                    {
                        _player?.Dispose();
                        _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 40);
                        _player.Init(streamProvider);
                    }
                    catch
                    {
                        _player?.Dispose();
                        var fallbackProvider = (streamProvider is SampleToWaveProvider16)
                            ? pipeline.ToWaveProvider()
                            : new SampleToWaveProvider16(pipeline);

                        _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 50);
                        _player.Init(fallbackProvider);
                    }
                }

                _player.PlaybackStopped += (s, e) =>
                {
                    if (IsRunning && e.Exception != null)
                    {
                        SetStatus(false, $"Externe Wiedergabe getrennt: {e.Exception.Message}. Wiederverbindung läuft...");
                        ScheduleReconnect();
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

                SetStatus(true, $"Aktiv: GoXLR-Mix ({CurrentSourceName}) → '{targetDevice.FriendlyName}' (Kristallklar, minimale Latenz, GoXLR-Fader & Master aktiv)");
            }
            catch (Exception ex)
            {
                StopInternal();
                SetStatus(false, $"Externe Ausgabe Fehler: {ex.Message}");
                ScheduleReconnect();
            }
        }
    }

    private void ScheduleReconnect()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_lastTargetDeviceId) && string.IsNullOrEmpty(_lastTargetDeviceName)) return;
            _reconnectTimer?.Dispose();
            _reconnectTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var settings = AppSettings.Load();
                    if (!settings.EnableBluetoothHeadset) return;

                    using var enumerator = new MMDeviceEnumerator();
                    var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                    var found = renderDevices.Any(d =>
                        (!string.IsNullOrEmpty(_lastTargetDeviceId) && d.ID.Equals(_lastTargetDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(_lastTargetDeviceName) && d.FriendlyName.Contains(_lastTargetDeviceName, StringComparison.OrdinalIgnoreCase)));

                    if (found)
                    {
                        Start(_lastTargetDeviceId ?? "", _lastTargetDeviceName ?? "", _lastMuteMicSidetone, _lastSourceMode);
                    }
                }
                catch { }
            }, null, 3000, 3000);
        }
    }

    public void Start(string targetDeviceId, string targetDeviceName, string sourceMode, bool muteMicSidetone = true)
        => Start(targetDeviceId, targetDeviceName, muteMicSidetone, sourceMode);

    private void AddCaptureToMixer(WasapiCapture capture, MixingSampleProvider mixer, string fullName, string shortName)
    {
        // Real-time jitter buffer with active max-latency pruning (40ms) and 15ms initial cushion
        // completely eliminating delay while guaranteeing stable, glitch-free audio output.
        var jitterBuffer = new RealtimeJitterBuffer(capture.WaveFormat, maxLatencyMs: 40, prebufferMs: 15);

        capture.DataAvailable += (s, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                jitterBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded, capture.WaveFormat);
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
        _jitterBuffers.Add(jitterBuffer);

        ISampleProvider sampleProvider = jitterBuffer;

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

        // 3. Per-channel GoXLR volume, mute, and Wireless Routing control
        sampleProvider = new ChannelVolumeSampleProvider(sampleProvider, this, shortName);

        mixer.AddMixerInput(sampleProvider);
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
            SetStatus(false, "Externe Ausgabe inaktiv. Kopfhörer-Signal wird über die GoXLR 3,5 mm Klinkenbuchse ausgegeben.");
        }
    }

    private void StopInternal()
    {
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;

        foreach (var c in _captures)
        {
            try { c.StopRecording(); c.Dispose(); } catch { }
        }
        _captures.Clear();
        _jitterBuffers.Clear();

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
    /// Sample provider wrapper that applies per-channel GoXLR volume, mute, and Routing matrix state.
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

            // 1. Check if this channel is routed to Wireless Kopfhörer in the Routing Matrix
            if (!_router.IsChannelRouted(_channelName))
            {
                Array.Clear(buffer, offset, read);
                return read;
            }

            // 2. Scale with individual GoXLR channel fader volume and mute
            if (_router.TryGetChannelLevel(_channelName, out var vol, out var muted))
            {
                if (muted || vol <= 0.001f)
                {
                    Array.Clear(buffer, offset, read);
                    return read;
                }
                if (Math.Abs(vol - 1.0f) > 0.005f)
                {
                    ScaleSamples(buffer.AsSpan(offset, read), vol);
                }
            }

            return read;
        }
    }

    /// <summary>
    /// High-performance WASAPI Loopback Capture with low latency (20ms instead of NAudio default 100ms).
    /// </summary>
    public sealed class LowLatencyWasapiLoopbackCapture : WasapiCapture
    {
        public LowLatencyWasapiLoopbackCapture(MMDevice device, int bufferMilliseconds = 20)
            : base(device, false, bufferMilliseconds)
        {
        }

        protected override AudioClientStreamFlags GetAudioClientStreamFlags()
        {
            return AudioClientStreamFlags.Loopback;
        }
    }

    /// <summary>
    /// Jitter buffer optimized for external outputs (Bluetooth, 2.4GHz wireless headsets, USB DACs).
    /// Features low-latency pre-buffering (15ms cushion) and strict 40ms max-lag cap
    /// with frame-aligned boundary clipping to preserve stereo imaging and sub-second responsiveness.
    /// </summary>
    public sealed class RealtimeJitterBuffer : ISampleProvider
    {
        private readonly float[] _ringBuffer;
        private readonly int _capacity;
        private readonly int _maxTargetLatencySamples;
        private readonly int _prebufferSamples;
        private int _writePos;
        private int _readPos;
        private int _bufferedSamples;
        private bool _isBuffering = true;
        private readonly object _lock = new();

        public WaveFormat WaveFormat { get; }

        public RealtimeJitterBuffer(WaveFormat format, int maxLatencyMs = 40, int prebufferMs = 15)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            int samplesPerMs = Math.Max((format.SampleRate * format.Channels) / 1000, 1);
            _maxTargetLatencySamples = Math.Max(samplesPerMs * maxLatencyMs, 480);
            _prebufferSamples = Math.Max(samplesPerMs * prebufferMs, 192); // 15ms initial cushion
            _capacity = samplesPerMs * 400; // 400ms ring buffer capacity
            _ringBuffer = new float[_capacity];
        }

        public void AddSamples(byte[] buffer, int offset, int count, WaveFormat inputFormat)
        {
            if (count <= 0) return;

            lock (_lock)
            {
                int samplesToAdd;
                ReadOnlySpan<byte> byteSpan = buffer.AsSpan(offset, count);

                if ((inputFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                     (inputFormat is WaveFormatExtensible we && we.SubFormat == SubtypeIeeeFloat)) && inputFormat.BitsPerSample == 32)
                {
                    ReadOnlySpan<float> floatSpan = MemoryMarshal.Cast<byte, float>(byteSpan);
                    samplesToAdd = floatSpan.Length;
                    PruneExcess(samplesToAdd);

                    if (_writePos + samplesToAdd <= _capacity)
                    {
                        floatSpan.CopyTo(_ringBuffer.AsSpan(_writePos));
                        _writePos = (_writePos + samplesToAdd) % _capacity;
                    }
                    else
                    {
                        int first = _capacity - _writePos;
                        floatSpan[..first].CopyTo(_ringBuffer.AsSpan(_writePos));
                        floatSpan[first..].CopyTo(_ringBuffer.AsSpan(0));
                        _writePos = samplesToAdd - first;
                    }
                }
                else if (inputFormat.BitsPerSample == 16)
                {
                    ReadOnlySpan<short> shortSpan = MemoryMarshal.Cast<byte, short>(byteSpan);
                    samplesToAdd = shortSpan.Length;
                    PruneExcess(samplesToAdd);
                    const float inv32768 = 1.0f / 32768.0f;
                    for (int i = 0; i < samplesToAdd; i++)
                    {
                        _ringBuffer[_writePos] = shortSpan[i] * inv32768;
                        _writePos = (_writePos + 1) % _capacity;
                    }
                }
                else
                {
                    return;
                }

                _bufferedSamples += samplesToAdd;

                // Stop pre-buffering once initial cushion is reached
                if (_isBuffering && _bufferedSamples >= _prebufferSamples)
                {
                    _isBuffering = false;
                }
            }
        }

        private void PruneExcess(int incoming)
        {
            if (_bufferedSamples + incoming > _maxTargetLatencySamples)
            {
                int excess = (_bufferedSamples + incoming) - _maxTargetLatencySamples;
                int channels = Math.Max(WaveFormat.Channels, 1);
                excess = (excess / channels) * channels; // preserve stereo frame boundaries!
                if (excess > 0)
                {
                    _readPos = (_readPos + excess) % _capacity;
                    _bufferedSamples -= excess;
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_bufferedSamples <= 0)
                {
                    _isBuffering = true;
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                if (_isBuffering)
                {
                    if (_bufferedSamples < _prebufferSamples)
                    {
                        Array.Clear(buffer, offset, count);
                        return count;
                    }
                    _isBuffering = false;
                }

                int toRead = Math.Min(count, _bufferedSamples);
                int channels = Math.Max(WaveFormat.Channels, 1);
                toRead = (toRead / channels) * channels;

                if (toRead <= 0)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                if (_readPos + toRead <= _capacity)
                {
                    Array.Copy(_ringBuffer, _readPos, buffer, offset, toRead);
                    _readPos = (_readPos + toRead) % _capacity;
                }
                else
                {
                    int first = _capacity - _readPos;
                    Array.Copy(_ringBuffer, _readPos, buffer, offset, first);
                    Array.Copy(_ringBuffer, 0, buffer, offset + first, toRead - first);
                    _readPos = toRead - first;
                }
                _bufferedSamples -= toRead;

                if (toRead < count)
                {
                    Array.Clear(buffer, offset + toRead, count - toRead);
                }

                return count;
            }
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
                ScaleSamples(buffer.AsSpan(offset, read), vol);
            }

            // Transparent soft-limiting to eliminate digital clipping and harshness
            ApplySoftLimit(buffer.AsSpan(offset, read));

            return read;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplySoftLimit(Span<float> span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                float s = span[i];
                if (s > 0.98f)
                {
                    span[i] = 0.98f + 0.02f * (float)Math.Tanh((s - 0.98f) / 0.02f);
                }
                else if (s < -0.98f)
                {
                    span[i] = -0.98f + 0.02f * (float)Math.Tanh((s + 0.98f) / 0.02f);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ScaleSamples(Span<float> span, float vol)
    {
        if (Vector.IsHardwareAccelerated && span.Length >= Vector<float>.Count)
        {
            var vVol = new Vector<float>(vol);
            int i = 0;
            int limit = span.Length - Vector<float>.Count;
            while (i <= limit)
            {
                var v = new Vector<float>(span.Slice(i));
                (v * vVol).CopyTo(span.Slice(i));
                i += Vector<float>.Count;
            }
            for (; i < span.Length; i++) span[i] *= vol;
        }
        else
        {
            for (int i = 0; i < span.Length; i++) span[i] *= vol;
        }
    }
}
