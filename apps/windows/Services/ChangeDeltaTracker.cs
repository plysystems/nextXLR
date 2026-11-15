using System;
using System.Collections.Generic;
using System.Linq;
using NextXLR.ViewModels;

namespace NextXLR.Services;

public sealed class ChannelSnapshot
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public double Volume { get; set; }
    public double SubmixVolume { get; set; }
    public bool Muted { get; set; }
    public bool SubmixMuted { get; set; }
}

public sealed class MicSnapshot
{
    public double Gain { get; set; }
    public bool GateEnabled { get; set; }
    public double GateThreshold { get; set; }
    public double GateAttenuation { get; set; }
    public double CompThreshold { get; set; }
    public int CompRatio { get; set; }
    public double CompMakeup { get; set; }
    public double DeEsser { get; set; }
    public bool RnnoiseEnabled { get; set; }
    public double RnnoiseIntensity { get; set; }
}

public sealed class RouteSnapshot
{
    public uint ChannelId { get; set; }
    public uint BusId { get; set; }
    public string ChannelName { get; set; } = "";
    public string BusName { get; set; } = "";
    public bool IsRouted { get; set; }
}

/// <summary>
/// Precise delta tracking engine for NEXT : XLR.
/// Detects exact parameter differences between consecutive saves or autosaves
/// and generates human-readable audit log entries:
/// e.g. "Chat von 50% auf 75% gestellt und gespeichert. [AUTOSAVE]".
/// </summary>
public sealed class ChangeDeltaTracker
{
    private static readonly Lazy<ChangeDeltaTracker> _instance = new(() => new ChangeDeltaTracker());
    public static ChangeDeltaTracker Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly Dictionary<uint, ChannelSnapshot> _channels = new();
    private readonly Dictionary<string, RouteSnapshot> _routes = new();
    private MicSnapshot? _mic;
    private bool _hasBaseline;

    private ChangeDeltaTracker() { }

    /// <summary>
    /// Initializes or resets the baseline snapshots from current ViewModel state.
    /// </summary>
    public void InitializeBaseline(MainViewModel vm)
    {
        lock (_lock)
        {
            _channels.Clear();
            foreach (var c in vm.Channels)
            {
                _channels[c.Id] = new ChannelSnapshot
                {
                    Id = c.Id,
                    Name = c.Name,
                    Volume = c.Volume,
                    SubmixVolume = c.SubmixVolume,
                    Muted = c.Muted,
                    SubmixMuted = c.SubmixMuted
                };
            }

            _routes.Clear();
            foreach (var row in vm.RouteRows)
            {
                foreach (var cell in row.Cells)
                {
                    string key = $"{cell.ChannelId}_{cell.BusId}";
                    _routes[key] = new RouteSnapshot
                    {
                        ChannelId = cell.ChannelId,
                        BusId = cell.BusId,
                        ChannelName = row.ChannelName,
                        BusName = GetBusName(cell.BusId),
                        IsRouted = cell.IsRouted
                    };
                }
            }

            if (vm.Mic != null)
            {
                _mic = new MicSnapshot
                {
                    Gain = vm.Mic.Gain,
                    GateEnabled = vm.Mic.GateEnabled,
                    GateThreshold = vm.Mic.GateThreshold,
                    GateAttenuation = vm.Mic.GateAttenuation,
                    CompThreshold = vm.Mic.CompThreshold,
                    CompRatio = vm.Mic.CompRatio,
                    CompMakeup = vm.Mic.CompMakeup,
                    DeEsser = vm.Mic.DeEsser,
                    RnnoiseEnabled = vm.Mic.RnnoiseEnabled,
                    RnnoiseIntensity = vm.Mic.RnnoiseIntensity
                };
            }

            _hasBaseline = true;
        }
    }

    /// <summary>
    /// Detects changes to faders, volumes, mutes and routing, logs each delta to StorageLoggerService,
    /// and advances the baseline snapshot.
    /// </summary>
    public int DetectAndCommitSetupChanges(MainViewModel vm, bool isAutosave, string? filePath = null)
    {
        lock (_lock)
        {
            string trigger = isAutosave ? "Autosave" : "Manuell";
            string tag = isAutosave ? "AUTOSAVE" : "MANUELL";
            int changes = 0;

            if (!_hasBaseline)
            {
                InitializeBaseline(vm);
                string targetName = ProfileService.ActiveSetupProfileName;
                StorageLoggerService.Log(
                    $"Profil: {targetName}",
                    $"Profil '{targetName}' initial gesichert. [{tag}]",
                    trigger,
                    filePath);
                return 1;
            }

            // 1. Channel Faders, Submix & Mutes
            foreach (var ch in vm.Channels)
            {
                if (!_channels.TryGetValue(ch.Id, out var snap))
                {
                    _channels[ch.Id] = new ChannelSnapshot
                    {
                        Id = ch.Id,
                        Name = ch.Name,
                        Volume = ch.Volume,
                        SubmixVolume = ch.SubmixVolume,
                        Muted = ch.Muted,
                        SubmixMuted = ch.SubmixMuted
                    };
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(ch.Name) ? $"Kanal {ch.Id}" : ch.Name;

                // Fader Volume
                int oldVol = (int)Math.Round(snap.Volume * 100.0);
                int newVol = (int)Math.Round(ch.Volume * 100.0);
                if (Math.Abs(oldVol - newVol) >= 1)
                {
                    StorageLoggerService.Log(
                        $"Fader {name}",
                        $"{name} von {oldVol}% auf {newVol}% gestellt und gespeichert. [{tag}]",
                        trigger,
                        filePath);
                    changes++;
                    snap.Volume = ch.Volume;
                }

                // Submix Volume
                int oldSub = (int)Math.Round(snap.SubmixVolume * 100.0);
                int newSub = (int)Math.Round(ch.SubmixVolume * 100.0);
                if (Math.Abs(oldSub - newSub) >= 1)
                {
                    StorageLoggerService.Log(
                        $"Submix {name}",
                        $"Kopfhörer-Submix {name} von {oldSub}% auf {newSub}% gestellt und gespeichert. [{tag}]",
                        trigger,
                        filePath);
                    changes++;
                    snap.SubmixVolume = ch.SubmixVolume;
                }

                // Mute
                if (snap.Muted != ch.Muted)
                {
                    string oldVal = snap.Muted ? "Stumm" : "Aktiv";
                    string newVal = ch.Muted ? "Stumm" : "Aktiv";
                    StorageLoggerService.Log(
                        $"Mute {name}",
                        $"{name} von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                        trigger,
                        filePath);
                    changes++;
                    snap.Muted = ch.Muted;
                }

                // Submix Mute
                if (snap.SubmixMuted != ch.SubmixMuted)
                {
                    string oldVal = snap.SubmixMuted ? "Stumm" : "Aktiv";
                    string newVal = ch.SubmixMuted ? "Stumm" : "Aktiv";
                    StorageLoggerService.Log(
                        $"Submix-Mute {name}",
                        $"Kopfhörer-Submix {name} von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                        trigger,
                        filePath);
                    changes++;
                    snap.SubmixMuted = ch.SubmixMuted;
                }
            }

            // 2. Routing Matrix
            foreach (var row in vm.RouteRows)
            {
                foreach (var cell in row.Cells)
                {
                    string key = $"{cell.ChannelId}_{cell.BusId}";
                    if (!_routes.TryGetValue(key, out var snap))
                    {
                        _routes[key] = new RouteSnapshot
                        {
                            ChannelId = cell.ChannelId,
                            BusId = cell.BusId,
                            ChannelName = row.ChannelName,
                            BusName = GetBusName(cell.BusId),
                            IsRouted = cell.IsRouted
                        };
                        continue;
                    }

                    if (snap.IsRouted != cell.IsRouted)
                    {
                        string oldVal = snap.IsRouted ? "Aktiv" : "Deaktiviert";
                        string newVal = cell.IsRouted ? "Aktiv" : "Deaktiviert";
                        string bus = snap.BusName;
                        StorageLoggerService.Log(
                            $"Routing {row.ChannelName} ➔ {bus}",
                            $"Routing {row.ChannelName} ➔ {bus} von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                            trigger,
                            filePath);
                        changes++;
                        snap.IsRouted = cell.IsRouted;
                    }
                }
            }

            // Fallback if no specific delta detected (e.g. initial or manual save)
            if (changes == 0)
            {
                string targetName = ProfileService.ActiveSetupProfileName;
                StorageLoggerService.Log(
                    $"Profil: {targetName}",
                    $"Profil '{targetName}' gespeichert. [{tag}]",
                    trigger,
                    filePath);
            }

            return changes;
        }
    }

    /// <summary>
    /// Detects changes to microphone DSP settings (Gain, Gate, Compressor, De-Esser, AI Denoise),
    /// logs each delta to StorageLoggerService, and advances the baseline snapshot.
    /// </summary>
    public int DetectAndCommitMicChanges(MainViewModel vm, bool isAutosave, string? filePath = null)
    {
        lock (_lock)
        {
            string trigger = isAutosave ? "Autosave" : "Manuell";
            string tag = isAutosave ? "AUTOSAVE" : "MANUELL";
            int changes = 0;

            if (vm.Mic == null) return 0;
            var m = vm.Mic;

            if (_mic == null)
            {
                _mic = new MicSnapshot
                {
                    Gain = m.Gain,
                    GateEnabled = m.GateEnabled,
                    GateThreshold = m.GateThreshold,
                    GateAttenuation = m.GateAttenuation,
                    CompThreshold = m.CompThreshold,
                    CompRatio = m.CompRatio,
                    CompMakeup = m.CompMakeup,
                    DeEsser = m.DeEsser,
                    RnnoiseEnabled = m.RnnoiseEnabled,
                    RnnoiseIntensity = m.RnnoiseIntensity
                };

                string targetName = ProfileService.ActiveMicProfileName;
                StorageLoggerService.Log(
                    $"Mikrofon-Profil: {targetName}",
                    $"Mikrofon-DSP Profil '{targetName}' gespeichert. [{tag}]",
                    trigger,
                    filePath);
                return 1;
            }

            // Gain
            if (Math.Abs(m.Gain - _mic.Gain) >= 1.0)
            {
                int oldVal = (int)Math.Round(_mic.Gain);
                int newVal = (int)Math.Round(m.Gain);
                StorageLoggerService.Log(
                    "Mikrofon-Gain",
                    $"Mikrofon-Gain von {oldVal} dB auf {newVal} dB gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.Gain = m.Gain;
            }

            // Gate Threshold
            if (Math.Abs(m.GateThreshold - _mic.GateThreshold) >= 1.0)
            {
                int oldVal = (int)Math.Round(_mic.GateThreshold);
                int newVal = (int)Math.Round(m.GateThreshold);
                StorageLoggerService.Log(
                    "Mikrofon-Gate",
                    $"Mikrofon-Gate Schwellenwert von {oldVal} dB auf {newVal} dB gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.GateThreshold = m.GateThreshold;
            }

            // Gate Enabled
            if (m.GateEnabled != _mic.GateEnabled)
            {
                string oldVal = _mic.GateEnabled ? "Aktiv" : "Bypass";
                string newVal = m.GateEnabled ? "Aktiv" : "Bypass";
                StorageLoggerService.Log(
                    "Mikrofon-Gate",
                    $"Mikrofon-Gate von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.GateEnabled = m.GateEnabled;
            }

            // Compressor Threshold
            if (Math.Abs(m.CompThreshold - _mic.CompThreshold) >= 1.0)
            {
                int oldVal = (int)Math.Round(_mic.CompThreshold);
                int newVal = (int)Math.Round(m.CompThreshold);
                StorageLoggerService.Log(
                    "Mikrofon-Kompressor",
                    $"Mikrofon-Kompressor Schwellenwert von {oldVal} dB auf {newVal} dB gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.CompThreshold = m.CompThreshold;
            }

            // Compressor Ratio
            if (m.CompRatio != _mic.CompRatio)
            {
                string oldVal = _mic.CompRatio < m.Ratios.Length ? m.Ratios[_mic.CompRatio] : $"{_mic.CompRatio}:1";
                string newVal = m.CompRatio < m.Ratios.Length ? m.Ratios[m.CompRatio] : $"{m.CompRatio}:1";
                StorageLoggerService.Log(
                    "Mikrofon-Kompressor",
                    $"Mikrofon-Kompressor Ratio von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.CompRatio = m.CompRatio;
            }

            // Compressor Makeup
            if (Math.Abs(m.CompMakeup - _mic.CompMakeup) >= 1.0)
            {
                int oldVal = (int)Math.Round(_mic.CompMakeup);
                int newVal = (int)Math.Round(m.CompMakeup);
                StorageLoggerService.Log(
                    "Mikrofon-Kompressor",
                    $"Mikrofon-Kompressor Makeup-Gain von {oldVal} dB auf {newVal} dB gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.CompMakeup = m.CompMakeup;
            }

            // De-Esser
            if (Math.Abs(m.DeEsser - _mic.DeEsser) >= 2.0)
            {
                int oldVal = (int)Math.Round(_mic.DeEsser);
                int newVal = (int)Math.Round(m.DeEsser);
                StorageLoggerService.Log(
                    "Mikrofon De-Esser",
                    $"Mikrofon De-Esser von {oldVal}% auf {newVal}% gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.DeEsser = m.DeEsser;
            }

            // RNNoise Enabled
            if (m.RnnoiseEnabled != _mic.RnnoiseEnabled)
            {
                string oldVal = _mic.RnnoiseEnabled ? "Aktiv" : "Bypass";
                string newVal = m.RnnoiseEnabled ? "Aktiv" : "Bypass";
                StorageLoggerService.Log(
                    "Mikrofon KI-Denoise",
                    $"Mikrofon KI-Rauschunterdrückung von {oldVal} auf {newVal} gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.RnnoiseEnabled = m.RnnoiseEnabled;
            }

            // RNNoise Intensity
            if (Math.Abs(m.RnnoiseIntensity - _mic.RnnoiseIntensity) >= 2.0)
            {
                int oldVal = (int)Math.Round(_mic.RnnoiseIntensity);
                int newVal = (int)Math.Round(m.RnnoiseIntensity);
                StorageLoggerService.Log(
                    "Mikrofon KI-Denoise",
                    $"Mikrofon KI-Intensität von {oldVal}% auf {newVal}% gestellt und gespeichert. [{tag}]",
                    trigger,
                    filePath);
                changes++;
                _mic.RnnoiseIntensity = m.RnnoiseIntensity;
            }

            if (changes == 0)
            {
                string targetName = ProfileService.ActiveMicProfileName;
                StorageLoggerService.Log(
                    $"Mikrofon-Profil: {targetName}",
                    $"Mikrofon-Profil '{targetName}' gespeichert. [{tag}]",
                    trigger,
                    filePath);
            }

            return changes;
        }
    }

    private static string GetBusName(uint busId) => busId switch
    {
        0 => "Kopfhörer",
        1 => "Broadcast Stream",
        2 => "Chat Mic",
        3 => "Line Out",
        888 => "Wireless Headset",
        999 => "Sampler",
        _ => $"Bus {busId}"
    };
}
