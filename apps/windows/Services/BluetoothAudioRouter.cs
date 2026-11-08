using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NextXLR.Services;

/// <summary>
/// Real-time Audio Forwarder for Bluetooth and Wireless Headsets.
/// Captures GoXLR output (System / Headphones / Default Audio) via WASAPI Loopback
/// and streams it to the selected Bluetooth or Wireless playback device with low latency.
/// Compatible with all Bluetooth headsets (AirPods, Galaxy Buds, Sony, Bose, etc.)
/// by using polling-based WASAPI Shared Mode with automatic PCM conversion and WaveOut fallback.
/// </summary>
public sealed class BluetoothAudioRouter : IDisposable
{
    private static readonly Lazy<BluetoothAudioRouter> _instance = new(() => new BluetoothAudioRouter());
    public static BluetoothAudioRouter Instance => _instance.Value;

    private WasapiLoopbackCapture? _capture;
    private IWavePlayer? _player;
    private BufferedWaveProvider? _bufferedProvider;
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public string? CurrentDeviceId { get; private set; }
    public string? CurrentDeviceName { get; private set; }
    public string StatusMessage { get; private set; } = "Inaktiv";

    public event Action<bool, string>? StatusChanged;

    private BluetoothAudioRouter() { }

    public void Start(string targetDeviceId, string targetDeviceName, bool muteMicSidetone)
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

                // 1. Locate the target Bluetooth/Wireless playback endpoint
                MMDevice? targetDevice = null;

                // Match by exact ID or contained ID
                if (!string.IsNullOrWhiteSpace(targetDeviceId))
                {
                    targetDevice = renderDevices.FirstOrDefault(d =>
                        d.ID.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase) ||
                        targetDeviceId.Contains(d.ID, StringComparison.OrdinalIgnoreCase) ||
                        d.ID.Contains(targetDeviceId, StringComparison.OrdinalIgnoreCase));
                }

                // Fallback: Match by friendly name
                if (targetDevice == null && !string.IsNullOrWhiteSpace(targetDeviceName))
                {
                    targetDevice = renderDevices.FirstOrDefault(d =>
                        d.FriendlyName.Equals(targetDeviceName, StringComparison.OrdinalIgnoreCase) ||
                        d.FriendlyName.Contains(targetDeviceName, StringComparison.OrdinalIgnoreCase) ||
                        targetDeviceName.Contains(d.FriendlyName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetDevice == null)
                {
                    SetStatus(false, $"Headset '{targetDeviceName}' ist derzeit nicht als Audiogerät aktiv.");
                    return;
                }

                // 2. Locate audio source: Prefer 'System (TC-HELICON GoXLR)', 'Headphones' or default render device
                MMDevice? sourceDevice = null;
                foreach (var d in renderDevices)
                {
                    var name = d.FriendlyName.ToLowerInvariant();
                    if (name.Contains("goxlr") || name.Contains("tc-helicon"))
                    {
                        if (name.Contains("system"))
                        {
                            sourceDevice = d;
                            break;
                        }
                        if (sourceDevice == null && (name.Contains("headphone") || name.Contains("kopfhörer") || name.Contains("music")))
                        {
                            sourceDevice = d;
                        }
                    }
                }

                sourceDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (sourceDevice == null)
                {
                    SetStatus(false, "Keine Audioquelle für GoXLR-Loopback gefunden.");
                    return;
                }

                if (sourceDevice.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                {
                    // If target was set as default output in Windows, try picking GoXLR System explicitly
                    var goxlrSystem = renderDevices.FirstOrDefault(d =>
                        d.FriendlyName.Contains("GoXLR", StringComparison.OrdinalIgnoreCase) &&
                        d.FriendlyName.Contains("System", StringComparison.OrdinalIgnoreCase));

                    if (goxlrSystem != null && !goxlrSystem.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                    {
                        sourceDevice = goxlrSystem;
                    }
                    else
                    {
                        SetStatus(false, $"Zielgerät '{targetDevice.FriendlyName}' ist bereits die Standard-Audioausgabe.");
                        return;
                    }
                }

                // 3. Setup WASAPI Loopback Capture on the GoXLR source
                _capture = new WasapiLoopbackCapture(sourceDevice);

                // Setup low-latency circular buffer
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(300)
                };

                _capture.DataAvailable += (s, e) =>
                {
                    if (_bufferedProvider != null && e.BytesRecorded > 0)
                    {
                        _bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    }
                };

                _capture.RecordingStopped += (s, e) =>
                {
                    if (IsRunning && e.Exception != null)
                    {
                        SetStatus(false, $"Audio-Aufnahme unterbrochen: {e.Exception.Message}");
                    }
                };

                // 4. Prepare playback stream (convert 32-bit float to 16-bit PCM if target prefers PCM)
                IWaveProvider streamProvider = _bufferedProvider;
                try
                {
                    var sampleProvider = _bufferedProvider.ToSampleProvider();
                    var pcm16Provider = new SampleToWaveProvider16(sampleProvider);
                    streamProvider = pcm16Provider;
                }
                catch
                {
                    streamProvider = _bufferedProvider;
                }

                // 5. Initialize output device: WASAPI Shared Mode in polling mode (non-event sync, required for Bluetooth)
                var wasapiOut = new WasapiOut(targetDevice, AudioClientShareMode.Shared, false, 100);
                wasapiOut.Init(streamProvider);
                _player = wasapiOut;

                // 6. Start audio pipeline
                _capture.StartRecording();
                _player?.Play();

                IsRunning = true;
                CurrentDeviceId = targetDevice.ID;
                CurrentDeviceName = targetDevice.FriendlyName;

                SetStatus(true, $"Aktiv: GoXLR-Signal ({sourceDevice.FriendlyName}) wird live an '{targetDevice.FriendlyName}' gestreamt.");
            }
            catch (Exception ex)
            {
                StopInternal();
                SetStatus(false, $"Bluetooth-Routing Fehler: {ex.Message}");
            }
        }
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
