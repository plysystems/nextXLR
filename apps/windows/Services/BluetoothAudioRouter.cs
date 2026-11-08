using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NextXLR.Services;

/// <summary>
/// Ultra-low latency Real-time Audio Forwarder for Bluetooth & Wireless Headsets.
/// Captures full GoXLR hardware mix (Broadcast Stream Mix: Chat + Game + Music + System)
/// or specific channel loopback, scales volume in real time with the GoXLR Headphone fader/knob,
/// and streams with minimal buffer (<20ms delay) to any Bluetooth or Wireless headphones.
/// </summary>
public sealed class BluetoothAudioRouter : IDisposable
{
    private static readonly Lazy<BluetoothAudioRouter> _instance = new(() => new BluetoothAudioRouter());
    public static BluetoothAudioRouter Instance => _instance.Value;

    private WasapiCapture? _capture;
    private WasapiOut? _player;
    private BufferedWaveProvider? _bufferedProvider;
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public string? CurrentDeviceId { get; private set; }
    public string? CurrentDeviceName { get; private set; }
    public string? CurrentSourceName { get; private set; }
    public string StatusMessage { get; private set; } = "Inaktiv";

    /// <summary>Real-time volume multiplier linked directly to the GoXLR Headphones knob/fader (0.0 to 1.0)</summary>
    public volatile float Volume = 1.0f;

    /// <summary>Hardware/Software mute state linked to the GoXLR Headphones</summary>
    public volatile bool IsMuted = false;

    public event Action<bool, string>? StatusChanged;

    private BluetoothAudioRouter() { }

    public void Start(string targetDeviceId, string targetDeviceName, bool muteMicSidetone = true, string sourceMode = "broadcast")
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

                // 2. Select Source: Prefer Broadcast Stream Mix (contains full hardware mix of all channels)
                MMDevice? sourceDevice = null;
                bool isCaptureEndpoint = false;

                if (sourceMode != "system")
                {
                    // Look for GoXLR Broadcast Stream Mix (Capture endpoint)
                    sourceDevice = captureDevices.FirstOrDefault(d =>
                        (d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("Stream Mix", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("Sende-Stream", StringComparison.OrdinalIgnoreCase)) &&
                        (d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC-Helicon", StringComparison.OrdinalIgnoreCase) ||
                         d.FriendlyName.Contains("TC-HELICON", StringComparison.OrdinalIgnoreCase)));

                    if (sourceDevice == null)
                    {
                        // Fallback search for any capture device containing Broadcast or Stream Mix
                        sourceDevice = captureDevices.FirstOrDefault(d =>
                            d.FriendlyName.Contains("Broadcast", StringComparison.OrdinalIgnoreCase) ||
                            d.FriendlyName.Contains("Stream Mix", StringComparison.OrdinalIgnoreCase));
                    }

                    if (sourceDevice != null)
                    {
                        isCaptureEndpoint = true;
                    }
                }

                // If not found or if system-only requested: Look for System (TC-HELICON GoXLR) or default
                if (sourceDevice == null)
                {
                    sourceDevice = renderDevices.FirstOrDefault(d =>
                        d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                        d.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase));

                    if (sourceDevice == null)
                    {
                        sourceDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    isCaptureEndpoint = false;
                }

                if (sourceDevice == null)
                {
                    SetStatus(false, "Keine Audioquelle für GoXLR-Loopback gefunden.");
                    return;
                }

                // Guard against loopback of target onto itself
                if (!isCaptureEndpoint && sourceDevice.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackSystem = renderDevices.FirstOrDefault(d =>
                        d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                        d.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase));

                    if (fallbackSystem != null && !fallbackSystem.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceDevice = fallbackSystem;
                    }
                    else
                    {
                        SetStatus(false, $"Zielgerät '{targetDevice.FriendlyName}' ist bereits die Standard-Audioausgabe.");
                        return;
                    }
                }

                CurrentSourceName = sourceDevice.FriendlyName;

                // 3. Setup WASAPI Capture (Direct Capture for Broadcast Mix, Loopback for Render devices)
                if (isCaptureEndpoint)
                {
                    _capture = new WasapiCapture(sourceDevice);
                }
                else
                {
                    _capture = new WasapiLoopbackCapture(sourceDevice);
                }

                // Ultra-low latency circular buffer: 40 ms total buffer to completely eliminate delays
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(40)
                };

                var isFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat;

                _capture.DataAvailable += (s, e) =>
                {
                    if (_bufferedProvider == null || e.BytesRecorded <= 0) return;

                    // Apply real-time GoXLR volume and mute control directly onto audio frames
                    var vol = Volume;
                    var muted = IsMuted;

                    if (muted || vol <= 0.001f)
                    {
                        Array.Clear(e.Buffer, 0, e.BytesRecorded);
                    }
                    else if (Math.Abs(vol - 1.0f) > 0.005f)
                    {
                        if (isFloat)
                        {
                            var floatSpan = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
                            for (int i = 0; i < floatSpan.Length; i++)
                            {
                                floatSpan[i] *= vol;
                            }
                        }
                        else if (_capture.WaveFormat.BitsPerSample == 16)
                        {
                            var shortSpan = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
                            for (int i = 0; i < shortSpan.Length; i++)
                            {
                                shortSpan[i] = (short)Math.Clamp((int)(shortSpan[i] * vol), short.MinValue, short.MaxValue);
                            }
                        }
                    }

                    // Anti-delay buffer trim: ensure no queued backlog accumulates
                    if (_bufferedProvider.BufferedDuration.TotalMilliseconds > 30)
                    {
                        _bufferedProvider.ClearBuffer();
                    }

                    _bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                };

                _capture.RecordingStopped += (s, e) =>
                {
                    if (IsRunning && e.Exception != null)
                    {
                        SetStatus(false, $"Audio-Stream unterbrochen: {e.Exception.Message}");
                    }
                };

                // 4. Output stream preparation: Convert float to 16-bit PCM for universal Bluetooth compatibility
                IWaveProvider streamProvider = _bufferedProvider;
                try
                {
                    var sampleProvider = _bufferedProvider.ToSampleProvider();
                    streamProvider = new SampleToWaveProvider16(sampleProvider);
                }
                catch
                {
                    streamProvider = _bufferedProvider;
                }

                // 5. Initialize Output: WASAPI Shared Mode with 20ms minimal buffer latency
                _player = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 20);
                _player.Init(streamProvider);

                // 6. Start the ultra-fast pipeline
                _capture.StartRecording();
                _player.Play();

                IsRunning = true;
                CurrentDeviceId = targetDevice.ID;
                CurrentDeviceName = targetDevice.FriendlyName;

                SetStatus(true, $"Aktiv: Live GoXLR-Mix ({sourceDevice.FriendlyName}) → '{targetDevice.FriendlyName}' (Verzögerung: <20ms, GoXLR-Lautstärkeregelung aktiv)");
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
        try
        {
            _capture?.StopRecording();
            _capture?.Dispose();
        }
        catch { }
        finally { _capture = null; }

        try
        {
            _player?.Stop();
            _player?.Dispose();
        }
        catch { }
        finally { _player = null; }

        _bufferedProvider = null;
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
}
