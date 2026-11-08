using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NextXLR.Services;

/// <summary>
/// Live Audio Router for Bluetooth and Wireless Headsets.
/// Captures GoXLR Headphone/System audio via WASAPI Loopback and streams it in real-time
/// to the selected Bluetooth or Wireless playback endpoint without physical cables.
/// </summary>
public sealed class BluetoothAudioRouter : IDisposable
{
    private static readonly Lazy<BluetoothAudioRouter> _instance = new(() => new BluetoothAudioRouter());
    public static BluetoothAudioRouter Instance => _instance.Value;

    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _renderOut;
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

            if (string.IsNullOrWhiteSpace(targetDeviceId))
            {
                SetStatus(false, "Kein Zielgerät ausgewählt.");
                return;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();

                // 1. Locate the target Bluetooth/Wireless playback endpoint
                MMDevice? targetDevice = null;
                var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var d in renderDevices)
                {
                    if (d.ID.Equals(targetDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDevice = d;
                        break;
                    }
                }

                if (targetDevice == null)
                {
                    // Fallback search by friendly name
                    targetDevice = renderDevices.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(targetDeviceName) &&
                        d.FriendlyName.Contains(targetDeviceName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetDevice == null)
                {
                    SetStatus(false, $"Headset '{targetDeviceName}' ist derzeit nicht verbunden.");
                    return;
                }

                // 2. Locate GoXLR Headphones or default audio source for loopback capture
                MMDevice? sourceDevice = null;
                foreach (var d in renderDevices)
                {
                    var name = d.FriendlyName.ToLowerInvariant();
                    // Prefer GoXLR Headphones or GoXLR System
                    if (name.Contains("goxlr") || name.Contains("tc-helicon"))
                    {
                        if (name.Contains("headphone") || name.Contains("kopfhörer"))
                        {
                            sourceDevice = d;
                            break;
                        }
                        if (sourceDevice == null && (name.Contains("system") || name.Contains("line out")))
                        {
                            sourceDevice = d;
                        }
                    }
                }

                // Fallback to default render device if GoXLR hardware endpoint not specifically found
                sourceDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (sourceDevice == null)
                {
                    SetStatus(false, "Keine Audioquelle für Kopfhörer-Loopback gefunden.");
                    return;
                }

                // Do not route onto the same device to prevent acoustic feedback
                if (sourceDevice.ID.Equals(targetDevice.ID, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus(false, "Quelle und Ziel sind identisch.");
                    return;
                }

                // 3. Setup WASAPI Loopback Capture
                _capture = new WasapiLoopbackCapture(sourceDevice);

                // Setup circular audio buffer with low latency
                _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(250)
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
                        SetStatus(false, $"Aufnahme unterbrochen: {e.Exception.Message}");
                    }
                };

                // 4. Setup WASAPI Output on target Bluetooth headset
                _renderOut = new WasapiOut(targetDevice, AudioClientShareMode.Shared, true, 40);

                // Format conversion if target audio client requires resampled stream
                IWaveProvider playbackProvider = _bufferedProvider;
                if (!targetDevice.AudioClient.MixFormat.Equals(_capture.WaveFormat))
                {
                    try
                    {
                        var resampler = new MediaFoundationResampler(_bufferedProvider, targetDevice.AudioClient.MixFormat)
                        {
                            ResamplerQuality = 60
                        };
                        playbackProvider = resampler;
                    }
                    catch
                    {
                        // Fall back to original format if resampler isn't available
                        playbackProvider = _bufferedProvider;
                    }
                }

                _renderOut.Init(playbackProvider);

                // 5. Start audio forwarding
                _capture.StartRecording();
                _renderOut.Play();

                IsRunning = true;
                CurrentDeviceId = targetDevice.ID;
                CurrentDeviceName = targetDevice.FriendlyName;

                SetStatus(true, $"Aktiv: Kopfhörer-Mix wird live an '{targetDevice.FriendlyName}' gestreamt.");
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
            _renderOut?.Stop();
            _renderOut?.Dispose();
        }
        catch { }
        finally { _renderOut = null; }

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

